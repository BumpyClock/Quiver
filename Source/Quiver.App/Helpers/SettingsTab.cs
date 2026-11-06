namespace Quiver.App.Helpers;

public enum SettingsTab
{
    Browsers,
    Rulesets,
    QuickView,
    Settings,
    About,
}

public static class SettingsTabs
{
    // Parses command-line pages and settings navigation tags. Unknown names open Browsers.
    public static SettingsTab Parse(string name) => name.ToLowerInvariant() switch
    {
        "rulesets" => SettingsTab.Rulesets,
        "quickview" => SettingsTab.QuickView,
        "settings" => SettingsTab.Settings,
        "about" => SettingsTab.About,
        _ => SettingsTab.Browsers,
    };
}
