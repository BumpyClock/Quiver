using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Quiver.App.Helpers;
using Quiver.App.Services.Interfaces;
using Quiver.Library;
using Quiver.Library.Models;
using Microsoft.UI.Xaml.Media.Imaging;
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
    private Dictionary<(Guid, string?), BrowserDisplayState> _browserStates = [];
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
        var previous = Browsers.GroupBy(item => item.Key)
            .ToDictionary(group => group.Key, group => group.First());
        var nextStates = new Dictionary<(Guid, string?), BrowserDisplayState>();
        var items = _settingsService.LoadSettings().Browsers.Where(browser => !browser.Hidden)
            .SelectMany(ExpandProfiles)
            .Select(candidate =>
            {
                var (browser, profile) = candidate;
                var key = (browser.Id, profile?.Directory);
                var state = BrowserDisplayState.From(browser, profile);
                nextStates[key] = state;
                return previous.TryGetValue(key, out var item)
                    && ReferenceEquals(item.Model, browser)
                    && _browserStates.TryGetValue(key, out var oldState)
                    && oldState == state ? item : new BrowserItemViewModel(browser, profile);
            }).ToArray();
        _browserStates = nextStates;
        // The selector refreshes on every open; leave in-flight icon loads alone when nothing changed.
        if (items.SequenceEqual(Browsers))
        {
            return;
        }

        _iconLoadCancellation?.Cancel();
        var cancellation = new CancellationTokenSource();
        _iconLoadCancellation = cancellation;
        ApplyBrowserItems(items);
        _ = LoadIconsAsync(items, cancellation);
    }

    // A browser with configured alternate launches or a pinned profile keeps one item; otherwise each
    // detected profile gets its own item once there is more than one.
    private static IEnumerable<(Browser Browser, BrowserProfile? Profile)> ExpandProfiles(Browser browser)
    {
        bool configured = browser.AlternateLaunches is { Count: > 0 }
            || browser.LaunchArgs?.Contains("--profile-directory", StringComparison.OrdinalIgnoreCase) == true;
        IReadOnlyList<BrowserProfile> profiles = configured ? [] : BrowserProfiles.Detect(browser.ExePath);
        if (profiles.Count < 2)
        {
            return [(browser, null)];
        }

        return profiles.Select(profile => (browser, (BrowserProfile?)profile));
    }

    private void ApplyBrowserItems(BrowserItemViewModel[] items)
    {
        for (int targetIndex = 0; targetIndex < items.Length; targetIndex++)
        {
            BrowserItemViewModel item = items[targetIndex];
            if (targetIndex < Browsers.Count && ReferenceEquals(Browsers[targetIndex], item))
            {
                continue;
            }

            int sameBrowserIndex = -1;
            for (int index = targetIndex; index < Browsers.Count; index++)
            {
                if (Browsers[index].Key == item.Key)
                {
                    sameBrowserIndex = index;
                    break;
                }
            }

            if (sameBrowserIndex >= 0)
            {
                if (sameBrowserIndex != targetIndex)
                {
                    Browsers.Move(sameBrowserIndex, targetIndex);
                }

                if (!ReferenceEquals(Browsers[targetIndex], item))
                {
                    Browsers[targetIndex] = item;
                }
                continue;
            }

            Browsers.Insert(targetIndex, item);
        }

        while (Browsers.Count > items.Length)
        {
            Browsers.RemoveAt(Browsers.Count - 1);
        }
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
                await Task.WhenAll(items.Where(item => item.Icon is null || NeedsAvatar(item)).Select(async item =>
                {
                    await concurrency.WaitAsync(cancellation.Token);
                    try
                    {
                        cancellation.Token.ThrowIfCancellationRequested();
                        var icon = item.Icon ?? await _iconLoader.LoadIconAsync(item.Model, cancellation.Token);
                        var avatar = NeedsAvatar(item)
                            ? await LoadAvatarAsync(item.Profile!.AvatarPath!, cancellation.Token)
                            : item.ProfileAvatar;
                        if (!cancellation.IsCancellationRequested)
                        {
                            item.Icon = icon;
                            item.ProfileAvatar = avatar;
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

    private static bool NeedsAvatar(BrowserItemViewModel item) =>
        item.ProfileAvatar is null && !string.IsNullOrEmpty(item.Profile?.AvatarPath);

    private async Task<BitmapImage?> LoadAvatarAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            return await _iconLoader.LoadIconFromImage(path, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Debug.WriteLine($"Could not load profile picture {path}: {ex.Message}");
            return null;
        }
    }

    private sealed record BrowserDisplayState(
        string Name, string ExePath, string? LaunchArgs, string? IconPath,
        BrowserIconSource? IconSource, int? IconIndex, string AlternateLaunches, BrowserProfile? Profile)
    {
        public static BrowserDisplayState From(Browser browser, BrowserProfile? profile) => new(
            browser.Name, browser.ExePath, browser.LaunchArgs, browser.Icon?.Path,
            browser.Icon?.Source, browser.Icon?.Index,
            string.Join("\u001f", browser.AlternateLaunches?.Select(launch =>
                $"{launch.Id}\u001e{launch.ItemName}\u001e{launch.LaunchArgs}") ?? []),
            profile);
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
            if (browserItem.ProfileLaunch is AlternateLaunch profileLaunch)
            {
                UriLauncher.Alternative(Url, browser, profileLaunch);
            }
            else
            {
                UriLauncher.Default(Url, browser);
            }
            BrowserLaunched?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex);
        }
    }
}
