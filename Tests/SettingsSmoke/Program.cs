using Quiver.App.Services;
using Quiver.App.Services.Interfaces;
using Quiver.App.ViewModels;
using Quiver.Library.Models;
using Quiver.Library.Serialization;
using System.Collections.ObjectModel;
using System.Text.Json;

static void Check(bool condition, string caseName)
{
    if (!condition)
    {
        throw new Exception($"Settings smoke check failed: {caseName}");
    }
}

string fixtureDirectory = Path.Combine(Directory.GetCurrentDirectory(), "Tests", "SettingsSmoke", ".fixture");
Directory.CreateDirectory(fixtureDirectory);
string fixturePath = Path.Combine(fixtureDirectory, Guid.NewGuid().ToString("N") + ".json");

try
{
    var browser = new Browser("Example", "example.exe");
    var original = new Settings
    {
        Browsers = [browser],
        Rulesets = [new Ruleset { RulesetName = "Keep rules" }],
        QuickView = new QuickViewSettings { AdditionalBrowserArguments = "--original" }
    };
    File.WriteAllText(fixturePath, JsonSerializer.Serialize(original, SelectorJsonSerializerContext.Default.Settings));

    var service = new JsonFileService(fixturePath);
    var sections = new List<SettingsSection>();
    service.SettingsChanged += (_, args) => sections.Add(args.Section);
    var viewModel = new QuickViewPageViewModel(service);

    viewModel.Option_AdditionalBrowserArguments = "--changed";
    Check(sections.SequenceEqual([SettingsSection.QuickView]), "argument commit emits QuickView only");
    viewModel.Option_AdditionalBrowserArguments = "--changed";
    Check(sections.Count == 1, "same argument commit emits no save");

    service.UpdateAppSettings(service.LoadSettings().AppSettings);
    service.UpdateBrowsers(new ObservableCollection<Browser>(service.LoadSettings().Browsers));
    service.UpdateRulesets(new ObservableCollection<Ruleset>(service.LoadSettings().Rulesets));
    Check(sections.SequenceEqual([
        SettingsSection.QuickView,
        SettingsSection.AppSettings,
        SettingsSection.Browsers,
        SettingsSection.Rulesets]), "exact section events");

    var reloaded = new JsonFileService(fixturePath).LoadSettings();
    Check(reloaded.QuickView.AdditionalBrowserArguments == "--changed", "arguments persisted");
    Check(reloaded.Browsers.Count == 1 && reloaded.Browsers[0].Id == browser.Id, "browser retained");
    Check(reloaded.Rulesets.Count == 1 && reloaded.Rulesets[0].RulesetName == "Keep rules", "ruleset retained");
}
finally
{
    File.Delete(fixturePath);
    File.Delete(fixturePath + ".tmp");
}

Console.WriteLine("Settings smoke checks passed.");
