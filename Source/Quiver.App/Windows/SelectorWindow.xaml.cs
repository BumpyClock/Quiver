using Quiver.App.Controls;
using Quiver.App.Helpers;
using Quiver.App.Services.Interfaces;
using Quiver.App.ViewModels;
using Quiver.Library;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Composition;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;
using Windows.ApplicationModel.DataTransfer;
using Windows.Graphics;
using Windows.System;
using Windows.UI.ViewManagement;
using WinRT;
using WinRT.Interop;
using WinUIEx;

namespace Quiver.App.Windows;

[GeneratedBindableCustomProperty]
public sealed partial class SelectorWindow : Window
{
    private static readonly TimeSpan OpenAnimationDuration = TimeSpan.FromMilliseconds(180);
    private static readonly UISettings AnimationSettings = new();

    public SelectorPageViewModel ViewModel { get; }
    private readonly IQuickViewService quickViewService;
    private readonly ISettingsService settingsService;
    private readonly IntPtr hwnd;

    private readonly CircleAcrylicLayer acrylicLayer;

    private bool isHiddenToTray = true;

    #region Window Lifecycle
    public SelectorWindow()
    {
        IServiceProvider services = App.Services ?? throw new InvalidOperationException("Application services are not configured.");
        ViewModel = services.GetRequiredService<SelectorPageViewModel>();
        quickViewService = services.GetRequiredService<IQuickViewService>();
        settingsService = services.GetRequiredService<ISettingsService>();
        ViewModel.BrowserLaunched += ViewModel_BrowserLaunched;
        ViewModel.PropertyChanged += ViewModel_PropertyChanged;
        hwnd = WindowNative.GetWindowHandle(this);

        var presenter = OverlappedPresenter.Create();
        presenter.IsAlwaysOnTop = true;
        presenter.IsMaximizable = false;
        presenter.IsMinimizable = false;
        presenter.IsResizable = false;
        presenter.SetBorderAndTitleBar(false, false);
        AppWindow.SetPresenter(presenter);
        AppWindow.IsShownInSwitchers = false;

        Activated += Window_Activated;
        Closed += SelectorWindow_Closed;

        InitializeComponent();
        SystemBackdrop = new TransparentTintBackdrop();
        RemoveWindowFrame();
        acrylicLayer = new CircleAcrylicLayer(hwnd);
        DiscRoot.ActualThemeChanged += (_, _) => UpdateShape();
        settingsService.SettingsChanged += SettingsChanged;
        UpdateHub();
    }

    public void Init(CliArgs args)
    {
        ViewModel.Url = args.Url;

        if (args.IsRunAsMin)
        {
            MinimizeWindow();
            return;
        }

        ShowWindow();
    }

    private void Window_Activated(object sender, WindowActivatedEventArgs args)
    {
        if (args.WindowActivationState != WindowActivationState.Deactivated || isHiddenToTray)
        {
            return;
        }

#if DEBUG
        // No minimize on debug when not in focus
#else
        if (ViewModel.AppSettings.MinimizeOnFocusLoss)
        {
            MinimizeWindow();
        }
#endif
    }

    private void SelectorWindow_Closed(object sender, WindowEventArgs args)
    {
        if (!App.IsExiting)
        {
            args.Handled = true;
            MinimizeWindow();
            return;
        }

        settingsService.SettingsChanged -= SettingsChanged;
        ViewModel.CancelIconLoading();
        ViewModel.BrowserLaunched -= ViewModel_BrowserLaunched;
        ViewModel.PropertyChanged -= ViewModel_PropertyChanged;
        Activated -= Window_Activated;
        acrylicLayer.Dispose();
    }
    #endregion

    #region Window Lifecycle Helper methods
    internal void MinimizeWindow()
    {
        isHiddenToTray = true;
        EditUrlFlyout.Hide();
        HubMenu.Hide();
        AppWindow.Hide();
    }

    public void ShowWindow()
    {
        isHiddenToTray = false;
        var bounds = CursorPosition.SquareCenteredOnCursor(RadialMetrics.For(ViewModel.Browsers.Count).WindowSize);
        AppWindow.MoveAndResize(bounds);
        AppWindow.Show();
        AppWindow.MoveAndResize(bounds);
        UpdateShape();
        SetWindowPos(hwnd, HwndTopmost, 0, 0, 0, 0, SwpNoMove | SwpNoSize);
        Activate();
        this.SetForegroundWindow();
        HubButton.Focus(FocusState.Programmatic);
        PlayOpenAnimation();
    }

