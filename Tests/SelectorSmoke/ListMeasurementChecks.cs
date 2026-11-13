using Quiver.App.Controls;
using Quiver.App.Helpers;
using Windows.Graphics;

namespace Quiver.SelectorSmoke;

internal static class ListMeasurementChecks
{
    private static readonly ListMetrics[] Measured =
        [ListMetrics.Compact, new(68, 88, 72), new(84, 104, 92), new(92, 112, 100)];

    public static void Run()
    {
        CheckBoundaryScale();
        foreach (var metrics in Measured) CheckAllocation(metrics);
        Console.WriteLine("PASS: measured List geometry keeps whole rows, recovery and footer apart across DPI 97-480, including 101 DPI at 454px.");
    }

    private static void CheckBoundaryScale()
    {
        double scale = 101 / 96.0;
        var compact = ListMetrics.Compact;
        int width = CursorPosition.NativeLength(ListMetrics.WindowWidth, scale);
        int pixels = CursorPosition.NativeLength(compact.Height(6), scale);
        var frame = compact.Frame(new SizeInt32(width, pixels), scale, 6);
        Check(pixels == 454 && frame.Body == 6 * compact.Row,
            FormattableString.Invariant($"101-DPI 454px List allocates six whole rows in double precision ({pixels}px, {frame.Body})"));
        Check(compact.Viewport(pixels, (float)scale) == 5 * compact.Row,
            "narrowing the 101-DPI scale to float loses the sixth row, so allocation must stay double");
        Check(compact.Frame(new SizeInt32(width, pixels - 1), scale, 6).Body == 5 * compact.Row,
            "a 101-DPI window one pixel short gains no clipped row");
    }

    private static void CheckAllocation(ListMetrics metrics)
    {
        double nonBody = metrics.Height(1) - metrics.Row;
        var area = new RectInt32(-7680, -4320, 15360, 8640);
        for (int dpi = 97; dpi <= 480; dpi++)
        {
            double scale = dpi / 96.0;
            for (int count = 0; count == 0 || metrics.Height(count) > metrics.Height(count - 1); count++)
            {
                var bounds = CursorPosition.CalculateBounds(ListMetrics.WindowWidth, metrics.Height(count),
                    ListMetrics.Anchor, new PointInt32(0, 0), area, scale, SelectorPlacement.BesideCursor);
                var size = new SizeInt32(bounds.Width, bounds.Height);
                var frame = metrics.Frame(size, scale, count);
                string at = FormattableString.Invariant($"(row {metrics.Row}, count {count}, DPI {dpi}, {bounds.Height}px)");
                double expected = count == 0 ? metrics.Empty : count * metrics.Row;
                Check(frame.Body == expected, $"rounded native List height keeps the measured body {at}");
                Check(ListMetrics.ShadowMargin + ListMetrics.Padding + frame.Body + ListMetrics.FooterGap <= frame.FooterTop + 0.5 / scale + 1e-9,
                    $"footer starts below the measured body and gap {at}");
                Check(Math.Abs(frame.FooterTop + metrics.Footer + ListMetrics.Padding - ListMetrics.ShadowMargin - frame.Height) < 1e-9,
                    $"footer fits inside the List frame {at}");
                if (count == 0) continue;
                var shortened = metrics.Frame(new SizeInt32(size.Width, size.Height - 1), scale, count);
                Check(shortened.Body == (count - 1) * metrics.Row
                    && CursorPosition.NativeLength(nonBody + shortened.Body, scale) <= size.Height - 1,
                    $"a window one pixel short loses only the clipped row {at}");
            }
        }
    }

    private static void Check(bool condition, string name)
    {
        if (!condition) throw new Exception("List measurement check failed: " + name);
    }
}
