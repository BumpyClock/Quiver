using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using XamlPath = Microsoft.UI.Xaml.Shapes.Path;
using Quiver.App.Controls;
using Quiver.App.Helpers;
using System.Numerics;

namespace Quiver.UiSmoke;

internal static class RenderingChecks
{
    public static async Task CheckBackdropLayerAsync(Window window)
    {
        var host = new Grid();
        var plate = new XamlPath { Fill = new SolidColorBrush(Microsoft.UI.Colors.Gray) };
        window.Content = new Grid { Children = { host, plate } };
        using var layer = new BackdropShapeLayer(host, plate);
        for (int index = 0; index < 8; index++)
        {
            var metrics = RadialMetrics.For(index + 2);
            var shape = BackdropShape.FromMetrics(metrics, new Vector2((float)metrics.WindowWidth, (float)metrics.WindowHeight));
            layer.SetShape(shape, index % 2 == 0);
            Check(plate.Data is GeometryGroup { FillRule: FillRule.Nonzero }
                && plate.Width == shape.Size.X && plate.Height == shape.Size.Y,
                "backdrop is an antialiased XAML surface in the content coordinate space");
            layer.SetTheme(true);
            layer.SetTheme(true);
            layer.SetTheme(false);
            layer.SetVisible(false);
            layer.SetVisible(true);
            await Task.Delay(20);
        }

        layer.Dispose();
        layer.Dispose();
        bool rejected = false;
        try
        {
            layer.SetTheme(true);
        }
        catch (ObjectDisposedException)
        {
            rejected = true;
        }
        Check(rejected, "disposed backdrop layer rejects rendering updates");
    }

    private static void Check(bool condition, string name)
    {
        if (!condition) throw new Exception($"Rendering check failed: {name}");
    }
}
