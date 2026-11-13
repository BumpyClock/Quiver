using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Quiver.App.Controls;
using Quiver.App.Helpers;
using Quiver.App.Windows;
using Quiver.Library.Models;
using Windows.Graphics;

namespace Quiver.SelectorSmoke;

internal static class SelectorFitChecks
{
    private static readonly double[] Scales = [1, 1.25, 1.5, 1.75, 2, 2.5, 3];

    public static void Run()
    {
        CheckListViewportRows();
        CheckRadialFit();
        Console.WriteLine("PASS: List viewport rows across DPI 97-480 and fit-aware radial fallback bounds.");
    }

    private static void CheckListViewportRows()
    {
        int maximumRows = (int)Math.Round((ListMetrics.WindowHeight(int.MaxValue) - ListMetrics.WindowHeight(1))
            / ListMetrics.RowHeight) + 1;
        double nonRowHeight = ListMetrics.WindowHeight(1) - ListMetrics.RowHeight;
        var area = new RectInt32(-7680, -4320, 15360, 8640);
        for (int dpi = 97; dpi <= 480; dpi++)
        {
            double scale = dpi / 96.0;
            for (int count = 1; count <= maximumRows; count++)
            {
                int pixels = CursorPosition.CalculateBounds(ListMetrics.WindowWidth, ListMetrics.WindowHeight(count),
                    ListMetrics.Anchor, new PointInt32(0, 0), area, scale, SelectorPlacement.BesideCursor).Height;
                int rows = Rows(ListMetrics.ViewportHeight(pixels, scale));
                Check(rows == count, $"rounded native List height keeps every nominal row (count {count}, DPI {dpi}, "
                    + $"{pixels}px, {rows} rows)");
                Check((nonRowHeight + rows * ListMetrics.RowHeight) * scale <= pixels + 0.5,
                    $"List rows and footer exceed the native window by less than half a pixel (count {count}, DPI {dpi})");
                int shortened = Rows(ListMetrics.ViewportHeight(pixels - 1, scale));
                Check(shortened == count - 1, $"a window one pixel short loses the clipped row (count {count}, DPI {dpi})");
                Check(CursorPosition.NativeLength(nonRowHeight + shortened * ListMetrics.RowHeight, scale) <= pixels - 1,
                    $"shortened List rows fit the shortened window (count {count}, DPI {dpi})");
            }
        }
        var constrained = CursorPosition.CalculateBounds(ListMetrics.WindowWidth, ListMetrics.WindowHeight(30),
            ListMetrics.Anchor, new PointInt32(10, 10), new RectInt32(0, 0, 1280, 400), 1.75, SelectorPlacement.BesideCursor);
        int constrainedRows = Rows(ListMetrics.ViewportHeight(constrained.Height, 1.75));
        Check(constrained.Height == 400
            && CursorPosition.NativeLength(nonRowHeight + constrainedRows * ListMetrics.RowHeight, 1.75) <= 400
            && CursorPosition.NativeLength(nonRowHeight + (constrainedRows + 1) * ListMetrics.RowHeight, 1.75) > 400,
            "work-area-constrained List windows show only rows that fit completely");

        static int Rows(double viewport) => (int)Math.Round(viewport / ListMetrics.RowHeight);
    }

