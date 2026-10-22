using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Effects;
using Microsoft.Graphics.Canvas.Geometry;
using Microsoft.UI.Dispatching;
using System;
using System.Collections.Generic;
using Windows.Foundation;
using System.Numerics;
using System.Runtime.InteropServices;
using Windows.UI;
using Windows.UI.Composition;
using Windows.UI.Composition.Desktop;
using Windows.UI.ViewManagement;
using WinRT;

namespace Quiver.App.Helpers;

/// <summary>
/// Paints host-backdrop acrylic behind the selector, clipped to the union of its petals and name pill.
/// </summary>
internal sealed partial class AcrylicShapeLayer : IDisposable
{
    private static readonly Guid IID_ICompositorDesktopInterop = new("29E691FA-4567-4DCA-B319-D0F207EB6807");
    private static IntPtr dispatcherQueueController;

    private readonly Compositor compositor;
    private readonly DesktopWindowTarget target;
    private readonly ContainerVisual root;
    private readonly SpriteVisual plate;
    private readonly ShapeVisual edge;
    private readonly UISettings uiSettings = new();
    private readonly DispatcherQueue dispatcherQueue;
    private readonly bool hostBackdropSupported;
    private AcrylicShape? shape;
    private bool isDark = true;
    private bool disposed;
    private CompositionEffectFactory? acrylicFactory;
    private bool acrylicFactoryIsDark;

    public AcrylicShapeLayer(IntPtr hwnd)
    {
        EnsureDispatcherQueue();
        int useHostBackdrop = 1;
        hostBackdropSupported = DwmSetWindowAttribute(hwnd, DwmaUseHostBackdropBrush, ref useHostBackdrop, sizeof(int)) >= 0;
        dispatcherQueue = DispatcherQueue.GetForCurrentThread();
        compositor = new Compositor();
        target = CreateDesktopWindowTarget(compositor, hwnd);

        root = compositor.CreateContainerVisual();
        root.RelativeSizeAdjustment = Vector2.One;
        target.Root = root;

        plate = compositor.CreateSpriteVisual();
        plate.RelativeSizeAdjustment = Vector2.One;
        root.Children.InsertAtTop(plate);

        edge = compositor.CreateShapeVisual();
        edge.RelativeSizeAdjustment = Vector2.One;
        root.Children.InsertAtTop(edge);

        uiSettings.AdvancedEffectsEnabledChanged += UISettings_AdvancedEffectsEnabledChanged;
    }

    public void SetShape(AcrylicShape shape, bool dark)
    {
        this.shape = shape;
        isDark = dark;

        // One plate clipped to the union keeps overlapping petals seamless.
        CanvasGeometry outline = CanvasGeometry.CreateRoundedRectangle(
            null, shape.Pill, shape.PillCornerRadius, shape.PillCornerRadius);
        foreach (var (center, radius) in shape.Circles)
        {
            outline = outline.CombineWith(
                CanvasGeometry.CreateCircle(null, center, radius), Matrix3x2.Identity, CanvasGeometryCombine.Union);
        }

        var path = new CompositionPath(outline);
        plate.Clip = compositor.CreateGeometricClip(compositor.CreatePathGeometry(path));

        // The window region cuts the outer half of the stroke, leaving a hairline that separates the
        // acrylic from a backdrop of the same tone.
        var stroke = compositor.CreateSpriteShape(compositor.CreatePathGeometry(path));
        stroke.StrokeBrush = compositor.CreateColorBrush(EdgeColor);
        stroke.StrokeThickness = 2;
        edge.Shapes.Clear();
        edge.Shapes.Add(stroke);
        bool useBlur = hostBackdropSupported && uiSettings.AdvancedEffectsEnabled;
        plate.Brush = useBlur ? CreateAcrylicBrush() : compositor.CreateColorBrush(FallbackColor);
    }

    private void UISettings_AdvancedEffectsEnabledChanged(UISettings sender, object args)
    {
        dispatcherQueue.TryEnqueue(() =>
        {
            if (!disposed && shape is not null)
            {
                SetShape(shape, isDark);
            }
        });
    }

    public void PlayOpenAnimation(Vector2 centerPx, TimeSpan duration)
    {
        var easing = compositor.CreateCubicBezierEasingFunction(new Vector2(0.16f, 1f), new Vector2(0.3f, 1f));
        root.CenterPoint = new Vector3(centerPx, 0);

        var scale = compositor.CreateVector3KeyFrameAnimation();
        scale.InsertKeyFrame(0f, new Vector3(0.82f, 0.82f, 1));
        scale.InsertKeyFrame(1f, Vector3.One, easing);
        scale.Duration = duration;

        var opacity = compositor.CreateScalarKeyFrameAnimation();
        opacity.InsertKeyFrame(0f, 0f);
        opacity.InsertKeyFrame(1f, 1f, easing);
        opacity.Duration = duration;

        root.StartAnimation("Scale", scale);
        root.StartAnimation("Opacity", opacity);
    }

