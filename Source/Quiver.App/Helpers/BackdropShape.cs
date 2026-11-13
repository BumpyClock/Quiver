using Microsoft.Graphics.Canvas.Geometry;
using Microsoft.UI.Xaml.Media;
using Quiver.App.Controls;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;
using Windows.Foundation;
using Windows.Graphics;

namespace Quiver.App.Helpers;

internal sealed partial record BackdropShape(
    IReadOnlyList<(Vector2 Center, float Radius)> Circles, IReadOnlyList<Vector2[]> Connectors,
    Rect Pill, float PillCornerRadius, Vector2 Size)
{
    private const int RegionAnd = 1;
    private const int RegionOr = 2;
    private const int Winding = 2;

    public static BackdropShape FromMetrics(RadialMetrics metrics, Vector2 size, double padding = 0)
    {
        float scale = (float)(size.X / metrics.WindowWidth);
        var center = new Vector2((float)metrics.FlowerCenter.X, (float)metrics.FlowerCenter.Y) * scale;
        var circles = new List<(Vector2 Center, float Radius)>(metrics.Count + 1);
        var connectors = new List<Vector2[]>(metrics.Count);
        if (metrics.CenterFillRadius > 0)
        {
            circles.Add((center, (float)(metrics.CenterFillRadius + padding) * scale));
        }
        for (int index = 0; index < metrics.Count; index++)
        {
            var point = metrics.ItemCenter(index, default);
            circles.Add((center + new Vector2((float)point.X, (float)point.Y) * scale,
                (float)(RadialMetrics.PetalRadius + padding) * scale));
            if (metrics.Count > 1)
            {
                var tangents = metrics.SelectionTangents(index, metrics.FlowerCenter, RadialMetrics.SelectionInset + padding);
                connectors.Add([ToPixels(tangents.PetalStart), ToPixels(tangents.PetalEnd),
                    ToPixels(tangents.HubStart), ToPixels(tangents.HubEnd)]);
            }
        }
        var pill = metrics.PillBounds;
        return new BackdropShape(circles, connectors,
            new Rect((pill.X - padding) * scale, (pill.Y - padding) * scale,
                (pill.Width + 2 * padding) * scale, (pill.Height + 2 * padding) * scale),
            (float)(RadialMetrics.PillCornerRadius + padding) * scale, size);

        Vector2 ToPixels(Point point) => new((float)point.X * scale, (float)point.Y * scale);
    }

    public static BackdropShape FromList(Vector2 size, float scale, double padding = 0)
    {
        var bounds = ListMetrics.Bounds(size.X / scale, size.Y / scale);
        return new BackdropShape([], [],
            new Rect((bounds.X - padding) * scale, (bounds.Y - padding) * scale,
                (bounds.Width + 2 * padding) * scale, (bounds.Height + 2 * padding) * scale),
            (float)(ListMetrics.CornerRadius + padding) * scale, size);
    }

    public CanvasGeometry CreateGeometry()
    {
        CanvasGeometry outline = CanvasGeometry.CreateRoundedRectangle(null, Pill, PillCornerRadius, PillCornerRadius);
        try
        {
            foreach (var (center, radius) in Circles)
            {
                using var circle = CanvasGeometry.CreateCircle(null, center, radius);
                var combined = outline.CombineWith(circle, Matrix3x2.Identity, CanvasGeometryCombine.Union, 0.001f);
                outline.Dispose();
                outline = combined;
            }
            foreach (var points in Connectors)
            {
                using var connector = CanvasGeometry.CreatePolygon(null, points);
                var combined = outline.CombineWith(connector, Matrix3x2.Identity, CanvasGeometryCombine.Union, 0.001f);
                outline.Dispose();
                outline = combined;
            }
            return outline;
        }
        catch
        {
            outline.Dispose();
            throw;
        }
    }

    public GeometryGroup CreateXamlGeometry()
    {
        var group = new GeometryGroup { FillRule = FillRule.Nonzero };
        foreach (var (center, radius) in Circles)
            group.Children.Add(new EllipseGeometry
            {
                Center = new Point(center.X, center.Y), RadiusX = radius, RadiusY = radius
            });
        foreach (var points in Connectors)
        {
            var figure = new PathFigure
            {
                StartPoint = new Point(points[0].X, points[0].Y), IsClosed = true
            };
            for (int index = 1; index < points.Length; index++)
                figure.Segments.Add(new LineSegment { Point = new Point(points[index].X, points[index].Y) });
            group.Children.Add(new PathGeometry { Figures = { figure } });
        }
        var pill = new PathFigure
        {
            StartPoint = new Point(Pill.Left + PillCornerRadius, Pill.Top), IsClosed = true
        };
        foreach (var (line, arc) in new[]
        {
            (new Point(Pill.Right - PillCornerRadius, Pill.Top), new Point(Pill.Right, Pill.Top + PillCornerRadius)),
            (new Point(Pill.Right, Pill.Bottom - PillCornerRadius), new Point(Pill.Right - PillCornerRadius, Pill.Bottom)),
            (new Point(Pill.Left + PillCornerRadius, Pill.Bottom), new Point(Pill.Left, Pill.Bottom - PillCornerRadius)),
            (new Point(Pill.Left, Pill.Top + PillCornerRadius), new Point(Pill.Left + PillCornerRadius, Pill.Top))
        })
        {
            pill.Segments.Add(new LineSegment { Point = line });
            pill.Segments.Add(new ArcSegment
            {
                Point = arc, Size = new Size(PillCornerRadius, PillCornerRadius),
                SweepDirection = SweepDirection.Clockwise
            });
        }
        group.Children.Add(new PathGeometry { Figures = { pill } });
        return group;
    }

    public unsafe IntPtr CreateWindowRegion(int inset)
    {
        IntPtr region = CreateRoundRectRgn(
            (int)Math.Floor(Pill.Left), (int)Math.Floor(Pill.Top),
            (int)Math.Ceiling(Pill.Right) + 1, (int)Math.Ceiling(Pill.Bottom) + 1,
            (int)Math.Round(2 * PillCornerRadius), (int)Math.Round(2 * PillCornerRadius));
        if (region == IntPtr.Zero)
        {
            Debug.WriteLine("Could not create the selector window region.");
            return IntPtr.Zero;
        }
        try
        {
            foreach (var (center, radius) in Circles)
            {
                if (!Combine(CreateEllipticRgn(
                    (int)MathF.Floor(center.X - radius), (int)MathF.Floor(center.Y - radius),
                    (int)MathF.Ceiling(center.X + radius) + 1, (int)MathF.Ceiling(center.Y + radius) + 1), RegionOr))
                {
                    return IntPtr.Zero;
                }
            }
            var vertices = stackalloc PointInt32[4];
            foreach (var points in Connectors)
            {
                for (int index = 0; index < points.Length; index++)
                {
                    vertices[index] = new PointInt32((int)Math.Round(points[index].X), (int)Math.Round(points[index].Y));
                }
                if (!Combine(CreatePolygonRgn(vertices, points.Length, Winding), RegionOr)) return IntPtr.Zero;
            }
            if (!Combine(CreateRectRgn(inset, inset, (int)Size.X - inset, (int)Size.Y - inset), RegionAnd))
            {
                return IntPtr.Zero;
            }
            IntPtr result = region;
            region = IntPtr.Zero;
            return result;
        }
        finally
        {
            if (region != IntPtr.Zero) DeleteObject(region);
        }

        bool Combine(IntPtr component, int mode)
        {
            if (component == IntPtr.Zero)
            {
                Debug.WriteLine("Could not create a selector region component.");
                return false;
            }
            try
            {
                if (CombineRgn(region, region, component, mode) != 0) return true;
                Debug.WriteLine("Could not combine the selector window region.");
                return false;
            }
            finally
            {
                DeleteObject(component);
            }
        }
    }

    [LibraryImport("gdi32.dll")]
    private static partial IntPtr CreateRoundRectRgn(int left, int top, int right, int bottom, int widthEllipse, int heightEllipse);

    [LibraryImport("gdi32.dll")]
    private static unsafe partial IntPtr CreatePolygonRgn(PointInt32* points, int count, int fillMode);

    [LibraryImport("gdi32.dll")]
    private static partial int CombineRgn(IntPtr destination, IntPtr source1, IntPtr source2, int mode);

    [LibraryImport("gdi32.dll")]
    private static partial IntPtr CreateRectRgn(int left, int top, int right, int bottom);

    [LibraryImport("gdi32.dll")]
    private static partial IntPtr CreateEllipticRgn(int left, int top, int right, int bottom);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DeleteObject(IntPtr hObject);
}
