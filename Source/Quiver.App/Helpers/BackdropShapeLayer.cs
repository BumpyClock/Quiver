using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Effects;
using Microsoft.Graphics.Canvas.Geometry;
using Microsoft.UI.Dispatching;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;
using Windows.Foundation;
using Windows.UI;
using Windows.UI.Composition;
using Windows.UI.Composition.Desktop;
using Windows.UI.ViewManagement;
using WinRT;

namespace Quiver.App.Helpers;

internal sealed partial class BackdropShapeLayer : IDisposable
{
    private static readonly Guid IID_ICompositorDesktopInterop = new("29E691FA-4567-4DCA-B319-D0F207EB6807");
    private static readonly Color DarkAcrylicTintColor = Color.FromArgb(0x99, 0x20, 0x20, 0x20);
    private static readonly Color DarkFallbackColor = Color.FromArgb(0xFF, 0x20, 0x20, 0x20);
    private const float ShadowBlurRadius = 16;
    private const float ShadowOffsetY = 4;
    private static IntPtr dispatcherQueueController;

    private readonly Compositor compositor;
    private readonly DesktopWindowTarget target;
    private readonly DispatcherQueue dispatcherQueue;
    private readonly UISettings uiSettings = new();
    private readonly ContainerVisual root;
    private readonly CompositionPathGeometry outlineGeometry;
    private readonly CompositionColorBrush fallbackBrush;
    private readonly ShapeVisual silhouetteMask;
    private readonly CompositionSpriteShape silhouetteMaskFill;
    private readonly CompositionVisualSurface silhouetteMaskSurface;
    private readonly CompositionSurfaceBrush silhouetteMaskBrush;
    private readonly CompositionMaskBrush plateBrush;
    private readonly SpriteVisual plate;
    private readonly DropShadow shadow;
    private readonly SpriteVisual shadowHost;
    private readonly CubicBezierEasingFunction openEasing;
    private readonly Vector3KeyFrameAnimation openScaleAnimation;
    private readonly ScalarKeyFrameAnimation openOpacityAnimation;
    private CompositionBackdropBrush? backdropBrush;
    private CompositionEffectFactory? acrylicFactory;
    private CompositionEffectBrush? acrylicBrush;
    private bool acrylicUnsupported;
    private bool isDark = true;
    private bool disposed;

    public BackdropShapeLayer(IntPtr hwnd)
    {
        EnsureDispatcherQueue();
        // A host backdrop brush renders empty unless the window opts in.
        int useHostBackdrop = 1;
        if (DwmSetWindowAttribute(hwnd, DwmaUseHostBackdropBrush, ref useHostBackdrop, sizeof(int)) < 0)
        {
            acrylicUnsupported = true;
        }

        dispatcherQueue = DispatcherQueue.GetForCurrentThread();
        compositor = new Compositor();
        target = CreateDesktopWindowTarget(compositor, hwnd);

        root = compositor.CreateContainerVisual();
        root.RelativeSizeAdjustment = Vector2.One;
        target.Root = root;

        outlineGeometry = compositor.CreatePathGeometry();
        fallbackBrush = compositor.CreateColorBrush(DarkFallbackColor);

        silhouetteMask = compositor.CreateShapeVisual();
        silhouetteMaskFill = compositor.CreateSpriteShape(outlineGeometry);
        silhouetteMaskFill.FillBrush = fallbackBrush;
        silhouetteMask.Shapes.Add(silhouetteMaskFill);
        silhouetteMaskSurface = compositor.CreateVisualSurface();
        silhouetteMaskSurface.SourceVisual = silhouetteMask;
        silhouetteMaskBrush = compositor.CreateSurfaceBrush(silhouetteMaskSurface);
        silhouetteMaskBrush.Stretch = CompositionStretch.None;
        silhouetteMaskBrush.HorizontalAlignmentRatio = 0;
        silhouetteMaskBrush.VerticalAlignmentRatio = 0;

        shadow = compositor.CreateDropShadow();
        shadow.BlurRadius = ShadowBlurRadius;
        shadow.Offset = new Vector3(0, ShadowOffsetY, 0);
        shadow.Mask = silhouetteMaskBrush;
        ApplyShadowTheme();
        shadowHost = compositor.CreateSpriteVisual();
        shadowHost.RelativeSizeAdjustment = Vector2.One;
        shadowHost.Shadow = shadow;
        root.Children.InsertAtTop(shadowHost);

        plateBrush = compositor.CreateMaskBrush();
        plateBrush.Mask = silhouetteMaskBrush;
        plate = compositor.CreateSpriteVisual();
        plate.RelativeSizeAdjustment = Vector2.One;
        plate.Brush = plateBrush;
        root.Children.InsertAtTop(plate);
        UpdateMaterial();

        openEasing = compositor.CreateCubicBezierEasingFunction(new Vector2(0.16f, 1f), new Vector2(0.3f, 1f));
        openScaleAnimation = compositor.CreateVector3KeyFrameAnimation();
        openScaleAnimation.InsertKeyFrame(0f, new Vector3(0.82f, 0.82f, 1));
        openScaleAnimation.InsertKeyFrame(1f, Vector3.One, openEasing);
        openOpacityAnimation = compositor.CreateScalarKeyFrameAnimation();
        openOpacityAnimation.InsertKeyFrame(0f, 0f);
        openOpacityAnimation.InsertKeyFrame(1f, 1f, openEasing);

        uiSettings.AdvancedEffectsEnabledChanged += UISettings_AdvancedEffectsEnabledChanged;
    }

