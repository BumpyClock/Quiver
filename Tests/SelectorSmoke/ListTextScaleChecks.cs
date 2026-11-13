using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Quiver.App.Controls;
using Quiver.App.ViewModels;
using Quiver.App.Windows;
using Quiver.Library;
using Quiver.Library.Models;
using Windows.Foundation;
using Windows.System;
using Windows.UI.ViewManagement;

namespace Quiver.SelectorSmoke;

internal static class ListTextScaleChecks
{
    private static readonly double[] TextScales = [1, 1.5, 2, 2.25, 1.5, 2.25, 1];

    public static async Task RunAsync(SelectorWindow window, TestSettings settings, string? directory)
    {
        var root = (FrameworkElement)window.Content;
        var ring = (ItemsRepeater)root.FindName("BrowserList");
        var scroll = (ScrollViewer)root.FindName("BrowserScroll");
        var card = (Button)root.FindName("NamePill");
        var listHost = (FrameworkElement)root.FindName("ListHost");
        var empty = (FrameworkElement)root.FindName("EmptyListMessage");
        var manage = (HyperlinkButton)root.FindName("ManageBrowsersButton");
        var preference = settings.LoadSettings().AppSettings;
        var originals = settings.LoadSettings().Browsers.ToArray();
        var scaled = new Dictionary<FrameworkElement, (object FontSize, bool Enabled)>();
        var failures = new List<string>();
        var report = new List<string> { "path,count,textScale,rowHeight,viewport,footerTop,footerHeight,emptyBottom,windowHeight,extent,rowsExtent" };
        double textScale = 1;
        TypedEventHandler<ItemsRepeater, ItemsRepeaterElementPreparedEventArgs> prepared = (_, args) => Scale(args.Element);
        ring.ElementPrepared += prepared;
        Console.WriteLine(FormattableString.Invariant(
            $"INFO: List text-scale fixture multiplies scalable font sizes linearly (system TextScaleFactor {new UISettings().TextScaleFactor}, raster {root.XamlRoot.RasterizationScale})."));
        try
        {
            settings.UpdateAppSettings(new AppSettings { SelectorLayout = SelectorLayout.List });
            foreach (int count in new[] { 0, 2, 30 })
            {
                textScale = 1;
                Restore();
                Populate(count);
                window.ShowWindow();
                Inject(count);
                foreach (double next in TextScales)
                {
                    Restore();
                    textScale = next;
                    Scale(root);
                    Scale(window.ListRowProbe);
                    Inject(count);
                    settings.UpdateAppSettings(new AppSettings { SelectorLayout = SelectorLayout.List });
                    await Task.Delay(150);
                    root.UpdateLayout();
                    CheckFrame(count, "refresh");
                    if (count == 30) await CheckWrapAsync();
                    window.MinimizeWindow();
                    window.ShowWindow();
                    Inject(count);
                    await Task.Delay(150);
                    root.UpdateLayout();
                    CheckFrame(count, "open");
                    if (directory is not null)
                        await ListPolishChecks.CaptureAsync(window, directory, FormattableString.Invariant($"list-{count}-text-{textScale}"));
                    if (count == 30) await CheckWrapAsync();
                }
                window.MinimizeWindow();
            }
        }
        finally
        {
            ring.ElementPrepared -= prepared;
            Restore();
            settings.LoadSettings().Browsers.Clear();
            foreach (var browser in originals) settings.LoadSettings().Browsers.Add(browser);
            window.ViewModel.RefreshBrowsers();
            window.MinimizeWindow();
            settings.UpdateAppSettings(preference);
        }
        if (directory is not null) File.WriteAllLines(Path.Combine(directory, "list-text-scale.csv"), report);
        foreach (string line in report) Console.WriteLine("MEASURE: " + line);
        if (failures.Count > 0)
            throw new Exception("List text-scale checks failed:\n  " + string.Join("\n  ", failures.Distinct()));
        Console.WriteLine("PASS: List text scale 1-2.25 (row text, keycaps, initials, recovery, footer, whole rows, wrap reveal, tooltips).");

        void Populate(int count)
        {
            settings.LoadSettings().Browsers.Clear();
            for (int index = 0; index < count; index++)
                settings.LoadSettings().Browsers.Add(new Browser(index % 3 == 1 ? "Google Chrome" : "Microsoft Edge",
                    $"text-scale-{index}.exe"));
            window.ViewModel.RefreshBrowsers();
        }

        void Inject(int count)
        {
            for (int index = 0; index < count; index++)
            {
                var browser = settings.LoadSettings().Browsers[index];
                BrowserProfile? profile = index % 3 == 1 ? null
                    : new($"Scale {index}", index % 3 == 0 ? $"Research👩‍💻 {index + 1}" : $"Café Ågyp {index + 1}", null);
                window.ViewModel.Browsers[index] = new BrowserItemViewModel(browser, profile);
            }
        }

        void CheckFrame(int count, string path)
        {
            string at = FormattableString.Invariant($"(count {count}, text {textScale}, {path})");
            Expect(window.ViewModel.Browsers.Count(item => item.Profile is not null)
                == Enumerable.Range(0, count).Count(index => index % 3 != 1),
                $"profiled entries are present {at}");
            var cardRect = Bounds(card, root);
            var hostRect = Bounds(listHost, root);
            double scale = root.XamlRoot.RasterizationScale;
            double windowHeight = window.AppWindow.Size.Height / scale;
            var geometry = window.ListGeometry;
            var frame = geometry.Frame(window.AppWindow.Size, scale, count);
            var body = Bounds((FrameworkElement)root.FindName("ListBody"), root);
            Expect(Math.Abs(body.Height - frame.Body) < 0.01 && Math.Abs(cardRect.Top - frame.FooterTop) < 0.51
                && Math.Abs(cardRect.Height - geometry.Footer) < 0.01,
                FormattableString.Invariant($"native body and footer apply the double-scale List frame {at} (body {body.Height}/{frame.Body}, footer {cardRect.Top}/{frame.FooterTop})"));
            Expect(cardRect.Bottom <= hostRect.Bottom - 7.5 && hostRect.Bottom <= windowHeight - 19.5,
                FormattableString.Invariant($"footer and List frame stay inside the native window {at} (card {cardRect.Bottom}, host {hostRect.Bottom}, window {windowHeight})"));
            foreach (var text in Texts(card))
                Expect(Inside(TextRect(text, root), cardRect), $"footer text {text.Name} fits the footer {at}");
            double rowHeight = 0;
            if (count == 0)
            {
                var emptyRect = Bounds(empty, root);
                foreach (var text in Texts(empty))
                {
                    var rect = TextRect(text, root);
                    Expect(rect.Bottom <= cardRect.Top - 0.5 && Inside(rect, hostRect),
                        $"recovery text '{text.Text}' stays above the footer {at}");
                }
                var link = Bounds(manage, root);
                Expect(link.Bottom <= cardRect.Top - 0.5, $"recovery link and footer hit regions are separate {at}");
                Expect(Inside(link, Bounds((FrameworkElement)root.FindName("ListBody"), root)),
                    $"recovery link stays inside the measured List body {at}");
                report.Add(FormattableString.Invariant(
                    $"{path},{count},{textScale},,,{cardRect.Top:F2},{cardRect.Height:F2},{emptyRect.Bottom:F2},{root.ActualHeight:F2}"));
                return;
            }
            var viewport = Bounds(scroll, root);
            Expect(viewport.Top + scroll.ViewportHeight <= cardRect.Top - 0.5, $"row viewport ends above the footer {at}");
            var rows = Enumerable.Range(0, count).Select(index => ring.TryGetElement(index)).OfType<ListBrowserItem>()
                .Where(item => item.ActualHeight > 0).ToList();
            rowHeight = rows[0].ActualHeight;
            Expect(rows.All(item => Math.Abs(item.ActualHeight - geometry.Row) < 0.01),
                FormattableString.Invariant($"realized rows use the measured row height {at} ({rowHeight}/{geometry.Row})"));
            Expect(Math.Abs(scroll.ViewportHeight - Math.Min(frame.Body, count * geometry.Row)) < 0.51,
                FormattableString.Invariant($"scroll viewport matches the allocated whole rows {at} ({scroll.ViewportHeight}/{frame.Body})"));
            double wholeRows = scroll.ViewportHeight / rowHeight;
            Expect(Math.Abs(wholeRows - Math.Round(wholeRows)) * rowHeight * root.XamlRoot.RasterizationScale <= 1
                && wholeRows >= 0.99, FormattableString.Invariant($"viewport holds whole rows {at} ({scroll.ViewportHeight}/{rowHeight})"));
            Expect(count > Math.Round(wholeRows) || scroll.ScrollableHeight < 0.5, $"Lists that fit do not scroll {at}");
            foreach (var item in rows)
            {
                var rowRect = new Rect(0, 0, item.ActualWidth, item.ActualHeight);
                foreach (var text in Texts(item))
                {
                    var rect = TextRect(text, item);
                    Expect(Inside(rect, rowRect), FormattableString.Invariant(
                        $"row text '{text.Text}' fits its row {at} (text {rect.Top:F1}-{rect.Bottom:F1}, row {item.ActualHeight:F1})")
                        + FormattableString.Invariant($" [index {ring.GetElementIndex(item)}, identity {((FrameworkElement)item.FindName("IdentityText")).ActualHeight}, fonts {string.Join("/", Texts(item).Select(t => $"{t.Text}:{t.FontSize}:{t.ActualHeight}:{t.IsTextScaleFactorEnabled}"))}, scaledKnown {Texts(item).Count(t => scaled.ContainsKey(t))}]"));
                    Expect(text.ActualHeight >= Natural(text) - 1,
                        FormattableString.Invariant($"row text '{text.Text}' is not squeezed {at} ({text.ActualHeight} < {Natural(text)})"));
                }
                var keycap = (FrameworkElement)item.FindName("ShortcutBadge");
                if (keycap.Visibility == Visibility.Visible)
                    Expect(Inside(TextRect((TextBlock)item.FindName("ShortcutText"), keycap), new Rect(0, 0, keycap.ActualWidth, keycap.ActualHeight)),
                        $"shortcut digit fits its keycap {at}");
                var avatar = (FrameworkElement)item.FindName("ProfileBadge");
                if (item.BrowserItem?.Profile is not null)
                {
                    var initial = Texts(avatar).Single();
                    Expect(Natural(initial) <= avatar.ActualHeight + 0.5, $"decorative profile initial fits its avatar {at}");
                    Expect(Inside(Bounds(avatar, item), rowRect), $"profile avatar fits its row {at}");
                    var identity = Bounds((FrameworkElement)item.FindName("IdentityText"), item);
                    if (keycap.Visibility == Visibility.Visible)
                        Expect(identity.Right <= Bounds(keycap, item).Left - 0.5, $"identity text clears the keycap {at}");
                }
                var button = (Button)item.FindName("LaunchButton");
                Expect((ToolTipService.GetToolTip(button) as string) == item.BrowserItem!.Name,
                    $"tooltip keeps the full browser and profile label {at}");
            }
            report.Add(FormattableString.Invariant(
                $"{path},{count},{textScale},{rowHeight:F2},{scroll.ViewportHeight:F2},{cardRect.Top:F2},{cardRect.Height:F2},,{root.ActualHeight:F2},{scroll.ExtentHeight:F2},{count * geometry.Row:F2}"));
        }

        async Task CheckWrapAsync()
        {
            string at = FormattableString.Invariant($"(text {textScale})");
            double rows = 30 * window.ListGeometry.Row;
            Expect(await PollAsync(() => Math.Abs(scroll.ExtentHeight - rows) < 0.5),
                FormattableString.Invariant($"uniform row extent remains exact after a live size change {at} ({scroll.ExtentHeight}/{rows})"));
            var first = (ListBrowserItem)ring.GetOrCreateElement(0);
            first.FocusItem(FocusState.Keyboard);
            await Task.Delay(50);
            Expect(window.NavigateSelection(VirtualKey.Up), $"Up wraps to the last row {at}");
            bool revealed = await PollAsync(() => ring.TryGetElement(29) is ListBrowserItem { IsSelected: true } last
                && last.TransformToVisual(scroll).TransformPoint(default).Y is var top
                && top >= -0.5 && top + last.ActualHeight <= scroll.ViewportHeight + 0.5);
            string detail = FormattableString.Invariant(
                $"{at} (offset {scroll.VerticalOffset}, scrollable {scroll.ScrollableHeight}, extent {scroll.ExtentHeight}/{rows}, viewport {scroll.ViewportHeight})");
            Expect(revealed, "wrapped last row is fully revealed " + detail);
            Expect(window.NavigateSelection(VirtualKey.Down), $"Down wraps to the first row {at}");
            Expect(await PollAsync(() => scroll.VerticalOffset < 1 && ring.TryGetElement(0) is ListBrowserItem { IsSelected: true }),
                FormattableString.Invariant($"first row is revealed after Down wrap {at} (offset {scroll.VerticalOffset})"));
        }

        void Expect(bool condition, string name)
        {
            if (!condition) failures.Add(name);
        }

        void Scale(DependencyObject element)
        {
            if (textScale == 1) return;
            if (element is TextBlock { IsTextScaleFactorEnabled: true } text && !scaled.ContainsKey(text))
            {
                scaled[text] = (text.ReadLocalValue(TextBlock.FontSizeProperty), true);
                text.FontSize *= textScale;
                text.IsTextScaleFactorEnabled = false;
            }
            else if (element is FontIcon { IsTextScaleFactorEnabled: true } icon && !scaled.ContainsKey(icon))
            {
                scaled[icon] = (icon.ReadLocalValue(FontIcon.FontSizeProperty), true);
                icon.FontSize *= textScale;
                icon.IsTextScaleFactorEnabled = false;
            }
            for (int index = 0; index < VisualTreeHelper.GetChildrenCount(element); index++)
                Scale(VisualTreeHelper.GetChild(element, index));
        }

        void Restore()
        {
            foreach (var (element, (fontSize, enabled)) in scaled)
            {
                var property = element is TextBlock ? TextBlock.FontSizeProperty : FontIcon.FontSizeProperty;
                if (fontSize == DependencyProperty.UnsetValue) element.ClearValue(property);
                else element.SetValue(property, fontSize);
                if (element is TextBlock text) text.IsTextScaleFactorEnabled = enabled;
                else ((FontIcon)element).IsTextScaleFactorEnabled = enabled;
            }
            scaled.Clear();
        }
    }

