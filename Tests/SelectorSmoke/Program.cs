using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Quiver.App.Controls;
using Quiver.App.Services.Interfaces;
using Quiver.App.ViewModels;
using Quiver.App.Windows;
using Quiver.Library;
using Quiver.Library.Models;
using System.Collections.ObjectModel;
using System.Reflection;
using System.Runtime.InteropServices;
using Windows.Graphics;

namespace Quiver.SelectorSmoke;

internal static partial class Program
{
    [LibraryImport("Microsoft.ui.xaml.dll")]
    private static partial void XamlCheckProcessRequirements();

    [STAThread]
    private static void Main()
    {
        XamlCheckProcessRequirements();
        WinRT.ComWrappersSupport.InitializeComWrappers();
        Application.Start(_ =>
        {
            SynchronizationContext.SetSynchronizationContext(
                new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread()));
            new SmokeApplication();
        });
    }
}

public sealed partial class SmokeApplication : Application
{
    public SmokeApplication() => InitializeComponent();

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        SelectorWindow? window = null;
        ServiceProvider? services = null;
        string resultPath = Path.Combine(AppContext.BaseDirectory, "selector-smoke-result.txt");
        try
        {
            CheckMetrics();
            services = new ServiceCollection()
                .AddSingleton<ISettingsService, TestSettings>()
                .AddSingleton<IQuickViewService, TestQuickView>()
                .AddSingleton<IIconLoader, TestIcons>()
                .AddTransient<SelectorPageViewModel>()
                .BuildServiceProvider();
            typeof(Quiver.App.App).GetProperty(nameof(Quiver.App.App.Services))!.SetValue(null, services);
            window = new SelectorWindow();
            window.ViewModel.Url = "https://a-long-sharepoint-hostname-for-the-selector.example.com/document";
            var root = (FrameworkElement)window.Content;
            var loaded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            root.Loaded += (_, _) => loaded.TrySetResult();
            window.AppWindow.IsShownInSwitchers = false;
            var metrics = RadialMetrics.For(window.ViewModel.Browsers.Count);
            window.AppWindow.MoveAndResize(new RectInt32(-32000, -32000,
                (int)metrics.WindowWidth, (int)metrics.WindowHeight));
            window.AppWindow.Show(activateWindow: false);
            await loaded.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var ring = (ItemsRepeater)root.FindName("BrowserRing");
            // Window x:Bind initializes on activation; keep this test offscreen and inactive.
            ring.ItemsSource = window.ViewModel.Browsers;
            var first = (RadialBrowserItem)ring.GetOrCreateElement(0);
            var second = (RadialBrowserItem)ring.GetOrCreateElement(1);
            root.UpdateLayout();
            var pill = (Button)root.FindName("NamePill");
            var title = (TextBlock)root.FindName("PillTitle");
            var host = (TextBlock)root.FindName("PillHost");
            int focusChanges = 0;
            root.GotFocus += (_, _) => focusChanges++;
            first.FocusItem(FocusState.Keyboard);
            second.FocusItem(FocusState.Keyboard);
            await CheckSettledAsync(second, "Browser 2");
            Check(host.ActualWidth > 0 && host.ActualWidth < pill.ActualWidth - 40,
                "long link hosts stay within the card and leave room for link options");

            second.FocusItem(FocusState.Programmatic);
            first.FocusItem(FocusState.Programmatic);
            await CheckSettledAsync(first, "Browser 1");

            first.FocusItem(FocusState.Keyboard);
            second.FocusItem(FocusState.Keyboard);
            pill.Focus(FocusState.Programmatic);
            await Task.Delay(100);
            Check(ReferenceEquals(FocusManager.GetFocusedElement(root.XamlRoot), pill),
                "stale browser focus notifications do not steal focus from link options");
            Check(title.Text == "Browser 1", "stale focus does not replace the selected browser label");
            int before = focusChanges;
            await Task.Delay(100);
            Check(focusChanges == before, "focus stays on link options without new input");

            second.FocusItem(FocusState.Keyboard);
            await CheckSettledAsync(second, "Browser 2");
            File.WriteAllText(resultPath, "PASS: selector focus settles and stale notifications cannot change selection.");
            Console.WriteLine(File.ReadAllText(resultPath));

            async Task CheckSettledAsync(RadialBrowserItem item, string name)
            {
                await Task.Delay(100);
                var focused = FocusManager.GetFocusedElement(root.XamlRoot) as DependencyObject;
                while (focused is not null && !ReferenceEquals(focused, item))
                {
                    focused = VisualTreeHelper.GetParent(focused);
                }
                Check(ReferenceEquals(focused, item), "latest browser focus wins");
                Check(title.Text == name, "selected browser label follows the latest focus");
                int before = focusChanges;
                await Task.Delay(100);
                Check(focusChanges == before, "browser focus stays stable without new input");
                Check(title.Text == name, "selected browser label stays stable without new input");
            }
        }
        catch (Exception error)
        {
            Environment.ExitCode = 1;
            File.WriteAllText(resultPath, error.ToString());
            Console.Error.WriteLine(error);
        }
        finally
        {
            typeof(Quiver.App.App).GetProperty("IsExiting", BindingFlags.Static | BindingFlags.NonPublic)!
                .SetValue(null, true);
            if (window is not null)
            {
                window.Content = null;
                window.Close();
            }
            services?.Dispose();
            Exit();
        }
    }

    private static void Check(bool condition, string name)
    {
        if (!condition) throw new Exception($"Selector check failed: {name}");
    }

    private static void CheckMetrics()
    {
        var pair = RadialMetrics.For(2);
        Check(pair.FlowerHeight == RadialMetrics.ItemSize && pair.FlowerOrigin.Y == RadialMetrics.PetalRadius,
            "two browsers occupy one row without an empty square viewport");
        Check(pair.PillTop - pair.FlowerTop - pair.FlowerHeight == RadialMetrics.PillGap,
            "link options sit directly below the visible browser petals");
        Check(pair.RingRadius == 58 && RadialMetrics.For(4).RingRadius == 64
            && Math.Abs(RadialMetrics.For(6).RingRadius - 80) < 0.0001,
            "ink stamp ring radii match the 2, 4, and 6 browser concept");
        foreach (int count in new[] { 2, 3, 4, 6, 10 })
        {
            var metrics = RadialMetrics.For(count);
            Check(metrics.CenterFillRadius > 0 && metrics.CenterFillRadius < metrics.RingRadius,
                "the ink center disc joins the petals without covering them");
            Check(metrics.PillWidth >= 264, "link options keep the concept caption width");
        }
        foreach (int count in new[] { 0, 1, 2, 3, 6, 10 })
        {
            var metrics = RadialMetrics.For(count);
            for (int index = 0; index < count; index++)
            {
                var center = metrics.ItemCenter(index, metrics.FlowerOrigin);
                Check(center.X - RadialMetrics.PetalRadius >= -0.0001
                    && center.Y - RadialMetrics.PetalRadius >= -0.0001
                    && center.X + RadialMetrics.PetalRadius <= metrics.FlowerSize + 0.0001
                    && center.Y + RadialMetrics.PetalRadius <= metrics.FlowerHeight + 0.0001,
                    "every browser petal fits inside its visible host");
            }
        }
    }
}

