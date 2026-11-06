using Quiver.App.Services;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Quiver.UiSmoke;

internal static class IconLoaderStateChecks
{
    private static readonly TimeSpan WaitLimit = TimeSpan.FromSeconds(5);

    public static async Task CheckAsync(string fixtureDirectory, string pngPath)
    {
        byte[] png = await File.ReadAllBytesAsync(pngPath);
        string cacheDirectory = Path.Combine(fixtureDirectory, Guid.NewGuid().ToString("N"));
        var loader = new IconLoaderService(cacheDirectory);
        var server = new TcpListener(IPAddress.Loopback, 0);
        server.Start();
        try
        {
            string baseUrl = $"http://127.0.0.1:{((IPEndPoint)server.LocalEndpoint).Port}/";

            string sharedUrl = baseUrl + "shared.png";
            using (var leaving = new CancellationTokenSource())
            {
                var connection = server.AcceptTcpClientAsync();
                var leavingWaiter = loader.LoadIconFromURL(sharedUrl, leaving.Token);
                var stayingWaiter = loader.LoadIconFromURL(sharedUrl);
                using var peer = await connection.WaitAsync(WaitLimit);
                await ReadRequestAsync(peer);
                leaving.Cancel();
                Check(await IsCanceledAsync(leavingWaiter), "leaving icon waiter is canceled");
                await RespondAsync(peer, png);
                var image = await stayingWaiter.WaitAsync(WaitLimit);
                Check(image is not null, "remaining waiter keeps the shared icon load alive");
                var cached = loader.LoadIconFromURL(sharedUrl);
                Check(cached.IsCompletedSuccessfully && ReferenceEquals(cached.Result, image),
                    "shared icon result is returned synchronously from the decoded cache");
            }

            string abandonedUrl = baseUrl + "abandoned.png";
            using (var first = new CancellationTokenSource())
            using (var second = new CancellationTokenSource())
            {
                var connection = server.AcceptTcpClientAsync();
                var firstWaiter = loader.LoadIconFromURL(abandonedUrl, first.Token);
                var secondWaiter = loader.LoadIconFromURL(abandonedUrl, second.Token);
                using var peer = await connection.WaitAsync(WaitLimit);
                await ReadRequestAsync(peer);
                first.Cancel();
                Check(await IsCanceledAsync(firstWaiter), "first icon waiter is canceled");
                second.Cancel();
                Check(await IsCanceledAsync(secondWaiter), "last icon waiter is canceled");
                Check(await IsClosedAsync(peer), "last waiter leaving cancels the shared icon request");

                var retryConnection = server.AcceptTcpClientAsync();
                var retry = loader.LoadIconFromURL(abandonedUrl);
                Check(!retry.IsCompleted, "abandoned icon load is not cached");
                using var retryPeer = await retryConnection.WaitAsync(WaitLimit);
                await ReadRequestAsync(retryPeer);
                await RespondAsync(retryPeer, png);
                Check(await retry.WaitAsync(WaitLimit) is not null, "icon load after abandonment starts a fresh request");
            }

            string shell32 = Path.Combine(Environment.SystemDirectory, "shell32.dll");
            Check(await loader.GetExeIconCountAsync(shell32) > 0, "executable icons counted before shutdown");
            string stalledUrl = baseUrl + "stalled.png";
            {
                var connection = server.AcceptTcpClientAsync();
                var firstWaiter = loader.LoadIconFromURL(stalledUrl);
                var secondWaiter = loader.LoadIconFromURL(stalledUrl);
                using var peer = await connection.WaitAsync(WaitLimit);
                await loader.StopAsync().WaitAsync(WaitLimit);
                Check(await firstWaiter.WaitAsync(WaitLimit) is null && await secondWaiter.WaitAsync(WaitLimit) is null,
                    "shutdown resolves every waiter of an active icon load with no icon");
                Check(loader.FlushCacheMaintenanceAsync().IsCompleted,
                    "shutdown leaves no icon loads or cache maintenance to drain");
            }
            Check(await loader.GetExeIconCountAsync(shell32) == 0, "shutdown blocks executable icon counting");
        }
        finally
        {
            server.Stop();
            await loader.StopAsync();
            if (Directory.Exists(cacheDirectory)) Directory.Delete(cacheDirectory, recursive: true);
        }
    }

    private static async Task ReadRequestAsync(TcpClient peer)
    {
        var stream = peer.GetStream();
        var request = new StringBuilder();
        var buffer = new byte[4096];
        while (!request.ToString().Contains("\r\n\r\n"))
        {
            int read = await stream.ReadAsync(buffer).AsTask().WaitAsync(WaitLimit);
            if (read == 0) throw new IOException("Icon request closed before its headers were sent.");
            request.Append(Encoding.ASCII.GetString(buffer, 0, read));
        }
    }

    private static async Task RespondAsync(TcpClient peer, byte[] body)
    {
        var stream = peer.GetStream();
        byte[] headers = Encoding.ASCII.GetBytes(
            $"HTTP/1.1 200 OK\r\nContent-Type: image/png\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n");
        await stream.WriteAsync(headers);
        await stream.WriteAsync(body);
        await stream.FlushAsync();
    }

    private static async Task<bool> IsCanceledAsync(Task task)
    {
        try { await task.WaitAsync(WaitLimit); }
        catch (OperationCanceledException) { return true; }
        return false;
    }

    private static async Task<bool> IsClosedAsync(TcpClient peer)
    {
        try { return await peer.GetStream().ReadAsync(new byte[1]).AsTask().WaitAsync(WaitLimit) == 0; }
        catch (IOException) { return true; }
        catch (TimeoutException) { return false; }
    }

    private static void Check(bool condition, string name)
    {
        if (!condition) throw new Exception($"Icon loader state check failed: {name}");
    }
}
