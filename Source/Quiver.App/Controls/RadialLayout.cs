using Microsoft.UI.Xaml.Controls;
using System;
using Windows.Foundation;

namespace Quiver.App.Controls;

/// <summary>
/// Sizes shared by the radial selector window and its ring layout. All values are in DIPs.
/// </summary>
public readonly record struct RadialMetrics(int Count, double RingRadius, double DiscRadius, double WindowSize)
{
    public const double ItemSize = 60;
    public const double HubRadius = 56;
    private const double ItemSpacing = 14;
    private const double MinRingRadius = 104;
    private const double DiscPadding = 10;

    public static RadialMetrics For(int count)
    {
        // Grow the ring once the items no longer fit side by side on the minimum circumference.
        double ringRadius = Math.Max(MinRingRadius, count * (ItemSize + ItemSpacing) / (2 * Math.PI));
        double discRadius = ringRadius + ItemSize / 2 + DiscPadding;
        return new RadialMetrics(count, ringRadius, discRadius, 2 * discRadius);
    }

    public Point ItemCenter(int index, Point origin)
    {
        // Two items sit left and right; otherwise start at the top and go clockwise.
        double start = Count == 2 ? Math.PI : -Math.PI / 2;
        double angle = start + 2 * Math.PI * index / Math.Max(Count, 1);
        return new Point(origin.X + RingRadius * Math.Cos(angle), origin.Y + RingRadius * Math.Sin(angle));
    }
}

/// <summary>
/// Arranges ItemsRepeater children evenly on a circle around the center of the available space.
/// </summary>
public sealed partial class RadialLayout : NonVirtualizingLayout
{
    protected override Size MeasureOverride(NonVirtualizingLayoutContext context, Size availableSize)
    {
        var metrics = RadialMetrics.For(context.Children.Count);
        foreach (var child in context.Children)
        {
            child.Measure(new Size(RadialMetrics.ItemSize, RadialMetrics.ItemSize));
        }

        double size = 2 * metrics.DiscRadius;
        return new Size(size, size);
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
