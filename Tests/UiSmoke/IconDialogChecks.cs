using Microsoft.UI.Xaml.Media.Imaging;
using Quiver.App.Services.Interfaces;
using Quiver.App.ViewModels;
using Quiver.Library.Models;

namespace Quiver.UiSmoke;

internal static class IconDialogChecks
{
    public static async Task RunAsync(BitmapImage image)
    {
        var loader = new GatedIconLoader(image);
        var dialog = new BrowserIconDialogViewModel(loader, "fixture.exe", null);
        await dialog.SelectSourceAsync(BrowserIconSource.Url);
        Check(loader.Requests == 0 && dialog.SelectedSource == BrowserIconSource.Executable && dialog.CanInteract,
            "dialog ignores source changes before it opens");
        await dialog.OpenAsync();
        Check(dialog.ExeIcons.Count == 16 && dialog.Selection?.Icon.Index == 0 && dialog.CanSave,
            "opening loads the first executable page");

        var gate = loader.Hold();
        var paging = dialog.LoadMoreExecutableIconsAsync();
        Check(dialog.IsLoadingExeIcons && !dialog.IsBusy, "paging shows the page spinner, not the dialog spinner");
        Check(!dialog.CanInteract && !dialog.CanSave && !dialog.CanLoadMoreExeIcons && !dialog.CanLoadPreviousExeIcons,
            "paging blocks interaction and save");
        dialog.UrlText = "https://example.com/icon.png";
        await dialog.SelectSourceAsync(BrowserIconSource.Url);
        await dialog.LoadUrlAsync();
        Check(dialog.SelectedSource == BrowserIconSource.Executable && loader.UrlRequests == 0,
            "paging rejects source switches and URL loads");
        gate.SetResult();
        await paging;
        Check(dialog.CanInteract && dialog.HasPreviousExeIcons && dialog.ExeIcons[0].Icon.Index == 16
            && dialog.Selection?.Icon.Index == 0, "page finishes and keeps the off-page selection");

        await dialog.SelectSourceAsync(BrowserIconSource.Url);
        Check(dialog.Selection?.Source == "https://example.com/icon.png", "URL tab loads its pending URL");
        gate = loader.Hold();
        dialog.UrlText = "https://example.com/other.png";
        var staleLoad = dialog.LoadUrlAsync();
        dialog.UrlText = "https://example.com/edited.png";
        gate.SetResult();
        await staleLoad;
        Check(dialog.Selection is null && !dialog.HasError && dialog.CanInteract, "URL edited during load discards the result");

        gate = loader.Hold();
        var closedLoad = dialog.LoadUrlAsync();
        dialog.Close();
        gate.SetResult();
        await closedLoad;
        Check(dialog.Selection is null && !dialog.IsOpen && !dialog.CanInteract && !dialog.HasError,
            "close during image load discards the result");
        await dialog.SelectSourceAsync(BrowserIconSource.Executable);
        Check(dialog.SelectedSource == BrowserIconSource.Url, "closed dialog ignores further input");

        var closingLoader = new GatedIconLoader(image);
        var closing = new BrowserIconDialogViewModel(closingLoader, "fixture.exe", null);
        gate = closingLoader.Hold();
        var initialLoad = closing.OpenAsync();
        Check(closing.IsBusy && !closing.IsLoadingExeIcons && !closing.CanInteract, "initial load shows the dialog spinner only");
        closing.Close();
        gate.SetResult();
        await initialLoad;
        Check(closing.ExeIcons.Count == 0 && closing.Selection is null && !closing.HasError,
            "close during executable load discards the result");
    }

    private static void Check(bool condition, string caseName)
    {
        if (!condition) throw new Exception($"UiSmoke failed: {caseName}");
    }

    // Held requests ignore cancellation so they can finish after Close, like a loader that missed the token.
    private sealed class GatedIconLoader(BitmapImage image) : IIconLoader
    {
        private TaskCompletionSource? gate;
        public int Requests { get; private set; }
        public int UrlRequests { get; private set; }

        public TaskCompletionSource Hold() => gate = new(TaskCreationOptions.RunContinuationsAsynchronously);

        private async Task<T> PassAsync<T>(T value)
        {
            Requests++;
            if (gate is { } held) await held.Task;
            return value;
        }

        public Task StopAsync() => Task.CompletedTask;
        public Task FlushCacheMaintenanceAsync() => Task.CompletedTask;
        public Task<BitmapImage?> LoadIconAsync(Browser browser, CancellationToken cancellationToken = default) => PassAsync<BitmapImage?>(image);
        public Task<BitmapImage?> LoadIconFromExe(string exePath, CancellationToken cancellationToken = default) => PassAsync<BitmapImage?>(image);
        public Task<BitmapImage?> LoadIconFromExe(string exePath, int iconIndex, CancellationToken cancellationToken = default) => PassAsync<BitmapImage?>(image);
        public Task<int> GetExeIconCountAsync(string exePath, CancellationToken cancellationToken = default) => PassAsync(32);
        public Task<BitmapImage?> LoadIconFromIco(string icoPath, CancellationToken cancellationToken = default) => PassAsync<BitmapImage?>(image);
        public Task<BitmapImage?> LoadIconFromImage(string imagePath, CancellationToken cancellationToken = default) => PassAsync<BitmapImage?>(image);

        public Task<BitmapImage?> LoadIconFromURL(string url, CancellationToken cancellationToken = default)
        {
            UrlRequests++;
            return PassAsync<BitmapImage?>(image);
        }
    }
}
