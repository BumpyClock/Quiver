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
    private const int ExeIconBatchSize = 16;
    private readonly IIconLoader iconLoader;
    private readonly CancellationTokenSource closed = new();
    private readonly string exePath;
    private readonly int originalIconIndex;
    private readonly string? originalLocalPath;
    private BrowserIconChoice? localChoice;
    private BrowserIconChoice? urlChoice;
    private bool executableIconsLoaded;
    private bool isClosed;
    private int executableIconCount;
    private int executablePageStart;
    private BrowserIconChoice? originalExeChoice;
    private BrowserIconChoice? selectedExeChoice;
    private bool changingExecutablePage;

    public ObservableCollection<BrowserIconChoice> ExeIcons { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPreview))]
    [NotifyPropertyChangedFor(nameof(ShowEmptyState))]
    public partial BrowserIconSource SelectedSource { get; set; } = BrowserIconSource.Executable;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSave))]
    [NotifyPropertyChangedFor(nameof(HasPreview))]
    [NotifyPropertyChangedFor(nameof(ShowEmptyState))]
    public partial BrowserIconChoice? Selection { get; set; }

    [ObservableProperty]
    public partial BrowserIconChoice? SelectedExeIcon { get; set; }

    [ObservableProperty]
    public partial string UrlText { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSave))]
    [NotifyPropertyChangedFor(nameof(CanInteract))]
    [NotifyPropertyChangedFor(nameof(ShowEmptyState))]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    public partial string ErrorMessage { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanLoadMoreExeIcons))]
    [NotifyPropertyChangedFor(nameof(CanLoadPreviousExeIcons))]
    public partial bool IsLoadingExeIcons { get; set; }

    public bool CanSave => !IsBusy && Selection is not null;
    public bool CanInteract => !IsBusy;
    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);
    public bool HasPreview => SelectedSource != BrowserIconSource.Executable && Selection is not null;
    public bool ShowEmptyState => SelectedSource != BrowserIconSource.Executable && Selection is null && !IsBusy;
    public bool HasMoreExeIcons => executablePageStart + ExeIconBatchSize < executableIconCount;
    public bool HasPreviousExeIcons => executablePageStart > 0;
    public bool CanLoadMoreExeIcons => !IsLoadingExeIcons && HasMoreExeIcons && !isClosed;
    public bool CanLoadPreviousExeIcons => !IsLoadingExeIcons && HasPreviousExeIcons && !isClosed;
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
        Selection = value switch
        {
            BrowserIconSource.Executable => selectedExeChoice,
            BrowserIconSource.LocalImage => localChoice,
            BrowserIconSource.Url => urlChoice,
            _ => null
        };
    }

    partial void OnSelectedExeIconChanged(BrowserIconChoice? value)
    {
        if (changingExecutablePage || value is null) return;
        selectedExeChoice = value;
        if (SelectedSource == BrowserIconSource.Executable) Selection = value;
        OnPropertyChanged(nameof(HasSelectedExeIcon));
        OnPropertyChanged(nameof(SelectedExeLabel));
    }

    partial void OnUrlTextChanged(string value)
    {
        urlChoice = null;
        if (SelectedSource == BrowserIconSource.Url)
        {
            Selection = null;
            ErrorMessage = string.Empty;
        }
    }

    public async Task SelectSourceAsync(BrowserIconSource source)
    {
        if (IsBusy || isClosed) return;
        SelectedSource = source;
        switch (source)
        {
            case BrowserIconSource.Executable:
                if (!executableIconsLoaded)
                    await LoadExecutableIconsAsync();
                else if (selectedExeChoice is null)
                    ErrorMessage = "No icons found. Check the executable path, or choose a local image or URL.";
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
        IsBusy = true;
        ErrorMessage = string.Empty;
        try
        {
            executableIconCount = await iconLoader.GetExeIconCountAsync(exePath, closed.Token);
            if (isClosed) return;
            if (originalIconIndex >= ExeIconBatchSize && originalIconIndex < executableIconCount)
            {
                originalExeChoice = await CreateExecutableChoiceAsync(originalIconIndex);
                if (isClosed) return;
                if (originalExeChoice is not null)
                {
                    selectedExeChoice = originalExeChoice;
                    if (SelectedSource == BrowserIconSource.Executable) Selection = originalExeChoice;
                    OnPropertyChanged(nameof(HasSelectedExeIcon));
                    OnPropertyChanged(nameof(SelectedExeLabel));
                }
            }
            OnPropertyChanged(nameof(HasMoreExeIcons));
            OnPropertyChanged(nameof(CanLoadMoreExeIcons));
            executableIconsLoaded = true;
            await LoadExecutablePageAsync(0);
            if (isClosed) return;
            if (selectedExeChoice is null)
                ErrorMessage = "No icons found. Check the executable path, or choose a local image or URL.";
        }
        catch (OperationCanceledException) when (isClosed)
        {
        }
        finally
        {
            if (!isClosed) IsBusy = false;
        }
    }

    public Task LoadMoreExecutableIconsAsync() =>
        LoadExecutablePageAsync(executablePageStart + ExeIconBatchSize);

    public Task LoadPreviousExecutableIconsAsync() =>
        LoadExecutablePageAsync(executablePageStart - ExeIconBatchSize);

    private async Task LoadExecutablePageAsync(int pageStart)
    {
        if (IsLoadingExeIcons || isClosed || pageStart < 0 || pageStart >= executableIconCount) return;
        IsLoadingExeIcons = true;
        try
        {
            var page = new List<BrowserIconChoice>(ExeIconBatchSize);
            int end = Math.Min(pageStart + ExeIconBatchSize, executableIconCount);
            for (int index = pageStart; index < end && !isClosed; index++)
            {
                var choice = index == originalIconIndex && originalExeChoice is not null
                    ? originalExeChoice
                    : await CreateExecutableChoiceAsync(index);
                if (isClosed) return;
                if (choice is not null) page.Add(choice);
            }

            changingExecutablePage = true;
            try
            {
                ExeIcons.Clear();
                foreach (var choice in page) ExeIcons.Add(choice);
                executablePageStart = pageStart;
                if (selectedExeChoice is null)
                {
                    selectedExeChoice = page.FirstOrDefault(choice => choice.Icon.Index == originalIconIndex)
                        ?? page.FirstOrDefault();
                    if (SelectedSource == BrowserIconSource.Executable) Selection = selectedExeChoice;
                    OnPropertyChanged(nameof(HasSelectedExeIcon));
                    OnPropertyChanged(nameof(SelectedExeLabel));
                }
                SelectedExeIcon = page.FirstOrDefault(choice => choice.Icon.Index == selectedExeChoice?.Icon.Index);
            }
            finally
            {
                changingExecutablePage = false;
            }
        }
        catch (OperationCanceledException) when (isClosed)
        {
        }
        finally
        {
            if (!isClosed)
            {
                IsLoadingExeIcons = false;
                OnPropertyChanged(nameof(HasMoreExeIcons));
                OnPropertyChanged(nameof(HasPreviousExeIcons));
                OnPropertyChanged(nameof(CanLoadMoreExeIcons));
                OnPropertyChanged(nameof(CanLoadPreviousExeIcons));
            }
        }
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
        if (IsBusy || isClosed) return;
        string url = UrlText.Trim();
        if (!IsWebUrl(url))
        {
            Selection = null;
            urlChoice = null;
            ErrorMessage = "Enter a direct image URL starting with https:// or http://.";
            return;
        }
        await LoadImageAsync(url, fromUrl: true);
    }

    private async Task LoadImageAsync(string source, bool fromUrl)
    {
        if (IsBusy || isClosed) return;
        IsBusy = true;
        ErrorMessage = string.Empty;
        Selection = null;
        if (fromUrl) urlChoice = null;
        else localChoice = null;
        try
        {
            var image = fromUrl
                ? await iconLoader.LoadIconFromURL(source, closed.Token)
                : await iconLoader.LoadIconFromImage(source, closed.Token);
            // Ignore work completed after Cancel, or a URL edited while the request was in flight.
            if (isClosed || (fromUrl && UrlText.Trim() != source)) return;
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
            Selection = choice;
        }
        catch (OperationCanceledException) when (isClosed)
        {
        }
        finally
        {
            if (!isClosed) IsBusy = false;
        }
    }

    public void Close()
    {
        isClosed = true;
        closed.Cancel();
    }

    private static bool IsWebUrl(string source) =>
        Uri.TryCreate(source, UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);

    private static string Dimensions(BitmapImage image) => $"{image.PixelWidth} × {image.PixelHeight} pixels";
}