internal sealed class TestSettings : ISettingsService
{
    private readonly Settings settings = new()
    {
        Browsers = new([new Browser("Browser 1", "selector-test-1.exe"),
            new Browser("Browser 2", "selector-test-2.exe")]),
    };
    public Settings LoadSettings() => settings;
    public PreparedRulesets PreparedRulesets => throw new NotSupportedException();
    public Task FlushAsync() => Task.CompletedTask;
    public event EventHandler<SettingsChangedEventArgs>? SettingsChanged { add { } remove { } }
    public void UpdateAppSettings(AppSettings value) => throw new NotSupportedException();
    public void UpdateQuickView(QuickViewSettings value) => throw new NotSupportedException();
    public void UpdateBrowsers(ObservableCollection<Browser> value) => throw new NotSupportedException();
    public void UpdateRulesets(ObservableCollection<Ruleset> value) => throw new NotSupportedException();
}

internal sealed class TestQuickView : IQuickViewService
{
    public bool IsQuickViewEnabled => false;
    public bool TryOpen(string? url) => false;
    public bool TryOpenIfModifierKeyActivated(string? url) => false;
}

internal sealed class TestIcons : IIconLoader
{
    public Task StopAsync() => Task.CompletedTask;
    public Task FlushCacheMaintenanceAsync() => Task.CompletedTask;
    public Task<BitmapImage?> LoadIconAsync(Browser browser, CancellationToken cancellationToken = default) => Task.FromResult<BitmapImage?>(null);
    public Task<BitmapImage?> LoadIconFromExe(string path, CancellationToken cancellationToken = default) => Task.FromResult<BitmapImage?>(null);
    public Task<BitmapImage?> LoadIconFromExe(string path, int index, CancellationToken cancellationToken = default) => Task.FromResult<BitmapImage?>(null);
    public Task<int> GetExeIconCountAsync(string path, CancellationToken cancellationToken = default) => Task.FromResult(0);
    public Task<BitmapImage?> LoadIconFromIco(string path, CancellationToken cancellationToken = default) => Task.FromResult<BitmapImage?>(null);
    public Task<BitmapImage?> LoadIconFromImage(string path, CancellationToken cancellationToken = default) => Task.FromResult<BitmapImage?>(null);
    public Task<BitmapImage?> LoadIconFromURL(string url, CancellationToken cancellationToken = default) => Task.FromResult<BitmapImage?>(null);
}
