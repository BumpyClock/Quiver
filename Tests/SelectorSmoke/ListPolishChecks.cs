using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Quiver.App.Controls;
using Quiver.App.Services;
using Quiver.App.ViewModels;
using Quiver.App.Windows;
using Quiver.Library;
using Quiver.Library.Models;
using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text.Json;
using Windows.Graphics;
using Windows.Graphics.Imaging;
using Windows.System;

namespace Quiver.SelectorSmoke;

internal static partial class ListPolishChecks
{
    [LibraryImport("dwmapi.dll")]
    private static partial int DwmFlush();

    private sealed record FrameObservation(string Image, string Action, int Count, string Theme,
        string FocusedButton, string FocusState, string SelectedBrowser, double VerticalOffset, double ViewportHeight);

    private const string FixtureUrl = "https://github.com/BumpyClock/Quiver/pull/29";

    public static async Task RunChecksAsync(SelectorWindow window, TestSettings settings)
    {
        await CheckFixturesAsync(window, settings, directory: null, edge: null, chrome: null);
        Console.WriteLine("PASS: List polish fixtures (profiles, artwork, gutter, whole rows, address, recovery focus, rounding).");
    }

    public static async Task RunAsync(SelectorWindow window, TestSettings settings)
    {
        string directory = Environment.GetEnvironmentVariable("QUIVER_SELECTOR_CAPTURE_DIRECTORY")
            ?? throw new InvalidOperationException("List polish mode requires QUIVER_SELECTOR_CAPTURE_DIRECTORY.");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "capture-bounds.csv"),
            "image,rootWidthDip,rootHeightDip,rawWidthPx,rawHeightPx,viewportWidthPx,viewportHeightPx\n");
        settings.UpdateAppSettings(new AppSettings { SelectorLayout = SelectorLayout.List });
        var root = (FrameworkElement)window.Content;
        var ring = (ItemsRepeater)root.FindName("BrowserList");
        var loader = new IconLoaderService(Path.Combine(AppContext.BaseDirectory, "list-polish-icon-cache"));
        BitmapImage? edge;
        BitmapImage? chrome;
        try
        {
            edge = await loader.LoadIconFromExe(Path.Combine(Environment.GetFolderPath(
                Environment.SpecialFolder.ProgramFilesX86), "Microsoft", "Edge", "Application", "msedge.exe"));
            chrome = await loader.LoadIconFromExe(Path.Combine(Environment.GetFolderPath(
                Environment.SpecialFolder.ProgramFiles), "Google", "Chrome", "Application", "chrome.exe"));
        }
        finally { await loader.StopAsync(); }
        Check(edge is not null && chrome is not null, "production icon decoder loads installed browsers");
        window.ViewModel.Browsers[0].Icon = edge;
        window.ViewModel.Browsers[1].Icon = chrome;
        var samples = new List<string> { "sample,showReturnMs,dwmFlushMs,firstRenderingMs,animated,scale,opacity" };
        for (int sample = 0; sample < 12; sample++)
        {
            window.MinimizeWindow();
            await Task.Delay(50);
            var clock = Stopwatch.StartNew();
            var rendering = new TaskCompletionSource<double>(TaskCreationOptions.RunContinuationsAsynchronously);
            EventHandler<object>? handler = null;
            handler = (_, _) =>
            {
                CompositionTarget.Rendering -= handler;
                rendering.TrySetResult(clock.Elapsed.TotalMilliseconds);
            };
            CompositionTarget.Rendering += handler;
            window.ShowWindow();
            double returned = clock.Elapsed.TotalMilliseconds;
            var visual = ElementCompositionPreview.GetElementVisual((FrameworkElement)root.FindName("ListHost"));
            bool animated = visual.TryGetAnimationController("Opacity") is not null
                || visual.TryGetAnimationController("Scale") is not null;
            Check(!animated && visual.Opacity == 1 && visual.Scale == Vector3.One,
                "List opens opaque and unscaled without an entrance animation");
            Check(ReferenceEquals(ring.ItemsSource, window.ViewModel.Browsers)
                && ring.TryGetElement(0) is ListBrowserItem { ActualHeight: 52 },
                "List returns from ShowWindow with bound, arranged rows");
            Check(DwmFlush() >= 0, "opening compositor flush succeeds");
            double flushed = clock.Elapsed.TotalMilliseconds;
            double rendered;
            try { rendered = await rendering.Task.WaitAsync(TimeSpan.FromSeconds(5)); }
            finally { CompositionTarget.Rendering -= handler; }
            samples.Add(FormattableString.Invariant(
                $"{sample},{returned:F3},{flushed:F3},{rendered:F3},{animated},{visual.Scale.X},{visual.Opacity}"));
            if (sample == 0) await CaptureAsync(window, directory, "list-first-rendering-frame", settle: false);
            await Task.Delay(220);
        }
        File.WriteAllLines(Path.Combine(directory, "list-opening-timing.csv"), samples);
        await CheckFixturesAsync(window, settings, directory, edge, chrome);
    }

    private static async Task CheckFixturesAsync(SelectorWindow window, TestSettings settings, string? directory,
        BitmapImage? edge, BitmapImage? chrome)
    {
        var root = (FrameworkElement)window.Content;
        var ring = (ItemsRepeater)root.FindName("BrowserList");
        var scroll = (ScrollViewer)root.FindName("BrowserScroll");
        var card = (Button)root.FindName("NamePill");
        var preference = settings.LoadSettings().AppSettings;
        var originals = settings.LoadSettings().Browsers.ToArray();
        var observations = new List<FrameObservation>();
        settings.UpdateAppSettings(new AppSettings { SelectorLayout = SelectorLayout.List });
        window.ViewModel.Url = FixtureUrl;
        try
        {
            foreach (int count in new[] { 0, 1, 2, 6, 30 })
            {
                settings.LoadSettings().Browsers.Clear();
                for (int index = 0; index < count; index++)
                    settings.LoadSettings().Browsers.Add(new Browser(index == 2 ? "Google Chrome" : "Microsoft Edge",
                        $"list-fixture-{index}.exe"));
                window.ViewModel.RefreshBrowsers();
                window.ShowWindow();
                for (int index = 0; index < count; index++)
                {
                    string[] suffixes = ["Work", "Personal", "Chrome", "North", "Central", "South", "Research👩‍💻", "Cafe\u0301"];
                    var browser = settings.LoadSettings().Browsers[index];
                    BrowserProfile? profile = index == 2 ? null : new($"Fixture {index}", index < 2 ? suffixes[index]
                        : "A very long custom profile " + suffixes[index % suffixes.Length] + (index >= 8 ? $" {index + 1}" : string.Empty), null);
                    var item = new BrowserItemViewModel(browser, profile) { Icon = index == 2 ? chrome : edge };
                    window.ViewModel.Browsers[index] = item;
                    if (ring.TryGetElement(index) is ListBrowserItem control) control.BrowserItem = item;
                }
                root.UpdateLayout();
                await Task.Delay(100);
                foreach (var theme in new[] { ElementTheme.Light, ElementTheme.Dark })
                {
                    root.RequestedTheme = theme;
                    root.UpdateLayout();
                    await Task.Delay(60);
                    string prefix = $"list-{count}-{theme.ToString().ToLowerInvariant()}";
                    if (count == 0)
                    {
                        var manage = (HyperlinkButton)root.FindName("ManageBrowsersButton");
                        Check(((SolidColorBrush)manage.FocusVisualPrimaryBrush).Color == ((SolidColorBrush)card.FocusVisualPrimaryBrush).Color
                            && ((SolidColorBrush)manage.Foreground).Color == ((SolidColorBrush)card.FocusVisualPrimaryBrush).Color
                            && manage.FocusVisualMargin.Equals(card.FocusVisualMargin)
                            && manage.FocusVisualPrimaryThickness.Equals(card.FocusVisualPrimaryThickness),
                            "recovery link and destination card share system-accent focus treatment");
                        manage.Focus(FocusState.Keyboard);
                        await RecordAsync(prefix + "-recovery-focus", "Empty-state Manage browsers recovery focus", manage);
                        card.Focus(FocusState.Keyboard);
                        await RecordAsync(prefix + "-empty", "Empty state and link options", card);
                        continue;
                    }
                    var first = (ListBrowserItem)ring.GetOrCreateElement(0);
                    first.FocusItem(FocusState.Keyboard);
                    await SmokeApplication.WaitForAsync(() => first.IsSelected && scroll.VerticalOffset < 1,
                        "List fixture reveals its first keyboard target");
                    await RecordAsync(prefix + "-selected-0", "First row keyboard focus", (Button)first.FindName("LaunchButton"));
                    CheckVisibleRows();
                    CheckListRounding(count);
                    first.FocusItem(FocusState.Pointer);
                    await RecordAsync(prefix + "-pointer-0", "Same first selection in pointer mode", (Button)first.FindName("LaunchButton"));
                    card.Focus(FocusState.Keyboard);
                    await RecordAsync(prefix + "-card-focus", "Link options focus retains browser selection", card);
                    first.FocusItem(FocusState.Keyboard);
                    if (count == 30)
                    {
                        Check(window.NavigateSelection(VirtualKey.Up), "production Up wraps to last row");
                        await SmokeApplication.WaitForAsync(() => ring.TryGetElement(29) is ListBrowserItem { IsSelected: true }
                            && scroll.VerticalOffset > 0, "long List reveals its final target");
                        var last = (ListBrowserItem)ring.TryGetElement(29);
                        var point = last.TransformToVisual(scroll).TransformPoint(default);
                        Check(point.Y >= -0.01 && point.Y + last.ActualHeight <= scroll.ViewportHeight + 0.01,
                            "last row is fully visible after production wrap");
                        await RecordAsync(prefix + "-selected-last", "Production Up wraps and fully reveals last row",
                            (Button)last.FindName("LaunchButton"));
                        CheckVisibleRows();
                        Check(window.NavigateSelection(VirtualKey.Down), "production Down wraps to first row");
                        await SmokeApplication.WaitForAsync(() => ring.TryGetElement(0) is ListBrowserItem { IsSelected: true }
                            && scroll.VerticalOffset < 1, "long List returns to the first target");
                    }

                    async Task RecordAsync(string name, string action, Control expected)
                    {
                        await Task.Delay(80);
                        Check(ReferenceEquals(FocusManager.GetFocusedElement(root.XamlRoot), expected),
                            "List trace records actual native focus");
                        var peer = FrameworkElementAutomationPeer.CreatePeerForElement(expected)
                            ?? throw new Exception("Native List button automation peer missing.");
                        var destination = new Uri(window.ViewModel.Url);
                        Check(((TextBlock)root.FindName("PillHost")).Text == destination.PathAndQuery + destination.Fragment,
                            "List address line shows the useful path without repeating the host");
                        Check(AutomationProperties.GetHelpText(card) == window.ViewModel.Url
                            && (ToolTipService.GetToolTip(card) as string) == window.ViewModel.Url,
                            "complete destination remains available to tooltips and UI Automation");
                        if (directory is null) return;
                        string selected = Enumerable.Range(0, count).Select(index => ring.TryGetElement(index))
                            .OfType<ListBrowserItem>().FirstOrDefault(item => item.IsSelected)?.BrowserItem?.Name ?? string.Empty;
                        await CaptureAsync(window, directory, name);
                        observations.Add(new(name + ".png", action, count, theme.ToString(), peer.GetName(),
                            expected.FocusState.ToString(), selected, scroll.VerticalOffset, scroll.ViewportHeight));
                    }
                }
                window.MinimizeWindow();
            }
            if (directory is not null)
                File.WriteAllText(Path.Combine(directory, "list-interaction-trace.json"),
                    JsonSerializer.Serialize(observations, new JsonSerializerOptions { WriteIndented = true }));
        }
        finally
        {
            settings.LoadSettings().Browsers.Clear();
            foreach (var browser in originals) settings.LoadSettings().Browsers.Add(browser);
            window.ViewModel.RefreshBrowsers();
            root.RequestedTheme = ElementTheme.Default;
            window.MinimizeWindow();
            settings.UpdateAppSettings(preference);
        }

        void CheckListRounding(int count)
        {
            double scale = root.XamlRoot.RasterizationScale;
            var first = (ListBrowserItem)ring.TryGetElement(0);
            Check(!root.UseLayoutRounding && ((FrameworkElement)root.FindName("ListHost")).UseLayoutRounding
                && card.UseLayoutRounding && ((Button)first.FindName("LaunchButton")).UseLayoutRounding,
                "List subtree and its footer restore layout rounding under the unrounded radial root");
            var rowParts = Enumerable.Range(0, count).Select(index => ring.TryGetElement(index)).OfType<ListBrowserItem>()
                .SelectMany(item => new[] { item, item.FindName("BrowserName"), item.FindName("ProfileLine"),
                    item.FindName("ShortcutBadge"), item.FindName("BrowserIcon") })
                .OfType<FrameworkElement>().Where(element => element.Visibility == Visibility.Visible);
            var cardParts = new[] { root.FindName("PillTitle"), root.FindName("PillHost") }.OfType<FrameworkElement>();
            foreach (var element in new FrameworkElement[] { scroll, card }.Concat(cardParts).Concat(rowParts))
            {
                var origin = element.TransformToVisual(root).TransformPoint(default);
                Check(IsDevicePixel(origin.X * scale) && IsDevicePixel(origin.Y * scale) && (element is TextBlock
                    || (IsDevicePixel(element.ActualWidth * scale) && IsDevicePixel(element.ActualHeight * scale))),
                    FormattableString.Invariant($"List element {element.Name}{element.GetType().Name} lands on device pixels ")
                    + FormattableString.Invariant($"(scale {scale}, origin {origin.X},{origin.Y}, size {element.ActualWidth}x{element.ActualHeight})"));
            }
            int rows = (int)Math.Round(ListMetrics.ViewportHeight(window.AppWindow.Size.Height, scale) / ListMetrics.RowHeight);
            int visible = Math.Min(count, rows);
            Check(Math.Abs(scroll.ViewportHeight * scale - Math.Round(visible * ListMetrics.RowHeight * scale)) <= 1,
                FormattableString.Invariant($"scroll viewport shows whole rows in device pixels (count {count}, ")
                + FormattableString.Invariant($"viewport {scroll.ViewportHeight}, rows {rows}, scale {scale})"));
            Check(count > rows || scroll.ScrollableHeight == 0,
                FormattableString.Invariant($"Lists that fit their window do not scroll (count {count}, scrollable {scroll.ScrollableHeight})"));
            Check(((TextBlock)first.FindName("ProfileName")).TextTrimming == TextTrimming.CharacterEllipsis,
                "profile text keeps a native character-ellipsis fallback");

            static bool IsDevicePixel(double value) => Math.Abs(value - Math.Round(value)) < 0.02;
        }

        void CheckVisibleRows()
        {
            double? primaryTop = null;
            foreach (int index in Enumerable.Range(0, window.ViewModel.Browsers.Count))
            {
                if (ring.TryGetElement(index) is not ListBrowserItem item) continue;
                var rowPoint = item.TransformToVisual(scroll).TransformPoint(default);
                if (rowPoint.Y + item.ActualHeight <= 0 || rowPoint.Y >= scroll.ViewportHeight) continue;
                var button = (Button)item.FindName("LaunchButton");
                Check(((SolidColorBrush)button.FocusVisualPrimaryBrush).Color == ((SolidColorBrush)card.FocusVisualPrimaryBrush).Color,
                    "browser rows and destination card share the current system-accent focus color");
                var browserName = (TextBlock)item.FindName("BrowserName");
                double nameTop = browserName.TransformToVisual(item).TransformPoint(default).Y;
                if (primaryTop is { } referenceTop)
                    Check(Math.Abs(nameTop - referenceTop) <= 0.6,
                        "profiled and unprofiled browser labels keep the same first-line rhythm");
                else primaryTop = nameTop;
                var boundary = (Border)item.FindName("RowBoundary");
                Check(boundary.BorderThickness.Bottom >= 1 && boundary.ActualWidth >= item.ActualWidth - 16.01,
                    "unselected rows retain a visible persistent extent separator");
                Check(browserName.Text == item.BrowserItem!.Model.Name,
                    "browser identity has its own complete primary line");
                if (item.BrowserItem.Profile is { } profile)
                {
                    var profileName = (TextBlock)item.FindName("ProfileName");
                    string suffix = profile.Name[(profile.Name.LastIndexOf(' ') + 1)..];
                    Check(profileName.Text.EndsWith(suffix, StringComparison.Ordinal),
                        "visible profile text retains the distinguishing suffix");
                    var avatar = (FrameworkElement)item.FindName("ProfileBadge");
                    var icon = (FrameworkElement)item.FindName("BrowserIcon");
                    var avatarPoint = avatar.TransformToVisual(item).TransformPoint(default);
                    var iconPoint = icon.TransformToVisual(item).TransformPoint(default);
                    Check(avatarPoint.X >= iconPoint.X + icon.ActualWidth,
                        "profile avatars do not cover browser artwork");
                }
                Check(rowPoint.X + item.ActualWidth <= scroll.ActualWidth - 12,
                    "row focus and shortcut bounds leave a separate scrollbar gutter");
                double scale = root.XamlRoot.RasterizationScale;
                double wholeRows = Math.Round(scroll.ViewportHeight / ListMetrics.RowHeight);
                Check(Math.Abs(scroll.ViewportHeight * scale - Math.Round(wholeRows * ListMetrics.RowHeight * scale)) <= 1,
                    "the scroll viewport contains whole rows without boundary slivers");
                var peer = FrameworkElementAutomationPeer.CreatePeerForElement(button);
                Check(peer?.GetName() == item.BrowserItem!.Name
                    && peer.GetAutomationControlType() == AutomationControlType.Button,
                    "List UI Automation preserves complete browser/profile identity and button role");
                Check((ToolTipService.GetToolTip(button) as string)?.StartsWith(item.BrowserItem.Name) == true,
                    "List tooltips preserve complete launch identity");
            }
        }
    }

    private static async Task CaptureAsync(SelectorWindow window, string directory, string name, bool settle = true)
    {
        if (settle)
        {
            window.AppWindow.Move(new PointInt32(160, 160));
            await Task.Delay(100);
        }
        Check(DwmFlush() >= 0, "List capture compositor flush succeeds");
        var root = (FrameworkElement)window.Content;
        var bitmap = new RenderTargetBitmap();
        await bitmap.RenderAsync(root);
        int width = Math.Min(bitmap.PixelWidth, (int)Math.Ceiling(root.ActualWidth * root.XamlRoot.RasterizationScale));
        int height = Math.Min(bitmap.PixelHeight, (int)Math.Ceiling(root.ActualHeight * root.XamlRoot.RasterizationScale));
        byte[] source = (await bitmap.GetPixelsAsync()).ToArray();
        byte[] viewport = new byte[width * height * 4];
        for (int row = 0; row < height; row++)
            Buffer.BlockCopy(source, row * bitmap.PixelWidth * 4, viewport, row * width * 4, width * 4);
        File.AppendAllText(Path.Combine(directory, "capture-bounds.csv"), FormattableString.Invariant(
            $"{name},{root.ActualWidth:F3},{root.ActualHeight:F3},{bitmap.PixelWidth},{bitmap.PixelHeight},{width},{height}\n"));
        using var file = File.Create(Path.Combine(directory, name + ".png"));
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, file.AsRandomAccessStream());
        encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied,
            (uint)width, (uint)height, 96, 96, viewport);
        await encoder.FlushAsync();
    }

    private static void Check(bool condition, string name)
    {
        if (!condition) throw new Exception("List polish check failed: " + name);
    }
}
