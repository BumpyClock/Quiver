using Quiver.App.Services.Interfaces;
using Quiver.Library;
using Quiver.Library.Models;
using Quiver.Library.Serialization;
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Diagnostics;
using System.Text.Json;
using System.Threading.Tasks;

namespace Quiver.App.Services;

public class JsonFileService : ISettingsService
{
    private readonly string settingsPath;
    private Settings? settings;
    private PreparedRulesets? preparedRulesets;
    private readonly object saveLock = new();
    private byte[]? pendingJson;
    private Task? saveTask;

    public PreparedRulesets PreparedRulesets => preparedRulesets ??= RuleMatch.PrepareRulesets(LoadSettings().Rulesets);

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
        // Capture on the UI thread so the writer never reads mutable settings.
        byte[] json = JsonSerializer.SerializeToUtf8Bytes(settings, SelectorJsonSerializerContext.Default.Settings);
        lock (saveLock)
        {
            pendingJson = json;
            saveTask ??= Task.Run(WritePendingSettingsAsync);
        }
        SettingsChanged?.Invoke(this, new SettingsChangedEventArgs(section));
    }

    public Task FlushAsync()
    {
        lock (saveLock)
        {
            if (pendingJson is not null) saveTask ??= Task.Run(WritePendingSettingsAsync);
            return saveTask ?? Task.CompletedTask;
        }
    }

    private async Task WritePendingSettingsAsync()
    {
        byte[]? json = null;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(settingsPath))!);
            while (true)
            {
                lock (saveLock)
                {
                    json = pendingJson;
                    pendingJson = null;
                    if (json is null)
                    {
                        saveTask = null;
                        return;
                    }
                }
                string temporaryPath = settingsPath + ".tmp";
                await File.WriteAllBytesAsync(temporaryPath, json).ConfigureAwait(false);
                File.Move(temporaryPath, settingsPath, overwrite: true);
            }
        }
        catch (Exception ex)
        {
            lock (saveLock)
            {
                pendingJson ??= json;
                saveTask = null;
            }
            Debug.WriteLine($"Could not save settings: {ex}");
            throw;
        }
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
        preparedRulesets = null;
        SaveSettings(SettingsSection.Rulesets);
    }
}
