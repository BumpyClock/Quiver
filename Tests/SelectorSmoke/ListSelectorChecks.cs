using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using Quiver.App.Controls;
using Quiver.App.Helpers;
using Quiver.App.Windows;
using Quiver.Library.Models;
using System.Drawing;
using System.Drawing.Imaging;
using Windows.Graphics;
using Windows.System;

namespace Quiver.SelectorSmoke;

internal static class ListSelectorChecks
{
    public static async Task RunAsync(SelectorWindow window, TestSettings settings)
    {
        CheckPlacement();
        window.MinimizeWindow();
        var root = (FrameworkElement)window.Content;
        settings.UpdateAppSettings(new AppSettings { SelectorLayout = SelectorLayout.List });
        await Task.Delay(50);
        window.ShowWindow();
        var openingVisual = ElementCompositionPreview.GetElementVisual((FrameworkElement)root.FindName("ListHost"));
        bool openingAnimation = openingVisual.TryGetAnimationController("Opacity") is not null
            || openingVisual.TryGetAnimationController("Scale") is not null;
        await CaptureOpeningAsync(window);
        Check(!openingAnimation, "list is immediately opaque and does not restart the radial entrance animation");
        Check(((FrameworkElement)root.FindName("ListHost")).Visibility == Visibility.Visible,
            "list background shares the rows' XAML presentation instead of a separately animated plate");
        window.AppWindow.Move(new PointInt32(-32000, -32000));
        root.UpdateLayout();
        var ring = (ItemsRepeater)root.FindName("BrowserList");
        var scroll = (ScrollViewer)root.FindName("BrowserScroll");
        var first = (ListBrowserItem)ring.GetOrCreateElement(0);
        var second = (ListBrowserItem)ring.GetOrCreateElement(1);
        root.UpdateLayout();
        Check(ring.Layout is StackLayout, "settings switch the live picker to a list");
        Check(((FrameworkElement)root.FindName("FlowerHost")).Visibility == Visibility.Collapsed,
            "list hides the radial host and selection ink");
        CheckHostFitsWindow(window);
        Check(scroll.ScrollableHeight == 0, "two browser rows fit without a scrollbar");
        var p1 = first.TransformToVisual(ring).TransformPoint(default);
        var p2 = second.TransformToVisual(ring).TransformPoint(default);
        Check(first.ActualHeight == 52
            && first.ActualWidth > 250 && Math.Abs(p1.X - p2.X) < 1 && Math.Abs(p2.Y - p1.Y - 52) < 1,
            "browser choices form full-width vertical rows");
        second.FocusItem(FocusState.Keyboard);
        await SmokeApplication.WaitForAsync(() => second.IsSelected && ReferenceEquals(
            Microsoft.UI.Xaml.Input.FocusManager.GetFocusedElement(root.XamlRoot), second.FindName("LaunchButton")),
            "list focus and selection converge");
        Check(second.IsSelected && !first.IsSelected
            && ((FrameworkElement)second.FindName("ListSelection")).Opacity == 1,
            "keyboard focus selects and highlights the matching row");
        Check(((Button)second.FindName("LaunchButton")).UseSystemFocusVisuals,
            "list rows retain visible keyboard focus");
        Check(second.FindName("IconTile") is null, "list rows do not instantiate a radial visual subtree");

        var alternate = new AlternateLaunch("Private", "--private");
        settings.LoadSettings().Browsers[1].AlternateLaunches = [alternate];
        window.ViewModel.RefreshBrowsers();
        root.UpdateLayout();
        second = (ListBrowserItem)ring.GetOrCreateElement(1);
        var launch = (Button)second.FindName("LaunchButton");
        Check(launch.ContextFlyout is MenuFlyout menu && menu.Items.Count == 1
            && ((MenuFlyoutItem)menu.Items[0]).Text == "Private", "list retains alternate launches");

        foreach (var theme in new[] { ElementTheme.Dark, ElementTheme.Light })
        {
            root.RequestedTheme = theme;
            root.UpdateLayout();
            await Task.Delay(50);
            var label = (TextBlock)second.FindName("BrowserName");
            var color = ((SolidColorBrush)label.Foreground).Color;
            Check((color.R < 128) == (theme == ElementTheme.Light), "list labels follow live theme changes");
            await CaptureAsync(window, theme.ToString().ToLowerInvariant());
        }

        for (int i = 3; i <= 30; i++)
            settings.LoadSettings().Browsers.Add(new Browser($"Browser {i}", $"selector-test-{i}.exe"));
        window.ViewModel.RefreshBrowsers();
        await Task.Delay(100);
        root.UpdateLayout();
        Check(scroll.ScrollableHeight > 0 && window.AppWindow.Size.Height <= 600 * root.XamlRoot.RasterizationScale + 1,
            "long browser lists scroll inside a bounded picker");
        ((Button)root.FindName("NamePill")).Focus(FocusState.Programmatic);
        Check(window.NavigateSelection(VirtualKey.Down), "arrow navigation enters the first browser");
        await SmokeApplication.WaitForAsync(() => ring.TryGetElement(0) is ListBrowserItem { IsSelected: true }
            && ReferenceEquals(Microsoft.UI.Xaml.Input.FocusManager.GetFocusedElement(root.XamlRoot),
                ((ListBrowserItem)ring.TryGetElement(0)).FindName("LaunchButton")),
            "production navigation focuses the first browser");
        Check(window.NavigateSelection(VirtualKey.Up), "up wraps from the first browser to the last");
        try
        {
            await SmokeApplication.WaitForAsync(() => ring.TryGetElement(29) is ListBrowserItem { IsSelected: true }
                && scroll.VerticalOffset > 0, "production navigation reveals the offscreen last browser");
        }
        catch
        {
            var target = ring.TryGetElement(29) as ListBrowserItem;
            var focused = Microsoft.UI.Xaml.Input.FocusManager.GetFocusedElement(root.XamlRoot) as DependencyObject;
            while (focused is not null and not BrowserItemControl) focused = VisualTreeHelper.GetParent(focused);
            Console.WriteLine($"Navigation state: rowY={target?.TransformToVisual(ring).TransformPoint(default).Y}, target realized={target is not null}, selected={target?.IsSelected}, "
                + $"height={target?.ActualHeight}, focused={(focused as BrowserItemControl)?.BrowserItem?.Name}, "
                + $"offset={scroll.VerticalOffset}, scrollable={scroll.ScrollableHeight}, viewport={scroll.ViewportHeight}");
            throw;
        }
        var last = (ListBrowserItem)ring.TryGetElement(29);
        Check(last.IsSelected && scroll.VerticalOffset > 0, "production navigation focuses and scrolls offscreen choices");
        var lastPosition = last.TransformToVisual(scroll).TransformPoint(default);
        Check(lastPosition.Y >= -0.01 && lastPosition.Y + last.ActualHeight <= scroll.ViewportHeight + 0.01,
            "keyboard wrap reveals the entire last browser row, not just a nonzero scroll offset");
        Check(window.NavigateSelection(VirtualKey.Down), "down wraps back to the first browser");
        await SmokeApplication.WaitForAsync(() => ring.TryGetElement(0) is ListBrowserItem { IsSelected: true }
            && scroll.VerticalOffset < 1, "production navigation reveals the first browser after wrapping down");
        Check(window.NavigateSelection(VirtualKey.Up), "up can reveal the last browser repeatedly");
        await SmokeApplication.WaitForAsync(() => ring.TryGetElement(29) is ListBrowserItem { IsSelected: true }
            && scroll.VerticalOffset > 0, "repeated keyboard wrapping preserves selection and scrolling");
        last = (ListBrowserItem)ring.TryGetElement(29);
        Check(!window.NavigateSelection(VirtualKey.F5), "unrelated keys do not navigate");
        CheckHostFitsWindow(window);
        Check(last.ShortcutVisibility == Visibility.Collapsed, "rows beyond nine have no misleading number hint");
        window.MinimizeWindow();
        window.ShowWindow();
        window.AppWindow.Move(new PointInt32(-32000, -32000));
        first = (ListBrowserItem)ring.GetOrCreateElement(0);
        await SmokeApplication.WaitForAsync(() => first.IsSelected && scroll.VerticalOffset < 1,
            "reopened list returns to the first row");
        Check(first.IsSelected && scroll.VerticalOffset < 1, "reopening resets selection and scrolling to the first browser");
        CheckHostFitsWindow(window);
        var shortSize = window.AppWindow.Size;
        shortSize.Height = Math.Min(shortSize.Height, (int)Math.Round(300 *
            CursorPosition.WindowScale(WinRT.Interop.WindowNative.GetWindowHandle(window))));
        window.AppWindow.Resize(shortSize);
        await SmokeApplication.WaitForAsync(() => Math.Abs(((FrameworkElement)root.FindName("ListHost")).Height
            - (shortSize.Height / CursorPosition.WindowScale(WinRT.Interop.WindowNative.GetWindowHandle(window)) - 40)) < 0.01,
            "list sizes to a short actual window before the shape is cached");
        CheckHostFitsWindow(window);
        window.MinimizeWindow();
        var toggledBrowser = settings.LoadSettings().Browsers[29];
        toggledBrowser.Hidden = true;
        window.ViewModel.RefreshBrowsers();
        await Task.Delay(100);
        toggledBrowser.Hidden = false;
        window.ViewModel.RefreshBrowsers();
        await Task.Delay(100);
        CheckHostFitsWindow(window);
        window.ShowWindow();
        CheckHostFitsWindow(window);
        window.AppWindow.Move(new PointInt32(-32000, -32000));

        settings.LoadSettings().Browsers.Clear();
        window.ViewModel.RefreshBrowsers();
        await Task.Delay(100);
        Check(((FrameworkElement)root.FindName("EmptyListMessage")).Visibility == Visibility.Visible
            && ((Button)root.FindName("NamePill")).Visibility == Visibility.Visible,
            "empty lists keep a useful message and link options");
        Check(!window.NavigateSelection(VirtualKey.Down), "empty lists do not attempt browser focus");
        settings.LoadSettings().Browsers.Add(new Browser("Restored 1", "selector-test-1.exe"));
        settings.LoadSettings().Browsers.Add(new Browser("Restored 2", "selector-test-2.exe"));
        window.ViewModel.RefreshBrowsers();
        settings.UpdateAppSettings(new AppSettings { SelectorLayout = SelectorLayout.Radial });
        var radial = (ItemsRepeater)root.FindName("BrowserRing");
        await SmokeApplication.WaitForAsync(() => ((FrameworkElement)root.FindName("FlowerHost")).Visibility == Visibility.Visible,
            "settings restore radial layout");
        root.UpdateLayout();
        var restored = (RadialBrowserItem)radial.GetOrCreateElement(0);
        Check(restored.ActualHeight == 104 && radial.Layout is RadialLayout
            && ((FrameworkElement)root.FindName("ListHost")).Visibility == Visibility.Collapsed
            && ((FrameworkElement)root.FindName("SelectionOutline")).Visibility == Visibility.Visible,
            "switching back restores petals and radial selection, not list rows");
        window.MinimizeWindow();
        Console.WriteLine("PASS: list placement, rows, selection, themes, alternate launches, scrolling, reopen, empty state, and layout switching.");
    }

