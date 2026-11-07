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
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;
using Windows.ApplicationModel.DataTransfer;
using Windows.Foundation;
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

    private readonly BackdropShapeLayer backdropLayer;

    private enum SelectorState { Hidden, Shown, Closed }

    private SelectorState state = SelectorState.Hidden;
    private bool resizeQueued;
    private RadialMetrics metrics;
    private (RadialMetrics Metrics, int Width, int Height)? appliedShape;
    private int selectedIndex;
    private Vector3KeyFrameAnimation? openScaleAnimation;
    private ScalarKeyFrameAnimation? openOpacityAnimation;

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
        ViewModel.Browsers.CollectionChanged += ViewModel_Browsers_CollectionChanged;
        SystemBackdrop = new TransparentTintBackdrop();
        RemoveWindowFrame();
        backdropLayer = new BackdropShapeLayer(hwnd);
        DiscRoot.ActualThemeChanged += DiscRoot_ActualThemeChanged;
        AppWindow.Changed += AppWindow_Changed;
        settingsService.SettingsChanged += SettingsChanged;
        metrics = RadialMetrics.For(ViewModel.Browsers.Count);
        ApplyLayout();
        Select(selectedIndex);
    }

    public void Init(CliArgs args)
    {
        ViewModel.Url = (args as CliArgs.OpenUrl)?.Url ?? string.Empty;

        if (args is CliArgs.Minimized)
        {
            MinimizeWindow();
            return;
        }

        ShowWindow();
    }

    private void Window_Activated(object sender, WindowActivatedEventArgs args)
    {
        if (args.WindowActivationState != WindowActivationState.Deactivated || state != SelectorState.Shown)
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
        if (!App.IsShuttingDown)
        {
            args.Handled = true;
            MinimizeWindow();
            return;
        }

        settingsService.SettingsChanged -= SettingsChanged;
        state = SelectorState.Closed;
        ViewModel.CancelIconLoading();
        ViewModel.BrowserLaunched -= ViewModel_BrowserLaunched;
        ViewModel.PropertyChanged -= ViewModel_PropertyChanged;
        ViewModel.Browsers.CollectionChanged -= ViewModel_Browsers_CollectionChanged;
        AppWindow.Changed -= AppWindow_Changed;
        DiscRoot.ActualThemeChanged -= DiscRoot_ActualThemeChanged;
        Activated -= Window_Activated;
        openScaleAnimation?.Dispose();
        openOpacityAnimation?.Dispose();
        backdropLayer.Dispose();
    }
    #endregion

    #region Window Lifecycle Helper methods
    internal void MinimizeWindow()
    {
        if (state == SelectorState.Closed) return;
        state = SelectorState.Hidden;
        EditUrlFlyout.Hide();
        LinkMenu.Hide();
        AppWindow.Hide();
    }

    public void ShowWindow()
    {
        if (state == SelectorState.Closed) return;
        ViewModel.RefreshBrowsers();
        state = SelectorState.Shown;
        ApplyMetrics();
        Select(0);
        var bounds = CursorPosition.AnchoredAtCursor(metrics.WindowWidth, metrics.WindowHeight, metrics.FlowerCenter);
        AppWindow.MoveAndResize(bounds);
        AppWindow.Show();
        AppWindow.MoveAndResize(bounds);
        UpdateShape();
        SetWindowPos(hwnd, HwndTopmost, 0, 0, 0, 0, SwpNoMove | SwpNoSize);
        Activate();
        this.SetForegroundWindow();
        DiscRoot.UpdateLayout();
        FocusSelection();
        PlayOpenAnimation();
    }

    private void ResizeAroundFlowerCenter()
    {
        resizeQueued = false;
        if (state == SelectorState.Closed)
        {
            return;
        }

        var previous = metrics;
        bool changed = ApplyMetrics();
        if (state == SelectorState.Hidden)
        {
            return;
        }

        if (changed)
        {
            var current = AppWindow.Position;
            double scale = DiscRoot.XamlRoot?.RasterizationScale ?? 1.0;
            AppWindow.MoveAndResize(new RectInt32(
                current.X + (int)Math.Round((previous.FlowerCenter.X - metrics.FlowerCenter.X) * scale),
                current.Y + (int)Math.Round((previous.FlowerCenter.Y - metrics.FlowerCenter.Y) * scale),
                (int)Math.Round(metrics.WindowWidth * scale),
                (int)Math.Round(metrics.WindowHeight * scale)));
        }
        UpdateShape();
    }

    private bool ApplyMetrics()
    {
        var next = RadialMetrics.For(ViewModel.Browsers.Count);
        bool changed = metrics != next;
        if (changed)
        {
            metrics = next;
            ApplyLayout();
        }
        Select(selectedIndex);
        return changed;
    }

    private void ApplyLayout()
    {
        SelectionOutline.Data = null;
        FlowerHost.Width = metrics.FlowerSize;
        FlowerHost.Height = metrics.FlowerHeight;
        FlowerHost.Margin = new Thickness(0, metrics.FlowerTop, 0, 0);
        NamePill.Width = metrics.PillWidth;
        NamePill.Height = RadialMetrics.PillHeight;
        NamePill.Margin = new Thickness(0, metrics.PillTop, 0, 0);
        HubButton.Visibility = metrics.CenterFillRadius > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UpdateShape()
    {
        var size = AppWindow.Size;
        var shapeKey = (metrics, size.Width, size.Height);
        bool isDark = DiscRoot.ActualTheme != ElementTheme.Light;
        if (appliedShape == shapeKey)
        {
            backdropLayer.SetTheme(isDark);
            return;
        }

        float scale = (float)(size.Width / metrics.WindowWidth);
        var pixelSize = new Vector2(size.Width, size.Height);
        backdropLayer.SetShape(BackdropShape.FromMetrics(metrics, pixelSize), isDark);

        // GDI regions are aliased, so the region extends past the antialiased edge to hold the drop shadow.
        var paddedShape = BackdropShape.FromMetrics(metrics, pixelSize, RadialMetrics.ShadowMargin);
        IntPtr region = paddedShape.CreateWindowRegion((int)Math.Ceiling(WinUiEdgeStripInset * scale));
        if (region == IntPtr.Zero)
        {
            Debug.WriteLine("Could not create the selector window region.");
            return;
        }

        try
        {
            if (SetWindowRgn(hwnd, region, true) == 0)
            {
                Debug.WriteLine("Could not apply the selector window region.");
                return;
            }

            region = IntPtr.Zero;
            appliedShape = shapeKey;
        }
        finally
        {
            if (region != IntPtr.Zero)
            {
                DeleteObject(region);
            }
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
        float scale = (float)(size.Width / metrics.WindowWidth);
        backdropLayer.PlayOpenAnimation(
            new Vector2((float)metrics.FlowerCenter.X, (float)metrics.FlowerCenter.Y) * scale, OpenAnimationDuration);

        var flowerVisual = ElementCompositionPreview.GetElementVisual(FlowerHost);
        EnsureOpenAnimations(flowerVisual.Compositor);
        var scaleAnimation = openScaleAnimation!;
        var opacityAnimation = openOpacityAnimation!;
        flowerVisual.CenterPoint = new Vector3((float)metrics.FlowerOrigin.X, (float)metrics.FlowerOrigin.Y, 0);
        flowerVisual.StartAnimation("Scale", scaleAnimation);
        flowerVisual.StartAnimation("Opacity", opacityAnimation);

        var pillVisual = ElementCompositionPreview.GetElementVisual(NamePill);
        pillVisual.CenterPoint = new Vector3(
            (float)metrics.PillWidth / 2, (float)(metrics.FlowerCenter.Y - metrics.PillTop), 0);
        pillVisual.StartAnimation("Scale", scaleAnimation);
        pillVisual.StartAnimation("Opacity", opacityAnimation);
    }

    private void EnsureOpenAnimations(Compositor compositor)
    {
        if (openScaleAnimation is not null && openOpacityAnimation is not null)
        {
            return;
        }

        var easing = compositor.CreateCubicBezierEasingFunction(new Vector2(0.16f, 1f), new Vector2(0.3f, 1f));
        openScaleAnimation = compositor.CreateVector3KeyFrameAnimation();
        openScaleAnimation.InsertKeyFrame(0f, new Vector3(0.82f, 0.82f, 1));
        openScaleAnimation.InsertKeyFrame(1f, Vector3.One, easing);
        openScaleAnimation.Duration = OpenAnimationDuration;

        openOpacityAnimation = compositor.CreateScalarKeyFrameAnimation();
        openOpacityAnimation.InsertKeyFrame(0f, 0f);
        openOpacityAnimation.InsertKeyFrame(1f, 1f, easing);
        openOpacityAnimation.Duration = OpenAnimationDuration;
    }
    #endregion

    #region Selection
    private void Select(int index)
    {
        int count = ViewModel.Browsers.Count;
        int previousIndex = selectedIndex;
        selectedIndex = count == 0 ? 0 : Math.Clamp(index, 0, count - 1);
        SelectionOutline.Visibility = count == 0 ? Visibility.Collapsed : Visibility.Visible;
        if (count > 0 && (SelectionOutline.Data is null || previousIndex != selectedIndex))
        {
            SelectionOutline.Data = metrics.SelectionGeometry(selectedIndex);
        }
        for (int i = 0; i < count; i++)
        {
            if (BrowserRing.TryGetElement(i) is RadialBrowserItem item)
            {
                item.IsSelected = i == selectedIndex;
            }
        }
        UpdatePill();
    }

    private void FocusSelection()
    {
        if (BrowserRing.TryGetElement(selectedIndex) is RadialBrowserItem item)
        {
            item.FocusItem(FocusState.Programmatic);
        }
        else
        {
            NamePill.Focus(FocusState.Programmatic);
        }
    }

    private void UpdatePill()
    {
        bool hasUrl = Uri.TryCreate(ViewModel.Url, UriKind.Absolute, out var uri);
        string link = !hasUrl ? ViewModel.Url ?? string.Empty
            : uri!.IsFile ? System.IO.Path.GetFileName(uri.LocalPath)
            : string.IsNullOrEmpty(uri.Host) ? ViewModel.Url
            : uri.Host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? uri.Host[4..] : uri.Host;

        PillTitle.Text = string.IsNullOrEmpty(link) ? "Choose a browser" : link;
        PillHost.Text = ViewModel.Url;
        PillLock.Visibility = hasUrl && uri!.Scheme == Uri.UriSchemeHttps ? Visibility.Visible : Visibility.Collapsed;
        PillLink.Visibility = string.IsNullOrEmpty(link) ? Visibility.Collapsed : Visibility.Visible;
    }

    private void HubButton_Click(object sender, RoutedEventArgs e) => LinkMenu.ShowAt(HubButton);

    private void LinkMenu_Opening(object sender, object e)
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
        item.IsSelected = args.Index == selectedIndex;
        item.PointerEntered -=BrowserItem_PointerEntered;
        item.PointerEntered += BrowserItem_PointerEntered;
        item.GotFocus -= BrowserItem_GotFocus;
        item.GotFocus += BrowserItem_GotFocus;
        item.LaunchRequested -= BrowserItem_LaunchRequested;
        item.LaunchRequested += BrowserItem_LaunchRequested;
        item.AlternateLaunchRequested -= BrowserItem_AlternateLaunchRequested;
        item.AlternateLaunchRequested += BrowserItem_AlternateLaunchRequested;
    }

    private void BrowserRing_ElementIndexChanged(ItemsRepeater sender, ItemsRepeaterElementIndexChangedEventArgs args)
    {
        if (args.Element is RadialBrowserItem item)
        {
            item.ShortcutNumber = args.NewIndex + 1;
            item.IsSelected = args.NewIndex == selectedIndex;
        }
    }

    private void BrowserItem_GotFocus(object sender, RoutedEventArgs e)
    {
        int focused = GetFocusedItemIndex();
        if (focused >= 0 && sender is RadialBrowserItem item && BrowserRing.GetElementIndex(item) == focused)
        {
            Select(focused);
        }
    }

    private void BrowserItem_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        if (sender is not RadialBrowserItem item || BrowserRing.GetElementIndex(item) is not (>= 0 and var index))
        {
            return;
        }

        Select(index);
        if (GetFocusedItemIndex() is >= 0 and var focused && focused != index)
        {
            item.FocusItem(FocusState.Programmatic);
        }
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
            UpdatePill();
        }
    }

    private void ViewModel_Browsers_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        => QueueGeometryUpdate();

    private void AppWindow_Changed(AppWindow sender, AppWindowChangedEventArgs args)
    {
        if (args.DidSizeChange)
        {
            QueueGeometryUpdate();
        }
    }

    private void DiscRoot_ActualThemeChanged(FrameworkElement sender, object args)
        => backdropLayer.SetTheme(DiscRoot.ActualTheme != ElementTheme.Light);

    private void QueueGeometryUpdate()
    {
        if (state == SelectorState.Closed || resizeQueued)
        {
            return;
        }

        resizeQueued = true;
        if (!DispatcherQueue.TryEnqueue(ResizeAroundFlowerCenter))
        {
            resizeQueued = false;
            Debug.WriteLine("Could not enqueue the selector geometry update.");
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

    private void SettingsBtnClick(object sender, RoutedEventArgs e) => App.ShowSettings(SettingsTab.Settings);

    private void RulesBtnClick(object sender, RoutedEventArgs e) => App.ShowSettings(SettingsTab.Rulesets);

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
        if (state == SelectorState.Shown)
        {
            FocusSelection();
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

        int next = GetFocusedItemIndex() < 0
            ? GetItemClosestToDirection(e.Key, count)
            : ((selectedIndex + step) % count + count) % count;
        (BrowserRing.TryGetElement(next) as RadialBrowserItem)?.FocusItem(FocusState.Keyboard);
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

    private int GetItemClosestToDirection(VirtualKey key, int count)
    {
        var target = key switch
        {
            VirtualKey.Up => new Point(0, -1),
            VirtualKey.Down => new Point(0, 1),
            VirtualKey.Left => new Point(-1, 0),
            _ => new Point(1, 0)
        };
        int best = 0;
        double bestDot = double.MinValue;
        for (int i = 0; i < count; i++)
        {
            var center = metrics.ItemCenter(i, default);
            double dot = center.X * target.X + center.Y * target.Y;
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

        App.ShowSettings(SettingsTab.Rulesets);
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

    private const double WinUiEdgeStripInset = 4;
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
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DeleteObject(IntPtr hObject);

    [LibraryImport("user32.dll")]
    private static partial int SetWindowRgn(IntPtr hWnd, IntPtr hRgn, [MarshalAs(UnmanagedType.Bool)] bool bRedraw);
    #endregion
}
