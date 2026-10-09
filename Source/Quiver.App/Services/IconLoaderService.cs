using Quiver.App.Services.Interfaces;
using Quiver.Library;
using Quiver.Library.Models;
using Microsoft.UI.Xaml.Media.Imaging;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Windows.Graphics.Imaging;

namespace Quiver.App.Services;

public partial class IconLoaderService : IIconLoader
{
    /// <summary>
    /// The selector takes up 80x80 pixels. so size 256 can cover display scaling till 300%.
    /// </summary>
    private const int IconSize = 256;
    private const int MaxEncodedBytes = 8 * 1024 * 1024;
    private const long MaxDiskCacheBytes = 64L * 1024 * 1024;
    private const int MaxDecodedEntries = 64;
    private static readonly TimeSpan MaxCacheAge = TimeSpan.FromDays(30);
    private static readonly HttpClient HttpClient = new() { Timeout = TimeSpan.FromSeconds(10) };
    private readonly string cacheDirectory;
    private readonly Dictionary<string, PendingIconLoad> pending = [];
    // Abandoned requests leave deduplication before their cache work has finished.
    private readonly HashSet<PendingIconLoad> activeLoads = [];
    private readonly Dictionary<string, LinkedListNode<(string Key, BitmapImage Image, DateTime LoadedAt)>> decoded = [];
    private readonly LinkedList<(string Key, BitmapImage Image, DateTime LoadedAt)> decodedOrder = [];
    private readonly object sync = new();
    private Task diskMaintenanceTask = Task.CompletedTask;
    private bool diskMaintenanceRunning;
    private bool diskMaintenanceRequested;
    private bool stopping;

    private sealed class PendingIconLoad
    {
        public CancellationTokenSource Cancellation { get; } = new();
        public TaskCompletionSource<BitmapImage?> Completion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Waiters { get; set; } = 1;
        public bool Abandoned { get; set; }
    }

    public IconLoaderService(string? cacheDirectory = null)
    {
        this.cacheDirectory = cacheDirectory ?? Path.Combine(Constants.APP_SETTINGS_DIR, "cache", "icons");
    }

    #region Primary Methods
    public async Task<BitmapImage?> LoadIconAsync(Browser browser, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var IconConfig = browser.Icon;
        if (!string.IsNullOrWhiteSpace(IconConfig?.Path))
        {
            var customIcon = IconConfig.Source switch
            {
                BrowserIconSource.LocalImage => await LoadIconFromImage(IconConfig.Path, cancellationToken),
                BrowserIconSource.Url => await LoadIconFromURL(IconConfig.Path, cancellationToken),
                _ => null
            };
            if (customIcon is not null) return customIcon;
        }

        if (string.IsNullOrWhiteSpace(browser.ExePath)) return null;

        int index = IconConfig?.Source == BrowserIconSource.Executable ? Math.Max(0, IconConfig.Index) : 0;
        var icon = await LoadIconFromExe(browser.ExePath, index, cancellationToken);
        return icon ?? (index != 0 ? await LoadIconFromExe(browser.ExePath, cancellationToken) : null);
    }

    public Task<BitmapImage?> LoadIconFromExe(string exePath, CancellationToken cancellationToken = default) =>
        LoadIconFromExe(exePath, 0, cancellationToken);

    public Task<BitmapImage?> LoadIconFromExe(string exePath, int iconIndex, CancellationToken cancellationToken = default) =>
        LoadLocalIconAsync(exePath, cancellationToken, iconIndex);

