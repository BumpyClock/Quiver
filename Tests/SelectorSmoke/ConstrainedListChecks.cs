using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Quiver.App.Controls;
using Quiver.App.Helpers;
using Quiver.App.Windows;
using Quiver.Library;
using Quiver.Library.Models;
using Windows.Foundation;
using Windows.Graphics;
using Windows.System;

namespace Quiver.SelectorSmoke;

internal static class ConstrainedListChecks
{
    private static readonly ListMetrics Enlarged = new(92, 112, 100);

    public static void Run()
    {
        foreach (int pixels in new[] { 600, 680 })
            foreach (int count in new[] { 0, 1, 2, 30 })
            {
                var frame = Allocate(Enlarged, pixels, 3, count, out var bounds);
                string at = FormattableString.Invariant($"(300%, {pixels}px work area, count {count})");
                Check(bounds.Height == pixels, $"the enlarged List fills the work area height without exceeding it {at}");
                Check(frame.Scrolls && frame.Body == (count == 0 ? Enlarged.Empty : Enlarged.Row),
                    FormattableString.Invariant($"the enlarged List scrolls the whole frame around one measured row or the recovery {at} ({frame})"));
                double viewport = frame.Height - 2 * ListMetrics.Padding;
                Check(Enlarged.Row <= viewport && Enlarged.Empty <= viewport && Enlarged.Footer <= viewport,
                    FormattableString.Invariant($"each enlarged target fits the frame viewport {at} (viewport {viewport})"));
            }
        foreach (var metrics in ListMeasurementChecks.Measured)
            foreach (double scale in new[] { 1, 101 / 96.0, 1.25, 1.5, 2, 2.25, 2.5, 3, 3.5 })
                for (int pixels = 80; pixels <= 1800; pixels++)
                    foreach (int count in new[] { 0, 1, 2, 3, 30 })
                        CheckFrame(metrics, pixels, scale, count);
        Console.WriteLine("PASS: constrained List geometry keeps one whole row or the recovery, the gap and the footer reachable in one scrolling frame (300% at 600px and 680px, DPI 96-336, 80-1800px).");
    }

    private static void CheckFrame(ListMetrics metrics, int pixels, double scale, int count)
    {
        var frame = Allocate(metrics, pixels, scale, count, out var bounds);
        string at = FormattableString.Invariant($"(row {metrics.Row}, count {count}, scale {scale}, {pixels}px area, {bounds.Height}px window)");
        Check(bounds.Height <= pixels, $"the List window stays inside the work area {at}");
        double minimum = count == 0 ? metrics.Empty : metrics.Row;
        bool pinned = CursorPosition.NativeLength(metrics.Height(1) - metrics.Row + minimum, scale) <= bounds.Height;
        Check(frame.Scrolls != pinned, $"the frame scrolls exactly when the pinned footer and one row or recovery cannot fit {at}");
        double rows = frame.Body / metrics.Row;
        Check(count == 0 ? frame.Body == metrics.Empty : rows >= 1 && rows <= count && Math.Abs(rows - Math.Round(rows)) < 1e-9,
            $"the body keeps whole rows or the full recovery {at}");
        if (!frame.Scrolls)
        {
            Check(frame.Extent == frame.Height && frame.FooterTop + metrics.Footer + 2 * ListMetrics.Padding - frame.Height is > -1e-9 and < 1e-9,
                $"the pinned footer ends at the frame padding {at}");
            return;
        }
        double viewport = frame.Height - 2 * ListMetrics.Padding;
        Check(frame.FooterTop == frame.Body + ListMetrics.FooterGap && frame.Extent > frame.Height,
            $"the scrolling frame stacks body, gap and footer beyond the viewport {at}");
        Check(count == 0 || frame.Body == Math.Max(1, Math.Min(count, Math.Floor(viewport / metrics.Row))) * metrics.Row,
            $"the scrolling body shows every whole row its viewport can hold {at}");
        foreach (double current in new[] { 0, frame.Extent - frame.Height, (frame.Extent - frame.Height) / 2 })
        {
            CheckReveal(0, frame.Body, current, "body");
            CheckReveal(frame.FooterTop, frame.FooterTop + metrics.Footer, current, "footer");
        }

        void CheckReveal(double top, double bottom, double current, string target)
        {
            double offset = frame.Offset(current, top, bottom);
            Check(offset >= -1e-9 && offset <= frame.Extent - frame.Height + 1e-9, $"{target} reveal stays in the frame extent {at}");
            if (bottom - top <= viewport)
                Check(offset <= top + 1e-9 && bottom + 2 * ListMetrics.Padding <= offset + frame.Height + 1e-9,
                    FormattableString.Invariant($"{target} reveal shows the whole target with padding {at} (offset {offset}, from {current})"));
            else
                Check(Math.Abs(offset - Math.Min(top, frame.Extent - frame.Height)) < 1e-9, $"oversized {target} reveal starts at its top {at}");
        }
    }

