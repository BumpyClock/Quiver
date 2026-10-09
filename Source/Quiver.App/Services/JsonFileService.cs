using Quiver.App.Services.Interfaces;
using Quiver.Library;
using Quiver.Library.Models;
using Quiver.Library.Serialization;
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;

namespace Quiver.App.Services;

public class JsonFileService : ISettingsService
{
    private readonly string settingsPath;
    private Settings? settings;

    public JsonFileService(string? settingsPath = null)
    {
        this.settingsPath = settingsPath ?? Constants.APP_SETTINGS_MAIN;
    }

    public event EventHandler<SettingsChangedEventArgs>? SettingsChanged;

    // All windows share this instance; saving one section preserves the others.
    public Settings LoadSettings()
    {
        if (settings is not null)
        {
            return settings;
        }

        bool firstRun = !File.Exists(settingsPath);
        settings = firstRun
            ? new Settings { Browsers = new(GetBrowsers.FromRegistry()) }
            : JsonSerializer.Deserialize(File.ReadAllText(settingsPath), SelectorJsonSerializerContext.Default.Settings)
                ?? new Settings();

        settings.Browsers ??= [];
        settings.AppSettings ??= new();
        settings.QuickView ??= new();
        settings.Rulesets ??= [];
        foreach (var browser in settings.Browsers)
        {
            browser.AlternateLaunches ??= [];
        }

        if (firstRun)
        {
            SaveSettings(SettingsSection.Browsers);
        }

        return settings;
    }

    private void SaveSettings(SettingsSection section)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(settingsPath))!);
        string json = JsonSerializer.Serialize(settings, SelectorJsonSerializerContext.Default.Settings);
        // Replace only after the complete document is written.
        string temporaryPath = settingsPath + ".tmp";
        File.WriteAllText(temporaryPath, json);
        File.Move(temporaryPath, settingsPath, overwrite: true);
        SettingsChanged?.Invoke(this, new SettingsChangedEventArgs(section));
    }

    public void UpdateAppSettings(AppSettings appSettings)
    {
        LoadSettings().AppSettings = appSettings;
        SaveSettings(SettingsSection.AppSettings);
    }

    public void UpdateQuickView(QuickViewSettings quickView)
    {
        LoadSettings().QuickView = quickView;
        SaveSettings(SettingsSection.QuickView);
    }

    public void UpdateBrowsers(ObservableCollection<Browser> browsers)
    {
        LoadSettings().Browsers = browsers;
        SaveSettings(SettingsSection.Browsers);
    }

    public void UpdateRulesets(ObservableCollection<Ruleset> rulesets)
    {
        LoadSettings().Rulesets = [.. rulesets];
        SaveSettings(SettingsSection.Rulesets);
    }
}
