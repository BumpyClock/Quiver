using Microsoft.UI.Xaml;
using Quiver.App.Controls;
using Quiver.App.Helpers;
using System.Numerics;
using WinRT.Interop;

namespace Quiver.UiSmoke;

internal static class RenderingChecks
{
    public static async Task CheckBackdropLayerAsync(Window window)
    {
        using var layer = new BackdropShapeLayer(WindowNative.GetWindowHandle(window));
        for (int index = 0; index < 8; index++)
        {
            var metrics = RadialMetrics.For(index + 2);
            var shape = BackdropShape.FromMetrics(metrics, new Vector2((float)metrics.WindowWidth, (float)metrics.WindowHeight));
            layer.SetShape(shape, index % 2 == 0);
            layer.SetTheme(true);
            layer.SetTheme(true);
            layer.SetTheme(false);
            layer.PlayOpenAnimation(new Vector2(120, 100), TimeSpan.FromMilliseconds(10));
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
