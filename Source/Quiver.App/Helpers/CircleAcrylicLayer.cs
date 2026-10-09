using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Effects;
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.InteropServices;
using Windows.UI;
using Windows.UI.Composition;
using Windows.UI.Composition.Desktop;
using Windows.UI.ViewManagement;
using WinRT;

namespace Quiver.App.Helpers;

internal sealed partial class CircleAcrylicLayer : IDisposable
{
    private static readonly Guid IID_ICompositorDesktopInterop = new("29E691FA-4567-4DCA-B319-D0F207EB6807");
    private static IntPtr dispatcherQueueController;

    private readonly Compositor compositor;
    private readonly DesktopWindowTarget target;
    private readonly ContainerVisual root;
    private readonly ContainerVisual plates;
    private readonly UISettings uiSettings = new();
    private bool isDark = true;
    private CompositionEffectFactory? acrylicFactory;
    private bool acrylicFactoryIsDark;

    public CircleAcrylicLayer(IntPtr hwnd)
    {
        EnsureDispatcherQueue();
        int useHostBackdrop = 1;
        DwmSetWindowAttribute(hwnd, DwmaUseHostBackdropBrush, ref useHostBackdrop, sizeof(int));
        compositor = new Compositor();
        target = CreateDesktopWindowTarget(compositor, hwnd);

        root = compositor.CreateContainerVisual();
        root.RelativeSizeAdjustment = Vector2.One;
        target.Root = root;

        plates = compositor.CreateContainerVisual();
        plates.RelativeSizeAdjustment = Vector2.One;
        root.Children.InsertAtTop(plates);
    }

    public void SetCircles(IReadOnlyList<(Vector2 Center, float Radius)> circles, bool dark)
    {
        isDark = dark;
        plates.Children.RemoveAll();

        bool useBlur = uiSettings.AdvancedEffectsEnabled;
        foreach (var (center, radius) in circles)
        {
            var plate = compositor.CreateSpriteVisual();
            plate.Size = new Vector2(radius * 2);
            plate.Offset = new Vector3(center - new Vector2(radius), 0);
            plate.Brush = useBlur ? CreateAcrylicBrush() : compositor.CreateColorBrush(FallbackColor);
            var clipGeometry = compositor.CreateEllipseGeometry();
            clipGeometry.Center = new Vector2(radius);
            clipGeometry.Radius = new Vector2(radius);
            plate.Clip = compositor.CreateGeometricClip(clipGeometry);
            plates.Children.InsertAtTop(plate);
        }
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

    private Color FallbackColor => isDark ? Color.FromArgb(0xFF, 0x2C, 0x2C, 0x2C) : Color.FromArgb(0xFF, 0xF9, 0xF9, 0xF9);

    public void Dispose()
    {
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