    private static void CheckRadialFit()
    {
        RectInt32[] areas =
        [
            new(0, 0, 3840, 2112),
            new(0, 0, 1920, 1032),
            new(-1920, -1080, 1920, 1032),
            new(-1280, 200, 1280, 680),
            new(2560, -1440, 1366, 728),
            new(0, 0, 800, 600),
        ];
        int radial = 0;
        int fallback = 0;
        foreach (double scale in Scales)
        {
            foreach (var area in areas)
            {
                PointInt32[] cursors =
                [
                    new(area.X, area.Y),
                    new(area.X + area.Width - 1, area.Y + area.Height - 1),
                    new(area.X + area.Width / 2, area.Y + area.Height / 2),
                ];
                for (int count = 0; count <= 30; count++)
                {
                    var metrics = RadialMetrics.For(count);
                    var radialGeometry = SelectorWindow.GeometryFor(SelectorLayout.Radial, metrics);
                    int width = CursorPosition.NativeLength(radialGeometry.Width, scale);
                    int height = CursorPosition.NativeLength(radialGeometry.Height, scale);
                    foreach (var cursor in cursors)
                    {
                        var opening = new CursorSnapshot(cursor, area, scale);
                        var layout = SelectorWindow.EffectiveLayout(SelectorLayout.Radial, metrics, opening);
                        var bounds = opening.Place(SelectorWindow.GeometryFor(layout, metrics));
                        Check(Inside(bounds, area), $"effective {layout} picker fits the opening work area "
                            + $"(count {count}, scale {scale}, area {Describe(area)}, bounds {Describe(bounds)})");
                        if (layout == SelectorLayout.Radial)
                        {
                            radial++;
                            Check(bounds.Width == width && bounds.Height == height,
                                $"fitting radial pickers keep full flower and footer size (count {count}, scale {scale})");
                        }
                        else
                        {
                            fallback++;
                            Check(width > area.Width || height > area.Height,
                                $"List fallback happens only when full radial bounds overflow (count {count}, scale {scale})");
                        }
                        Check(SelectorWindow.EffectiveLayout(SelectorLayout.List, metrics, opening) == SelectorLayout.List,
                            "preferred List stays List");
                    }
                }
            }
        }
        Check(radial > 0 && fallback > 0, $"fit sweep exercises both radial and fallback ({radial}/{fallback})");

        foreach (double scale in Scales)
        {
            foreach (int count in new[] { 2, 3, 6, 10, 16 })
            {
                var metrics = RadialMetrics.For(count);
                int width = CursorPosition.NativeLength(metrics.WindowWidth, scale);
                int height = CursorPosition.NativeLength(metrics.WindowHeight, scale);
                int flowerOnly = CursorPosition.NativeLength(metrics.PillTop + RadialMetrics.ShadowMargin, scale);
                var cursor = new PointInt32(-900, -500);
                Check(Effective(new RectInt32(-1000, -600, width, height)) == SelectorLayout.Radial,
                    $"exact native radial fit stays radial (count {count}, scale {scale})");
                Check(Effective(new RectInt32(-1000, -600, width - 1, height + 400)) == SelectorLayout.List,
                    $"width-only overflow falls back to List (count {count}, scale {scale})");
                Check(flowerOnly < height && Effective(new RectInt32(-1000, -600, width + 400, flowerOnly)) == SelectorLayout.List,
                    $"footer-only overflow falls back to List (count {count}, scale {scale})");
                Check(Effective(new RectInt32(-1000, -600, width + 400, height - 1)) == SelectorLayout.List,
                    $"one-pixel height overflow falls back to List (count {count}, scale {scale})");
                Check(SelectorWindow.EffectiveLayout(SelectorLayout.Radial, metrics, default) == SelectorLayout.Radial
                    && SelectorWindow.EffectiveLayout((SelectorLayout)7, metrics, new CursorSnapshot(cursor, null, scale))
                        == SelectorLayout.Radial,
                    "unknown work areas and unknown stored layouts keep the radial preference");

                SelectorLayout Effective(RectInt32 area)
                    => SelectorWindow.EffectiveLayout(SelectorLayout.Radial, metrics, new CursorSnapshot(cursor, area, scale));
            }
        }
        var twelve = RadialMetrics.For(12);
        var small = new CursorSnapshot(new PointInt32(640, 360), new RectInt32(0, 0, 1280, 680), 1.5);
        var large = new CursorSnapshot(new PointInt32(-1920, 1080), new RectInt32(-3840, 0, 3840, 2112), 1.5);
        Check(SelectorWindow.EffectiveLayout(SelectorLayout.Radial, twelve, small) == SelectorLayout.List
            && SelectorWindow.EffectiveLayout(SelectorLayout.Radial, twelve, large) == SelectorLayout.Radial,
            "a later opening on a larger work area regains the preferred radial picker");
    }

