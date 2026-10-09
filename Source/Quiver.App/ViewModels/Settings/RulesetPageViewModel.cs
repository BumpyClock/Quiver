using CommunityToolkit.Mvvm.ComponentModel;
using Quiver.Library.Models;
using Quiver.App.Services.Interfaces;

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace Quiver.App.ViewModels;

public partial class RulesetPageViewModel : ObservableObject
{
    private readonly ISettingsService _settingsService;
    private readonly Dictionary<Guid, string> browserNames;

    [ObservableProperty]
    public partial ObservableCollection<Ruleset> Rulesets { get; set; }

    public ObservableCollection<RulesetItemViewModel> RulesetItems { get; } = [];

    [ObservableProperty]
    public partial AppSettings AppSettings { get; set; }

    public RulesetPageViewModel(ISettingsService settingsService)
    {
        _settingsService = settingsService;
        var settings = settingsService.LoadSettings();
        browserNames = new Dictionary<Guid, string>();
        foreach (var browser in settings.Browsers)
        {
            browserNames.TryAdd(browser.Id, browser.Name);
        }
        Rulesets = new(settings.Rulesets);
        AppSettings = settings.AppSettings;
        foreach (var ruleset in Rulesets)
        {
            RulesetItems.Add(CreateItem(ruleset));
        }
    }

    public bool Option_RuleMatching
    {
        get => AppSettings.RuleMatching;
        set
        {
            if (AppSettings.RuleMatching != value)
            {
                AppSettings.RuleMatching = value;
                _settingsService.UpdateAppSettings(AppSettings);
                OnPropertyChanged();
            }
        }
    }

    public void NewRuleset(Ruleset ruleset)
    {
        Rulesets.Add(ruleset);
        RulesetItems.Add(CreateItem(ruleset));
        SaveRulesets();
    }

    public void EditRuleset(Ruleset ruleset)
    {
        var existingRuleset = Rulesets.First(x => x.Id == ruleset.Id);
        var index = Rulesets.IndexOf(existingRuleset);
        if (index != -1)
        {
            Rulesets[index] = ruleset;
            RulesetItems[index] = CreateItem(ruleset);
            SaveRulesets();
        }
    }

    public void MoveRulesetUp(Guid Id)
    {
        var existingRuleset = Rulesets.First(x => x.Id == Id);
        var index = Rulesets.IndexOf(existingRuleset);
        if (index > 0)
        {
            Rulesets.Move(index, index - 1);
            RulesetItems.Move(index, index - 1);
            SaveRulesets();
        }
    }

    public void MoveRulesetDown(Guid Id)
    {
        var existingRuleset = Rulesets.First(x => x.Id == Id);
        var index = Rulesets.IndexOf(existingRuleset);
        if (index != -1 && index < Rulesets.Count - 1)
        {
            Rulesets.Move(index, index + 1);
            RulesetItems.Move(index, index + 1);
            SaveRulesets();
        }
    }

    public void DeleteRuleset(Guid Id)
    {
        var existingRuleset = Rulesets.First(x => x.Id == Id);
        if (existingRuleset != null)
        {
            var index = Rulesets.IndexOf(existingRuleset);
            Rulesets.RemoveAt(index);
            RulesetItems.RemoveAt(index);
            SaveRulesets();
        }
    }

    public Ruleset GetRuleset(Guid id)
    {
        return Rulesets.First(x => x.Id == id);
    }

    public string GetBrowserDisplayName(Guid browserId)
    {
        return browserNames.GetValueOrDefault(browserId) ?? "Missing browser";
    }

    private void SaveRulesets()
    {
        _settingsService.UpdateRulesets(Rulesets);
    }

    private RulesetItemViewModel CreateItem(Ruleset ruleset) =>
        new(ruleset, GetBrowserDisplayName(ruleset.BrowserId));
}

public sealed record RulesetItemViewModel(Ruleset Model, string BrowserDisplayName);
