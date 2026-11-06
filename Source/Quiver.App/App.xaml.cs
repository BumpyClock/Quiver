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
using System.Collections.Generic;
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
    private static AppState state = new AppState.PreLaunch([]);
    private TrayService? trayService;
    private static bool fatalExit;

    // Exiting returns to Idle when the flush fails. ShuttingDown has disposed the tray and stopped the
    // icon loader, so nothing leaves it.
    private abstract record AppState
    {
        private AppState() { }

        public sealed record PreLaunch(List<CliArgs> Redirected) : AppState;
        public sealed record Idle : AppState;
        public sealed record Matching(CliArgs Activation, CancellationTokenSource Cancellation) : AppState;
        public sealed record Exiting(CliArgs? Deferred) : AppState;
        public sealed record ShuttingDown : AppState;
    }

    internal static bool IsShuttingDown => state is AppState.ShuttingDown;

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
        if (state is not AppState.PreLaunch launch)
        {
            return;
        }

        state = new AppState.Idle();
        HandleActivation(CliArgs.GatherInfo(AppInstance.GetCurrent().GetActivatedEventArgs(), false));
        foreach (CliArgs redirected in launch.Redirected)
        {
            HandleActivation(redirected);
        }
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
        var cliArgs = CliArgs.GatherInfo(args, true);
        _ = dispatcherQueue.TryEnqueue(() => HandleActivation(cliArgs));
    }

    private void SettingsChanged(object? sender, SettingsChangedEventArgs e)
    {
        if ((e.Section & (SettingsSection.Rulesets | SettingsSection.Browsers | SettingsSection.AppSettings)) != 0
            && state is AppState.Matching matching)
        {
            MatchRulesAndShowSelector(matching.Activation);
        }
    }

    private static void CancelActivation()
    {
        if (state is AppState.Matching matching)
        {
            matching.Cancellation.Cancel();
            state = new AppState.Idle();
        }
    }

    private void HandleActivation(CliArgs cliArgs)
    {
        switch (state)
        {
            case AppState.PreLaunch launch:
                launch.Redirected.Add(cliArgs);
                return;
            case AppState.Exiting exiting:
                state = exiting with { Deferred = cliArgs };
                return;
            case AppState.ShuttingDown:
                return;
        }

        CancelActivation();
        IServiceProvider services = Services ?? throw new InvalidOperationException("Application services are not configured.");

        if (cliArgs is CliArgs.OpenSettings settings)
        {
            ShowSettings(settings.Page);
            return;
        }

        if (services.GetRequiredService<IQuickViewService>().TryOpenIfModifierKeyActivated((cliArgs as CliArgs.OpenUrl)?.Url))
        {
            return;
        }

        MatchRulesAndShowSelector(cliArgs);
    }

    private async void MatchRulesAndShowSelector(CliArgs cliArgs)
    {
        CancelActivation();
        if (state is not AppState.Idle) return;
        IServiceProvider services = Services ?? throw new InvalidOperationException("Application services are not configured.");

        var settingsService = services.GetRequiredService<ISettingsService>();
        var settings = settingsService.LoadSettings();
        if (settings.AppSettings.RuleMatching)
        {
            string url = (cliArgs as CliArgs.OpenUrl)?.Url ?? string.Empty;
            var snapshot = settingsService.PreparedRulesets;
            using var cancellation = new CancellationTokenSource();
            state = new AppState.Matching(cliArgs, cancellation);
            try
            {
                var match = await Task.Run(() => snapshot.Check(url, cancellation.Token), cancellation.Token);
                if (cancellation.IsCancellationRequested)
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
                if (state is AppState.Matching current && ReferenceEquals(current.Cancellation, cancellation))
                {
                    state = new AppState.Idle();
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

    private void ReloadApp() => ExitApp(restart: true);

    private void ExitApp() => ExitApp(restart: false);

    private async void ExitApp(bool restart)
    {
        if (state is AppState.Exiting or AppState.ShuttingDown)
        {
            return;
        }

        // A link still being rule-matched is deferred, so an aborted exit opens it instead of losing it.
        CliArgs? matching = (state as AppState.Matching)?.Activation;
        CancelActivation();
        state = new AppState.Exiting(Deferred: matching);
        bool flushed = await FlushPendingWorkAsync();
        if (state is not AppState.Exiting exiting)
        {
            return;
        }

        // A fatal error raised while the save-failure dialog was open still exits.
        if (!flushed && !fatalExit)
        {
            state = new AppState.Idle();
            if (exiting.Deferred is { } deferred)
            {
                HandleActivation(deferred);
            }
            return;
        }

        state = new AppState.ShuttingDown();
        trayService?.Dispose();
        if (restart && !fatalExit)
        {
            var reason = AppInstance.Restart(string.Empty);
            Debug.WriteLine($"Restart failed: {reason}");
        }
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
        CancelActivation();
        state = new AppState.ShuttingDown();
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
        CancelActivation();
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
