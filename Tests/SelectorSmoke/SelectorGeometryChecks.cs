using Microsoft.Graphics.Canvas.Geometry;
using Microsoft.UI.Xaml.Media;
using Quiver.App.Controls;
using Quiver.App.Helpers;
using System.Numerics;
using System.Runtime.InteropServices;
using Windows.Foundation;

namespace Quiver.SelectorSmoke;

internal static partial class SelectorGeometryChecks
{
    public static void Run()
    {
        int selections = 0;
        foreach (double dpiScale in new[] { 1, 1.25, 1.5, 1.75, 2, 3 })
        {
            for (int count = 0; count <= 10; count++)
            {
                var metrics = RadialMetrics.For(count);
                var size = new Vector2((float)Math.Round(metrics.WindowWidth * dpiScale),
                    (float)Math.Round(metrics.WindowHeight * dpiScale));
                float scale = size.X / (float)metrics.WindowWidth;
                var shape = BackdropShape.FromMetrics(metrics, size);
                using var backdrop = shape.CreateGeometry();
                // Ignore at most 0.05 DIP of Win2D arc flattening, not visible connector gaps.
                using var tolerance = backdrop.Stroke(0.1f * scale);
                using var tolerantBackdrop = backdrop.CombineWith(tolerance, Matrix3x2.Identity, CanvasGeometryCombine.Union, 0.001f);
                var padded = BackdropShape.FromMetrics(metrics, size, RadialMetrics.ShadowMargin);
                int inset = (int)Math.Ceiling(4 * scale);
                nint region = padded.CreateWindowRegion(inset);
                Check(region != 0, $"native region created for {count} browsers at {dpiScale}x");
                try
                {
                    for (int index = 0; index < count; index++)
                    {
                        using var selection = CreateSelection(metrics, index, scale);
                        using var stroke = selection.Stroke(2 * scale);
                        using var painted = selection.CombineWith(stroke, Matrix3x2.Identity, CanvasGeometryCombine.Union, 0.001f);
                        using var outside = painted.CombineWith(tolerantBackdrop, Matrix3x2.Identity, CanvasGeometryCombine.Exclude, 0.001f);
                        float area = outside.ComputeArea() / (scale * scale);
                        Check(area < 0.01, $"full selection and stroke fit acrylic/shadow mask ({count} browsers, "
                            + $"index {index}, {dpiScale}x, outside {area:F4} DIP squared)");

                        using var shadowStroke = selection.Stroke((float)(2 + 2 * RadialMetrics.ShadowMargin) * scale);
                        using var envelope = painted.CombineWith(shadowStroke, Matrix3x2.Identity, CanvasGeometryCombine.Union, 0.001f);
                        foreach (var (surface, rasterTolerance) in new[] { (painted, 0), (envelope, 2) })
                        {
                            using var boundary = surface.Outline();
                            float length = boundary.ComputePathLength();
                            for (float distance = 0; distance < length; distance += 0.5f)
                            {
                                var point = boundary.ComputePointOnPath(distance);
                                int x = (int)Math.Floor(point.X);
                                int y = (int)Math.Floor(point.Y);
                                if (x < inset + 1 || y < inset + 1 || x >= size.X - inset - 1 || y >= size.Y - inset - 1)
                                {
                                    continue;
                                }
                                bool covered = false;
                                // No tolerance for selection ink. GDI's shadow edge can differ by two physical pixels.
                                for (int dx = -rasterTolerance; dx <= rasterTolerance && !covered; dx++)
                                {
                                    for (int dy = -rasterTolerance; dy <= rasterTolerance && !covered; dy++)
                                    {
                                        covered = PtInRegion(region, x + dx, y + dy);
                                    }
                                }
                                Check(covered, $"native region contains selection ink or padded shadow envelope ({count} browsers, "
                                    + $"index {index}, {dpiScale}x, point {point}, raster tolerance {rasterTolerance})");
                            }
                        }
                        selections++;
                    }
                    Check(!PtInRegion(region, 0, 0), "native region preserves window-edge clipping");
                }
                finally
                {
                    DeleteObject(region);
                }
            }
        }
        Console.WriteLine($"PASS: {selections} full selection/stroke masks and native shadow envelopes across counts 0-10 and DPI 100-300%.");
    }

    internal static CanvasGeometry CreateSelection(RadialMetrics metrics, int index, float scale, Point? actualOffset = null)
    {
        using var builder = new CanvasPathBuilder(null);
        var offset = actualOffset ?? new Point(metrics.FlowerCenter.X - metrics.FlowerOrigin.X,
            metrics.FlowerCenter.Y - metrics.FlowerOrigin.Y);
        var figure = metrics.SelectionGeometry(index).Figures[0];
        builder.BeginFigure(ToPixels(figure.StartPoint));
        foreach (var segment in figure.Segments)
        {
            if (segment is ArcSegment arc)
            {
                builder.AddArc(ToPixels(arc.Point), (float)arc.Size.Width * scale, (float)arc.Size.Height * scale,
                    0, CanvasSweepDirection.Clockwise, arc.IsLargeArc ? CanvasArcSize.Large : CanvasArcSize.Small);
            }
            else
            {
                builder.AddLine(ToPixels(((LineSegment)segment).Point));
            }
        }
        builder.EndFigure(CanvasFigureLoop.Closed);
        return CanvasGeometry.CreatePath(builder);

        Vector2 ToPixels(Point point) => new((float)(point.X + offset.X) * scale, (float)(point.Y + offset.Y) * scale);
    }

    private static void Check(bool condition, string name)
    {
        if (!condition) throw new Exception($"Selector geometry check failed: {name}");
    }

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool PtInRegion(nint region, int x, int y);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DeleteObject(nint handle);
}