    private void ResizeAroundCenter()
    {
        if (isHiddenToTray)
        {
            return;
        }

        var current = AppWindow.Position;
        var size = AppWindow.Size;
        double scale = DiscRoot.XamlRoot?.RasterizationScale ?? 1.0;
        int newSize = (int)Math.Round(RadialMetrics.For(ViewModel.Browsers.Count).WindowSize * scale);
        AppWindow.MoveAndResize(new RectInt32(
            current.X + (size.Width - newSize) / 2,
            current.Y + (size.Height - newSize) / 2,
            newSize,
            newSize));
        UpdateShape();
    }

    private void UpdateShape()
    {
        var size = AppWindow.Size;
        var metrics = RadialMetrics.For(ViewModel.Browsers.Count);
        float scale = (float)(size.Width / metrics.WindowSize);
        var center = new Vector2(size.Width / 2f, size.Height / 2f);

        var circles = new List<(Vector2 Center, float Radius)>(metrics.Count + 1)
        {
            (center, (float)RadialMetrics.HubRadius * scale)
        };
        for (int i = 0; i < metrics.Count; i++)
        {
            var point = metrics.ItemCenter(i, default);
            circles.Add((center + new Vector2((float)point.X, (float)point.Y) * scale, (float)(RadialMetrics.ItemSize / 2) * scale));
        }

        acrylicLayer.SetCircles(circles, DiscRoot.ActualTheme != ElementTheme.Light);

        IntPtr region = CreateRectRgn(0, 0, 0, 0);
        foreach (var (circleCenter, radius) in circles)
        {
            IntPtr circle = CreateEllipticRgn(
                (int)MathF.Floor(circleCenter.X - radius), (int)MathF.Floor(circleCenter.Y - radius),
                (int)MathF.Ceiling(circleCenter.X + radius) + 1, (int)MathF.Ceiling(circleCenter.Y + radius) + 1);
            CombineRgn(region, region, circle, RgnOr);
            DeleteObject(circle);
        }

        if (SetWindowRgn(hwnd, region, true) == 0)
        {
            DeleteObject(region);
        }
    }

    private void RemoveWindowFrame()
    {
        int doNotRound = DwmCornerDoNotRound;
        DwmSetWindowAttribute(hwnd, DwmaWindowCornerPreference, ref doNotRound, sizeof(int));
        int noBorder = DwmColorNone;
        DwmSetWindowAttribute(hwnd, DwmaBorderColor, ref noBorder, sizeof(int));
    }

    private void PlayOpenAnimation()
    {
        if (!AnimationSettings.AnimationsEnabled)
        {
            return;
        }

        var size = AppWindow.Size;
        acrylicLayer.PlayOpenAnimation(new Vector2(size.Width / 2f, size.Height / 2f), OpenAnimationDuration);
        foreach (UIElement element in new UIElement[] { BrowserRing, HubButton })
        {
            var visual = ElementCompositionPreview.GetElementVisual(element);
            var compositor = visual.Compositor;
            var easing = compositor.CreateCubicBezierEasingFunction(new Vector2(0.16f, 1f), new Vector2(0.3f, 1f));
            visual.CenterPoint = new Vector3((float)element.ActualSize.X / 2, (float)element.ActualSize.Y / 2, 0);

            var scale = compositor.CreateVector3KeyFrameAnimation();
            scale.InsertKeyFrame(0f, new Vector3(0.82f, 0.82f, 1));
            scale.InsertKeyFrame(1f, Vector3.One, easing);
            scale.Duration = OpenAnimationDuration;

            var opacity = compositor.CreateScalarKeyFrameAnimation();
            opacity.InsertKeyFrame(0f, 0f);
            opacity.InsertKeyFrame(1f, 1f, easing);
            opacity.Duration = OpenAnimationDuration;

            visual.StartAnimation("Scale", scale);
            visual.StartAnimation("Opacity", opacity);
        }
    }
    #endregion

    #region Hub
    private void UpdateHub()
    {
        var (title, subtitle) = DescribeUrl(ViewModel.Url);
        HubTitle.Text = string.IsNullOrEmpty(title) ? Constants.NAME : title;
        HubSubtitle.Text = subtitle;
        HubSubtitle.Visibility = string.IsNullOrEmpty(subtitle) ? Visibility.Collapsed : Visibility.Visible;
    }

