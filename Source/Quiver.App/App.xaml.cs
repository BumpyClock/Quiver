using Quiver.App.Helpers;
using Quiver.App.Services;
using Quiver.App.Services.Interfaces;
using Quiver.App.ViewModels;
using Quiver.App.Windows;
using Quiver.Library;
using Quiver.Library.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Dispatching;
using Microsoft.Windows.AppLifecycle;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Windows.UI.StartScreen;
using WinUIEx;

namespace Quiver.App;

public partial class App : Microsoft.UI.Xaml.Application
{
    public static IServiceProvider? Services { get; private set; }

    private static SelectorWindow? _selectorWindow;
    private static SettingsWindow? _settingsWindow;
    private readonly DispatcherQueue dispatcherQueue;
    private CliArgs? _pendingActivation;
    private CliArgs? _matchingActivation;
    private CancellationTokenSource? _activationCancellation;
    private bool isLaunched;
    private TrayService? trayService;
    private static bool fatalExit;

    internal static bool IsExiting { get; private set; }

    public App()
    {
        dispatcherQueue = DispatcherQueue.GetForCurrentThread();
        dispatcherQueue.ShutdownStarting += DispatcherQueue_ShutdownStarting;
        Services = ConfigureServices();
        Services.GetRequiredService<ISettingsService>().SettingsChanged += SettingsChanged;
        InitializeComponent();
        Current.UnhandledException += Dispatcher_UnhandledException;
        DispatcherShutdownMode = Microsoft.UI.Xaml.DispatcherShutdownMode.OnLastWindowClose;
        AppInstance.GetCurrent().Activated += AppInstance_Activated;
    }

    private static ServiceProvider ConfigureServices()
    {
        var services = new ServiceCollection();

        services.AddSingleton<ISettingsService, JsonFileService>();
        services.AddSingleton<IIconLoader, IconLoaderService>();
        // selector
        services.AddSingleton<IWebViewEnvironmentService, WebViewEnvironmentService>();
        services.AddSingleton<IQuickViewService, QuickViewService>();
        services.AddTransient<SelectorPageViewModel>();
        // settings
        services.AddTransient<SettingsPageViewModel>();
        services.AddTransient<BrowsersPageViewModel>();
        services.AddTransient<RulesetPageViewModel>();
        services.AddTransient<QuickViewPageViewModel>();
        services.AddTransient<StoreRulesetViewModel>();

        return services.BuildServiceProvider();
    }

    protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
    {
        isLaunched = true;
        HandleActivation(_pendingActivation ?? CliArgs.GatherInfo(AppInstance.GetCurrent().GetActivatedEventArgs(), false));
        _pendingActivation = null;
        RegisterJumpList();
    }

