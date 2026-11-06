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
JsonFileService? service = null;

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

    service = new JsonFileService(fixturePath);
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

    await service.FlushAsync();
    var reloaded = new JsonFileService(fixturePath).LoadSettings();
    Check(reloaded.QuickView.AdditionalBrowserArguments == "--changed", "arguments persisted");
    Check(reloaded.Browsers.Count == 1 && reloaded.Browsers[0].Id == browser.Id, "browser retained");
    Check(reloaded.Rulesets.Count == 1 && reloaded.Rulesets[0].RulesetName == "Keep rules", "ruleset retained");

    var originalRules = service.PreparedRulesets;
    service.UpdateQuickView(service.LoadSettings().QuickView);
    Check(ReferenceEquals(originalRules, service.PreparedRulesets), "unrelated edit retains prepared rules");
    service.UpdateRulesets([new Ruleset { Rules = ["d$example.com"], RulesetName = "Updated" }]);
    Check(!ReferenceEquals(originalRules, service.PreparedRulesets), "ruleset edit replaces prepared rules");

    for (int index = 0; index < 100; index++)
    {
        service.LoadSettings().QuickView.AdditionalBrowserArguments = $"--revision={index}";
        service.UpdateQuickView(service.LoadSettings().QuickView);
    }
    await service.FlushAsync();
    var finalSettings = new JsonFileService(fixturePath).LoadSettings();
    Check(finalSettings.QuickView.AdditionalBrowserArguments == "--revision=99", "flush persists newest queued settings");
    Check(finalSettings.Rulesets[0].RulesetName == "Updated", "queued saves preserve other sections");
}
finally
{
    if (service is not null) await service.FlushAsync();
    File.Delete(fixturePath);
    File.Delete(fixturePath + ".tmp");
}

string blockedDirectory = Path.Combine(fixtureDirectory, Guid.NewGuid().ToString("N"));
string retryPath = Path.Combine(blockedDirectory, "settings.json");
var retryService = new JsonFileService(retryPath);
try
{
    File.WriteAllText(blockedDirectory, "directory blocked by file");
    retryService.UpdateQuickView(new QuickViewSettings { AdditionalBrowserArguments = "--retained" });
    bool rejected = false;
    try { await retryService.FlushAsync(); }
    catch (IOException) { rejected = true; }
    Check(rejected, "flush reports write failure");
    File.Delete(blockedDirectory);
    await retryService.FlushAsync();
    Check(new JsonFileService(retryPath).LoadSettings().QuickView.AdditionalBrowserArguments == "--retained",
        "flush retries retained settings after storage recovers");
}
finally
{
    File.Delete(retryPath);
    File.Delete(retryPath + ".tmp");
    if (Directory.Exists(blockedDirectory)) Directory.Delete(blockedDirectory);
    else File.Delete(blockedDirectory);
}

// Failed write keeps data pending; a later save (no flush) restarts the writer; edits during a write are not lost.
var unobserved = new List<Exception>();
EventHandler<UnobservedTaskExceptionEventArgs> onUnobserved = (_, args) => unobserved.Add(args.Exception);
TaskScheduler.UnobservedTaskException += onUnobserved;
string queueBlocked = Path.Combine(fixtureDirectory, Guid.NewGuid().ToString("N"));
string queuePath = Path.Combine(queueBlocked, "settings.json");
var queueService = new JsonFileService(queuePath);
try
{
    File.WriteAllText(queueBlocked, "directory blocked by file");
    queueService.UpdateQuickView(new QuickViewSettings { AdditionalBrowserArguments = "--first" });
    // WhenAny waits without observing the exception, so only the service's own continuation can observe it.
    Task? failedWrite = queueService.FlushAsync();
    await Task.WhenAny(failedWrite);
    Check(failedWrite.IsFaulted, "background write fails while storage is blocked");
    failedWrite = null;
    File.Delete(queueBlocked);
    queueService.UpdateQuickView(new QuickViewSettings { AdditionalBrowserArguments = "--second" });
    await queueService.FlushAsync();
    Check(new JsonFileService(queuePath).LoadSettings().QuickView.AdditionalBrowserArguments == "--second",
        "save after a failed background write restarts the writer");

    var writeStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var releaseWrite = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    queueService.WriteStartedForTest = () => { writeStarted.TrySetResult(); return releaseWrite.Task; };
    queueService.UpdateQuickView(new QuickViewSettings { AdditionalBrowserArguments = "--held" });
    await writeStarted.Task;
    var inFlight = queueService.FlushAsync();
    Check(!inFlight.IsCompleted, "flush returns the held write");
    queueService.UpdateQuickView(new QuickViewSettings { AdditionalBrowserArguments = "--during-write" });
    queueService.WriteStartedForTest = null;
    releaseWrite.SetResult();
    await inFlight;
    Check(new JsonFileService(queuePath).LoadSettings().QuickView.AdditionalBrowserArguments == "--during-write",
        "save queued during a write is persisted");
}
finally
{
    File.Delete(queuePath);
    File.Delete(queuePath + ".tmp");
    if (Directory.Exists(queueBlocked)) Directory.Delete(queueBlocked);
    else File.Delete(queueBlocked);
}
GC.Collect();
GC.WaitForPendingFinalizers();
TaskScheduler.UnobservedTaskException -= onUnobserved;
Check(unobserved.Count == 0, "background save failure is observed");

Console.WriteLine("Settings smoke checks passed.");
