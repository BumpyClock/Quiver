using Microsoft.UI.Xaml.Controls;
using System;
using Windows.Foundation;

namespace Quiver.App.Controls;

public readonly record struct RadialMetrics(int Count, double RingRadius, double OuterRadius, double WindowSize)
{
    public const double ItemSize = 80;
    public const double HubRadius = 50;
    private const double WindowMargin = 4;
    private const double PetalGap = 10;
    private const double HubGap = 8;

    public static RadialMetrics For(int count)
    {
        double petalRadius = ItemSize / 2;
        double ringRadius = HubRadius + HubGap + petalRadius;
        if (count >= 2)
        {
            ringRadius = Math.Max(ringRadius, (petalRadius + PetalGap / 2) / Math.Sin(Math.PI / count));
        }

        double outerRadius = ringRadius + petalRadius;
        return new RadialMetrics(count, ringRadius, outerRadius, 2 * (outerRadius + WindowMargin));
    }

    public Point ItemCenter(int index, Point origin)
    {
        const double top = -Math.PI / 2;
        double angle;
        if (Count == 2)
        {
            double halfSpread = Math.Asin((ItemSize / 2 + PetalGap / 2) / RingRadius);
            angle = top + (index == 0 ? -halfSpread : halfSpread);
        }
        else
        {
            angle = top + 2 * Math.PI * index / Math.Max(Count, 1);
        }

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

        double size = 2 * metrics.OuterRadius;
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
