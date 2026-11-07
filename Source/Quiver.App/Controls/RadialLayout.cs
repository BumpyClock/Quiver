using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System;
using Windows.Foundation;

namespace Quiver.App.Controls;

/// <summary>
/// Geometry shared by the selector's XAML layout, window region, and backdrop shape. Petals overlap their
/// neighbors and a center disc fills the gap between them; the name pill sits below the flower.
/// </summary>
public readonly record struct RadialMetrics(int Count, double RingRadius, double OuterRadius)
{
    public const double ItemSize = 104;
    public const double PetalRadius = ItemSize / 2;
    public const double PillHeight = 56;
    public const double PillCornerRadius = 12;
    public const double PillGap = 14;
    private const double MinPillWidth = 264;
    private const double MaxPillWidth = 320;
    public const double ShadowMargin = 20;
    public const double HubRadius = 24;
    private const double PairRingRadius = 58;
    private const double PairCenterFillRadius = 27;
    private const double MinRingRadius = 64;
    private const double HalfNeighborDistance = 40;
    private const double CenterFillInset = 20;

    public static RadialMetrics For(int count)
    {
        double ringRadius = count switch
        {
            <= 1 => 0,
            2 => PairRingRadius,
            _ => Math.Max(MinRingRadius, HalfNeighborDistance / Math.Sin(Math.PI / count))
        };

        return new RadialMetrics(count, ringRadius, count == 0 ? 0 : ringRadius + PetalRadius);
    }

    public double FlowerSize => 2 * OuterRadius;

    public double FlowerHeight => Count == 2 ? ItemSize : FlowerSize;

    public Point FlowerOrigin => new(OuterRadius, FlowerHeight / 2);

    public double CenterFillRadius => Count switch
    {
        <= 1 => 0,
        2 => PairCenterFillRadius,
        _ => RingRadius - CenterFillInset
    };

    public double PillWidth => Math.Clamp(FlowerSize, MinPillWidth, MaxPillWidth);

    public double FlowerTop => ShadowMargin;

    public double PillTop => FlowerTop + FlowerHeight + PillGap;

    public double WindowWidth => Math.Max(FlowerSize, PillWidth) + 2 * ShadowMargin;

    public double WindowHeight => PillTop + PillHeight + ShadowMargin;

    /// <summary>The flower center in window coordinates; it opens under the cursor.</summary>
    public Point FlowerCenter => new(WindowWidth / 2, FlowerTop + FlowerOrigin.Y);

    public Rect PillBounds => new((WindowWidth - PillWidth) / 2, PillTop, PillWidth, PillHeight);

    /// <summary>Direction from the flower center to the petal, in radians clockwise from the positive x axis.</summary>
    public double ItemAngle(int index)
    {
        if (Count == 2)
        {
            return index == 0 ? Math.PI : 0;
        }

        return -Math.PI / 2 + 2 * Math.PI * index / Math.Max(Count, 1);
    }

    public Point ItemCenter(int index, Point origin)
    {
        double angle = ItemAngle(index);
        return new Point(origin.X + RingRadius * Math.Cos(angle), origin.Y + RingRadius * Math.Sin(angle));
    }

    /// <summary>Shared tangent endpoints. Outset expands both circles for the stroke or native shadow band.</summary>
    public (Point PetalStart, Point PetalEnd, Point HubStart, Point HubEnd) SelectionTangents(
        int index, Point origin, double outset = 0)
    {
        double radius = PetalRadius - 1 + outset;
        double hubRadius = HubRadius + outset;
        var center = ItemCenter(index, origin);
        double angle = ItemAngle(index);
        double tangent = Math.Acos((hubRadius - radius) / RingRadius);
        var upper = new Point(Math.Cos(angle - tangent), Math.Sin(angle - tangent));
        var lower = new Point(Math.Cos(angle + tangent), Math.Sin(angle + tangent));
        return (
            new Point(center.X + radius * upper.X, center.Y + radius * upper.Y),
            new Point(center.X + radius * lower.X, center.Y + radius * lower.Y),
            new Point(origin.X + hubRadius * lower.X, origin.Y + hubRadius * lower.Y),
            new Point(origin.X + hubRadius * upper.X, origin.Y + hubRadius * upper.Y));
    }

    public PathGeometry SelectionGeometry(int index)
    {
        double radius = PetalRadius - 1;
        var center = ItemCenter(index, FlowerOrigin);
        if (Count <= 1)
        {
            var circle = new PathFigure { StartPoint = new Point(center.X, center.Y - radius), IsClosed = true };
            circle.Segments.Add(new ArcSegment
            {
                Point = new Point(center.X, center.Y + radius),
                Size = new Size(radius, radius),
                SweepDirection = SweepDirection.Clockwise
            });
            circle.Segments.Add(new ArcSegment
            {
                Point = circle.StartPoint,
                Size = new Size(radius, radius),
                SweepDirection = SweepDirection.Clockwise
            });
            return new PathGeometry { Figures = { circle } };
        }

        var tangents = SelectionTangents(index, FlowerOrigin);
        var figure = new PathFigure { StartPoint = tangents.PetalStart, IsClosed = true };
        figure.Segments.Add(new ArcSegment
        {
            Point = tangents.PetalEnd,
            Size = new Size(radius, radius),
            IsLargeArc = true,
            SweepDirection = SweepDirection.Clockwise
        });
        figure.Segments.Add(new LineSegment
        {
            Point = tangents.HubStart
        });
        figure.Segments.Add(new ArcSegment
        {
            Point = tangents.HubEnd,
            Size = new Size(HubRadius, HubRadius),
            SweepDirection = SweepDirection.Clockwise
        });
        return new PathGeometry { Figures = { figure } };
    }
}

public sealed partial class RadialLayout : NonVirtualizingLayout
{
    protected override Size MeasureOverride(NonVirtualizingLayoutContext context, Size availableSize)
    {
        var metrics = RadialMetrics.For(context.Children.Count);
        foreach (var child in context.Children)
        {
            child.Measure(new Size(RadialMetrics.ItemSize, RadialMetrics.ItemSize));
        }

        return new Size(metrics.FlowerSize, metrics.FlowerHeight);
    }

    protected override Size ArrangeOverride(NonVirtualizingLayoutContext context, Size finalSize)
    {
        var children = context.Children;
        var metrics = RadialMetrics.For(children.Count);
        var origin = new Point(finalSize.Width / 2, finalSize.Height / 2);
        for (int i = 0; i < children.Count; i++)
        {
            var center = metrics.ItemCenter(i, origin);
            children[i].Arrange(new Rect(
                center.X - RadialMetrics.ItemSize / 2,
                center.Y - RadialMetrics.ItemSize / 2,
                RadialMetrics.ItemSize,
                RadialMetrics.ItemSize));
        }

        return finalSize;
    }
}
