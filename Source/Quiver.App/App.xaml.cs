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
    private bool isLaunched;
    private TrayService? trayService;

    internal static bool IsExiting { get; private set; }

    public App()
    {
        dispatcherQueue = DispatcherQueue.GetForCurrentThread();
        dispatcherQueue.ShutdownStarting += DispatcherQueue_ShutdownStarting;
        Services = ConfigureServices();
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

    // Packaged launches only pass arguments through activation, so Settings is a Jump List task rather than a second tile.
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

    private void HandleActivation(CliArgs cliArgs)
    {
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

        var settings = services.GetRequiredService<ISettingsService>().LoadSettings();
        if (cliArgs.Url is not null
            && settings.AppSettings.RuleMatching
            && RuleMatch.CheckRulesets(cliArgs.Url, settings.Rulesets) is Ruleset matchingRuleset)
        {
            var selectedBrowser = settings.Browsers.FirstOrDefault(b => b.Id == matchingRuleset.BrowserId);
            if (selectedBrowser is not null)
            {
                try
                {
                    UriLauncher.ResolveAutomatically(cliArgs.Url, selectedBrowser, matchingRuleset.AlternateLaunchId);
                    return;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine(ex);
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

    private void ReloadApp()
    {
        // Restart ends this process before the new one starts, so the new one owns the single-instance key.
        trayService?.Dispose();
        var reason = AppInstance.Restart(string.Empty);
        Debug.WriteLine($"Restart failed: {reason}");
        ExitApp();
    }

    private void ExitApp()
    {
        IsExiting = true;
        trayService?.Dispose();
        Exit();
    }

    private void DispatcherQueue_ShutdownStarting(DispatcherQueue sender, DispatcherQueueShutdownStartingEventArgs args)
    {
        IsExiting = true;
        trayService?.Dispose();
        dispatcherQueue.ShutdownStarting -= DispatcherQueue_ShutdownStarting;
    }

    public static void ShowSettings(string page = "browsers")
    {
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
            ExitApp();
        }
    }

    [LibraryImport("user32.dll", EntryPoint = "MessageBoxW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int MessageBox(IntPtr hWnd, string text, string caption, uint type);
}