    private static (string Title, string Subtitle) DescribeUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return (string.Empty, string.Empty);
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return (url, string.Empty);
        }

        if (uri.IsFile)
        {
            return (System.IO.Path.GetFileName(uri.LocalPath), string.Empty);
        }

        if (string.IsNullOrEmpty(uri.Host))
        {
            return (url, string.Empty);
        }

        string host = uri.Host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? uri.Host[4..] : uri.Host;
        string rest = uri.PathAndQuery == "/" ? string.Empty : Uri.UnescapeDataString(uri.PathAndQuery);
        return (host, rest);
    }

    private void HubMenu_Opening(object sender, object e)
    {
        QuickViewMenuItem.IsEnabled = quickViewService.IsQuickViewEnabled;
    }
    #endregion

    #region Selector UI Event Handlers
    private void BrowserRing_ElementPrepared(ItemsRepeater sender, ItemsRepeaterElementPreparedEventArgs args)
    {
        if (args.Element is not RadialBrowserItem item)
        {
            return;
        }

        item.ShortcutNumber = args.Index + 1;
        item.LaunchRequested -= BrowserItem_LaunchRequested;
        item.LaunchRequested += BrowserItem_LaunchRequested;
        item.AlternateLaunchRequested -= BrowserItem_AlternateLaunchRequested;
        item.AlternateLaunchRequested += BrowserItem_AlternateLaunchRequested;
    }

    private void BrowserItem_LaunchRequested(object? sender, BrowserItemViewModel e) => ViewModel.LaunchBrowserCommand.Execute(e);

    private void BrowserItem_AlternateLaunchRequested(object? sender, AlternateLaunchRequestedEventArgs e)
    {
        try
        {
            UriLauncher.Alternative(ViewModel.Url, e.Browser, e.AlternateLaunch);
            MinimizeWindow();
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex);
        }
    }

    private void ViewModel_BrowserLaunched(object? sender, EventArgs e) => MinimizeWindow();

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SelectorPageViewModel.Url))
        {
            UpdateHub();
        }
        else if (e.PropertyName == nameof(SelectorPageViewModel.Browsers))
        {
            ResizeAroundCenter();
        }
    }

    private void SettingsChanged(object? sender, SettingsChangedEventArgs e)
    {
        if (e.Section.HasFlag(SettingsSection.AppSettings))
        {
            ViewModel.RefreshAppSettings();
        }

        if (e.Section.HasFlag(SettingsSection.Browsers))
        {
            ViewModel.RefreshBrowsers();
        }
    }

    private void LinkCopyBtnClick(object sender, RoutedEventArgs e)
    {
        try
        {
            CopyCurrentUrlToClipboard();
        }
        catch (Exception err)
        {
            Debug.WriteLine(err);
        }
    }

    private void EditUrlBtnClick(object sender, RoutedEventArgs e) => ShowEditUrlFlyout();

    private void SettingsBtnClick(object sender, RoutedEventArgs e) => App.ShowSettings("settings");

    private void RulesBtnClick(object sender, RoutedEventArgs e) => App.ShowSettings("rulesets");

    private void QuickViewBtnClick(object sender, RoutedEventArgs e)
    {
        if (quickViewService.TryOpen(ViewModel.Url))
        {
            MinimizeWindow();
        }
    }

    private void UrlTextBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter)
        {
            EditUrlFlyout.Hide();
            e.Handled = true;
        }
    }

    private void EditUrlFlyout_Closed(object sender, object e)
    {
        if (!isHiddenToTray)
        {
            HubButton.Focus(FocusState.Programmatic);
        }
    }

    private void ShowEditUrlFlyout()
    {
        FlyoutBase.ShowAttachedFlyout(DiscRoot);
        UrlTextBox.Focus(FocusState.Keyboard);
        UrlTextBox.SelectAll();
    }
    #endregion

    #region Keyboard
    private void DiscRoot_PreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (IsTextBoxKeyAccelerator() || IsModifierDown())
        {
            return;
        }

        int count = ViewModel.Browsers.Count;
        int? number = e.Key switch
        {
            >= VirtualKey.Number1 and <= VirtualKey.Number9 => e.Key - VirtualKey.Number1,
            >= VirtualKey.NumberPad1 and <= VirtualKey.NumberPad9 => e.Key - VirtualKey.NumberPad1,
            _ => null
        };
        if (number is int index)
        {
            if (index < count)
            {
                ViewModel.LaunchBrowserCommand.Execute(ViewModel.Browsers[index]);
            }

            e.Handled = true;
            return;
        }

        int step = e.Key switch
        {
            VirtualKey.Right or VirtualKey.Down => 1,
            VirtualKey.Left or VirtualKey.Up => -1,
            _ => 0
        };
        if (step == 0 || count == 0)
        {
            return;
        }

        int focused = GetFocusedItemIndex();
        int next = focused < 0
            ? GetItemClosestToDirection(e.Key, count)
            : ((focused + step) % count + count) % count;
        (BrowserRing.TryGetElement(next) as RadialBrowserItem)?.FocusItem();
        e.Handled = true;
    }

    private int GetFocusedItemIndex()
    {
        var focused = DiscRoot.XamlRoot is null ? null : FocusManager.GetFocusedElement(DiscRoot.XamlRoot) as DependencyObject;
        while (focused is not null and not RadialBrowserItem)
        {
            focused = VisualTreeHelper.GetParent(focused);
        }

        return focused is RadialBrowserItem item ? BrowserRing.GetElementIndex(item) : -1;
    }

    private static int GetItemClosestToDirection(VirtualKey key, int count)
    {
        var target = key switch
        {
            VirtualKey.Up => new global::Windows.Foundation.Point(0, -1),
            VirtualKey.Down => new global::Windows.Foundation.Point(0, 1),
            VirtualKey.Left => new global::Windows.Foundation.Point(-1, 0),
            _ => new global::Windows.Foundation.Point(1, 0)
        };
        var metrics = RadialMetrics.For(count);
        int best = 0;
        double bestDot = double.MinValue;
        for (int i = 0; i < count; i++)
        {
            var center = metrics.ItemCenter(i, default);
            double dot = (center.X * target.X + center.Y * target.Y) / metrics.RingRadius;
            if (dot > bestDot)
            {
                bestDot = dot;
                best = i;
            }
        }

        return best;
    }

    private void EscapeAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        MinimizeWindow();
        args.Handled = true;
    }

    private void CopyAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        if (IsTextBoxKeyAccelerator())
        {
            return;
        }

        CopyCurrentUrlToClipboard();
        args.Handled = true;
    }

    private void EditUrlAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        if (IsTextBoxKeyAccelerator())
        {
            return;
        }

        ShowEditUrlFlyout();
        args.Handled = true;
    }

    private void RulesAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        if (IsTextBoxKeyAccelerator())
        {
            return;
        }

        App.ShowSettings("rulesets");
        args.Handled = true;
    }

    private bool IsTextBoxKeyAccelerator()
    {
        var xamlRoot = DiscRoot.XamlRoot;
        return xamlRoot is not null && FocusManager.GetFocusedElement(xamlRoot) is TextBox;
    }

    private static bool IsModifierDown() => KeyboardState.IsCtrlKeyDown() || KeyboardState.IsAltKeyDown();
    #endregion

    #region Helper methods
    private void CopyCurrentUrlToClipboard()
    {
        DataPackage package = new();
        package.SetText(ViewModel.Url ?? string.Empty);
        Clipboard.SetContent(package);
        Clipboard.Flush();
    }

    private const int RgnOr = 2;
    private static readonly IntPtr HwndTopmost = new(-1);
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoMove = 0x0002;

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint flags);

    private const int DwmaWindowCornerPreference = 33;
    private const int DwmaBorderColor = 34;
    private const int DwmCornerDoNotRound = 1;
    private const int DwmColorNone = unchecked((int)0xFFFFFFFE);

    [LibraryImport("dwmapi.dll")]
    private static partial int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    [LibraryImport("gdi32.dll")]
    private static partial IntPtr CreateRectRgn(int left, int top, int right, int bottom);

    [LibraryImport("gdi32.dll")]
    private static partial int CombineRgn(IntPtr destination, IntPtr source1, IntPtr source2, int mode);

    [LibraryImport("gdi32.dll")]
    private static partial IntPtr CreateEllipticRgn(int left, int top, int right, int bottom);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DeleteObject(IntPtr hObject);

    [LibraryImport("user32.dll")]
    private static partial int SetWindowRgn(IntPtr hWnd, IntPtr hRgn, [MarshalAs(UnmanagedType.Bool)] bool bRedraw);
    #endregion
}
