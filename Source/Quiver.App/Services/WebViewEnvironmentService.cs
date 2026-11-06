using Quiver.App.Services.Interfaces;
using Quiver.Library;
using Quiver.Library.Models;
using Microsoft.Web.WebView2.Core;
using System;
using System.IO;
using System.Threading.Tasks;

namespace Quiver.App.Services;

public class WebViewEnvironmentService : IWebViewEnvironmentService
{
    private readonly Func<Task<CoreWebView2Environment>> createEnvironment;
    private Task<CoreWebView2Environment>? environmentTask;

    public WebViewEnvironmentService(ISettingsService settingsService)
        : this(() => CreateEnvironmentAsync(settingsService))
    {
    }

    internal WebViewEnvironmentService(Func<Task<CoreWebView2Environment>> createEnvironment)
    {
        this.createEnvironment = createEnvironment;
    }

    // Callers run on the UI thread, so a plain field shares one in-flight creation without locking.
    // A faulted or canceled creation is replaced so a later Quick View window can retry.
    public Task<CoreWebView2Environment> GetEnvironmentAsync()
    {
        if (environmentTask is null || environmentTask.IsFaulted || environmentTask.IsCanceled)
        {
            environmentTask = createEnvironment();
        }

        return environmentTask;
    }

    private static async Task<CoreWebView2Environment> CreateEnvironmentAsync(ISettingsService settingsService)
    {
        Settings settings = settingsService.LoadSettings();
        QuickViewSettings quickView = settings.QuickView ?? new QuickViewSettings();
        CoreWebView2EnvironmentOptions options = new()
        {
            ScrollBarStyle = CoreWebView2ScrollbarStyle.FluentOverlay,
            AdditionalBrowserArguments = quickView.AdditionalBrowserArguments ?? string.Empty,
            AreBrowserExtensionsEnabled = quickView.BrowserExtensionsEnabled
        };

        Directory.CreateDirectory(Constants.APP_SETTINGS_DIR);
        return await CoreWebView2Environment.CreateWithOptionsAsync(null, Constants.APP_SETTINGS_DIR, options);
    }
}
