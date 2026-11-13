using Microsoft.Graphics.Canvas.Geometry;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics.Imaging;
using System.Runtime.InteropServices.WindowsRuntime;
using Quiver.App.Controls;
using Quiver.App.Services;
using Quiver.App.ViewModels;
using Quiver.App.Windows;
using Quiver.Library;
using Quiver.Library.Models;
using System.Diagnostics;
using Windows.UI;
using System.Numerics;
using System.Runtime.InteropServices;
using Windows.Graphics;
using Microsoft.UI.Xaml.Input;
using System.Text.Json;
using Windows.System;

namespace Quiver.SelectorSmoke;

internal static partial class RadialPolishChecks
{
    [LibraryImport("dwmapi.dll")]
    private static partial int DwmFlush();

    public static async Task RunAsync(SelectorWindow window, TestSettings settings)
    {
        string? directory = Environment.GetEnvironmentVariable("QUIVER_SELECTOR_CAPTURE_DIRECTORY");
        var root = (FrameworkElement)window.Content;
        settings.UpdateAppSettings(new AppSettings { SelectorLayout = SelectorLayout.Radial });
        var loader = new IconLoaderService(Path.Combine(AppContext.BaseDirectory, "polish-icon-cache"));
        Microsoft.UI.Xaml.Media.Imaging.BitmapImage? edgeIcon;
        Microsoft.UI.Xaml.Media.Imaging.BitmapImage? chromeIcon;
        try
        {
            edgeIcon = await loader.LoadIconFromExe(Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Microsoft", "Edge", "Application", "msedge.exe"));
            chromeIcon = await loader.LoadIconFromExe(Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Google", "Chrome", "Application", "chrome.exe"));
        }
        finally { await loader.StopAsync(); }
        if (directory is not null)
            Check(edgeIcon is not null && chromeIcon is not null, "installed browser icons decode for native visual fixtures");
        window.ViewModel.Browsers[0].Icon = edgeIcon;
        window.ViewModel.Browsers[1].Icon = chromeIcon;
        if (directory is not null)
        {
            Directory.CreateDirectory(directory);
            var samples = new List<string> { "sample,showReturnMs,dwmFlushMs,firstRenderingMs,animated,scale,opacity" };
            for (int sample = 0; sample < 12; sample++)
            {
                window.MinimizeWindow();
                await Task.Delay(50);
                var clock = Stopwatch.StartNew();
                var firstRendering = new TaskCompletionSource<double>(TaskCreationOptions.RunContinuationsAsynchronously);
                EventHandler<object>? rendered = null;
                rendered = (_, _) =>
                {
                    CompositionTarget.Rendering -= rendered;
                    firstRendering.TrySetResult(clock.Elapsed.TotalMilliseconds);
                };
                CompositionTarget.Rendering += rendered;
                try
                {
                    window.ShowWindow();
                    double returned = clock.Elapsed.TotalMilliseconds;
                    CheckImmediatePresentation(window, root);
                    var visual = ElementCompositionPreview.GetElementVisual((FrameworkElement)root.FindName("FlowerHost"));
                    bool animated = visual.TryGetAnimationController("Opacity") is not null
                        || visual.TryGetAnimationController("Scale") is not null;
                    var scale = visual.Scale;
                    float opacity = visual.Opacity;
                    Check(DwmFlush() >= 0, "desktop compositor flush succeeds");
                    double flushed = clock.Elapsed.TotalMilliseconds;
                    double renderedAt = await firstRendering.Task.WaitAsync(TimeSpan.FromSeconds(5));
                    samples.Add(FormattableString.Invariant($"{sample},{returned:F3},{flushed:F3},{renderedAt:F3},{animated},{scale.X},{opacity}"));
                    if (sample == 0)
                        await CaptureAsync(window, directory, "first-rendering-frame", settle: false);
                    await Task.Delay(220);
                }
                finally { CompositionTarget.Rendering -= rendered; }
            }
            File.WriteAllLines(Path.Combine(directory, "radial-opening-timing.csv"), samples);
        }

        if (Environment.GetEnvironmentVariable("QUIVER_SELECTOR_SOLVE_SPACING") == "1")
        {
            window.ShowWindow();
            CheckImmediatePresentation(window, root);
            if (directory is null) window.AppWindow.Move(new PointInt32(-32000, -32000));
            SolveSpacing((ItemsRepeater)root.FindName("BrowserRing"));
            return;
        }

        var originals = settings.LoadSettings().Browsers.ToArray();
        try
        {
            foreach (int count in Enumerable.Range(2, 9))
            {
                settings.LoadSettings().Browsers.Clear();
                for (int i = 0; i < count; i++)
                    settings.LoadSettings().Browsers.Add(new Browser(i == 2 ? "Google Chrome" : $"Browser {i + 1}", $"fixture-{i}.exe"));
                window.ViewModel.RefreshBrowsers();
                window.ShowWindow();
                CheckImmediatePresentation(window, root);
                if (count == 2) await CheckDeferredArrangementIsRejectedAsync(window, root);
                if (directory is null) window.AppWindow.Move(new PointInt32(-32000, -32000));
                var ring = (ItemsRepeater)root.FindName("BrowserRing");
                for (int i = 0; i < count; i++)
                {
                    var browser = settings.LoadSettings().Browsers[i];
                    string[] suffixes = ["Work", "Personal", "Chrome", "North", "Central", "South", "East", "West", "Research👩‍💻", "Cafe\u0301"];
                    BrowserProfile? profile = i == 2 ? null : new($"Fixture {i}",
                        i < 2 ? suffixes[i] : "A very long custom profile " + suffixes[i], null);
                    var item = new BrowserItemViewModel(browser, profile)
                    {
                        Icon = i == 2 ? chromeIcon : edgeIcon
                    };
                    window.ViewModel.Browsers[i] = item;
                    ((RadialBrowserItem)ring.GetOrCreateElement(i)).BrowserItem = item;
                }
                root.UpdateLayout();
                foreach (var theme in new[] { ElementTheme.Light, ElementTheme.Dark })
                {
                    root.RequestedTheme = theme;
                    foreach (int selected in Enumerable.Range(0, count))
                    {
                        var focused = (RadialBrowserItem)ring.GetOrCreateElement(selected);
                        focused.FocusItem(FocusState.Keyboard);
                        await Task.Delay(60);
                        await SmokeApplication.WaitForAsync(() => focused.IsSelected
                            && ReferenceEquals(FocusManager.GetFocusedElement(root.XamlRoot), focused.FindName("LaunchButton")),
                            $"radial focus and selection converge (count={count}, selected={selected}, theme={theme})");
                        root.UpdateLayout();
                        CheckLabelInsets(ring, root, count);
                        CheckCompactPlacement(root, ring, count);
                        CheckNeighborClearance(ring, root, count, selected);
                        CheckAffordanceContrast(root);
                        await CheckAntialiasingAsync((FrameworkElement)root.FindName("SelectorPlate"));
                        if (directory is not null && (count is 2 or 3 or 6 || selected == 0))
                            await CaptureAsync(window, directory, $"radial-{count}-{theme.ToString().ToLowerInvariant()}-selected-{selected}");
                    }
                }
                if (count == 3 && directory is not null)
                    foreach (var theme in new[] { ElementTheme.Light, ElementTheme.Dark })
                        await CaptureInteractionAsync(window, root, ring, directory, theme);
                window.MinimizeWindow();
            }
        }
        finally
        {
            settings.LoadSettings().Browsers.Clear();
            foreach (var browser in originals) settings.LoadSettings().Browsers.Add(browser);
            window.ViewModel.RefreshBrowsers();
            root.RequestedTheme = ElementTheme.Default;
            window.MinimizeWindow();
        }
    }

    private static void CheckImmediatePresentation(SelectorWindow window, FrameworkElement root)
    {
        var visual = ElementCompositionPreview.GetElementVisual((FrameworkElement)root.FindName("FlowerHost"));
        Check(visual.TryGetAnimationController("Opacity") is null
            && visual.TryGetAnimationController("Scale") is null
            && visual.Opacity == 1 && visual.Scale == Vector3.One,
            "radial choices are fully opaque and unscaled without an entrance delay");
        var ring = (ItemsRepeater)root.FindName("BrowserRing");
        Check(ReferenceEquals(ring.ItemsSource, window.ViewModel.Browsers),
            "radial opening returns with bound choices");
        for (int index = 0; index < window.ViewModel.Browsers.Count; index++)
            Check(ring.TryGetElement(index) is RadialBrowserItem { ActualWidth: 104, ActualHeight: 104 },
                "radial opening returns with arranged choices");
    }

    private static async Task CheckDeferredArrangementIsRejectedAsync(SelectorWindow window, FrameworkElement root)
    {
        var ring = (ItemsRepeater)root.FindName("BrowserRing");
        var first = (RadialBrowserItem)ring.TryGetElement(0);
        double height = first.Height;
        var restored = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        first.Height = 0;
        root.UpdateLayout();
        bool rejected = false;
        try
        {
            Check(root.DispatcherQueue.TryEnqueue(() =>
            {
                first.Height = height;
                root.UpdateLayout();
                restored.TrySetResult();
            }), "deferred arrangement restoration is queued");
            try { CheckImmediatePresentation(window, root); }
            catch (Exception error) when (error.Message == "Radial polish check failed: radial opening returns with arranged choices")
            {
                rejected = true;
            }
            Check(rejected, "immediate assertion rejects choices whose arrangement is deferred to the dispatcher");
            await restored.Task.WaitAsync(TimeSpan.FromSeconds(5));
            CheckImmediatePresentation(window, root);
        }
        finally
        {
            first.Height = height;
            root.UpdateLayout();
        }
    }

    private sealed record InteractionStep(string Action, string SelectedBrowser, string FocusedButton, string FocusState, double BrowserStrokeDip, double KeyboardIndicatorOpacity, string Image);

    private static async Task CaptureInteractionAsync(SelectorWindow window, FrameworkElement root, ItemsRepeater ring, string directory, ElementTheme theme)
    {
        root.RequestedTheme = theme;
        string prefix = "interaction-" + theme.ToString().ToLowerInvariant();
        var steps = new List<InteractionStep>();
        var card = (Button)root.FindName("NamePill");
        var first = (RadialBrowserItem)ring.GetOrCreateElement(0);
        first.FocusItem(FocusState.Keyboard);
        await RecordAsync("Focus browser 1", 0, (Button)first.FindName("LaunchButton"));
        for (int step = 1; step <= 3; step++)
        {
            Check(window.NavigateSelection(VirtualKey.Down), "production Down navigation handles the key");
            var target = (RadialBrowserItem)ring.GetOrCreateElement(step % 3);
            await RecordAsync("Production Down navigation" + (step == 3 ? " wraps" : string.Empty), step % 3,
                (Button)target.FindName("LaunchButton"));
        }
        card.Focus(FocusState.Keyboard);
        await RecordAsync("Focus link options without replacing browser selection", 0, card);
        first.FocusItem(FocusState.Keyboard);
        await RecordAsync("Return to browser restores its focus outline", 0, (Button)first.FindName("LaunchButton"));
        first.FocusItem(FocusState.Pointer);
        await RecordAsync("Same browser selection in pointer mode has no keyboard ink", 0, (Button)first.FindName("LaunchButton"));
        File.WriteAllText(Path.Combine(directory, prefix + "-trace.json"),
            JsonSerializer.Serialize(steps, new JsonSerializerOptions { WriteIndented = true }));

        async Task RecordAsync(string action, int selected, Button expected)
        {
            await Task.Delay(100);
            Check(ReferenceEquals(FocusManager.GetFocusedElement(root.XamlRoot), expected),
                "interaction trace records actual native focus, not a requested target");
            var item = (RadialBrowserItem)ring.GetOrCreateElement(selected);
            Check(item.IsSelected, "interaction trace records the matching production selection");
            var peer = FrameworkElementAutomationPeer.CreatePeerForElement(expected)
                ?? throw new Exception("Native button automation peer missing in interaction trace");
            var outline = (Microsoft.UI.Xaml.Shapes.Path)root.FindName("SelectionOutline");
            Check(outline.StrokeThickness == (ReferenceEquals(expected, card) ? 0 : 2),
                "only browser focus owns the browser outline; selection fill persists when link options has focus");
            var keyboardRing = (Border)item.FindName("KeyboardFocusIndicator");
            Check(keyboardRing.Opacity == (ReferenceEquals(expected, card) || expected.FocusState != FocusState.Keyboard ? 0 : 1),
                "keyboard focus has a label underline, absent for pointer mode or link-card focus");
            string image = $"{prefix}-{steps.Count}.png";
            await CaptureAsync(window, directory, Path.GetFileNameWithoutExtension(image));
            steps.Add(new(action, item.BrowserItem!.Name, peer.GetName(), expected.FocusState.ToString(), outline.StrokeThickness, keyboardRing.Opacity, image));
        }
    }

    private static void CheckLabelInsets(ItemsRepeater ring, FrameworkElement root, int count)
    {
        for (int i = 0; i < count; i++)
        {
            var item = (RadialBrowserItem)ring.GetOrCreateElement(i);
            var label = (TextBlock)item.FindName("PetalName");
            var p = label.TransformToVisual(item).TransformPoint(default);
            double radius = item.ActualWidth / 2;
            foreach (double x in new[] { p.X, p.X + label.ActualWidth })
                foreach (double y in new[] { p.Y, p.Y + label.ActualHeight })
                    Check(Math.Sqrt(Math.Pow(x - radius, 2) + Math.Pow(y - radius, 2)) <= radius - 6,
                        $"browser label has six DIP circular edge clearance ({label.Text}, {label.ActualWidth}x{label.ActualHeight}, y={p.Y})");
            Check(label.FontSize >= 10, "edge clearance does not shrink established label type");
            string fullName = item.BrowserItem!.ShortName;
            if (fullName == "Google Chrome")
                Check(label.Text == fullName && !label.IsTextTrimmed, "Google Chrome remains fully readable");
            if (fullName.StartsWith("A very long"))
            {
                string suffix = fullName[(fullName.LastIndexOf(' ') + 1)..];
                Check(label.Text.Contains('\u2026') && label.Text.EndsWith(suffix, StringComparison.Ordinal)
                    && !label.Text.Contains(" \u2026") && !label.Text.Contains("\u2026 ")
                    && label.Text.EnumerateRunes().All(rune => rune.Value != 0xFFFD),
                    "middle-trimmed names retain complete distinguishing suffix words and Unicode text elements");
            }
            var indicator = (FrameworkElement)item.FindName("KeyboardFocusIndicator");
            var indicatorPosition = indicator.TransformToVisual(item).TransformPoint(default);
            Check(indicatorPosition.Y >= p.Y + label.ActualHeight + 2,
                "keyboard underline stays below the label, not around the icon");
            CheckBadges(item, root);
            var target = (Button)item.FindName("LaunchButton");
            Check((ToolTipService.GetToolTip(target) as string)?.StartsWith(item.BrowserItem!.Name) == true,
                "full browser and profile names remain discoverable when labels are trimmed");
            var peer = FrameworkElementAutomationPeer.CreatePeerForElement(target);
            Check(peer is not null && peer.GetName() == item.BrowserItem.Name
                && peer.GetAutomationControlType() == AutomationControlType.Button,
                "native UI Automation exposes each full browser/profile name as a button");
        }
    }

    private static void CheckBadges(RadialBrowserItem item, FrameworkElement rootElement)
    {
        var tile = (Grid)item.FindName("IconSlot");
        Check(item.FindName("IconSurface") is null && tile.Background is null,
            "radial artwork has no icon container fill or border");
        var keyboardRing = (Border)item.FindName("KeyboardFocusIndicator");
        var button = (Button)item.FindName("LaunchButton");
        Check(keyboardRing.Opacity == (button.FocusState == FocusState.Keyboard ? 1 : 0),
            "keyboard label underline follows actual focus state, not browser selection");
        var selection = (Microsoft.UI.Xaml.Shapes.Path)rootElement.FindName("SelectionOutline");
        Check(Contrast(((SolidColorBrush)keyboardRing.Background).Color, ((SolidColorBrush)selection.Fill).Color) >= 3,
            "keyboard label underline has at least 3:1 contrast against the selected petal");
        var artwork = (FrameworkElement)item.FindName("BrowserIcon");
        Check(artwork.ActualWidth == 40 && artwork.ActualHeight == 40,
            "bare browser logos use the enlarged 40 DIP artwork size");
        Check(ReferenceEquals(VisualTreeHelper.GetParent(artwork), tile),
            "browser artwork is drawn directly in the unpainted layout slot");
        var artworkPosition = artwork.TransformToVisual(tile).TransformPoint(default);
        var root = artwork.XamlRoot;
        var devicePosition = artwork.TransformToVisual(root.Content).TransformPoint(default);
        double scale = root.RasterizationScale;
        Check(Math.Abs(devicePosition.X * scale - Math.Round(devicePosition.X * scale)) < 0.01
            && Math.Abs(devicePosition.Y * scale - Math.Round(devicePosition.Y * scale)) < 0.01,
            "bitmap artwork lands on device pixels without rounding the vector plate");
        Check(Math.Abs(artworkPosition.X - artwork.Margin.Left) * scale <= 0.501
            && Math.Abs(artworkPosition.Y - artwork.Margin.Top) * scale <= 0.501,
            "pixel alignment never drifts beyond half a device pixel from the natural artwork placement");
        var hub = (FrameworkElement)rootElement.FindName("HubButton");
        var hubPosition = hub.TransformToVisual(tile).TransformPoint(default);
        using var hubShape = CanvasGeometry.CreateCircle(null,
            new Vector2((float)(hubPosition.X + hub.ActualWidth / 2), (float)(hubPosition.Y + hub.ActualHeight / 2)),
            (float)hub.ActualWidth / 2);
        using var interior = CanvasGeometry.CreateRectangle(null,
            new Windows.Foundation.Rect(1, 1, tile.ActualWidth - 2, tile.ActualHeight - 2));
        foreach (string name in new[] { "ShortcutBadge", "ProfileBadge" })
        {
            var badge = (FrameworkElement)item.FindName(name);
            if (badge.Visibility == Visibility.Collapsed) continue;
            var point = badge.TransformToVisual(tile).TransformPoint(default);
            using var shape = badge is Border border
                ? CanvasGeometry.CreateRoundedRectangle(null, new Windows.Foundation.Rect(point.X, point.Y, badge.ActualWidth, badge.ActualHeight),
                    (float)border.CornerRadius.TopLeft, (float)border.CornerRadius.TopLeft)
                : CanvasGeometry.CreateCircle(null, new Vector2((float)(point.X + badge.ActualWidth / 2),
                    (float)(point.Y + badge.ActualHeight / 2)), (float)badge.ActualWidth / 2);
            using var outside = shape.CombineWith(interior, Matrix3x2.Identity, CanvasGeometryCombine.Exclude);
            Check(outside.ComputeArea() < 0.01, "badges stay inside their unpainted layout slot");
            double overlapX = Math.Max(0, Math.Min(point.X + badge.ActualWidth, artworkPosition.X + artwork.ActualWidth)
                - Math.Max(point.X, artworkPosition.X));
            double overlapY = Math.Max(0, Math.Min(point.Y + badge.ActualHeight, artworkPosition.Y + artwork.ActualHeight)
                - Math.Max(point.Y, artworkPosition.Y));
            if (name == "ProfileBadge")
                Check(overlapX * overlapY > 0 && point.X < artworkPosition.X
                    && point.Y > artworkPosition.Y + artwork.ActualHeight / 2
                    && point.Y + badge.ActualHeight > artworkPosition.Y + artwork.ActualHeight,
                    "profile avatars overlay the lower-left edge of browser artwork, including unnumbered profiles");
            else
            {
                Check(overlapX * overlapY < 0.01, "number keycaps stay beside enlarged browser artwork");
                var flower = (FrameworkElement)rootElement.FindName("FlowerHost");
                double direction = item.TransformToVisual(flower).TransformPoint(default).X
                    + item.ActualWidth / 2 - flower.ActualWidth / 2;
                double badgeCenter = badge.TransformToVisual(item).TransformPoint(default).X + badge.ActualWidth / 2;
                if (Math.Abs(direction) > 0.01)
                    Check(Math.Sign(badgeCenter - item.ActualWidth / 2) == -Math.Sign(direction),
                        "hotkeys sit on each petal's center-facing side");
            }
            using var hubOverlap = shape.CombineWith(hubShape, Matrix3x2.Identity, CanvasGeometryCombine.Intersect);
            Check(hubOverlap.ComputeArea() < 0.01, "inward-facing badges leave the hub button clear");
        }
        var keycap = (Border)item.FindName("ShortcutBadge");
        var digit = (TextBlock)item.FindName("ShortcutText");
        Check(Contrast(((SolidColorBrush)digit.Foreground).Color, ((SolidColorBrush)keycap.Background).Color) >= 4.5,
            "shortcut digits meet 4.5:1 contrast in selected and unselected light/dark states");
    }

    private static void CheckCompactPlacement(FrameworkElement root, ItemsRepeater ring, int count)
    {
        double bottom = double.NegativeInfinity;
        for (int index = 0; index < count; index++)
        {
            var item = (FrameworkElement)ring.GetOrCreateElement(index);
            var position = item.TransformToVisual(root).TransformPoint(default);
            bottom = Math.Max(bottom, position.Y + item.ActualHeight);
        }
        var card = (FrameworkElement)root.FindName("NamePill");
        Check(Math.Abs(card.TransformToVisual(root).TransformPoint(default).Y - bottom - RadialMetrics.PillGap) < 0.01,
            "link card keeps a fixed gap below actual petal bounds, including every odd count");
        var flower = (FrameworkElement)root.FindName("FlowerHost");
        var hub = (FrameworkElement)root.FindName("HubButton");
        var hubPosition = hub.TransformToVisual(flower).TransformPoint(default);
        var origin = RadialMetrics.For(count).FlowerOrigin;
        Check(Math.Abs(hubPosition.Y + hub.ActualHeight / 2 - origin.Y) < 0.01,
            "tightly bounded odd flowers keep the hub on the shared radial center");
    }

    private static void CheckAffordanceContrast(FrameworkElement root)
    {
        var outline = (Microsoft.UI.Xaml.Shapes.Path)root.FindName("SelectionOutline");
        Check(Contrast(((SolidColorBrush)outline.Stroke).Color, ((SolidColorBrush)outline.Fill).Color) >= 3,
            "softened selection ink retains at least 3:1 contrast against its selection surface");
        var chevron = (FontIcon)root.FindName("PillChevron");
        var plate = (Microsoft.UI.Xaml.Shapes.Path)root.FindName("SelectorPlate");
        var background = plate.Fill is AcrylicBrush acrylic ? acrylic.FallbackColor : ((SolidColorBrush)plate.Fill).Color;
        Check(chevron.FontSize >= 12 && Contrast(((SolidColorBrush)chevron.Foreground).Color, background) >= 3,
            "link disclosure affordance meets 3:1 contrast against the native fallback palette");
    }

    private static double Contrast(Color foreground, Color background)
    {
        double opacity = foreground.A / 255d;
        double foregroundLuminance = Luminance(
            foreground.R * opacity + background.R * (1 - opacity),
            foreground.G * opacity + background.G * (1 - opacity),
            foreground.B * opacity + background.B * (1 - opacity));
        double backgroundLuminance = Luminance(background.R, background.G, background.B);
        return (Math.Max(foregroundLuminance, backgroundLuminance) + 0.05)
            / (Math.Min(foregroundLuminance, backgroundLuminance) + 0.05);

        static double Luminance(double red, double green, double blue) =>
            0.2126 * Linear(red) + 0.7152 * Linear(green) + 0.0722 * Linear(blue);
        static double Linear(double channel)
        {
            double value = channel / 255;
            return value <= 0.04045 ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4);
        }
    }

    private static void SolveSpacing(ItemsRepeater ring)
    {
        var item = (RadialBrowserItem)ring.GetOrCreateElement(0);
        var rectangles = new List<Windows.Foundation.Rect>();
        foreach (string name in new[] { "PetalName", "IconSlot" })
        {
            var element = (FrameworkElement)item.FindName(name);
            var point = element.TransformToVisual(item).TransformPoint(default);
            double width = element is TextBlock label ? label.MaxWidth : element.ActualWidth;
            rectangles.Add(new Windows.Foundation.Rect(-width / 2, point.Y - item.ActualHeight / 2, width, element.ActualHeight));
        }
        var output = new List<string>
        {
            "Premise under test: one fixed center-distance allowance clears neighboring content at every selection angle.",
            "Uses actual arranged icon height and maximum label width. Four DIP gap includes the one-DIP focus stroke."
        };
        for (int count = 3; count <= 10; count++)
        {
            for (double radius = 64; radius <= 256; radius += 0.25)
            {
                var metrics = new RadialMetrics(count, radius, radius + RadialMetrics.PetalRadius);
                bool fits = true;
                for (int selected = 0; selected < count && fits; selected++)
                {
                    using var selection = SelectorGeometryChecks.CreateSelection(metrics, selected, 1);
                    using var clearance = selection.Stroke(10);
                    using var reserved = selection.CombineWith(clearance, Matrix3x2.Identity, CanvasGeometryCombine.Union);
                    for (int neighbor = 0; neighbor < count && fits; neighbor++)
                    {
                        if (neighbor == selected) continue;
                        var center = metrics.ItemCenter(neighbor, metrics.FlowerCenter);
                        foreach (var rectangle in rectangles)
                        {
                            using var content = CanvasGeometry.CreateRectangle(null, new Windows.Foundation.Rect(
                                center.X + rectangle.X, center.Y + rectangle.Y, rectangle.Width, rectangle.Height));
                            using var overlap = content.CombineWith(reserved, Matrix3x2.Identity, CanvasGeometryCombine.Intersect);
                            if (overlap.ComputeArea() > 0.01) { fits = false; break; }
                        }
                    }
                }
                if (!fits) continue;
                output.Add(FormattableString.Invariant($"count={count}, requiredRadius={radius:F2}, halfNeighborDistance={radius * Math.Sin(Math.PI / count):F2}"));
                break;
            }
        }
        File.WriteAllLines(Path.Combine(AppContext.BaseDirectory, "radial-spacing-solution.txt"), output);
    }

    private static void CheckNeighborClearance(ItemsRepeater ring, FrameworkElement root, int count, int selected)
    {
        var flower = (FrameworkElement)root.FindName("FlowerHost");
        var offset = flower.TransformToVisual(root).TransformPoint(default);
        using var selection = SelectorGeometryChecks.CreateSelection(RadialMetrics.For(count), selected, 1, offset);
        using var clearance = selection.Stroke(10);
        using var reserved = selection.CombineWith(clearance, Matrix3x2.Identity, CanvasGeometryCombine.Union);
        for (int index = 0; index < count; index++)
        {
            if (index == selected) continue;
            var item = (RadialBrowserItem)ring.GetOrCreateElement(index);
            foreach (string name in new[] { "PetalName", "BrowserIcon", "ShortcutBadge", "ProfileBadge" })
            {
                var element = (FrameworkElement)item.FindName(name);
                if (element.Visibility == Visibility.Collapsed) continue;
                var point = element.TransformToVisual(root).TransformPoint(default);
                var bounds = new Windows.Foundation.Rect(point.X, point.Y, element.ActualWidth, element.ActualHeight);
                using var content = element is Border border
                    ? CanvasGeometry.CreateRoundedRectangle(null, bounds, (float)border.CornerRadius.TopLeft, (float)border.CornerRadius.TopLeft)
                    : name == "ProfileBadge"
                        ? CanvasGeometry.CreateCircle(null, new Vector2((float)(bounds.X + bounds.Width / 2),
                            (float)(bounds.Y + bounds.Height / 2)), (float)bounds.Width / 2)
                        : CanvasGeometry.CreateRectangle(null, bounds);
                using var overlap = content.CombineWith(reserved, Matrix3x2.Identity, CanvasGeometryCombine.Intersect);
                Check(overlap.ComputeArea() < 0.01,
                    $"selection leaves four DIP clearance to every neighboring {name} ({count} browsers, selected {selected}, neighbor {index}, "
                    + $"overlap area {overlap.ComputeArea():F3}, content {point.X:F2},{point.Y:F2} {element.ActualWidth:F2}x{element.ActualHeight:F2}, flower {offset.X:F2},{offset.Y:F2})");
            }
        }
    }

    private static async Task CheckAntialiasingAsync(FrameworkElement plate)
    {
        var bitmap = new RenderTargetBitmap();
        await bitmap.RenderAsync(plate);
        byte[] pixels = (await bitmap.GetPixelsAsync()).ToArray();
        int partialEdgePixels = 0;
        int width = bitmap.PixelWidth;
        for (int y = 1; y < bitmap.PixelHeight - 1; y++)
            for (int x = 1; x < width - 1; x++)
            {
                int alpha = (y * width + x) * 4 + 3;
                if (pixels[alpha] is > 0 and < 255
                    && (pixels[alpha - 4] == 0 || pixels[alpha + 4] == 0
                        || pixels[alpha - width * 4] == 0 || pixels[alpha + width * 4] == 0))
                    partialEdgePixels++;
            }
        Check(partialEdgePixels > 20, "rendered plate has antialiased boundary coverage, not a binary jagged mask");
    }

    private static async Task CaptureAsync(SelectorWindow window, string directory, string name, bool settle = true)
    {
        if (settle)
        {
            window.AppWindow.Move(new PointInt32(160, 160));
            await Task.Delay(120);
        }
        Check(DwmFlush() >= 0, "capture waits for the desktop compositor");
        var bitmap = new RenderTargetBitmap();
        await bitmap.RenderAsync((FrameworkElement)window.Content);
        var pixels = await bitmap.GetPixelsAsync();
        using var file = File.Create(Path.Combine(directory, name + ".png"));
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, file.AsRandomAccessStream());
        encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied,
            (uint)bitmap.PixelWidth, (uint)bitmap.PixelHeight, 96, 96, pixels.ToArray());
        await encoder.FlushAsync();
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception("Radial polish check failed: " + message);
    }
}