    private static async void RegisterJumpList()
    {
        try
        {
            if (!PackageIdentity.IsPackaged || !JumpList.IsSupported())
            {
                return;
            }

            JumpList jumpList = await JumpList.LoadCurrentAsync();
            jumpList.Items.Clear();
            JumpListItem settings = JumpListItem.CreateWithArguments("--settings", "Quiver Settings");
            settings.Logo = new Uri("ms-appx:///Assets/Package/Square44x44Logo.png");
            jumpList.Items.Add(settings);
            await jumpList.SaveAsync();
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex);
        }
    }

    private void AppInstance_Activated(object? sender, AppActivationArguments args)
    {
        // Read redirected arguments on the thread that received them; their Data is not usable from the UI thread.
        if (!isLaunched)
        {
            _pendingActivation = CliArgs.GatherInfo(args, false);
            return;
        }

        var cliArgs = CliArgs.GatherInfo(args, true);
        _ = dispatcherQueue.TryEnqueue(() => HandleActivation(cliArgs));
    }

    private void SettingsChanged(object? sender, SettingsChangedEventArgs e)
    {
        if ((e.Section & (SettingsSection.Rulesets | SettingsSection.Browsers | SettingsSection.AppSettings)) != 0
            && _matchingActivation is { } activation)
        {
            MatchRulesAndShowSelector(activation);
        }
    }

    private void CancelActivation()
    {
        _activationCancellation?.Cancel();
        _matchingActivation = null;
    }

    private void HandleActivation(CliArgs cliArgs)
    {
        CancelActivation();
        if (IsExiting)
        {
            return;
        }
        IServiceProvider services = Services ?? throw new InvalidOperationException("Application services are not configured.");

        if (cliArgs.SettingsPage is string page)
        {
            ShowSettings(page);
            return;
        }

        if (services.GetRequiredService<IQuickViewService>().TryOpenIfModifierKeyActivated(cliArgs.Url))
        {
            return;
        }

        MatchRulesAndShowSelector(cliArgs);
    }

    private async void MatchRulesAndShowSelector(CliArgs cliArgs)
    {
        CancelActivation();
        if (IsExiting) return;
        IServiceProvider services = Services ?? throw new InvalidOperationException("Application services are not configured.");

        var settingsService = services.GetRequiredService<ISettingsService>();
        var settings = settingsService.LoadSettings();
        if (cliArgs.Url is { } url && settings.AppSettings.RuleMatching)
        {
            var snapshot = settingsService.PreparedRulesets;
            using var cancellation = new CancellationTokenSource();
            _activationCancellation = cancellation;
            _matchingActivation = cliArgs;
            try
            {
                var match = await Task.Run(() => snapshot.Check(url, cancellation.Token), cancellation.Token);
                if (cancellation.IsCancellationRequested || IsExiting)
                {
                    return;
                }
                if (match is not null)
                {
                    var selectedBrowser = settingsService.LoadSettings().Browsers.FirstOrDefault(b => b.Id == match.BrowserId);
                    if (selectedBrowser is not null)
                    {
                        try
                        {
                            UriLauncher.ResolveAutomatically(url, selectedBrowser, match.AlternateLaunchId);
                            return;
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine(ex);
                        }
                    }
                }
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
            {
                return;
            }
            finally
            {
                if (ReferenceEquals(_activationCancellation, cancellation))
                {
                    _activationCancellation = null;
                    _matchingActivation = null;
                }
            }
        }


        _selectorWindow ??= new SelectorWindow();
        trayService ??= new TrayService(ShowSelector, () => ShowSettings("settings"), ReloadApp, ExitApp);
        _selectorWindow.Init(cliArgs);
    }

    private static void ShowSelector()
    {
        _selectorWindow ??= new SelectorWindow();
        _selectorWindow.ShowWindow();
    }

    private async void ReloadApp()
    {
        IsExiting = true;
        CancelActivation();
        if (!await FlushPendingWorkAsync())
        {
            IsExiting = false;
            return;
        }
        trayService?.Dispose();
        var reason = AppInstance.Restart(string.Empty);
        Debug.WriteLine($"Restart failed: {reason}");
        ExitApp();
    }

    private async void ExitApp()
    {
        IsExiting = true;
        CancelActivation();
        if (!await FlushPendingWorkAsync())
        {
            IsExiting = false;
            return;
        }
        trayService?.Dispose();
        Exit();
    }

    private static async Task<bool> FlushPendingWorkAsync()
    {
        try
        {
            _settingsWindow?.CommitPendingEdits();
            if (Services is { } services)
            {
                await services.GetRequiredService<ISettingsService>().FlushAsync();
                await services.GetRequiredService<IIconLoader>().StopAsync();
            }
            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex);
            if (fatalExit)
            {
                return true;
            }
            MessageBox(IntPtr.Zero, $"Settings could not be saved.\n\n{ex.Message}", "Quiver - Save Error", 0x00000010 | 0x00010000);
            return false;
        }
    }

    private async void DispatcherQueue_ShutdownStarting(DispatcherQueue sender, DispatcherQueueShutdownStartingEventArgs args)
    {
        IsExiting = true;
        CancelActivation();
        trayService?.Dispose();
        dispatcherQueue.ShutdownStarting -= DispatcherQueue_ShutdownStarting;
        var deferral = args.GetDeferral();
        try
        {
            await FlushPendingWorkAsync();
        }
        finally
        {
            deferral.Complete();
        }
    }

    public static void ShowSettings(string page = "browsers")
    {
        ((App)Current).CancelActivation();
        _selectorWindow?.MinimizeWindow();
        if (_settingsWindow is null)
        {
            _settingsWindow = new SettingsWindow();
            _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        }

        _settingsWindow.NavigateToPage(page);
        _settingsWindow.Restore();
        _settingsWindow.Activate();
        _settingsWindow.SetForegroundWindow();
    }

    private void Dispatcher_UnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        var exception = e.Exception?.GetBaseException();
        string title = exception is JsonException ? "Quiver - Invalid JSON" : "Quiver - Error";
        string summary = exception is JsonException
            ? "The UserSettings.json file contains invalid JSON."
            : "An unexpected error has occurred.";
        string errorMessage = $"{summary}\n\n{exception?.Message ?? e.Message}\n\nQuiver will close when you dismiss this message.";

        try
        {
            string crashDirectory = Path.Combine(Constants.APP_SETTINGS_DIR, "crashes");
            Directory.CreateDirectory(crashDirectory);
            string crashFile = Path.Combine(crashDirectory, $"{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}.txt");
            File.AppendAllText(crashFile, $"{e.Message}\n\n{e.Exception}\n");
            errorMessage += $"\n\nCrash log: {crashFile}";
        }
        catch (Exception logException)
        {
            Debug.WriteLine(logException);
        }

        try
        {
            const uint MB_ICONERROR = 0x00000010;
            const uint MB_TASKMODAL = 0x00002000;
            const uint MB_SETFOREGROUND = 0x00010000;

            MessageBox(IntPtr.Zero, errorMessage, title, MB_ICONERROR | MB_TASKMODAL | MB_SETFOREGROUND);
        }
        finally
        {
            fatalExit = true;
            e.Handled = true;
            ExitApp();
        }
    }

    [LibraryImport("user32.dll", EntryPoint = "MessageBoxW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int MessageBox(IntPtr hWnd, string text, string caption, uint type);
}
