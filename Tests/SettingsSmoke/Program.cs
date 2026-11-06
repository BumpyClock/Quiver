using Quiver.App.Services;
using Quiver.Library;
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

// Quick View launch target resolution and index mappings.
{
    var first = new Browser("First", "first.exe");
    var second = new Browser("Second", "second.exe") { AlternateLaunches = [new AlternateLaunch { ItemName = "Work" }] };
    Browser[] browsers = [first, second];
    Guid workId = second.AlternateLaunches![0].Id;

    var unset = new QuickViewSettings { LaunchMode = QuickViewLaunchMode.Browser };
    Check(unset.ResolveBrowserTarget(browsers) is { } t1 && t1.Browser == first && t1.AlternateLaunchId is null, "unset target resolves to first browser");
    Check(unset.BrowserId is null, "resolving does not rewrite persisted fields");
    var chosen = new QuickViewSettings { BrowserId = second.Id, AlternateLaunchId = workId };
    Check(chosen.ResolveBrowserTarget(browsers) is { } t2 && t2.Browser == second && t2.AlternateLaunchId == workId, "configured target is kept");
    var stale = new QuickViewSettings { BrowserId = Guid.NewGuid() };
    Check(stale.ResolveBrowserTarget(browsers)?.Browser == first, "stale browser falls back to first");
    Check(stale.ResolveBrowserTarget([]) is null, "no browsers resolves to nothing");
    var staleAlternate = new QuickViewSettings { BrowserId = second.Id, AlternateLaunchId = Guid.NewGuid() };
    Check(staleAlternate.ResolveBrowserTarget(browsers) is { } t3 && t3.Browser == second && t3.AlternateLaunchId is null,
        "stale alternate launch falls back to the configured browser's default");

    var indexSettings = new QuickViewSettings();
    var indexService = new IndexSettingsService(new Settings { Browsers = [first, second], QuickView = indexSettings });
    var indexModel = new QuickViewPageViewModel(indexService);
    indexModel.Option_LaunchMode = 1;
    indexModel.Option_ModifierKeys = 2;
    indexModel.Option_TrackingPrevention = 3;
    Check(indexSettings.LaunchMode == QuickViewLaunchMode.Browser && indexSettings.ModifierKeys == QuickViewModifierKeys.Ctrl
        && indexSettings.TrackingPrevention == QuickViewTrackingPreventionLevel.Strict, "index setters map to enums");
    Check(indexModel.Option_LaunchMode == 1 && indexModel.Option_ModifierKeys == 2 && indexModel.Option_TrackingPrevention == 3, "index getters map from enums");
    indexModel.Option_TrackingPrevention = 99;
    Check(indexSettings.TrackingPrevention == QuickViewTrackingPreventionLevel.Balanced, "out of range index uses default");
    Check(indexModel.Option_BrowserLaunchTarget?.BrowserId == first.Id, "page shows resolved launch target");
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

sealed class IndexSettingsService(Settings settings) : ISettingsService
{
    public event EventHandler<SettingsChangedEventArgs>? SettingsChanged;
    public PreparedRulesets PreparedRulesets => throw new NotSupportedException();
    public Settings LoadSettings() => settings;
    public Task FlushAsync() => Task.CompletedTask;
    public void UpdateAppSettings(AppSettings appSettings) { }
    public void UpdateQuickView(QuickViewSettings quickView) { }
    public void UpdateBrowsers(ObservableCollection<Browser> browsers) { }
    public void UpdateRulesets(ObservableCollection<Ruleset> rulesets) { }
}