    public static async Task CheckColdOpeningAsync(SelectorWindow window)
    {
        window.ShowWindow();
        var root = (FrameworkElement)window.Content;
        var ring = (ItemsRepeater)root.FindName("BrowserList");
        var flower = (FrameworkElement)root.FindName("ListHost");
        var visual = ElementCompositionPreview.GetElementVisual(flower);
        Check(ring.ItemsSource == window.ViewModel.Browsers
            && ring.TryGetElement(0) is ListBrowserItem { ActualHeight: 52 },
            "cold list has bound and arranged browser rows when shown");
        CheckHostFitsWindow(window);
        Check(((ScrollViewer)root.FindName("BrowserScroll")).ScrollableHeight == 0,
            "cold two-browser list fits without scrolling");
        Check(visual.Opacity == 1 && visual.Scale == System.Numerics.Vector3.One
            && visual.TryGetAnimationController("Opacity") is null,
            "cold list opens fully visible without a fade restart");
        await CaptureOpeningAsync(window, "cold");
        window.MinimizeWindow();
        Console.WriteLine("PASS: cold list opening is laid out and opaque without entrance animation.");
    }

    private static void CheckPlacement()
    {
        foreach (double scale in new[] { 1, 1.25, 1.5, 2, 3 })
        {
            var area = new RectInt32(-1920, -100, 1920, 900);
            foreach (var cursor in new[] { new PointInt32(-1800, 0), new PointInt32(-100, 0), new PointInt32(-5, 0),
                new PointInt32(-1800, 795), new PointInt32(-5, -95) })
            {
                var bounds = CursorPosition.CalculateBounds(ListMetrics.WindowWidth, ListMetrics.WindowHeight(30),
                    ListMetrics.Anchor, cursor, area, scale, SelectorPlacement.BesideCursor);
                Check(bounds.X >= area.X && bounds.Y >= area.Y
                    && bounds.X + bounds.Width <= area.X + area.Width
                    && bounds.Y + bounds.Height <= area.Y + area.Height,
                    "list fits the cursor monitor work area, including negative origins and high DPI");
                if (cursor.X == -100) Check(Math.Abs(cursor.X -
                    (bounds.X + bounds.Width - ListMetrics.ShadowMargin * scale) - ListMetrics.CursorGap * scale) <= 1,
                    "right-edge lists flip left with the same twelve-DIP gap");
            }
            var beside = CursorPosition.CalculateBounds(ListMetrics.WindowWidth, ListMetrics.WindowHeight(30),
                ListMetrics.Anchor, new PointInt32(300, 300), new RectInt32(0, 0, 3840, 2160), scale, SelectorPlacement.BesideCursor);
            Check(Math.Abs(beside.X + ListMetrics.ShadowMargin * scale - 300 - ListMetrics.CursorGap * scale) <= 1,
                "visible list begins twelve DIPs beside the pointer");
            var size = new System.Numerics.Vector2((float)(ListMetrics.WindowWidth * scale), (float)(ListMetrics.WindowHeight(30) * scale));
            using var shape = BackdropShape.FromList(size, (float)scale).CreateGeometry();
            Check(shape.FillContainsPoint(new System.Numerics.Vector2(100, 100) * (float)scale)
                && !shape.FillContainsPoint(System.Numerics.Vector2.Zero), "list backdrop covers the card, not transparent margins");
        }
        var narrow = CursorPosition.CalculateBounds(ListMetrics.WindowWidth, ListMetrics.WindowHeight(30),
            ListMetrics.Anchor, new PointInt32(790, 890), new RectInt32(0, 0, 800, 900), 3, SelectorPlacement.BesideCursor);
        Check(narrow.Width == 800 && narrow.Height == 900 && narrow.X == 0 && narrow.Y == 0,
            "narrow high-DPI work areas constrain both window dimensions");
        var opening = new CursorSnapshot(new PointInt32(300, 300), new RectInt32(0, 0, 3840, 2160), 1);
        var firstBounds = opening.Place(new SelectorGeometry(ListMetrics.WindowWidth, ListMetrics.WindowHeight(2),
            ListMetrics.Anchor, SelectorPlacement.BesideCursor));
        var refreshedBounds = opening.Place(new SelectorGeometry(ListMetrics.WindowWidth, ListMetrics.WindowHeight(30),
            ListMetrics.Anchor, SelectorPlacement.BesideCursor));
        Check(firstBounds.X == refreshedBounds.X && firstBounds.Y == refreshedBounds.Y,
            "refresh placement reuses the opening cursor rather than reading the current pointer");
        var narrowShape = BackdropShape.FromList(new System.Numerics.Vector2(narrow.Width, narrow.Height), 3);
        Check(narrowShape.Pill.X == 60 && narrowShape.Pill.Width == 680,
            "narrow cards retain real DPI rather than shrinking their shadow and corner scale");
    }

