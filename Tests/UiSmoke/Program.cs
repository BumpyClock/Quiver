using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media.Imaging;
using Quiver.App.Services;
using Quiver.App.Services.Interfaces;
using Quiver.App.ViewModels;
using Quiver.Library.Models;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace Quiver.UiSmoke;

internal static partial class Program
{
    [LibraryImport("Microsoft.ui.xaml.dll")]
    private static partial void XamlCheckProcessRequirements();

    [STAThread]
    private static void Main()
    {
        XamlCheckProcessRequirements();
        WinRT.ComWrappersSupport.InitializeComWrappers();
        Application.Start(_ =>
        {
            SynchronizationContext.SetSynchronizationContext(
                new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread()));
            new SmokeApplication();
        });
    }
}

internal sealed class SmokeApplication : Application
{
    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        string fixtureDirectory = Path.Combine(Directory.GetCurrentDirectory(), "Tests", "UiSmoke", ".fixture");
        Directory.CreateDirectory(fixtureDirectory);
        string fixturePath = Path.Combine(fixtureDirectory, Guid.NewGuid().ToString("N") + ".png");
        string concurrentPath = Path.Combine(fixtureDirectory, Guid.NewGuid().ToString("N") + ".png");
        string oversizedPath = Path.Combine(fixtureDirectory, Guid.NewGuid().ToString("N") + ".img");
        string cacheDirectory = Path.Combine(fixtureDirectory, Guid.NewGuid().ToString("N"));
        string evictionDirectory = Path.Combine(fixtureDirectory, Guid.NewGuid().ToString("N"));
        string portraitPath = Path.Combine(fixtureDirectory, Guid.NewGuid().ToString("N") + ".png");
        string smallPath = Path.Combine(fixtureDirectory, Guid.NewGuid().ToString("N") + ".png");
        var evictionSources = new List<string>();
        var loaders = new List<IconLoaderService>();
        try
        {
            using (var image = new Bitmap(1024, 512))
            {
                using var graphics = Graphics.FromImage(image);
                graphics.Clear(Color.CornflowerBlue);
                image.Save(fixturePath, ImageFormat.Png);
            }

            var loader = new IconLoaderService(cacheDirectory);
            loaders.Add(loader);
            var first = await loader.LoadIconFromImage(fixturePath);
            Check(first is not null, "real BitmapImage decode");
            Check(first!.DecodePixelWidth == 256 && first.DecodePixelHeight == 128,
                $"decode size request ({first.DecodePixelWidth}x{first.DecodePixelHeight}; source {first.PixelWidth}x{first.PixelHeight})");
            SaveBitmap(portraitPath, 512, 1024);
            var portrait = await loader.LoadIconFromImage(portraitPath);
            Check(portrait?.DecodePixelWidth == 128 && portrait.DecodePixelHeight == 256,
                "portrait decode keeps aspect and size bound");
            SaveBitmap(smallPath, 32, 16);
            var small = await loader.LoadIconFromImage(smallPath);
            Check(small?.DecodePixelWidth == 32 && small.DecodePixelHeight == 16,
                "small image is not upscaled");
            var ico = await loader.LoadIconFromIco(Path.Combine(Directory.GetCurrentDirectory(),
                "Source", "Quiver.App", "Assets", "internet.ico"));
            Check(ico is not null, "repository ICO decodes");
            var second = await loader.LoadIconFromImage(fixturePath);
            Check(ReferenceEquals(first, second), "decoded image reuse");

            File.WriteAllBytes(oversizedPath, new byte[8 * 1024 * 1024 + 1]);
            Check(await loader.LoadIconFromImage(oversizedPath) is null, "oversized local image rejected");

            File.Copy(fixturePath, concurrentPath);
            using var canceled = new CancellationTokenSource();
            var wait1 = loader.LoadIconFromImage(concurrentPath, canceled.Token);
            var wait2 = loader.LoadIconFromImage(concurrentPath);
            canceled.Cancel();
            bool canceledFirst = false;
            try { await wait1; } catch (OperationCanceledException) { canceledFirst = true; }
            Check(canceledFirst, "one pending waiter canceled");
            var sharedImage = await wait2;
            Check(sharedImage is not null, "other image waiter succeeds");
            Check(ReferenceEquals(sharedImage, await loader.LoadIconFromImage(concurrentPath)),
                "concurrent result retained in decoded cache");

            var chooser = new BrowserIconDialogViewModel(new FakeIconLoader(first), "fixture.exe",
                new BrowserIcon { Source = BrowserIconSource.Executable, Index = 20 });
            await chooser.SelectSourceAsync(BrowserIconSource.Executable);
            Check(chooser.ExeIcons.Count <= 16 && chooser.Selection?.Icon.Index == 20,
                "first chooser page bounded and original icon selected");
            await chooser.LoadMoreExecutableIconsAsync();
            Check(chooser.ExeIcons.Count <= 16 && chooser.Selection?.Icon.Index == 20,
                "second chooser page bounded and original icon preserved");
            chooser.SelectedExeIcon = null;
            Check(chooser.Selection?.Icon.Index == 20, "recycled GridView clears visual selection only");
            await chooser.LoadPreviousExecutableIconsAsync();
            Check(chooser.ExeIcons.Count <= 16 && chooser.Selection?.Icon.Index == 20,
                "previous page keeps off-page original selection");
            chooser.SelectedExeIcon = chooser.ExeIcons[2];
            await chooser.LoadMoreExecutableIconsAsync();
            Check(chooser.Selection?.Icon.Index == 2,
                "user-selected icon survives page change");
            chooser.Close();

            Directory.CreateDirectory(evictionDirectory);
            string staleRegular = Path.Combine(evictionDirectory, new string('A', 64) + ".img");
            string staleTemporary = Path.Combine(evictionDirectory,
                new string('B', 64) + ".img." + new string('C', 32) + ".tmp");
            string unrelated = Path.Combine(evictionDirectory, "keep.txt");
            File.WriteAllText(staleRegular, "stale");
            File.WriteAllText(staleTemporary, "stale");
            File.WriteAllText(unrelated, "keep");
            File.SetLastWriteTimeUtc(staleRegular, DateTime.UtcNow.AddDays(-31));
            File.SetLastWriteTimeUtc(staleTemporary, DateTime.UtcNow.AddHours(-2));
            var diskLoader = new IconLoaderService(evictionDirectory);
            loaders.Add(diskLoader);
            for (int index = 0; index < 10; index++)
            {
                string path = Path.Combine(evictionDirectory, index.ToString("D64") + ".img");
                using (var cacheSeed = new FileStream(path, FileMode.CreateNew))
                {
                    cacheSeed.SetLength(7 * 1024 * 1024);
                }
                File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(-20 + index));
            }
            Check(await diskLoader.LoadIconFromImage(fixturePath) is not null, "cache trim trigger loads");
            await diskLoader.FlushCacheMaintenanceAsync();
            Check(!File.Exists(staleRegular) && !File.Exists(staleTemporary),
                "stale regular and temporary cache files expire");
            Check(File.Exists(unrelated), "unrelated cache file retained");
            Check(Directory.EnumerateFiles(evictionDirectory).Sum(path => new FileInfo(path).Length)
                <= 64L * 1024 * 1024, "disk cache stays within 64 MiB");