    private static async Task<bool> PollAsync(Func<bool> condition)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        while (!condition() && clock.Elapsed < TimeSpan.FromSeconds(3)) await Task.Delay(20);
        return condition();
    }

    private static IEnumerable<TextBlock> Texts(DependencyObject element)
    {
        for (int index = 0; index < VisualTreeHelper.GetChildrenCount(element); index++)
        {
            var child = VisualTreeHelper.GetChild(element, index);
            if (child is UIElement { Visibility: Visibility.Collapsed }) continue;
            if (child is TextBlock text && !string.IsNullOrEmpty(text.Text)) yield return text;
            foreach (var nested in Texts(child)) yield return nested;
        }
    }

    private static double Natural(TextBlock text)
    {
        var copy = new TextBlock
        {
            Text = text.Text,
            FontFamily = text.FontFamily,
            FontSize = text.FontSize,
            FontWeight = text.FontWeight,
            FontStyle = text.FontStyle,
            IsTextScaleFactorEnabled = text.IsTextScaleFactorEnabled,
        };
        copy.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        return copy.DesiredSize.Height;
    }

    private static Rect TextRect(TextBlock text, UIElement relativeTo)
    {
        var origin = text.TransformToVisual(relativeTo).TransformPoint(default);
        return new Rect(origin.X, origin.Y, text.ActualWidth, Math.Max(text.ActualHeight, Natural(text)));
    }

    private static Rect Bounds(FrameworkElement element, UIElement relativeTo)
    {
        var origin = element.TransformToVisual(relativeTo).TransformPoint(default);
        return new Rect(origin.X, origin.Y, element.ActualWidth, element.ActualHeight);
    }

    private static bool Inside(Rect inner, Rect outer) => inner.Top >= outer.Top - 0.5 && inner.Bottom <= outer.Bottom + 0.5
        && inner.Left >= outer.Left - 0.5 && inner.Right <= outer.Right + 0.5;
}
