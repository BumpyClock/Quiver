using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;

namespace Quiver.App.Controls;

public sealed partial class RadialLayout : NonVirtualizingLayout
{
    protected override Size MeasureOverride(NonVirtualizingLayoutContext context, Size availableSize)
    {
        var metrics = RadialMetrics.For(context.Children.Count);
        for (int index = 0; index < context.Children.Count; index++)
        {
            var child = context.Children[index];
            if (child is RadialBrowserItem item)
                item.SetInwardSide(metrics.ItemCenter(index, metrics.FlowerOrigin).X <= metrics.FlowerOrigin.X);
            child.Measure(new Size(RadialMetrics.ItemSize, RadialMetrics.ItemSize));
        }

        return new Size(metrics.FlowerSize, metrics.FlowerHeight);
    }

    protected override Size ArrangeOverride(NonVirtualizingLayoutContext context, Size finalSize)
    {
        var children = context.Children;
        var metrics = RadialMetrics.For(children.Count);
        var origin = new Point(finalSize.Width / 2, metrics.FlowerOrigin.Y);
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
