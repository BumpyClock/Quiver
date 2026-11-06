using CommunityToolkit.Mvvm.ComponentModel;
using System.Text.Json.Serialization;

namespace Quiver.Library.Models;

public partial class QuickViewSettings : ObservableObject
{
    [ObservableProperty]
    public partial bool Enabled { get; set; } = true;

    [ObservableProperty]
    [property: JsonConverter(typeof(JsonStringEnumConverter<QuickViewLaunchMode>))]
    public partial QuickViewLaunchMode LaunchMode { get; set; } = QuickViewLaunchMode.WebView;

    [ObservableProperty]
    [property: JsonConverter(typeof(JsonStringEnumConverter<QuickViewModifierKeys>))]
    public partial QuickViewModifierKeys ModifierKeys { get; set; } = QuickViewModifierKeys.Alt;

    [ObservableProperty]
    public partial Guid? BrowserId { get; set; }

    [ObservableProperty]
    public partial Guid? AlternateLaunchId { get; set; }

    [ObservableProperty]
    public partial string AdditionalBrowserArguments { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool BrowserExtensionsEnabled { get; set; } = false;

    [ObservableProperty]
    [property: JsonConverter(typeof(JsonStringEnumConverter<QuickViewTrackingPreventionLevel>))]
    public partial QuickViewTrackingPreventionLevel TrackingPrevention { get; set; } = QuickViewTrackingPreventionLevel.Balanced;

    // BrowserId/AlternateLaunchId are persisted as-is and only apply in Browser mode. A stale alternate launch
    // resolves to that browser's default launch; a missing or stale browser resolves to the first browser's
    // default launch. Neither case rewrites the settings.
    public QuickViewBrowserTarget? ResolveBrowserTarget(IEnumerable<Browser> browsers)
    {
        Browser? configured = browsers.FirstOrDefault(browser => browser.Id == BrowserId);
        if (configured is not null)
        {
            bool alternateExists = configured.AlternateLaunches?.Any(launch => launch.Id == AlternateLaunchId) == true;
            return new QuickViewBrowserTarget(configured, alternateExists ? AlternateLaunchId : null);
        }

        Browser? first = browsers.FirstOrDefault();
        return first is null ? null : new QuickViewBrowserTarget(first, null);
    }
}

public sealed record QuickViewBrowserTarget(Browser Browser, Guid? AlternateLaunchId);

public enum QuickViewLaunchMode
{
    WebView,
    Browser
}

public enum QuickViewModifierKeys
{
    Alt,
    CtrlAlt,
    Ctrl
}

public enum QuickViewTrackingPreventionLevel
{
    None,
    Basic,
    Balanced,
    Strict
}
