using Quiver.App.Services.Interfaces;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Quiver.App.ViewModels;

internal class BrowsersPageViewModel
{
    private const int MaxConcurrentIconLoads = 4;
    private readonly ISettingsService settingsService;
    private readonly IIconLoader iconLoader;
    private readonly HashSet<BrowserItemViewModel> realizedItems = [];
    private readonly HashSet<BrowserItemViewModel> attemptedItems = [];
    private readonly Dictionary<BrowserItemViewModel, IconLoadRequest> activeIconLoads = [];
    private bool isPumpingIconLoads;

    public ObservableCollection<BrowserItemViewModel> Browsers { get; }

    public BrowsersPageViewModel(ISettingsService settingsService, IIconLoader iconLoader)
    {
        this.settingsService = settingsService;
        this.iconLoader = iconLoader;
        Browsers = new(settingsService.LoadSettings().Browsers.Select(browser => new BrowserItemViewModel(browser)));
        Browsers.CollectionChanged += Browsers_CollectionChanged;
    }

    public void SetBrowserRealized(BrowserItemViewModel item, bool isRealized)
    {
        if (isRealized)
        {
            if (!Browsers.Contains(item))
            {
                return;
            }

            realizedItems.Add(item);
            StartPendingIconLoads();
            return;
        }

        realizedItems.Remove(item);
        attemptedItems.Remove(item);
        CancelIconLoad(item);
        item.Icon = null;
    }

    public void CancelIconLoading()
    {
        foreach (BrowserItemViewModel item in realizedItems)
        {
            item.Icon = null;
        }
        realizedItems.Clear();
        attemptedItems.Clear();
        foreach (IconLoadRequest request in activeIconLoads.Values.ToArray())
        {
            Cancel(request);
        }
    }

    public void RefreshBrowserList(BrowserRefreshMode mode)
    {
        foreach (var browser in Library.GetBrowsers.FromRegistry())
        {
            if (mode == BrowserRefreshMode.AddAllDetectedAsNew
                || !Browsers.Any(existing => existing.Model.ExePath == browser.ExePath))
            {
                Browsers.Add(new BrowserItemViewModel(browser));
            }
        }
        SaveBrowsers();
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

    private void StartPendingIconLoads()
    {
        if (isPumpingIconLoads)
        {
            return;
        }

        isPumpingIconLoads = true;
        try
        {
            while (activeIconLoads.Count < MaxConcurrentIconLoads)
            {
                BrowserItemViewModel? item = FindPendingIconLoad();
                if (item is null)
                {
                    return;
                }

                attemptedItems.Add(item);
                var request = new IconLoadRequest(new CancellationTokenSource());
                activeIconLoads.Add(item, request);
                _ = LoadIconAsync(item, request);
            }
        }
        finally
        {
            isPumpingIconLoads = false;
        }
    }

    private BrowserItemViewModel? FindPendingIconLoad()
    {
        foreach (BrowserItemViewModel item in realizedItems)
        {
            if (item.Icon is null && !activeIconLoads.ContainsKey(item) && !attemptedItems.Contains(item))
            {
                return item;
            }
        }

        return null;
    }

    private async Task LoadIconAsync(BrowserItemViewModel item, IconLoadRequest request)
    {
        try
        {
            var icon = await iconLoader.LoadIconAsync(item.Model, request.Cancellation.Token);
            if (!request.Cancellation.IsCancellationRequested && realizedItems.Contains(item)
                && activeIconLoads.TryGetValue(item, out var activeRequest)
                && ReferenceEquals(activeRequest, request))
            {
                item.Icon = icon;
            }
        }
        catch (OperationCanceledException) when (request.Cancellation.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Could not load browser icon: {ex}");
        }
        finally
        {
            if (activeIconLoads.TryGetValue(item, out var activeRequest)
                && ReferenceEquals(activeRequest, request))
            {
                activeIconLoads.Remove(item);
            }
            request.Cancellation.Dispose();
            StartPendingIconLoads();
        }
    }

    private void CancelIconLoad(BrowserItemViewModel item)
    {
        if (activeIconLoads.TryGetValue(item, out var request))
        {
            Cancel(request);
        }
    }

    private static void Cancel(IconLoadRequest request)
    {
        try
        {
            request.Cancellation.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private void Browsers_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        foreach (BrowserItemViewModel item in realizedItems.ToArray())
        {
            if (!Browsers.Contains(item))
            {
                realizedItems.Remove(item);
                attemptedItems.Remove(item);
                CancelIconLoad(item);
                item.Icon = null;
            }
        }
    }

    private void SaveBrowsers() => settingsService.UpdateBrowsers(new(Browsers.Select(item => item.Model)));

    private sealed record IconLoadRequest(CancellationTokenSource Cancellation);
}

internal enum BrowserRefreshMode
{
    PreserveExistingByExePath,
    AddAllDetectedAsNew
}