            var memoryLoader = new IconLoaderService(cacheDirectory);
            loaders.Add(memoryLoader);
            var firstMemoryImage = await memoryLoader.LoadIconFromImage(fixturePath);
            for (int index = 0; index < 65; index++)
            {
                string sourcePath = Path.Combine(fixtureDirectory, Guid.NewGuid().ToString("N") + ".png");
                File.Copy(fixturePath, sourcePath);
                evictionSources.Add(sourcePath);
                Check(await memoryLoader.LoadIconFromImage(sourcePath) is not null, "distinct icon loads");
            }
            var reloadedMemoryImage = await memoryLoader.LoadIconFromImage(fixturePath);
            Check(reloadedMemoryImage is not null && !ReferenceEquals(firstMemoryImage, reloadedMemoryImage),
                "oldest decoded icon evicted after 64 entries");
            Check(ReferenceEquals(reloadedMemoryImage, await memoryLoader.LoadIconFromImage(fixturePath)),
                "evicted icon reload is reused");

            var pendingIcons = new PendingIconLoader();
            var browserSettings = new InMemorySettingsService(new Settings
            {
                Browsers = new(Enumerable.Range(0, 12).Select(index =>
                    new Browser($"Browser {index}", $"browser{index}.exe")))
            });
            var selector = new SelectorPageViewModel(browserSettings, pendingIcons);
            Check(pendingIcons.Started == 4, "selector starts at most four icon loads");
            selector.CancelIconLoading();
            await pendingIcons.AllCanceled.WaitAsync(TimeSpan.FromSeconds(2));
            Check(pendingIcons.Canceled == 4, $"selector cancels pending icon loads on close ({pendingIcons.Canceled}/4)");