    public static async Task RunAsync(SelectorWindow window, TestSettings settings)
    {
        var root = (FrameworkElement)window.Content;
        var flower = (FrameworkElement)root.FindName("FlowerHost");
        var list = (FrameworkElement)root.FindName("ListHost");
        var browsers = settings.LoadSettings().Browsers;
        var originals = browsers.ToArray();
        var preference = settings.LoadSettings().AppSettings;
        settings.UpdateAppSettings(new AppSettings { SelectorLayout = SelectorLayout.Radial });
        try
        {
            window.MinimizeWindow();
            await Task.Delay(50);
            window.ShowWindow();
            var opening = CursorPosition.Capture();
            Check(flower.Visibility == Visibility.Visible, "two browsers open in the preferred radial picker");
            for (int i = browsers.Count; i < 30; i++)
                browsers.Add(new Browser($"Fit {i + 1}", $"fit-fixture-{i}.exe"));
            window.ViewModel.RefreshBrowsers();
            var expected = SelectorWindow.EffectiveLayout(SelectorLayout.Radial, RadialMetrics.For(30), opening);
            await SmokeApplication.WaitForAsync(() => (expected == SelectorLayout.List) == (list.Visibility == Visibility.Visible)
                && (expected == SelectorLayout.Radial) == (flower.Visibility == Visibility.Visible),
                $"collection refresh applies the opening snapshot's effective {expected} layout");
            Check(InsideWorkArea(window, opening), "refreshed picker stays within the opening work area");
            window.MinimizeWindow();
            window.ShowWindow();
            Check((expected == SelectorLayout.List) == (list.Visibility == Visibility.Visible),
                $"reopening with thirty browsers keeps the effective {expected} layout");
            if (expected == SelectorLayout.List)
            {
                Check(((ItemsRepeater)root.FindName("BrowserList")).TryGetElement(0) is ListBrowserItem { IsSelected: true },
                    "fallback List opens with arranged, selected rows");
                Check(InsideWorkArea(window, opening), "fallback List window stays within the opening work area");
            }
            Check(settings.LoadSettings().AppSettings.SelectorLayout == SelectorLayout.Radial,
                "fit fallback never rewrites the stored radial preference");
            window.MinimizeWindow();
            while (browsers.Count > 2) browsers.RemoveAt(browsers.Count - 1);
            window.ViewModel.RefreshBrowsers();
            await Task.Delay(50);
            window.ShowWindow();
            Check(flower.Visibility == Visibility.Visible && list.Visibility == Visibility.Collapsed,
                "the next opening that fits regains the radial picker");
            Console.WriteLine(FormattableString.Invariant(
                $"PASS: native fit fallback (thirty browsers -> {expected} at scale {opening.Scale:F3}, work area {(opening.WorkArea is { } area ? Describe(area) : "unknown")}), stored preference kept, radial regained."));
        }
        finally
        {
            window.MinimizeWindow();
            browsers.Clear();
            foreach (var browser in originals) browsers.Add(browser);
            window.ViewModel.RefreshBrowsers();
            settings.UpdateAppSettings(preference);
            await Task.Delay(50);
        }
    }

    private static bool InsideWorkArea(SelectorWindow window, CursorSnapshot opening)
        => opening.WorkArea is not { } area || Inside(new RectInt32(window.AppWindow.Position.X, window.AppWindow.Position.Y,
            window.AppWindow.Size.Width, window.AppWindow.Size.Height), area);

    private static bool Inside(RectInt32 bounds, RectInt32 area) => bounds.X >= area.X && bounds.Y >= area.Y
        && bounds.X + bounds.Width <= area.X + area.Width && bounds.Y + bounds.Height <= area.Y + area.Height;

    private static string Describe(RectInt32 rect) => $"{rect.X},{rect.Y} {rect.Width}x{rect.Height}";

    private static void Check(bool condition, string name)
    {
        if (!condition) throw new Exception($"Selector fit check failed: {name}");
    }
}
