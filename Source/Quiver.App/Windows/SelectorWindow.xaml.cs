using Quiver.App.Controls;
using Quiver.App.Helpers;
using Quiver.App.Services.Interfaces;
using Quiver.App.ViewModels;
using Quiver.Library;
using Quiver.Library.Models;
using Microsoft.Extensions.DependencyInjection;
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
    public SelectorPageViewModel ViewModel { get; }
    private readonly IQuickViewService quickViewService;
    private readonly ISettingsService settingsService;
    private readonly IntPtr hwnd;

    private readonly BackdropShapeLayer backdropLayer;
    private readonly UISettings uiSettings = new();

    private enum SelectorState { Hidden, Shown, Closed }

    private SelectorState state = SelectorState.Hidden;
    private bool resizeQueued;
    private RadialMetrics metrics;
    private ListMetrics listMetrics = ListMetrics.Compact;
    private SelectorLayout layout;
    private CursorSnapshot openingCursor;
    private ItemsRepeater Items => layout == SelectorLayout.List ? BrowserList : BrowserRing;
    private SelectorGeometry Geometry => GeometryFor(layout, metrics, listMetrics);
    private SelectorLayout PreferredLayout => ViewModel.AppSettings.SelectorLayout;
    private (RadialMetrics Metrics, SelectorLayout Layout, int Width, int Height, double Scale)? appliedShape;
    private int selectedIndex;

    #region Window Lifecycle
    public SelectorWindow()
    {
        IServiceProvider services = App.Services ?? throw new InvalidOperationException("Application services are not configured.");
        ViewModel = services.GetRequiredService<SelectorPageViewModel>();
        quickViewService = services.GetRequiredService<IQuickViewService>();
        settingsService = services.GetRequiredService<ISettingsService>();
        hwnd = WindowNative.GetWindowHandle(this);

        var presenter = OverlappedPresenter.Create();
        presenter.IsAlwaysOnTop = true;
        presenter.IsMaximizable = false;
        presenter.IsMinimizable = false;
        presenter.IsResizable = false;
        presenter.SetBorderAndTitleBar(false, false);
        AppWindow.SetPresenter(presenter);
        AppWindow.IsShownInSwitchers = false;

        InitializeComponent();
        SystemBackdrop = new TransparentTintBackdrop();
        RemoveWindowFrame();
        backdropLayer = new BackdropShapeLayer(PlateShadowHost, SelectorPlate);
        metrics = RadialMetrics.For(ViewModel.Browsers.Count);
        layout = EffectiveLayout(PreferredLayout, metrics, openingCursor);
        ApplyLayout();
        Select(selectedIndex);

        ViewModel.BrowserLaunched += ViewModel_BrowserLaunched;
        ViewModel.PropertyChanged += ViewModel_PropertyChanged;
        ViewModel.Browsers.CollectionChanged += ViewModel_Browsers_CollectionChanged;
        DiscRoot.ActualThemeChanged += DiscRoot_ActualThemeChanged;
        AppWindow.Changed += AppWindow_Changed;
        settingsService.SettingsChanged += SettingsChanged;
        uiSettings.TextScaleFactorChanged += UiSettings_TextScaleFactorChanged;
        Activated += Window_Activated;
        Closed += SelectorWindow_Closed;
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
        uiSettings.TextScaleFactorChanged -= UiSettings_TextScaleFactorChanged;
        state = SelectorState.Closed;
        ViewModel.CancelIconLoading();
        ViewModel.BrowserLaunched -= ViewModel_BrowserLaunched;
        ViewModel.PropertyChanged -= ViewModel_PropertyChanged;
        ViewModel.Browsers.CollectionChanged -= ViewModel_Browsers_CollectionChanged;
        AppWindow.Changed -= AppWindow_Changed;
        DiscRoot.ActualThemeChanged -= DiscRoot_ActualThemeChanged;
        Activated -= Window_Activated;
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
        ShowWindow(CursorPosition.Capture());
    }

    internal void ShowWindow(CursorSnapshot opening)
    {
        if (state == SelectorState.Closed) return;
        openingCursor = opening;
        ViewModel.RefreshAppSettings();
        ViewModel.RefreshBrowsers();
        state = SelectorState.Shown;
        ApplyMetrics();
        Select(0);
        var bounds = openingCursor.Place(Geometry);
        AppWindow.MoveAndResize(bounds);
        Bindings.Update();
        UpdateShape();
        if (layout == SelectorLayout.List) BrowserScroll.ChangeView(null, 0, null, true);
        DiscRoot.UpdateLayout();
        AppWindow.Show();
        AppWindow.MoveAndResize(bounds);
        UpdateShape();
        SetWindowPos(hwnd, HwndTopmost, 0, 0, 0, 0, SwpNoMove | SwpNoSize);
        Activate();
        this.SetForegroundWindow();
        DiscRoot.UpdateLayout();
        FocusSelection();
    }

    private void ResizeForLayout()
    {
        resizeQueued = false;
        if (state == SelectorState.Closed)
        {
            return;
        }

        var previous = Geometry;
        bool changed = ApplyMetrics();
        if (state == SelectorState.Hidden)
        {
            return;
        }

        if (changed)
        {
            AppWindow.MoveAndResize(openingCursor.Resize(previous, Geometry, AppWindow.Position));
        }
        UpdateShape();
    }

    private bool ApplyMetrics()
    {
        var next = RadialMetrics.For(ViewModel.Browsers.Count);
        var nextLayout = EffectiveLayout(PreferredLayout, next, openingCursor);
        var nextList = nextLayout == SelectorLayout.List ? MeasureList(next.Count) : listMetrics;
        bool changed = metrics != next || layout != nextLayout || listMetrics != nextList;
        if (changed)
        {
            metrics = next;
            listMetrics = nextList;
            layout = nextLayout;
            ApplyLayout();
        }
        Select(selectedIndex);
        return changed;
    }

    internal static SelectorGeometry GeometryFor(SelectorLayout layout, RadialMetrics metrics, ListMetrics? list = null)
        => layout == SelectorLayout.List ? (list ?? ListMetrics.Compact).Geometry(metrics.Count)
            : new(metrics.WindowWidth, metrics.WindowHeight, metrics.FlowerCenter, SelectorPlacement.Centered);

    internal ListMetrics ListGeometry => listMetrics;

    internal ListBrowserItem ListRowProbe { get; private set; } = CreateListRowProbe();

    private static ListBrowserItem CreateListRowProbe() => new()
    {
        BrowserItem = new BrowserItemViewModel(new Browser("Ag", string.Empty), new BrowserProfile(string.Empty, "Ag", null)),
        ShortcutNumber = 1,
    };

    private ListMetrics MeasureList(int count)
    {
        EmptyListMessage.Visibility = count == 0 ? Visibility.Visible : Visibility.Collapsed;
        var link = PillLink.Visibility;
        PillLink.Visibility = Visibility.Visible;
        var measured = ListMetrics.Measure(ListRowProbe, EmptyListMessage, NamePill);
        PillLink.Visibility = link;
        return measured;
    }

    internal static SelectorLayout EffectiveLayout(SelectorLayout preferred, RadialMetrics metrics, CursorSnapshot opening)
        => preferred != SelectorLayout.List && opening.Fits(GeometryFor(SelectorLayout.Radial, metrics))
            ? SelectorLayout.Radial : SelectorLayout.List;

    private void ApplyLayout()
    {
        SelectionOutline.Data = null;
        FlowerHost.Visibility = layout == SelectorLayout.Radial ? Visibility.Visible : Visibility.Collapsed;
        ListHost.Visibility = layout == SelectorLayout.List ? Visibility.Visible : Visibility.Collapsed;
        backdropLayer.SetVisible(layout == SelectorLayout.Radial);
        if (layout == SelectorLayout.List) ApplyListLayout();
        else ApplyRadialLayout();
    }

    private void ApplyRadialLayout()
    {
        NamePill.UseLayoutRounding = false;
        NamePill.Translation = Vector3.Zero;
        FlowerHost.Width = metrics.FlowerSize;
        FlowerHost.Height = metrics.FlowerHeight;
        FlowerHost.Margin = new Thickness(0, metrics.FlowerTop, 0, 0);
        NamePill.Width = metrics.PillWidth;
        NamePill.Height = RadialMetrics.PillHeight;
        NamePill.Margin = new Thickness(0, metrics.PillTop, 0, 0);
        HubButton.Margin = new Thickness(0, metrics.FlowerOrigin.Y - RadialMetrics.HubRadius, 0, 0);
        HubButton.Visibility = metrics.CenterFillRadius > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ApplyListLayout()
    {
        ListRowsLayout.MinItemHeight = listMetrics.Row;
        BrowserList.Height = metrics.Count * listMetrics.Row;
        NamePill.UseLayoutRounding = true;
        NamePill.Height = listMetrics.Footer;
        NamePill.Translation = new Vector3(0, 0, 16);
        var visual = ElementCompositionPreview.GetElementVisual(NamePill);
        visual.StopAnimation("Scale");
        visual.StopAnimation("Opacity");
        visual.Scale = Vector3.One;
        visual.Opacity = 1;
        for (int index = 0; index < metrics.Count; index++)
        {
            if (BrowserList.TryGetElement(index) is FrameworkElement row) row.Height = listMetrics.Row;
        }
    }

    private void SizeListHost(SizeInt32 size, double scale)
    {
        var frame = listMetrics.Frame(size, scale, metrics.Count);
        ListHost.Width = frame.Width;
        ListHost.Height = frame.Height;
        ListBody.Height = frame.Body;
        BrowserScroll.MaxHeight = frame.Body;
        NamePill.Width = Math.Max(0, frame.Width - 2 * ListMetrics.Padding);
        NamePill.Margin = new Thickness(0, frame.FooterTop, 0, 0);
    }

    private void UpdateShape()
    {
        var size = AppWindow.Size;
        double scale = layout == SelectorLayout.List ? CursorPosition.WindowScale(hwnd) : size.Width / metrics.WindowWidth;
        if (layout == SelectorLayout.List) SizeListHost(size, scale);
        var shapeKey = (metrics, layout, size.Width, size.Height, scale);
        bool isDark = DiscRoot.ActualTheme != ElementTheme.Light;
        if (appliedShape == shapeKey)
        {
            backdropLayer.SetTheme(isDark);
            return;
        }

        var pixelSize = new Vector2(size.Width, size.Height);
        if (layout == SelectorLayout.Radial)
        {
            backdropLayer.SetShape(BackdropShape.FromMetrics(metrics,
                new Vector2((float)metrics.WindowWidth, (float)metrics.WindowHeight)), isDark);
        }

        // GDI regions are aliased, so the region extends past the antialiased edge to hold the drop shadow.
        var paddedShape = layout == SelectorLayout.List
            ? BackdropShape.FromList(pixelSize, (float)scale, ListMetrics.ShadowMargin)
            : BackdropShape.FromMetrics(metrics, pixelSize, RadialMetrics.ShadowMargin);
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
            if (Items.TryGetElement(i) is BrowserItemControl item)
            {
                item.IsSelected = i == selectedIndex;
            }
        }
        UpdatePill();
    }

    private void FocusSelection()
    {
        if (ViewModel.Browsers.Count == 0)
        {
            if (layout == SelectorLayout.List) ManageBrowsersButton.Focus(FocusState.Programmatic);
            else NamePill.Focus(FocusState.Programmatic);
        }
        else FocusBrowser(selectedIndex, FocusState.Programmatic);
    }

    private void FocusBrowser(int index, FocusState focusState)
    {
        if (layout == SelectorLayout.List)
        {
            RevealListItem(index);
            DiscRoot.UpdateLayout();
        }
        var element = layout == SelectorLayout.List ? Items.GetOrCreateElement(index) : Items.TryGetElement(index);
        if (element is not BrowserItemControl item)
        {
            NamePill.Focus(focusState);
            return;
        }
        item.FocusItem(focusState);
    }

    private void RevealListItem(int index)
    {
        double top = index * listMetrics.Row;
        double offset = Math.Min(top, Math.Max(BrowserScroll.VerticalOffset,
            top + listMetrics.Row - BrowserScroll.ViewportHeight));
        BrowserScroll.ChangeView(null, offset, null, true);
    }

    private void UpdatePill()
    {
        var address = SelectorAddress.For(ViewModel.Url, layout == SelectorLayout.List);
        PillTitle.Text = string.IsNullOrEmpty(address.Title) ? "Choose a browser" : address.Title;
        PillHost.Text = address.Detail;
        PillLock.Visibility = address.IsSecure ? Visibility.Visible : Visibility.Collapsed;
        PillLink.Visibility = string.IsNullOrEmpty(address.Title) ? Visibility.Collapsed : Visibility.Visible;
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
        if (args.Element is not BrowserItemControl item)
        {
            return;
        }

        item.ShortcutNumber = args.Index + 1;
        item.IsSelected = args.Index == selectedIndex;
        if (item is ListBrowserItem) item.Height = listMetrics.Row;
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
        if (args.Element is BrowserItemControl item)
        {
            item.ShortcutNumber = args.NewIndex + 1;
            item.IsSelected = args.NewIndex == selectedIndex;
        }
    }

    private void DiscRoot_FocusChanged(object sender, RoutedEventArgs e)
        => SelectionOutline.StrokeThickness = GetFocusedItemIndex() >= 0 ? 2 : 0;

    private void BrowserItem_GotFocus(object sender, RoutedEventArgs e)
    {
        int focused = GetFocusedItemIndex();
        if (focused >= 0 && sender is BrowserItemControl item && Items.GetElementIndex(item) == focused)
        {
            Select(focused);
            if (layout == SelectorLayout.List) RevealListItem(focused);
        }
    }

    private void BrowserItem_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        // Layout and scrolling can synthesize entry without new pointer input.
        if (e.IsGenerated) return;
        if (sender is not BrowserItemControl item || Items.GetElementIndex(item) is not (>= 0 and var index))
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

    private void UiSettings_TextScaleFactorChanged(UISettings sender, object args)
        => DiscRoot.DispatcherQueue.TryEnqueue(() =>
        {
            ListRowProbe = CreateListRowProbe();
            QueueGeometryUpdate();
        });

    private void DiscRoot_ActualThemeChanged(FrameworkElement sender, object args)
        => backdropLayer.SetTheme(DiscRoot.ActualTheme != ElementTheme.Light);

    private void QueueGeometryUpdate()
    {
        if (state == SelectorState.Closed || resizeQueued)
        {
            return;
        }

        resizeQueued = true;
        if (!DispatcherQueue.TryEnqueue(ResizeForLayout))
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
            QueueGeometryUpdate();
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

    private void ManageBrowsersBtnClick(object sender, RoutedEventArgs e) => App.ShowSettings(SettingsTab.Browsers);

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

        if (NavigateSelection(e.Key)) e.Handled = true;
    }

    internal bool NavigateSelection(VirtualKey key)
    {
        int count = ViewModel.Browsers.Count;
        int step = key switch
        {
            VirtualKey.Right or VirtualKey.Down => 1,
            VirtualKey.Left or VirtualKey.Up => -1,
            _ => 0
        };
        if (step == 0 || count == 0) return false;

        int next = GetFocusedItemIndex() < 0
            ? layout == SelectorLayout.List ? (step > 0 ? 0 : count - 1) : GetItemClosestToDirection(key, count)
            : ((selectedIndex + step) % count + count) % count;
        FocusBrowser(next, FocusState.Keyboard);
        return true;
    }

    private int GetFocusedItemIndex()
    {
        var focused = DiscRoot.XamlRoot is null ? null : FocusManager.GetFocusedElement(DiscRoot.XamlRoot) as DependencyObject;
        while (focused is not null and not BrowserItemControl)
        {
            focused = VisualTreeHelper.GetParent(focused);
        }

        return focused is BrowserItemControl item ? Items.GetElementIndex(item) : -1;
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