    private static ListFrame Allocate(ListMetrics metrics, int pixels, double scale, int count, out RectInt32 bounds)
    {
        bounds = CursorPosition.CalculateBounds(ListMetrics.WindowWidth, metrics.Height(count), ListMetrics.Anchor,
            new PointInt32(400, pixels / 2), new RectInt32(0, 0, 3840, pixels), scale, SelectorPlacement.BesideCursor);
        return metrics.Frame(new SizeInt32(bounds.Width, bounds.Height), scale, count);
    }

    private static void Check(bool condition, string name)
    {
        if (!condition) throw new Exception("Constrained List check failed: " + name);
    }

    public static async Task CheckAsync(SelectorWindow window, int count, double textScale, Action inject, Action<bool, string> expect,
        string? directory)
    {
        var root = (FrameworkElement)window.Content;
        var ring = (ItemsRepeater)root.FindName("BrowserList");
        var scroll = (ScrollViewer)root.FindName("BrowserScroll");
        var card = (Button)root.FindName("NamePill");
        var host = (FrameworkElement)root.FindName("ListHost");
        var manage = (HyperlinkButton)root.FindName("ManageBrowsersButton");
        var frameScroll = (ScrollViewer)root.FindName("ListFrameScroll");
        double raster = root.XamlRoot.RasterizationScale;
        foreach (double dips in new[] { 600 / 3.0, 680 / 3.0 })
        {
            int pixels = (int)Math.Floor(dips * raster);
            var area = new RectInt32(0, 0, 3840, pixels);
            window.ShowWindow(new CursorSnapshot(new PointInt32(400, pixels / 2), area, raster));
            inject();
            await Task.Delay(150);
            root.UpdateLayout();
            string at = FormattableString.Invariant($"(constrained {pixels}px at raster {raster}, count {count}, text {textScale})");
            var size = window.AppWindow.Size;
            var position = window.AppWindow.Position;
            expect(position.Y >= 0 && position.Y + size.Height <= pixels, $"constrained List stays inside the work area {at}");
            var geometry = window.ListGeometry;
            var frame = geometry.Frame(size, raster, count);
            expect(frame.Scrolls == (count == 0 ? geometry.Empty : geometry.Row) + geometry.Footer + ListMetrics.FooterGap
                    > window.AppWindow.Size.Height / raster - 2 * (ListMetrics.ShadowMargin + ListMetrics.Padding),
                FormattableString.Invariant($"the frame scrolls only when the pinned layout cannot fit {at} ({frame})"));
            expect(ReferenceEquals(VisualTreeHelper.GetParent(card), frame.Scrolls ? root.FindName("ListFrameContent") : root),
                $"only the scrolling List frame owns the footer {at}");
            expect(Math.Abs(frameScroll.ExtentHeight - frame.Extent) < 0.51 && Math.Abs(frameScroll.ViewportHeight - frame.Height) < 0.51
                && (frame.Scrolls ? frameScroll.ScrollableHeight > 0.5 : frameScroll.ScrollableHeight < 0.5),
                FormattableString.Invariant($"native frame scroller applies the allocated extent {at} (extent {frameScroll.ExtentHeight}/{frame.Extent}, viewport {frameScroll.ViewportHeight}/{frame.Height})"));
            expect(window.ViewModel.Browsers.Count(item => item.Profile is not null) == Enumerable.Range(0, count).Count(index => index % 3 != 1),
                $"profiled entries are present after the constrained reopen {at}");
            await SettleFocusAsync();
            frameScroll.ChangeView(null, frameScroll.ScrollableHeight, null, true);
            expect(await VisibleAsync(card), FormattableString.Invariant(
                $"scrolling the frame to its end shows the whole footer {at} (card {Bounds(card, root)}, visible {Visible()}, frame offset {frameScroll.VerticalOffset}/{frameScroll.ScrollableHeight})"));
            frameScroll.ChangeView(null, 0, null, true);
            var body = (FrameworkElement)root.FindName("ListBody");
            expect(await VisibleAsync(body), FormattableString.Invariant(
                $"scrolling the frame to its start shows the whole body {at} (body {Bounds(body, root)}, visible {Visible()}, frame offset {frameScroll.VerticalOffset})"));
            if (count == 0)
            {
                manage.Focus(FocusState.Keyboard);
                expect(await VisibleAsync(manage), $"recovery link is fully visible after keyboard focus {at}");
                await CaptureAsync("recovery");
                var link = Bounds(manage, root);
                expect(link.Bottom <= Bounds(card, root).Top - 0.5, $"recovery link and footer do not overlap {at}");
                card.Focus(FocusState.Keyboard);
                expect(await VisibleAsync(card), $"footer is fully visible after keyboard focus {at}");
                await CaptureAsync("footer");
                manage.Focus(FocusState.Keyboard);
                expect(await VisibleAsync(manage), $"recovery link is revealed again after footer focus {at}");
                continue;
            }
            expect(scroll.ViewportHeight >= geometry.Row - 0.5,
                FormattableString.Invariant($"constrained List shows at least one whole row {at} (viewport {scroll.ViewportHeight}, row {geometry.Row})"));
            var first = (ListBrowserItem)ring.GetOrCreateElement(0);
            first.FocusItem(FocusState.Keyboard);
            expect(await RowVisibleAsync(0), $"first row is fully visible after keyboard focus {at}");
            await CaptureAsync("first");
            expect(window.NavigateSelection(VirtualKey.Up), $"Up wraps to the last row {at}");
            expect(await RowVisibleAsync(count - 1), $"wrapped last row is fully visible {at}");
            await CaptureAsync("last");
            expect(window.NavigateSelection(VirtualKey.Down), $"Down wraps to the first row {at}");
            expect(await RowVisibleAsync(0), $"first row is revealed after Down wrap {at}");
            expect(FocusManager.TryMoveFocus(FocusNavigationDirection.Next, new FindNextElementOptions { SearchRoot = root })
                && await PollAsync(() => ReferenceEquals(FocusManager.GetFocusedElement(root.XamlRoot), card)),
                $"Tab moves from the rows to the footer {at}");
            expect(await VisibleAsync(card), $"footer is fully visible after Tab focus {at}");
            await CaptureAsync("footer");
            expect(Bounds(scroll, root).Top + scroll.ViewportHeight <= Bounds(card, root).Top - 0.5,
                $"row viewport ends above the footer {at}");
            expect(window.NavigateSelection(VirtualKey.Down), $"arrow navigation returns from the footer to the rows {at}");
            expect(await RowVisibleAsync(0), $"rows are revealed again after footer focus {at}");
        }
        card.Focus(FocusState.Programmatic);

        async Task CaptureAsync(string target)
        {
            if (directory is null) return;
            int pixels = window.AppWindow.Size.Height;
            await ListPolishChecks.CaptureAsync(window, directory,
                FormattableString.Invariant($"list-{count}-text-{textScale}-constrained-{pixels}px-{target}"), settle: false);
        }

        async Task<bool> RowVisibleAsync(int index) => await PollAsync(() =>
            ring.TryGetElement(index) is ListBrowserItem { IsSelected: true } row && row.ActualHeight > 0
            && Inside(Bounds(row, scroll), new Rect(0, 0, scroll.ActualWidth, scroll.ViewportHeight))
            && Inside(Bounds(row, root), Visible())
            && (row.BrowserItem?.Profile is null || row.FindName("ProfileBadge") is FrameworkElement { Visibility: Visibility.Visible } avatar
                && Inside(Bounds(avatar, root), Bounds(row, root))));

        async Task SettleFocusAsync()
        {
            object? previous = null;
            for (int attempt = 0; attempt < 20; attempt++)
            {
                object? focused = FocusManager.GetFocusedElement(root.XamlRoot);
                if (focused is not null && ReferenceEquals(focused, previous)) return;
                previous = focused;
                await Task.Delay(100);
            }
        }

        async Task<bool> VisibleAsync(FrameworkElement element)
            => await PollAsync(() => element.ActualHeight > 0 && Inside(Bounds(element, root), Visible()));

        Rect Visible()
        {
            var frame = Bounds(host, root);
            return new Rect(frame.X, frame.Y + ListMetrics.Padding, frame.Width, Math.Max(0, frame.Height - 2 * ListMetrics.Padding));
        }
    }