    public void SetShape(BackdropShape shape, bool dark)
    {
        ObjectDisposedException.ThrowIf(disposed, this);

        CanvasGeometry outline = CanvasGeometry.CreateRoundedRectangle(
            null, shape.Pill, shape.PillCornerRadius, shape.PillCornerRadius);
        try
        {
            foreach (var (center, radius) in shape.Circles)
            {
                using var circle = CanvasGeometry.CreateCircle(null, center, radius);
                var combined = outline.CombineWith(circle, Matrix3x2.Identity, CanvasGeometryCombine.Union);
                outline.Dispose();
                outline = combined;
            }

            outlineGeometry.Path = new CompositionPath(outline);
            silhouetteMask.Size = shape.Size;
            silhouetteMaskSurface.SourceSize = shape.Size;
        }
        finally
        {
            outline.Dispose();
        }

        SetTheme(dark);
    }

    public void SetTheme(bool dark)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (isDark != dark)
        {
            isDark = dark;
            ApplyShadowTheme();
        }
    }

    public void PlayOpenAnimation(Vector2 centerPx, TimeSpan duration)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        root.CenterPoint = new Vector3(centerPx, 0);
        openScaleAnimation.Duration = duration;
        openOpacityAnimation.Duration = duration;
        root.StartAnimation("Scale", openScaleAnimation);
        root.StartAnimation("Opacity", openOpacityAnimation);
    }

    private void ApplyShadowTheme()
    {
        shadow.Color = isDark ? Color.FromArgb(0xFF, 0x00, 0x00, 0x00) : Color.FromArgb(0xFF, 0x1A, 0x24, 0x30);
        shadow.Opacity = isDark ? 0.6f : 0.28f;
    }

    private void UpdateMaterial()
    {
        plateBrush.Source = uiSettings.AdvancedEffectsEnabled && GetAcrylicBrush() is { } acrylic ? acrylic : fallbackBrush;
    }

    private void UISettings_AdvancedEffectsEnabledChanged(UISettings sender, object args)
    {
        if (!dispatcherQueue.TryEnqueue(() =>
        {
            if (!disposed)
            {
                UpdateMaterial();
            }
        }))
        {
            Debug.WriteLine("Could not enqueue the selector material update.");
        }
    }

    private CompositionEffectBrush? GetAcrylicBrush()
    {
        if (acrylicBrush is not null || acrylicUnsupported)
        {
            return acrylicBrush;
        }

        try
        {
            backdropBrush ??= compositor.CreateHostBackdropBrush();
            if (acrylicFactory is null)
            {
                using var tint = new ColorSourceEffect { Color = DarkAcrylicTintColor };
                using var tinted = new CompositeEffect { Mode = CanvasComposite.SourceOver };
                tinted.Sources.Add(new CompositionEffectSourceParameter("Backdrop"));
                tinted.Sources.Add(tint);
                acrylicFactory = compositor.CreateEffectFactory(tinted);
            }

            acrylicBrush = acrylicFactory.CreateBrush();
            acrylicBrush.SetSourceParameter("Backdrop", backdropBrush);
            return acrylicBrush;
        }
        catch (Exception ex) when (ex is COMException or NotImplementedException)
        {
            Debug.WriteLine($"Acrylic unavailable (0x{ex.HResult:X8}): {ex.Message}");
            acrylicUnsupported = true;
            return null;
        }
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        uiSettings.AdvancedEffectsEnabledChanged -= UISettings_AdvancedEffectsEnabledChanged;
        target.Root = null;
        root.StopAnimation("Scale");
        root.StopAnimation("Opacity");
        root.Children.RemoveAll();
        shadowHost.Shadow = null;
        plate.Brush = null;
        plateBrush.Source = null;
        plateBrush.Mask = null;
        silhouetteMask.Shapes.Clear();
        plate.Dispose();
        plateBrush.Dispose();
        acrylicBrush?.Dispose();
        acrylicFactory?.Dispose();
        backdropBrush?.Dispose();
        shadow.Dispose();
        shadowHost.Dispose();
        silhouetteMaskBrush.Dispose();
        silhouetteMaskSurface.Dispose();
        silhouetteMaskFill.Dispose();
        silhouetteMask.Dispose();
        fallbackBrush.Dispose();
        outlineGeometry.Dispose();
        root.Dispose();
        openScaleAnimation.Dispose();
        openOpacityAnimation.Dispose();
        openEasing.Dispose();
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

internal sealed record BackdropShape(IReadOnlyList<(Vector2 Center, float Radius)> Circles, Rect Pill, float PillCornerRadius, Vector2 Size);
