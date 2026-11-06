using Microsoft.UI.Xaml.Media.Imaging;
using Quiver.App.Services.Interfaces;
using Quiver.App.ViewModels;
using Quiver.Library.Models;

namespace Quiver.UiSmoke;

internal static class IconConsumerChecks
{
    public static async Task RunAsync()
    {
        await CheckBrowsersPageSchedulerAsync();
        await CheckEditPreviewLatestWinsAsync();
    }

    private static async Task CheckBrowsersPageSchedulerAsync()
    {
        var loader = new ControlledIconLoader();
        var settings = new SettingsStub(new Settings
        {
            Browsers = new(Enumerable.Range(0, 8).Select(index => new Browser($"Browser {index}", $"b{index}.exe")))
        });
        var page = new BrowsersPageViewModel(settings, loader);
        var items = page.Browsers.ToArray();
        foreach (var item in items) page.SetBrowserRealized(item, true);
        Check(loader.Requests.Count == 4, "browsers page caps concurrent icon loads at four");

        var image = new BitmapImage();
        loader.Requests[0].Complete(image);
        await Task.Yield();
        Check(ReferenceEquals(items[0].Icon, image) && loader.Requests.Count == 5,
            "finishing a load sets its icon and starts the next pending item");

        page.SetBrowserRealized(items[1], false);
        Check(loader.Requests[1].Token.IsCancellationRequested && items[1].Icon is null,
            "unrealizing a loading item cancels its request");
        loader.Requests[1].Complete(image);
        await Task.Yield();
        Check(items[1].Icon is null, "canceled load never sets the icon");

        page.SetBrowserRealized(items[0], false);
        Check(items[0].Icon is null, "unrealizing a loaded item clears its icon");

        var failing = new ControlledIconLoader();
        var failingPage = new BrowsersPageViewModel(settings, failing);
        var failingItem = failingPage.Browsers[0];
        failingPage.SetBrowserRealized(failingItem, true);
        failing.Requests[0].Fail(new InvalidOperationException("boom"));
        await Task.Yield();
        failingPage.SetBrowserRealized(failingItem, true);
        Check(failing.Requests.Count == 1 && failingItem.Icon is null,
            "failed icon load is not retried while the item stays realized");
        failingPage.SetBrowserRealized(failingItem, false);
        failingPage.SetBrowserRealized(failingItem, true);
        Check(failing.Requests.Count == 2, "failed icon load retries after the item is realized again");

        var removalLoader = new ControlledIconLoader();
        var removalPage = new BrowsersPageViewModel(settings, removalLoader);
        var removed = removalPage.Browsers[7];
        removalPage.SetBrowserRealized(removed, true);
        removalPage.Browsers.Remove(removed);
        Check(removalLoader.Requests[0].Token.IsCancellationRequested && removed.Icon is null,
            "removing a realized item cancels its load");
    }

    private static async Task CheckEditPreviewLatestWinsAsync()
    {
        var loader = new ControlledIconLoader();
        var settings = new SettingsStub(new Settings { Browsers = [new Browser("Edit", "edit.exe")] });
        var editor = new EditBrowserPageViewModel(settings.LoadSettings().Browsers[0], settings, loader);
        var older = new BitmapImage();
        var newer = new BitmapImage();

        editor.ExePath = "other.exe";
        Check(loader.Requests.Count == 2, "changing the path starts a new preview request");
        loader.Requests[1].Complete(newer);
        await editor.IconPreviewLoadTask;
        loader.Requests[0].Complete(older);
        await Task.Yield();
        Check(ReferenceEquals(editor.IconPreview, newer), "older preview never overwrites newer");

        editor.ExePath = "third.exe";
        editor.CancelIconPreviewLoad();
        loader.Requests[2].Complete(older);
        await Task.Yield();
        Check(ReferenceEquals(editor.IconPreview, newer), "canceled preview load does not write");

        editor.ExePath = "fourth.exe";
        var applied = new BitmapImage();
        editor.ApplyIcon(new BrowserIcon(), applied);
        loader.Requests[3].Complete(older);
        await Task.Yield();
        Check(ReferenceEquals(editor.IconPreview, applied), "applied icon is not overwritten by an older load");

        editor.ExePath = "fifth.exe";
        editor.ExePath = "sixth.exe";
        loader.Requests[4].Fail(new InvalidOperationException("stale"));
        await Task.Yield();
        Check(ReferenceEquals(editor.IconPreview, applied), "stale preview failure does not clear the preview");
        loader.Requests[5].Fail(new InvalidOperationException("current"));
        await editor.IconPreviewLoadTask;
        Check(editor.IconPreview is null, "current preview failure clears the preview");
    }

    private static void Check(bool condition, string caseName)
    {
        if (!condition) throw new Exception($"UiSmoke failed: {caseName}");
    }

    private sealed class ControlledIconLoader : IIconLoader
    {
        public List<Request> Requests { get; } = [];

        public Task<BitmapImage?> LoadIconAsync(Browser browser, CancellationToken cancellationToken = default)
        {
            var request = new Request(cancellationToken);
            Requests.Add(request);
            return request.Source.Task;
        }

        public sealed class Request(CancellationToken token)
        {
            public CancellationToken Token { get; } = token;
            public TaskCompletionSource<BitmapImage?> Source { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
            public void Complete(BitmapImage? image) => Source.TrySetResult(image);
            public void Fail(Exception exception) => Source.TrySetException(exception);
        }

        public Task StopAsync() => Task.CompletedTask;
        public Task FlushCacheMaintenanceAsync() => Task.CompletedTask;
        public Task<BitmapImage?> LoadIconFromExe(string exePath, CancellationToken cancellationToken = default) => Task.FromResult<BitmapImage?>(null);
        public Task<BitmapImage?> LoadIconFromExe(string exePath, int iconIndex, CancellationToken cancellationToken = default) => Task.FromResult<BitmapImage?>(null);
        public Task<int> GetExeIconCountAsync(string exePath, CancellationToken cancellationToken = default) => Task.FromResult(0);
        public Task<BitmapImage?> LoadIconFromIco(string icoPath, CancellationToken cancellationToken = default) => Task.FromResult<BitmapImage?>(null);
        public Task<BitmapImage?> LoadIconFromImage(string imagePath, CancellationToken cancellationToken = default) => Task.FromResult<BitmapImage?>(null);
        public Task<BitmapImage?> LoadIconFromURL(string url, CancellationToken cancellationToken = default) => Task.FromResult<BitmapImage?>(null);
    }

    private sealed class SettingsStub(Settings settings) : ISettingsService
    {
        public Quiver.Library.PreparedRulesets PreparedRulesets => Quiver.Library.RuleMatch.PrepareRulesets(settings.Rulesets);
        public Task FlushAsync() => Task.CompletedTask;
        public event EventHandler<SettingsChangedEventArgs>? SettingsChanged
        {
            add { }
            remove { }
        }
        public Settings LoadSettings() => settings;
        public void UpdateAppSettings(AppSettings appSettings) => throw new NotSupportedException();
        public void UpdateQuickView(QuickViewSettings quickView) => throw new NotSupportedException();
        public void UpdateBrowsers(System.Collections.ObjectModel.ObservableCollection<Browser> browsers) { }
        public void UpdateRulesets(System.Collections.ObjectModel.ObservableCollection<Ruleset> rulesets) => throw new NotSupportedException();
    }
}