    public static async Task CheckLayoutSwitchAsync(SelectorWindow window, TestSettings settings, Action<double> scaleText,
        Action restoreText, Action<bool, string> expect)
    {
        var root = (Panel)window.Content;
        var card = (Button)root.FindName("NamePill");
        var frameScroll = (ScrollViewer)root.FindName("ListFrameScroll");
        var content = (Panel)root.FindName("ListFrameContent");
        var flower = (FrameworkElement)root.FindName("FlowerHost");
        double raster = root.XamlRoot.RasterizationScale;
        var browsers = settings.LoadSettings().Browsers;
        while (browsers.Count > 2) browsers.RemoveAt(browsers.Count - 1);
        settings.UpdateAppSettings(new AppSettings { SelectorLayout = SelectorLayout.List });
        int pixels = (int)Math.Floor(200 * raster);
        window.ShowWindow(new CursorSnapshot(new PointInt32(400, pixels / 2), new RectInt32(0, 0, 3840, pixels), raster));
        await Task.Delay(150);
        card.Focus(FocusState.Keyboard);
        expect(ReferenceEquals(VisualTreeHelper.GetParent(card), root) && card.Translation.Z == 16,
            "a pinned List footer stays a translated root sibling");
        scaleText(2.25);
        settings.UpdateAppSettings(new AppSettings { SelectorLayout = SelectorLayout.List });
        expect(await PollAsync(() => ReferenceEquals(VisualTreeHelper.GetParent(card), content) && frameScroll.VerticalOffset > 0.5),
            FormattableString.Invariant($"growing text moves the focused footer into the scrolling frame and reveals it (offset {frameScroll.VerticalOffset})"));
        expect(card.FocusState == FocusState.Keyboard, $"the reparented footer keeps keyboard focus ({card.FocusState})");
        restoreText();
        settings.UpdateAppSettings(new AppSettings { SelectorLayout = SelectorLayout.List });
        expect(await PollAsync(() => ReferenceEquals(VisualTreeHelper.GetParent(card), root) && frameScroll.ScrollableHeight < 0.5
                && frameScroll.VerticalOffset == 0),
            "restoring text returns the footer to its pinned root position with an unscrolled frame");
        expect(card.FocusState == FocusState.Keyboard && card.Translation.Z == 16,
            $"the pinned footer keeps keyboard focus and its translation ({card.FocusState})");
        window.ShowWindow(CursorPosition.Capture() with { WorkArea = null });
        await Task.Delay(150);
        card.Focus(FocusState.Keyboard);
        settings.UpdateAppSettings(new AppSettings { SelectorLayout = SelectorLayout.Radial });
        expect(await PollAsync(() => flower.Visibility == Visibility.Visible && ReferenceEquals(VisualTreeHelper.GetParent(card), root)),
            "switching to Radial keeps the footer at the selector root");
        expect(root.Children.IndexOf(card) == root.Children.IndexOf(flower) + 1 && card.Translation == System.Numerics.Vector3.Zero,
            $"the Radial footer keeps its stacking order without a translation ({root.Children.IndexOf(card)}, {root.Children.IndexOf(flower)})");
        expect(card.FocusState == FocusState.Keyboard, $"the Radial footer keeps keyboard focus after the switch ({card.FocusState})");
        settings.UpdateAppSettings(new AppSettings { SelectorLayout = SelectorLayout.List });
        expect(await PollAsync(() => flower.Visibility == Visibility.Collapsed && card.Translation.Z == 16),
            "switching back to List restores the pinned footer");
        expect(card.FocusState == FocusState.Keyboard, $"the List footer keeps keyboard focus after the switch ({card.FocusState})");
        settings.UpdateAppSettings(new AppSettings { SelectorLayout = SelectorLayout.Radial });
        pixels = (int)Math.Floor(150 * raster);
        var constrained = new CursorSnapshot(new PointInt32(400, pixels / 2), new RectInt32(0, 0, 3840, pixels), raster);
        window.ShowWindow(constrained);
        await Task.Delay(150);
        card.Focus(FocusState.Keyboard);
        expect(await PollAsync(() => flower.Visibility == Visibility.Collapsed && ReferenceEquals(VisualTreeHelper.GetParent(card), content)
                && frameScroll.VerticalOffset > 0.5),
            "a Radial preference on a short work area falls back to a scrolling List frame that owns and reveals the footer");
        window.ShowWindow(CursorPosition.Capture() with { WorkArea = null });
        await Task.Delay(150);
        var radial = RadialMetrics.For(window.ViewModel.Browsers.Count);
        expect(flower.Visibility == Visibility.Visible && ReferenceEquals(VisualTreeHelper.GetParent(card), root)
            && root.Children.IndexOf(card) == root.Children.IndexOf(flower) + 1,
            "reopening Radial returns the footer from the List frame to its root stacking position");
        expect(card.Width == radial.PillWidth && card.Height == RadialMetrics.PillHeight && card.Margin.Top == radial.PillTop
            && card.Translation == System.Numerics.Vector3.Zero,
            FormattableString.Invariant($"the returned Radial footer uses Radial geometry ({card.Width}x{card.Height} at {card.Margin.Top})"));
        window.ShowWindow(constrained);
        await Task.Delay(150);
        card.Focus(FocusState.Keyboard);
        expect(await PollAsync(() => ReferenceEquals(VisualTreeHelper.GetParent(card), content) && frameScroll.VerticalOffset > 0.5),
            "reopening on the short work area moves the footer back into the frame and reveals it");
        settings.UpdateAppSettings(new AppSettings { SelectorLayout = SelectorLayout.List });
        window.MinimizeWindow();
    }

    private static async Task<bool> PollAsync(Func<bool> condition)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        while (!condition() && clock.Elapsed < TimeSpan.FromSeconds(2)) await Task.Delay(20);
        return condition();
    }

    private static Rect Bounds(FrameworkElement element, UIElement relativeTo)
    {
        var origin = element.TransformToVisual(relativeTo).TransformPoint(default);
        return new Rect(origin.X, origin.Y, element.ActualWidth, element.ActualHeight);
    }

    private static bool Inside(Rect inner, Rect outer) => inner.Height > 0 && inner.Top >= outer.Top - 0.5
        && inner.Bottom <= outer.Bottom + 0.5 && inner.Left >= outer.Left - 0.5 && inner.Right <= outer.Right + 0.5;
}
