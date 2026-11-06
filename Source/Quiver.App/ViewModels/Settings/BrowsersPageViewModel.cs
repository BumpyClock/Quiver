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
    // Presence means realized. Done covers loaded and failed alike, so a failed item is not retried until unrealized.
    private readonly Dictionary<BrowserItemViewModel, IconLoadState> iconStates = [];
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

            iconStates.TryAdd(item, new IconLoadState.Pending());
            StartPendingIconLoads();
            return;
        }

        Unrealize(item);
    }

    public void CancelIconLoading()
    {
        foreach (BrowserItemViewModel item in iconStates.Keys.ToArray())
        {
            Unrealize(item);
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
            while (iconStates.Values.Count(state => state is IconLoadState.Loading) < MaxConcurrentIconLoads)
            {
                BrowserItemViewModel? item = iconStates
                    .FirstOrDefault(entry => entry.Value is IconLoadState.Pending).Key;
                if (item is null)
                {
                    return;
                }

                var request = new IconLoadRequest(new CancellationTokenSource());
                iconStates[item] = new IconLoadState.Loading(request);
                _ = LoadIconAsync(item, request);
            }
        }
        finally
        {
            isPumpingIconLoads = false;
        }
    }

    private async Task LoadIconAsync(BrowserItemViewModel item, IconLoadRequest request)
    {
        try
        {
            var icon = await iconLoader.LoadIconAsync(item.Model, request.Cancellation.Token);
            if (!request.Cancellation.IsCancellationRequested && IsCurrent(item, request))
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
            if (IsCurrent(item, request))
            {
                iconStates[item] = new IconLoadState.Done();
            }
            request.Cancellation.Dispose();
            StartPendingIconLoads();
        }
    }

    private bool IsCurrent(BrowserItemViewModel item, IconLoadRequest request)
        => iconStates.TryGetValue(item, out var state)
            && state is IconLoadState.Loading loading
            && ReferenceEquals(loading.Request, request);

    private void Unrealize(BrowserItemViewModel item)
    {
        if (iconStates.Remove(item, out var state) && state is IconLoadState.Loading loading)
        {
            Cancel(loading.Request);
        }
        item.Icon = null;
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
        foreach (BrowserItemViewModel item in iconStates.Keys.ToArray())
        {
            if (!Browsers.Contains(item))
            {
                Unrealize(item);
            }
        }
    }

    private void SaveBrowsers() => settingsService.UpdateBrowsers(new(Browsers.Select(item => item.Model)));

    private sealed record IconLoadRequest(CancellationTokenSource Cancellation);

    private abstract record IconLoadState
    {
        public sealed record Pending : IconLoadState;
        public sealed record Loading(IconLoadRequest Request) : IconLoadState;
        public sealed record Done : IconLoadState;
    }
}

internal enum BrowserRefreshMode
{
    PreserveExistingByExePath,
    AddAllDetectedAsNew
}
