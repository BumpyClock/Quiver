using CommunityToolkit.Mvvm.ComponentModel;
using Quiver.Library.Models;
using Quiver.App.Services.Interfaces;

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace Quiver.App.ViewModels;

public partial class QuickViewPageViewModel : ObservableObject
{
    [ObservableProperty]
    public partial QuickViewSettings QuickView { get; set; }

    public ObservableCollection<QuickViewBrowserLaunchOption> BrowserLaunchOptions { get; } = [];

    private readonly ISettingsService settingsService;

    public QuickViewPageViewModel(ISettingsService settingsService)
    {
        QuickView = settingsService.LoadSettings().QuickView ?? new QuickViewSettings();
        LoadBrowserLaunchOptions(settingsService.LoadSettings().Browsers);
        this.settingsService = settingsService;
    }

    public bool Option_Enabled
    {
        get => QuickView.Enabled;
        set
        {
            if (QuickView.Enabled == value)
            {
                return;
            }

            QuickView.Enabled = value;
            SaveQuickView();
            OnPropertyChanged();
            OnPropertyChanged(nameof(WebViewOptionsEnabled));
            OnPropertyChanged(nameof(BrowserLaunchOptionsEnabled));
        }
    }

    public int Option_LaunchMode
    {
        get => ToIndex(QuickView.LaunchMode);
        set
        {
            QuickViewLaunchMode launchMode = FromIndex(value, QuickViewLaunchMode.WebView);

            if (QuickView.LaunchMode == launchMode)
            {
                return;
            }

            QuickView.LaunchMode = launchMode;
            SaveQuickView();
            OnPropertyChanged();
            OnPropertyChanged(nameof(WebViewOptionsEnabled));
            OnPropertyChanged(nameof(BrowserLaunchOptionsEnabled));
            OnPropertyChanged(nameof(Option_BrowserLaunchTarget));
        }
    }

    public int Option_ModifierKeys
    {
        get => ToIndex(QuickView.ModifierKeys);
        set
        {
            QuickViewModifierKeys modifierKeys = FromIndex(value, QuickViewModifierKeys.Alt);

            if (QuickView.ModifierKeys == modifierKeys)
            {
                return;
            }

            QuickView.ModifierKeys = modifierKeys;
            SaveQuickView();
            OnPropertyChanged();
        }
    }

    public QuickViewBrowserLaunchOption? Option_BrowserLaunchTarget
    {
        get
        {
            if (QuickView.ResolveBrowserTarget(settingsService.LoadSettings().Browsers) is not { } target)
            {
                return null;
            }

            return BrowserLaunchOptions.FirstOrDefault(option =>
                option.BrowserId == target.Browser.Id && option.AlternateLaunchId == target.AlternateLaunchId);
        }
        set
        {
            if (value is null || value == Option_BrowserLaunchTarget)
            {
                return;
            }

            QuickView.BrowserId = value.BrowserId;
            QuickView.AlternateLaunchId = value.AlternateLaunchId;
            SaveQuickView();
            OnPropertyChanged();
        }
    }
    public string Option_AdditionalBrowserArguments
    {
        get => QuickView.AdditionalBrowserArguments;
        set
        {
            value ??= string.Empty;
            if (QuickView.AdditionalBrowserArguments == value)
            {
                return;
            }

            QuickView.AdditionalBrowserArguments = value;
            SaveQuickView();
            OnPropertyChanged();
        }
    }

    public bool Option_BrowserExtensionsEnabled
    {
        get => QuickView.BrowserExtensionsEnabled;
        set
        {
            if (QuickView.BrowserExtensionsEnabled == value)
            {
                return;
            }

            QuickView.BrowserExtensionsEnabled = value;
            SaveQuickView();
            OnPropertyChanged();
        }
    }

    public int Option_TrackingPrevention
    {
        get => ToIndex(QuickView.TrackingPrevention);
        set
        {
            QuickViewTrackingPreventionLevel trackingPrevention = FromIndex(value, QuickViewTrackingPreventionLevel.Balanced);

            if (QuickView.TrackingPrevention == trackingPrevention)
            {
                return;
            }

            QuickView.TrackingPrevention = trackingPrevention;
            SaveQuickView();
            OnPropertyChanged();
        }
    }

    public bool WebViewOptionsEnabled => Option_Enabled
        && QuickView.LaunchMode == QuickViewLaunchMode.WebView;

    public bool BrowserLaunchOptionsEnabled => Option_Enabled
        && QuickView.LaunchMode == QuickViewLaunchMode.Browser;

    private void SaveQuickView()
    {
        settingsService.UpdateQuickView(QuickView);
    }

    private void LoadBrowserLaunchOptions(IEnumerable<Browser> browsers)
    {
        foreach (Browser browser in browsers.Where(browser => !browser.Hidden))
        {
            BrowserLaunchOptions.Add(new QuickViewBrowserLaunchOption(
                $"{browser.Name} - Default",
                browser.Id,
                null));

            if (browser.AlternateLaunches is not { Count: > 0 })
            {
                continue;
            }

            foreach (AlternateLaunch alternateLaunch in browser.AlternateLaunches)
            {
                BrowserLaunchOptions.Add(new QuickViewBrowserLaunchOption(
                    $"{browser.Name} - {alternateLaunch.ItemName}",
                    browser.Id,
                    alternateLaunch.Id));
            }
        }
    }

    // ComboBox item order in QuickViewPage.xaml follows the enum declaration order.
    private static int ToIndex<T>(T value) where T : struct, Enum => Array.IndexOf(Enum.GetValues<T>(), value);

    private static T FromIndex<T>(int index, T fallback) where T : struct, Enum
    {
        T[] values = Enum.GetValues<T>();
        return index >= 0 && index < values.Length ? values[index] : fallback;
    }
}

public sealed record QuickViewBrowserLaunchOption(
    string DisplayName,
    Guid BrowserId,
    Guid? AlternateLaunchId);