    private CompositionEffectBrush CreateAcrylicBrush()
    {
        if (acrylicFactory is null || acrylicFactoryIsDark != isDark)
        {
            acrylicFactory = CreateAcrylicFactory();
            acrylicFactoryIsDark = isDark;
        }

        var brush = acrylicFactory.CreateBrush();
        brush.SetSourceParameter("Backdrop", compositor.CreateHostBackdropBrush());
        return brush;
    }

    private CompositionEffectFactory CreateAcrylicFactory()
    {
        var backdropHueAtPlateBrightness = new BlendEffect
        {
            Mode = BlendEffectMode.Color,
            Background = new GaussianBlurEffect
            {
                BlurAmount = 18f,
                BorderMode = EffectBorderMode.Hard,
                Source = new CompositionEffectSourceParameter("Backdrop"),
            },
            Foreground = new ColorSourceEffect { Color = LuminosityColor },
        };
        var tinted = new CompositeEffect { Mode = CanvasComposite.SourceOver };
        tinted.Sources.Add(backdropHueAtPlateBrightness);
        tinted.Sources.Add(new ColorSourceEffect { Color = TintColor });
        return compositor.CreateEffectFactory(tinted);
    }

    private Color LuminosityColor => isDark ? Color.FromArgb(0xD9, 0x2C, 0x2C, 0x2C) : Color.FromArgb(0xD9, 0xFC, 0xFC, 0xFC);

    private Color TintColor => isDark ? Color.FromArgb(0x59, 0x2C, 0x2C, 0x2C) : Color.FromArgb(0x59, 0xFC, 0xFC, 0xFC);

    private Color EdgeColor => isDark ? Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x1A, 0x00, 0x00, 0x00);

    private Color FallbackColor => isDark ? Color.FromArgb(0xFF, 0x2C, 0x2C, 0x2C) : Color.FromArgb(0xFF, 0xF9, 0xF9, 0xF9);

    public void Dispose()
    {
        disposed = true;
        uiSettings.AdvancedEffectsEnabledChanged -= UISettings_AdvancedEffectsEnabledChanged;
        target.Dispose();
        compositor.Dispose();
    }

    #region Interop
    private static void EnsureDispatcherQueue()
    {
        if (global::Windows.System.DispatcherQueue.GetForCurrentThread() is not null || dispatcherQueueController != IntPtr.Zero)
        {
            return;
        }

        var options = new DispatcherQueueOptions
        {
            Size = Marshal.SizeOf<DispatcherQueueOptions>(),
            ThreadType = DqTypeThreadCurrent,
            ApartmentType = DqtatComSta,
        };
        Marshal.ThrowExceptionForHR(CreateDispatcherQueueController(options, out dispatcherQueueController));
    }

    private static unsafe DesktopWindowTarget CreateDesktopWindowTarget(Compositor compositor, IntPtr hwnd)
    {
        IntPtr unknown = MarshalInspectable<Compositor>.FromManaged(compositor);
        try
        {
            Guid iid = IID_ICompositorDesktopInterop;
            Marshal.ThrowExceptionForHR(Marshal.QueryInterface(unknown, in iid, out IntPtr interop));
            try
            {
                var vtable = *(IntPtr**)interop;
                var create = (delegate* unmanaged[Stdcall]<IntPtr, IntPtr, int, IntPtr*, int>)vtable[CreateDesktopWindowTargetSlot];
                IntPtr targetAbi;
                Marshal.ThrowExceptionForHR(create(interop, hwnd, 0, &targetAbi));
                try
                {
                    return DesktopWindowTarget.FromAbi(targetAbi);
                }
                finally
                {
                    Marshal.Release(targetAbi);
                }
            }
            finally
            {
                Marshal.Release(interop);
            }
        }
        finally
        {
            Marshal.Release(unknown);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DispatcherQueueOptions
    {
        public int Size;
        public int ThreadType;
        public int ApartmentType;
    }

    private const int DwmaUseHostBackdropBrush = 17;
    private const int DqTypeThreadCurrent = 2;
    private const int DqtatComSta = 2;
    private const int CreateDesktopWindowTargetSlot = 3;

    [LibraryImport("dwmapi.dll")]
    private static partial int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    [LibraryImport("CoreMessaging.dll")]
    private static partial int CreateDispatcherQueueController(DispatcherQueueOptions options, out IntPtr dispatcherQueueController);
    #endregion
}

internal sealed record AcrylicShape(IReadOnlyList<(Vector2 Center, float Radius)> Circles, Rect Pill, float PillCornerRadius);
