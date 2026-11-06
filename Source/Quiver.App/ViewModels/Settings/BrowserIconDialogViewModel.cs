using CommunityToolkit.Mvvm.ComponentModel;
using Quiver.App.Services.Interfaces;
using Quiver.Library.Models;
using Microsoft.UI.Xaml.Media.Imaging;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Quiver.App.ViewModels;

public sealed record BrowserIconChoice(BrowserIcon Icon, BitmapImage Image, string Name, string Details, string Source);

public partial class BrowserIconDialogViewModel : ObservableObject
{
    // Every load starts from Idle and returns to it, so at most one load runs and any load blocks input.
    // Closed is terminal: work that finishes after Close is discarded.
    private enum DialogState { NotOpened, Idle, LoadingExecutable, LoadingExecutablePage, LoadingImage, Closed }

    private const int ExeIconBatchSize = 16;
    private const string NoExeIconsMessage = "No icons found. Check the executable path, or choose a local image or URL.";
    private readonly IIconLoader iconLoader;
    private readonly CancellationTokenSource closed = new();
    private readonly string exePath;
    private readonly int originalIconIndex;
    private readonly string? originalLocalPath;
    private BrowserIconChoice? localChoice;
    private BrowserIconChoice? urlChoice;
    private int? executableIconCount;
    private int executablePageStart;
    private BrowserIconChoice? originalExeChoice;
    private BrowserIconChoice? selectedExeChoice;