    private static void CheckHostFitsWindow(SelectorWindow window)
    {
        var host = (FrameworkElement)((FrameworkElement)window.Content).FindName("ListHost");
        var size = window.AppWindow.Size;
        var scale = CursorPosition.WindowScale(WinRT.Interop.WindowNative.GetWindowHandle(window));
        Check(Math.Abs(host.Width - (size.Width / scale - 40)) < 0.01
            && Math.Abs(host.Height - (size.Height / scale - 40)) < 0.01,
            "list host always matches actual window bounds, including hidden browser hide/unhide and cached shapes");
    }

    private static async Task CaptureOpeningAsync(SelectorWindow window, string kind = "warm")
    {
        string? directory = Environment.GetEnvironmentVariable("QUIVER_SELECTOR_CAPTURE_DIRECTORY");
        if (string.IsNullOrEmpty(directory)) return;
        Directory.CreateDirectory(directory);
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var samples = new List<string> { "frame,elapsedMs,x,y,width,height" };
        for (int frame = 0; frame < 12; frame++)
        {
            var position = window.AppWindow.Position;
            var size = window.AppWindow.Size;
            samples.Add($"{frame},{clock.ElapsedMilliseconds},{position.X},{position.Y},{size.Width},{size.Height}");
            using var bitmap = new Bitmap(size.Width, size.Height);
            using (var graphics = Graphics.FromImage(bitmap))
                graphics.CopyFromScreen(position.X, position.Y, 0, 0, new System.Drawing.Size(size.Width, size.Height));
            bitmap.Save(Path.Combine(directory, $"selector-opening-{kind}-{frame:D2}-{clock.ElapsedMilliseconds:D4}ms.png"), ImageFormat.Png);
            await Task.Delay(16);
        }
        File.WriteAllLines(Path.Combine(directory, $"selector-opening-{kind}.csv"), samples);
    }

    private static async Task CaptureAsync(SelectorWindow window, string theme)
    {
        string? directory = Environment.GetEnvironmentVariable("QUIVER_SELECTOR_CAPTURE_DIRECTORY");
        if (string.IsNullOrEmpty(directory)) return;
        Directory.CreateDirectory(directory);
        window.AppWindow.Move(new PointInt32(100, 100));
        await Task.Delay(250);
        var size = window.AppWindow.Size;
        using var bitmap = new Bitmap(size.Width, size.Height);
        using (var graphics = Graphics.FromImage(bitmap))
            graphics.CopyFromScreen(100, 100, 0, 0, new System.Drawing.Size(size.Width, size.Height));
        bitmap.Save(Path.Combine(directory, $"selector-list-{theme}.png"), ImageFormat.Png);
        window.AppWindow.Move(new PointInt32(-32000, -32000));
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception($"List selector check failed: {message}");
    }
}
