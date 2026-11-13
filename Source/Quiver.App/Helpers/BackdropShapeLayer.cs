using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Hosting;
using Path = Microsoft.UI.Xaml.Shapes.Path;
using System;
using System.Numerics;
using Windows.UI;
using Windows.UI.ViewManagement;

namespace Quiver.App.Helpers;

internal sealed class BackdropShapeLayer : IDisposable
{
    private readonly FrameworkElement host;
    private readonly Path plate;
    private readonly SpriteVisual shadowHost;
    private readonly DropShadow shadow;
    private readonly CompositionBrush mask;
    private readonly AccessibilitySettings accessibility = new();
    private bool disposed;

    public BackdropShapeLayer(FrameworkElement host, Path plate)
    {
        this.host = host;
        this.plate = plate;
        var compositor = ElementCompositionPreview.GetElementVisual(host).Compositor;
        mask = plate.GetAlphaMask();
        shadow = compositor.CreateDropShadow();
        shadow.Mask = mask;
        shadow.BlurRadius = 16;
        shadow.Offset = new Vector3(0, 4, 0);
        shadowHost = compositor.CreateSpriteVisual();
        shadowHost.RelativeSizeAdjustment = Vector2.One;
        shadowHost.Shadow = shadow;
        ElementCompositionPreview.SetElementChildVisual(host, shadowHost);
        SetTheme(true);
    }

    public void SetShape(BackdropShape shape, bool dark)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        plate.Data = shape.CreateXamlGeometry();
        host.Width = plate.Width = shape.Size.X;
        host.Height = plate.Height = shape.Size.Y;
        SetTheme(dark);
    }

    public void SetTheme(bool dark)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        shadow.Color = dark ? Color.FromArgb(255, 0, 0, 0) : Color.FromArgb(255, 26, 36, 48);
        shadow.Opacity = accessibility.HighContrast ? 0 : dark ? 0.55f : 0.22f;
    }

    public void SetVisible(bool visible)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        host.Visibility = plate.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        ElementCompositionPreview.SetElementChildVisual(host, null);
        shadowHost.Shadow = null;
        shadow.Mask = null;
        shadowHost.Dispose();
        shadow.Dispose();
        mask.Dispose();
    }
}
