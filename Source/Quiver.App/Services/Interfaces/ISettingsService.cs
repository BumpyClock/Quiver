using Quiver.Library.Models;
using System;
using System.Collections.ObjectModel;

namespace Quiver.App.Services.Interfaces;

public interface ISettingsService
{
    Settings LoadSettings();
    event EventHandler<SettingsChangedEventArgs>? SettingsChanged;
    void UpdateAppSettings(AppSettings appSettings);
    void UpdateQuickView(QuickViewSettings quickView);
    void UpdateBrowsers(ObservableCollection<Browser> browsers);
    void UpdateRulesets(ObservableCollection<Ruleset> rulesets);
}

[Flags]
public enum SettingsSection
{
    AppSettings = 1,
    QuickView = 2,
    Browsers = 4,
    Rulesets = 8
}

public sealed class SettingsChangedEventArgs(SettingsSection section) : EventArgs
{
    public SettingsSection Section { get; } = section;
}