            var stableSelector = new SelectorPageViewModel(browserSettings, new FakeIconLoader(first));
            var selectorItems = stableSelector.Browsers;
            var originalItem = selectorItems[0];
            int collectionChanges = 0;
            selectorItems.CollectionChanged += (_, _) => collectionChanges++;
            stableSelector.RefreshBrowsers();
            Check(ReferenceEquals(selectorItems, stableSelector.Browsers) && collectionChanges == 0,
                "unchanged selector refresh preserves collection without resets");
            browserSettings.LoadSettings().Browsers.Move(0, 2);
            stableSelector.RefreshBrowsers();
            Check(ReferenceEquals(selectorItems[2], originalItem), "selector reorder retains row and icon");
            browserSettings.LoadSettings().Browsers[2].Hidden = true;
            stableSelector.RefreshBrowsers();
            Check(selectorItems.Count == 11 && !selectorItems.Contains(originalItem), "selector removes hidden row");
            stableSelector.CancelIconLoading();

            var realizedIcons = new PendingIconLoader();
            var browserList = new BrowsersPageViewModel(browserSettings, realizedIcons);
            Check(realizedIcons.Started == 0, "offscreen browser icons stay unloaded");
            foreach (var item in browserList.Browsers) browserList.SetBrowserRealized(item, true);
            Check(realizedIcons.Started == 4, "realized browser icons load with bounded concurrency");
            browserList.CancelIconLoading();
            await realizedIcons.AllCanceled.WaitAsync(TimeSpan.FromSeconds(2));
            Check(realizedIcons.Canceled == 4, "browser page unload cancels active requests without starting queued work");

            var cachedBrowserList = new BrowsersPageViewModel(browserSettings, new FakeIconLoader(first));
            var realizedItem = cachedBrowserList.Browsers[0];
            cachedBrowserList.SetBrowserRealized(realizedItem, true);
            Check(ReferenceEquals(realizedItem.Icon, first), "realized browser row displays icon");
            cachedBrowserList.SetBrowserRealized(realizedItem, false);
            Check(realizedItem.Icon is null, "recycled browser row releases image reference");

            var previewIcons = new PendingIconLoader();
            var preview = new EditBrowserPageViewModel(browserSettings.LoadSettings().Browsers[0], browserSettings, previewIcons);
            var supersededPreview = preview.IconPreviewLoadTask;
            preview.ExePath = "replacement.exe";
            await supersededPreview.WaitAsync(TimeSpan.FromSeconds(2));
            Check(previewIcons.Started == 2 && previewIcons.Canceled == 1, "replacing preview cancels old request");
            preview.CancelIconPreviewLoad();
            await preview.IconPreviewLoadTask.WaitAsync(TimeSpan.FromSeconds(2));
            Check(previewIcons.Canceled == 2, "leaving editor cancels current preview");

            var firstRule = new Ruleset { RulesetName = "First" };
            var secondRule = new Ruleset { RulesetName = "Second" };
            var rulesSettings = new InMemorySettingsService(new Settings { Rulesets = [firstRule, secondRule] });
            var rules = new RulesetPageViewModel(rulesSettings);
            var firstRow = rules.RulesetItems[0];
            rules.MoveRulesetDown(firstRule.Id);
            Check(ReferenceEquals(rules.RulesetItems[1], firstRow), "moved ruleset retains row instance");
            Check(rulesSettings.LoadSettings().Rulesets[1].Id == firstRule.Id, "ruleset move persisted");