    public ObservableCollection<BrowserIconChoice> ExeIcons { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsOpen), nameof(IsBusy), nameof(IsLoadingExeIcons), nameof(CanInteract),
        nameof(CanSave), nameof(ShowEmptyState), nameof(CanLoadMoreExeIcons), nameof(CanLoadPreviousExeIcons))]
    private partial DialogState State { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPreview))]
    [NotifyPropertyChangedFor(nameof(ShowEmptyState))]
    public partial BrowserIconSource SelectedSource { get; set; } = BrowserIconSource.Executable;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSave))]
    [NotifyPropertyChangedFor(nameof(HasPreview))]
    [NotifyPropertyChangedFor(nameof(ShowEmptyState))]
    public partial BrowserIconChoice? Selection { get; private set; }

    [ObservableProperty]
    public partial BrowserIconChoice? SelectedExeIcon { get; set; }

    [ObservableProperty]
    public partial string UrlText { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    public partial string ErrorMessage { get; set; } = string.Empty;

    public bool IsOpen => State is not (DialogState.NotOpened or DialogState.Closed);
    public bool IsBusy => State is DialogState.LoadingExecutable or DialogState.LoadingImage;
    public bool IsLoadingExeIcons => State == DialogState.LoadingExecutablePage;
    // NotOpened counts as interactive so controls do not render disabled while the dialog animates in.
    public bool CanInteract => State is DialogState.NotOpened or DialogState.Idle;
    public bool CanSave => CanInteract && Selection is not null;
    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);
    public bool HasPreview => SelectedSource != BrowserIconSource.Executable && Selection is not null;
    public bool ShowEmptyState => SelectedSource != BrowserIconSource.Executable && Selection is null && !IsBusy;
    public bool HasMoreExeIcons => executablePageStart + ExeIconBatchSize < (executableIconCount ?? 0);
    public bool HasPreviousExeIcons => executablePageStart > 0;
    public bool CanLoadMoreExeIcons => State == DialogState.Idle && HasMoreExeIcons;
    public bool CanLoadPreviousExeIcons => State == DialogState.Idle && HasPreviousExeIcons;
    public bool HasSelectedExeIcon => selectedExeChoice is not null;
    public string SelectedExeLabel => selectedExeChoice is null ? string.Empty : $"Selected: {selectedExeChoice.Name}";

    public BrowserIconDialogViewModel(IIconLoader iconLoader, string exePath, BrowserIcon? icon)
    {
        this.iconLoader = iconLoader;
        this.exePath = exePath;
        originalIconIndex = icon?.Index ?? 0;
        if (icon?.Source == BrowserIconSource.Url)
        {
            UrlText = icon.Path ?? string.Empty;
            SelectedSource = BrowserIconSource.Url;
        }
        else if (icon?.Source == BrowserIconSource.LocalImage)
        {
            originalLocalPath = icon.Path;
            SelectedSource = BrowserIconSource.LocalImage;
        }
    }

    partial void OnSelectedSourceChanged(BrowserIconSource value)
    {
        ErrorMessage = string.Empty;
        RefreshSelection();
    }

    partial void OnSelectedExeIconChanged(BrowserIconChoice? value)
    {
        // Only idle changes are user picks; during a load the GridView echoes page resets through this binding.
        if (State != DialogState.Idle || value is null) return;
        SetSelectedExeChoice(value);
    }

    partial void OnUrlTextChanged(string value)
    {
        urlChoice = null;
        if (SelectedSource == BrowserIconSource.Url) ErrorMessage = string.Empty;
        RefreshSelection();
    }

    public Task OpenAsync()
    {
        if (State != DialogState.NotOpened) return Task.CompletedTask;
        State = DialogState.Idle;
        return SelectSourceAsync(SelectedSource);
    }

    public async Task SelectSourceAsync(BrowserIconSource source)
    {
        if (State != DialogState.Idle) return;
        SelectedSource = source;
        switch (source)
        {
            case BrowserIconSource.Executable:
                if (executableIconCount is null)
                    await LoadExecutableIconsAsync();
                else if (selectedExeChoice is null)
                    ErrorMessage = NoExeIconsMessage;
                break;
            case BrowserIconSource.LocalImage when localChoice is null && originalLocalPath is not null:
                await LoadLocalImageAsync(originalLocalPath);
                break;
            case BrowserIconSource.Url when urlChoice is null && !string.IsNullOrWhiteSpace(UrlText):
                await LoadUrlAsync();
                break;
        }
    }

    private async Task LoadExecutableIconsAsync()
    {
        if (!TryBegin(DialogState.LoadingExecutable)) return;
        ErrorMessage = string.Empty;
        try
        {
            int count = await iconLoader.GetExeIconCountAsync(exePath, closed.Token);
            var original = originalIconIndex >= ExeIconBatchSize && originalIconIndex < count
                ? await CreateExecutableChoiceAsync(originalIconIndex)
                : null;
            var page = await FetchExecutablePageAsync(0, count, original);
            if (State == DialogState.Closed) return;
            executableIconCount = count;
            originalExeChoice = original;
            if (original is not null) SetSelectedExeChoice(original);
            ShowExecutablePage(0, page);
            if (selectedExeChoice is null) ErrorMessage = NoExeIconsMessage;
        }
        catch (OperationCanceledException) when (State == DialogState.Closed)
        {
        }
        finally
        {
            Finish();
        }
    }

    public Task LoadMoreExecutableIconsAsync() =>
        LoadExecutablePageAsync(executablePageStart + ExeIconBatchSize);

    public Task LoadPreviousExecutableIconsAsync() =>
        LoadExecutablePageAsync(executablePageStart - ExeIconBatchSize);

    private async Task LoadExecutablePageAsync(int pageStart)
    {
        if (executableIconCount is not int count || pageStart < 0 || pageStart >= count) return;
        if (!TryBegin(DialogState.LoadingExecutablePage)) return;
        try
        {
            var page = await FetchExecutablePageAsync(pageStart, count, originalExeChoice);
            if (State == DialogState.Closed) return;
            ShowExecutablePage(pageStart, page);
        }
        catch (OperationCanceledException) when (State == DialogState.Closed)
        {
        }
        finally
        {
            Finish();
        }
    }

    private async Task<List<BrowserIconChoice>> FetchExecutablePageAsync(int pageStart, int count, BrowserIconChoice? original)
    {
        int end = Math.Min(pageStart + ExeIconBatchSize, count);
        var choices = await Task.WhenAll(Enumerable.Range(pageStart, end - pageStart).Select(index =>
            index == original?.Icon.Index
                ? Task.FromResult<BrowserIconChoice?>(original)
                : CreateExecutableChoiceAsync(index)));
        return choices.OfType<BrowserIconChoice>().ToList();
    }

    private void ShowExecutablePage(int pageStart, List<BrowserIconChoice> page)
    {
        ExeIcons.Clear();
        foreach (var choice in page) ExeIcons.Add(choice);
        executablePageStart = pageStart;
        if (selectedExeChoice is null)
            SetSelectedExeChoice(page.FirstOrDefault(choice => choice.Icon.Index == originalIconIndex) ?? page.FirstOrDefault());
        SelectedExeIcon = page.FirstOrDefault(choice => choice.Icon.Index == selectedExeChoice?.Icon.Index);
        OnPropertyChanged(nameof(HasMoreExeIcons));
        OnPropertyChanged(nameof(HasPreviousExeIcons));
    }

    private void SetSelectedExeChoice(BrowserIconChoice? choice)
    {
        selectedExeChoice = choice;
        OnPropertyChanged(nameof(HasSelectedExeIcon));
        OnPropertyChanged(nameof(SelectedExeLabel));
        RefreshSelection();
    }

    private async Task<BrowserIconChoice?> CreateExecutableChoiceAsync(int index)
    {
        var image = await iconLoader.LoadIconFromExe(exePath, index, closed.Token);
        return image is null ? null : new BrowserIconChoice(
            new BrowserIcon { Source = BrowserIconSource.Executable, Index = index },
            image, $"Icon {index}", Dimensions(image), exePath);
    }

    public Task LoadLocalImageAsync(string path) => LoadImageAsync(path, fromUrl: false);

    public async Task LoadUrlAsync()
    {
        if (State != DialogState.Idle) return;
        string url = UrlText.Trim();
        if (!IsWebUrl(url))
        {
            urlChoice = null;
            RefreshSelection();
            ErrorMessage = "Enter a direct image URL starting with https:// or http://.";
            return;
        }
        await LoadImageAsync(url, fromUrl: true);
    }

    private async Task LoadImageAsync(string source, bool fromUrl)
    {
        if (!TryBegin(DialogState.LoadingImage)) return;
        ErrorMessage = string.Empty;
        if (fromUrl) urlChoice = null;
        else localChoice = null;
        RefreshSelection();
        try
        {
            var image = fromUrl
                ? await iconLoader.LoadIconFromURL(source, closed.Token)
                : await iconLoader.LoadIconFromImage(source, closed.Token);
            // A URL edited while the request was in flight makes this result stale.
            if (State == DialogState.Closed || (fromUrl && UrlText.Trim() != source)) return;
            if (image is null)
            {
                ErrorMessage = fromUrl
                    ? "Couldn't load this image. Check the URL and your connection, then try again."
                    : "Couldn't open this image. Choose a supported image file that is still available.";
                return;
            }

            string name = fromUrl ? new Uri(source).Host : Path.GetFileName(source);
            var choice = new BrowserIconChoice(new BrowserIcon
            {
                Source = fromUrl ? BrowserIconSource.Url : BrowserIconSource.LocalImage,
                Path = source
            }, image, name, Dimensions(image), source);
            if (fromUrl) urlChoice = choice;
            else localChoice = choice;
            RefreshSelection();
        }
        catch (OperationCanceledException) when (State == DialogState.Closed)
        {
        }
        finally
        {
            Finish();
        }
    }

    public void Close()
    {
        State = DialogState.Closed;
        closed.Cancel();
    }

    private bool TryBegin(DialogState load)
    {
        if (State != DialogState.Idle) return false;
        State = load;
        return true;
    }

    private void Finish()
    {
        if (State != DialogState.Closed) State = DialogState.Idle;
    }

    private void RefreshSelection() => Selection = SelectedSource switch
    {
        BrowserIconSource.Executable => selectedExeChoice,
        BrowserIconSource.LocalImage => localChoice,
        BrowserIconSource.Url => urlChoice,
        _ => null
    };

    private static bool IsWebUrl(string source) =>
        Uri.TryCreate(source, UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);

    private static string Dimensions(BitmapImage image) => $"{image.PixelWidth} × {image.PixelHeight} pixels";
}