    public Task<int> GetExeIconCountAsync(string exePath, CancellationToken cancellationToken = default) => Task.Run(() =>
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (sync)
            {
                if (stopping) return 0;
            }
            if (string.IsNullOrWhiteSpace(exePath)) return 0;
            string path = Path.GetFullPath(Environment.ExpandEnvironmentVariables(exePath.Trim().Trim('"')));
            if (!File.Exists(path)) return 0;

            uint count = ExtractIconEx(path, -1, IntPtr.Zero, IntPtr.Zero, 0);
            return count <= int.MaxValue ? (int)count : 0;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Could not enumerate executable icons: {ex.Message}");
            return 0;
        }
    }, cancellationToken);

    [LibraryImport("shell32.dll", EntryPoint = "ExtractIconExW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial uint ExtractIconEx(string file, int index, IntPtr largeIcons, IntPtr smallIcons, uint count);

    public Task<BitmapImage?> LoadIconFromIco(string icoPath, CancellationToken cancellationToken = default) =>
        LoadIconFromImage(icoPath, cancellationToken);

    public Task<BitmapImage?> LoadIconFromImage(string imagePath, CancellationToken cancellationToken = default) =>
        LoadLocalIconAsync(imagePath, cancellationToken);

    public async Task<BitmapImage?> LoadIconFromURL(string url, CancellationToken cancellationToken = default)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)
                || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            {
                return null;
            }

            // Remote images refresh after the cache entry expires or the URL changes.
            return await LoadCachedIconAsync($"url|{uri.AbsoluteUri}",
                token => DownloadBoundedAsync(uri, token), cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Could not load icon from Url: {ex.Message}");
            return null;
        }
    }
    #endregion

    #region Helper Methods
    private async Task<BitmapImage?> LoadLocalIconAsync(string path, CancellationToken cancellationToken, int? iconIndex = null)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (sync)
            {
                if (stopping) return null;
            }
            if (string.IsNullOrWhiteSpace(path))
            {
                return null;
            }

            path = Path.GetFullPath(Environment.ExpandEnvironmentVariables(path.Trim().Trim('"')));
            var file = new FileInfo(path);
            if (!file.Exists)
            {
                return null;
            }
            if (!iconIndex.HasValue && file.Length > MaxEncodedBytes) return null;

            string key = $"icon|{path.ToUpperInvariant()}|{file.Length}|{file.LastWriteTimeUtc.Ticks}|{iconIndex}|{IconSize}";
            return await LoadCachedIconAsync(key, token => iconIndex.HasValue
                ? Task.Run(() => ExtractIcon(path, iconIndex.Value), token)
                : ReadLocalBoundedAsync(path, token), cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Could not load local icon: {ex.Message}");
            return null;
        }
    }

    private static MemoryStream ExtractIcon(string path, int iconIndex)
    {
        using var icon = Icon.ExtractIcon(path, iconIndex, IconSize)
            ?? throw new IOException($"Icon {iconIndex} was not found in '{path}'.");
        using var bitmap = icon.ToBitmap();
        var stream = new MemoryStream();
        try
        {
            bitmap.Save(stream, ImageFormat.Png);
            stream.Position = 0;
            return stream;
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }
    #endregion

    #region Cache Helper Methods
    private async Task<BitmapImage?> LoadCachedIconAsync(string key, Func<CancellationToken, Task<MemoryStream>> loadSource,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        PendingIconLoad request;
        bool startLoad = false;
        lock (sync)
        {
            if (stopping) return null;
            if (decoded.TryGetValue(key, out var node))
            {
                decodedOrder.Remove(node);
                if (node.Value.LoadedAt >= DateTime.UtcNow - MaxCacheAge)
                {
                    decodedOrder.AddFirst(node);
                    return node.Value.Image;
                }
                decoded.Remove(key);
            }
            if (pending.TryGetValue(key, out var existing))
            {
                request = existing;
                request.Waiters++;
            }
            else
            {
                request = new PendingIconLoad();
                pending.Add(key, request);
                activeLoads.Add(request);
                startLoad = true;
            }
        }

        if (startLoad)
        {
            string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)));
            _ = CompleteLoadAsync(key, Path.Combine(cacheDirectory, hash + ".img"), loadSource, request);
        }
        try
        {
            return await request.Completion.Task.WaitAsync(cancellationToken);
        }
        finally
        {
            bool cancel = false;
            lock (sync)
            {
                if (--request.Waiters == 0 && !request.Completion.Task.IsCompleted)
                {
                    request.Abandoned = true;
                    if (pending.TryGetValue(key, out var current) && ReferenceEquals(current, request))
                        pending.Remove(key);
                    cancel = true;
                }
            }
            if (cancel)
            {
                try { request.Cancellation.Cancel(); }
                catch (ObjectDisposedException) { }
            }
        }
    }

    private async Task CompleteLoadAsync(string key, string cachePath,
        Func<CancellationToken, Task<MemoryStream>> loadSource, PendingIconLoad request)
    {
        BitmapImage? image = null;
        MemoryStream? bytes = null;
        CancellationToken token = request.Cancellation.Token;
        try
        {
            token.ThrowIfCancellationRequested();
            DateTime fetchedAt = DateTime.UtcNow;
            try
            {
                var cacheFile = new FileInfo(cachePath);
                if (cacheFile.Exists && cacheFile.Length <= MaxEncodedBytes
                    && cacheFile.LastWriteTimeUtc >= DateTime.UtcNow - MaxCacheAge)
                {
                    bytes = await ReadLocalBoundedAsync(cachePath, token);
                    image = await DecodeAsync(bytes, token);
                    token.ThrowIfCancellationRequested();
                    fetchedAt = cacheFile.LastWriteTimeUtc;
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Could not read cached icon: {ex.Message}");
            }

            if (image is null)
            {
                bytes?.Dispose();
                bytes = await loadSource(token);
                if (bytes.Length > MaxEncodedBytes)
                    throw new InvalidDataException("Icon image exceeds the 8 MiB limit.");
                image = await DecodeAsync(bytes, token);
                await TryCacheAsync(cachePath, bytes, token);
                fetchedAt = DateTime.UtcNow;
            }

            lock (sync)
            {
                if (!request.Abandoned && !token.IsCancellationRequested)
                {
                    var node = decodedOrder.AddFirst((key, image, fetchedAt));
                    decoded.Add(key, node);
                    if (decoded.Count > MaxDecodedEntries)
                    {
                        var oldest = decodedOrder.Last!;
                        decoded.Remove(oldest.Value.Key);
                        decodedOrder.RemoveLast();
                    }
                }
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            image = null;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Could not load icon: {ex.Message}");
        }
        finally
        {
            request.Cancellation.Dispose();
            bytes?.Dispose();
            lock (sync)
            {
                if (pending.TryGetValue(key, out var current) && ReferenceEquals(current, request))
                    pending.Remove(key);
                activeLoads.Remove(request);
                request.Completion.SetResult(request.Abandoned ? null : image);
            }
        }
    }

    private static async Task<MemoryStream> DownloadBoundedAsync(Uri uri, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        using var response = await HttpClient.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength is > MaxEncodedBytes)
            throw new InvalidDataException("Icon image exceeds the 8 MiB limit.");
        await using var source = await response.Content.ReadAsStreamAsync(timeout.Token);
        return await ReadBoundedAsync(source, timeout.Token, response.Content.Headers.ContentLength);
    }

    private static async Task<MemoryStream> ReadLocalBoundedAsync(string path, CancellationToken cancellationToken)
    {
        await using var source = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite,
            bufferSize: 64 * 1024, useAsync: true);
        return await ReadBoundedAsync(source, cancellationToken);
    }

    private static async Task<MemoryStream> ReadBoundedAsync(Stream source,
        CancellationToken cancellationToken = default, long? expectedLength = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        expectedLength ??= source.CanSeek ? source.Length - source.Position : null;
        if (expectedLength is > MaxEncodedBytes)
            throw new InvalidDataException("Icon image exceeds the 8 MiB limit.");
        var destination = new MemoryStream((int)Math.Max(0, expectedLength ?? 0));
        byte[] buffer = ArrayPool<byte>.Shared.Rent(64 * 1024);
        try
        {
            int read;
            while ((read = await source.ReadAsync(buffer.AsMemory(0, 64 * 1024), cancellationToken)) != 0)
            {
                if (destination.Length + read > MaxEncodedBytes)
                    throw new InvalidDataException("Icon image exceeds the 8 MiB limit.");
                int requiredCapacity = (int)destination.Length + read;
                if (requiredCapacity > destination.Capacity)
                    destination.Capacity = Math.Min(MaxEncodedBytes,
                        Math.Max(requiredCapacity, destination.Capacity * 2));
                destination.Write(buffer, 0, read);
            }
            destination.Position = 0;
            return destination;
        }
        catch
        {
            destination.Dispose();
            throw;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static async Task<BitmapImage> DecodeAsync(MemoryStream bytes, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // WinRT owns this view; the payload stays alive for the subsequent cache write.
        using var stream = new MemoryStream(bytes.GetBuffer(), 0, (int)bytes.Length, writable: false);
        using var randomAccessStream = stream.AsRandomAccessStream();
        var decoder = await BitmapDecoder.CreateAsync(randomAccessStream);
        cancellationToken.ThrowIfCancellationRequested();
        double scale = Math.Min(1d, (double)IconSize / Math.Max(decoder.PixelWidth, decoder.PixelHeight));
        int width = Math.Max(1, (int)Math.Round(decoder.PixelWidth * scale));
        int height = Math.Max(1, (int)Math.Round(decoder.PixelHeight * scale));
        randomAccessStream.Seek(0);
        var bitmap = new BitmapImage { DecodePixelWidth = width, DecodePixelHeight = height };
        await bitmap.SetSourceAsync(randomAccessStream);
        cancellationToken.ThrowIfCancellationRequested();
        return bitmap;
    }

    private async Task TryCacheAsync(string cachePath, MemoryStream bytes, CancellationToken cancellationToken)
    {
        string temporaryPath = cachePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            Directory.CreateDirectory(cacheDirectory);
            await using (var destination = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write,
                FileShare.None, bufferSize: 1, useAsync: true))
            {
                await destination.WriteAsync(bytes.GetBuffer().AsMemory(0, (int)bytes.Length), cancellationToken);
            }
            cancellationToken.ThrowIfCancellationRequested();
            // A unique temporary file and atomic replacement keep concurrent readers safe.
            File.Move(temporaryPath, cachePath, overwrite: true);
            RequestDiskMaintenance();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Could not cache icon: {ex.Message}");
        }
        finally
        {
            try
            {
                File.Delete(temporaryPath);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Could not remove temporary icon: {ex.Message}");
            }
        }
    }

    private void RequestDiskMaintenance()
    {
        lock (sync)
        {
            diskMaintenanceRequested = true;
            if (diskMaintenanceRunning) return;
            diskMaintenanceRunning = true;
            diskMaintenanceTask = Task.Run(MaintainDiskCacheAsync);
        }
    }

    /// <summary>Drains active icon loads and their disk maintenance after callers stop starting new loads.</summary>
    public async Task FlushCacheMaintenanceAsync()
    {
        while (true)
        {
            Task[] work;
            lock (sync)
            {
                if (activeLoads.Count == 0 && diskMaintenanceTask.IsCompleted) return;
                work = activeLoads.Select(load => (Task)load.Completion.Task).Append(diskMaintenanceTask).ToArray();
            }
            await Task.WhenAll(work).ConfigureAwait(false);
        }
    }

    /// <summary>Permanently stops new icon loads and drains existing cache work.</summary>
    public async Task StopAsync()
    {
        PendingIconLoad[] loads;
        lock (sync)
        {
            stopping = true;
            loads = activeLoads.ToArray();
            foreach (var load in loads) load.Abandoned = true;
            pending.Clear();
        }
        foreach (var load in loads)
        {
            try { load.Cancellation.Cancel(); }
            catch (ObjectDisposedException) { }
        }
        await FlushCacheMaintenanceAsync().ConfigureAwait(false);
    }

    private async Task MaintainDiskCacheAsync()
    {
        while (true)
        {
            // One scan covers a burst of icon writes; no image waits for maintenance.
            await Task.Delay(100).ConfigureAwait(false);
            lock (sync) diskMaintenanceRequested = false;
            TrimDiskCache();
            lock (sync)
            {
                if (diskMaintenanceRequested) continue;
                diskMaintenanceRunning = false;
                return;
            }
        }
    }

    private void TrimDiskCache()
    {
        try
        {
            var files = new List<FileInfo>();
            long totalBytes = 0;
            DateTime staleEntry = DateTime.UtcNow - MaxCacheAge;
            DateTime staleTemporary = DateTime.UtcNow - TimeSpan.FromHours(1);
            foreach (string path in Directory.EnumerateFiles(cacheDirectory))
            {
                var file = new FileInfo(path);
                string[] parts = file.Name.Split('.');
                if (parts.Length == 4 && IsHash(parts[0]) && parts[1].Length is > 0 and <= 12
                    && IsHex(parts[2], 32) && parts[3] == "tmp")
                {
                    if (file.LastWriteTimeUtc < staleTemporary) file.Delete();
                    else totalBytes += file.Length;
                    continue;
                }
                if (parts.Length is < 1 or > 2 || !IsHash(parts[0])
                    || (parts.Length == 2 && (parts[1].Length is < 1 or > 12)))
                    continue;
                if (file.LastWriteTimeUtc < staleEntry || file.Length > MaxEncodedBytes)
                {
                    file.Delete();
                    continue;
                }
                files.Add(file);
                totalBytes += file.Length;
            }
            if (totalBytes <= MaxDiskCacheBytes) return;
            files.Sort((left, right) => left.LastWriteTimeUtc.CompareTo(right.LastWriteTimeUtc));
            foreach (var file in files)
            {
                if (totalBytes <= MaxDiskCacheBytes) break;
                long length = file.Length;
                file.Delete();
                totalBytes -= length;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Could not trim icon cache: {ex.Message}");
        }
    }

    private static bool IsHash(string value) => IsHex(value, 64);

    private static bool IsHex(string value, int length) =>
        value.Length == length && value.All(Uri.IsHexDigit);
    #endregion
}
