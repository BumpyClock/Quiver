using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Quiver.App.Helpers;
using Quiver.App.Services.Interfaces;
using Quiver.Library.Models;
using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using WinRT;

namespace Quiver.App.ViewModels;

[GeneratedBindableCustomProperty]
public partial class SelectorPageViewModel : ObservableObject
{
    private readonly ISettingsService _settingsService;
    private readonly IIconLoader _iconLoader;
    private CancellationTokenSource? _iconLoadCancellation;
    private Dictionary<Guid, BrowserDisplayState> _browserStates = [];
    public event EventHandler? BrowserLaunched;

    public SelectorPageViewModel(ISettingsService settingsService, IIconLoader iconLoader)
    {
        _settingsService = settingsService;
        _iconLoader = iconLoader;
        Settings settings = _settingsService.LoadSettings();
        AppSettings = settings.AppSettings ?? new AppSettings();
        RefreshBrowsers();
    }

    [ObservableProperty]
    public partial string Url { get; set; } = string.Empty;

    [ObservableProperty]
    public partial ObservableCollection<BrowserItemViewModel> Browsers { get; set; } = new();

    [ObservableProperty]
    public partial AppSettings AppSettings { get; set; }

    public void RefreshAppSettings()
    {
        AppSettings = _settingsService.LoadSettings().AppSettings;
    }

    public void RefreshBrowsers()
    {
        _iconLoadCancellation?.Cancel();
        var cancellation = new CancellationTokenSource();
        _iconLoadCancellation = cancellation;
        var previous = Browsers.GroupBy(item => item.Model.Id)
            .ToDictionary(group => group.Key, group => group.First());
        var nextStates = new Dictionary<Guid, BrowserDisplayState>();
        var items = _settingsService.LoadSettings().Browsers.Where(browser => !browser.Hidden)
            .Select(browser =>
            {
                var state = BrowserDisplayState.From(browser);
                nextStates[browser.Id] = state;
                return previous.TryGetValue(browser.Id, out var item)
                    && ReferenceEquals(item.Model, browser)
                    && _browserStates.TryGetValue(browser.Id, out var oldState)
                    && oldState == state ? item : new BrowserItemViewModel(browser);
            }).ToArray();
        _browserStates = nextStates;
        Browsers = new(items);
        _ = LoadIconsAsync(items, cancellation);
    }

    public void CancelIconLoading()
    {
        _iconLoadCancellation?.Cancel();
        _iconLoadCancellation = null;
    }

    private async Task LoadIconsAsync(BrowserItemViewModel[] items, CancellationTokenSource cancellation)
    {
        using (cancellation)
        using (var concurrency = new SemaphoreSlim(4))
        {
            try
            {
                await Task.WhenAll(items.Where(item => item.Icon is null).Select(async item =>
                {
                    await concurrency.WaitAsync(cancellation.Token);
                    try
                    {
                        cancellation.Token.ThrowIfCancellationRequested();
                        var icon = await _iconLoader.LoadIconAsync(item.Model, cancellation.Token);
                        if (!cancellation.IsCancellationRequested)
                        {
                            item.Icon = icon;
                        }
                    }
                    finally
                    {
                        concurrency.Release();
                    }
                }));
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
            catch (Exception ex)
            {
                Debug.WriteLine($"Could not load selector icons: {ex}");
            }
            finally
            {
                if (ReferenceEquals(_iconLoadCancellation, cancellation))
                {
                    _iconLoadCancellation = null;
                }
            }
        }
    }

    private sealed record BrowserDisplayState(
        string Name, string ExePath, string? LaunchArgs, string? IconPath,
        BrowserIconSource? IconSource, int? IconIndex, string AlternateLaunches)
    {
        public static BrowserDisplayState From(Browser browser) => new(
            browser.Name, browser.ExePath, browser.LaunchArgs, browser.Icon?.Path,
            browser.Icon?.Source, browser.Icon?.Index,
            string.Join("\u001f", browser.AlternateLaunches?.Select(launch =>
                $"{launch.Id}\u001e{launch.ItemName}\u001e{launch.LaunchArgs}") ?? []));
    }

    private IRelayCommand<BrowserItemViewModel>? launchBrowserCommand;

    public IRelayCommand<BrowserItemViewModel> LaunchBrowserCommand
    {
        get
        {
            return launchBrowserCommand ??= new RelayCommand<BrowserItemViewModel>(LaunchBrowser);
        }
    }

    private void LaunchBrowser(BrowserItemViewModel? browserItem)
    {
        if (browserItem is null)
        {
            return;
        }

        Browser browser = browserItem.Model;
        Debug.WriteLine($"Launching {browser.Name} with URL: {Url}");
        try
        {
            UriLauncher.ResolveAutomatically(Url, browser, null);
            BrowserLaunched?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex);
        }
    }
}