            await File.WriteAllTextAsync(Path.Combine(fixtureDirectory, "last-result.log"), "UiSmoke passed\n");
        }
        catch (Exception ex)
        {
            await File.WriteAllTextAsync(Path.Combine(fixtureDirectory, "last-result.log"), ex.ToString());
            Environment.ExitCode = 1;
        }
        finally
        {
            await Task.WhenAll(loaders.Select(loader => loader.FlushCacheMaintenanceAsync()));
            File.Delete(fixturePath);
            File.Delete(concurrentPath);
            File.Delete(oversizedPath);
            File.Delete(portraitPath);
            File.Delete(smallPath);
            foreach (string sourcePath in evictionSources) File.Delete(sourcePath);
            foreach (string directory in new[] { cacheDirectory, evictionDirectory })
            {
                if (!Directory.Exists(directory)) continue;
                foreach (string cacheFile in Directory.EnumerateFiles(directory)) File.Delete(cacheFile);
                Directory.Delete(directory);
            }
            Exit();
        }
    }

    private static void Check(bool condition, string caseName)
    {
        if (!condition) throw new Exception($"UiSmoke failed: {caseName}");
    }

    private static void SaveBitmap(string path, int width, int height)
    {
        using var image = new Bitmap(width, height);
        using var graphics = Graphics.FromImage(image);
        graphics.Clear(Color.CornflowerBlue);
        image.Save(path, ImageFormat.Png);
    }

    private sealed class FakeIconLoader(BitmapImage image) : IIconLoader
    {
        public Task<BitmapImage?> LoadIconAsync(Browser browser, CancellationToken cancellationToken = default) => Task.FromResult<BitmapImage?>(image);
        public Task<BitmapImage?> LoadIconFromExe(string exePath, CancellationToken cancellationToken = default) => Task.FromResult<BitmapImage?>(image);
        public Task<BitmapImage?> LoadIconFromExe(string exePath, int iconIndex, CancellationToken cancellationToken = default) => Task.FromResult<BitmapImage?>(image);
        public Task<int> GetExeIconCountAsync(string exePath, CancellationToken cancellationToken = default) => Task.FromResult(32);
        public Task<BitmapImage?> LoadIconFromIco(string icoPath, CancellationToken cancellationToken = default) => Task.FromResult<BitmapImage?>(image);
        public Task<BitmapImage?> LoadIconFromImage(string imagePath, CancellationToken cancellationToken = default) => Task.FromResult<BitmapImage?>(image);
        public Task<BitmapImage?> LoadIconFromURL(string url, CancellationToken cancellationToken = default) => Task.FromResult<BitmapImage?>(image);
    }

    private sealed class PendingIconLoader : IIconLoader
    {
        public int Started { get; private set; }
        public int Canceled { get; private set; }
        private readonly TaskCompletionSource allCanceled = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task AllCanceled => allCanceled.Task;

        public async Task<BitmapImage?> LoadIconAsync(Browser browser, CancellationToken cancellationToken = default)
        {
            Started++;
            try { await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken); }
            catch (OperationCanceledException)
            {
                if (++Canceled == 4) allCanceled.TrySetResult();
                throw;
            }
            return null;
        }

        public Task<BitmapImage?> LoadIconFromExe(string exePath, CancellationToken cancellationToken = default) => Task.FromResult<BitmapImage?>(null);
        public Task<BitmapImage?> LoadIconFromExe(string exePath, int iconIndex, CancellationToken cancellationToken = default) => Task.FromResult<BitmapImage?>(null);
        public Task<int> GetExeIconCountAsync(string exePath, CancellationToken cancellationToken = default) => Task.FromResult(0);
        public Task<BitmapImage?> LoadIconFromIco(string icoPath, CancellationToken cancellationToken = default) => Task.FromResult<BitmapImage?>(null);
        public Task<BitmapImage?> LoadIconFromImage(string imagePath, CancellationToken cancellationToken = default) => Task.FromResult<BitmapImage?>(null);
        public Task<BitmapImage?> LoadIconFromURL(string url, CancellationToken cancellationToken = default) => Task.FromResult<BitmapImage?>(null);
    }

    private sealed class InMemorySettingsService(Settings settings) : ISettingsService
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
        public void UpdateBrowsers(System.Collections.ObjectModel.ObservableCollection<Browser> browsers) => throw new NotSupportedException();
        public void UpdateRulesets(System.Collections.ObjectModel.ObservableCollection<Ruleset> rulesets) => settings.Rulesets = [.. rulesets];
    }
}
