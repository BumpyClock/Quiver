using Microsoft.UI.Xaml.Controls;
using System;
using Windows.Foundation;

namespace Quiver.App.Controls;

/// <summary>
/// Geometry shared by the selector's XAML layout, window region, and acrylic shape. Petals overlap their
/// neighbors and a center disc fills the gap between them; the name pill sits below the flower.
/// </summary>
public readonly record struct RadialMetrics(int Count, double RingRadius, double OuterRadius)
{
    public const double ItemSize = 100;
    public const double PetalRadius = ItemSize / 2;
    public const double PillHeight = 48;
    public const double PillCornerRadius = 12;
    private const double MinPillWidth = 220;
    private const double MaxPillWidth = 320;
    private const double PillGap = 8;
    private const double WindowMargin = 4;
    // Ring radius as a multiple of the petal radius when few petals leave room to spare.
    private const double MinRingRatio = 1.2;
    // Half the distance between neighboring petal centers, as a fraction of the petal radius.
    private const double NeighborSpacing = 0.85;

    public static RadialMetrics For(int count)
    {
        double ringRadius = count switch
        {
            <= 1 => 0,
            2 => MinRingRatio * PetalRadius,
            _ => Math.Max(MinRingRatio * PetalRadius, NeighborSpacing * PetalRadius / Math.Sin(Math.PI / count))
        };

        return new RadialMetrics(count, ringRadius, count == 0 ? 0 : ringRadius + PetalRadius);
    }

    public double FlowerSize => 2 * OuterRadius;

    public double CenterFillRadius => Count > 1 ? RingRadius - PetalRadius / 2 : 0;

    public double PillWidth => Math.Clamp(FlowerSize, MinPillWidth, MaxPillWidth);

    public double FlowerTop => WindowMargin;

    public double PillTop => FlowerTop + FlowerSize + PillGap;

    public double WindowWidth => Math.Max(FlowerSize, PillWidth) + 2 * WindowMargin;

    public double WindowHeight => PillTop + PillHeight + WindowMargin;

    /// <summary>The flower center in window coordinates; it opens under the cursor.</summary>
    public Point FlowerCenter => new(WindowWidth / 2, FlowerTop + OuterRadius);

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

        return new Size(metrics.FlowerSize, metrics.FlowerSize);
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
