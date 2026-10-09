using Quiver.App.Services.Interfaces;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace Quiver.App.ViewModels;

internal class BrowsersPageViewModel
{
    private readonly ISettingsService settingsService;
    private readonly IIconLoader iconLoader;
    private CancellationTokenSource? iconLoadCancellation;

    public ObservableCollection<BrowserItemViewModel> Browsers { get; }

    public BrowsersPageViewModel(ISettingsService settingsService, IIconLoader iconLoader)
    {
        this.settingsService = settingsService;
        this.iconLoader = iconLoader;
        Browsers = new(settingsService.LoadSettings().Browsers.Select(browser => new BrowserItemViewModel(browser)));
    }

    public async Task LoadIconsAsync()
    {
        iconLoadCancellation?.Cancel();
        var cancellation = new CancellationTokenSource();
        iconLoadCancellation = cancellation;
        using (cancellation)
        using (var concurrency = new SemaphoreSlim(4))
        {
            try
            {
                await Task.WhenAll(Browsers.Where(item => item.Icon is null).ToArray().Select(async item =>
                {
                    await concurrency.WaitAsync(cancellation.Token);
                    try
                    {
                        cancellation.Token.ThrowIfCancellationRequested();
                        var icon = await iconLoader.LoadIconAsync(item.Model, cancellation.Token);
                        if (!cancellation.IsCancellationRequested)
                        {
                            item.Icon = icon;
                        }
                    }
                    finally
                    {
                        concurrency.Release();
                    }
                }));
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
            catch (Exception ex)
            {
                Debug.WriteLine($"Could not load browser icons: {ex}");
            }
            finally
            {
                if (ReferenceEquals(iconLoadCancellation, cancellation))
                {
                    iconLoadCancellation = null;
                }
            }
        }
    }

    public void CancelIconLoading()
    {
        iconLoadCancellation?.Cancel();
        iconLoadCancellation = null;
    }

    public async Task RefreshBrowserListAsync(BrowserRefreshMode mode)
    {
        CancelIconLoading();
        foreach (var browser in Library.GetBrowsers.FromRegistry())
        {
            if (mode == BrowserRefreshMode.AddAllDetectedAsNew
                || !Browsers.Any(existing => existing.Model.ExePath == browser.ExePath))
            {
                Browsers.Add(new BrowserItemViewModel(browser));
            }
        }
        SaveBrowsers();
        await LoadIconsAsync();
    }

    public void DeleteBrowser(Guid browserId)
    {
        var browser = Browsers.FirstOrDefault(browser => browser.Model.Id == browserId);
        if (browser != null && Browsers.Remove(browser))
        {
            SaveBrowsers();
        }
    }

    internal void UpdateBrowserOrder() => SaveBrowsers();

    private void SaveBrowsers() => settingsService.UpdateBrowsers(new(Browsers.Select(item => item.Model)));
}

internal enum BrowserRefreshMode
{
    PreserveExistingByExePath,
    AddAllDetectedAsNew
}
