using Microsoft.Web.WebView2.Core;
using Quiver.App.Services;

namespace Quiver.UiSmoke;

internal static class QuickViewStartupChecks
{
    public static async Task CheckEnvironmentRetryAsync()
    {
        var creations = new List<TaskCompletionSource<CoreWebView2Environment>>();
        var service = new WebViewEnvironmentService(() =>
        {
            var creation = new TaskCompletionSource<CoreWebView2Environment>();
            creations.Add(creation);
            return creation.Task;
        });

        Task<CoreWebView2Environment> pending = service.GetEnvironmentAsync();
        Check(ReferenceEquals(pending, service.GetEnvironmentAsync()) && creations.Count == 1,
            "windows opened during environment creation share one creation");

        creations[0].SetException(new InvalidOperationException("WebView2 runtime missing"));
        Check(await FailsAsync(pending), "faulted environment creation reaches the waiting window");
        Task<CoreWebView2Environment> afterFault = service.GetEnvironmentAsync();
        Check(creations.Count == 2 && !ReferenceEquals(pending, afterFault),
            "faulted environment creation is retried by the next window");

        creations[1].SetCanceled();
        Check(await FailsAsync(afterFault), "canceled environment creation reaches the waiting window");
        Task<CoreWebView2Environment> afterCancel = service.GetEnvironmentAsync();
        Check(creations.Count == 3 && !ReferenceEquals(afterFault, afterCancel),
            "canceled environment creation is retried by the next window");

        // The service only tracks task outcome, so a placeholder result stands in for a real environment.
        creations[2].SetResult(null!);
        await afterCancel;
        Check(ReferenceEquals(afterCancel, service.GetEnvironmentAsync()) && creations.Count == 3,
            "created environment is shared by later windows");
    }

    private static async Task<bool> FailsAsync(Task task)
    {
        try
        {
            await task;
            return false;
        }
        catch (Exception)
        {
            return true;
        }
    }

    private static void Check(bool condition, string name)
    {
        if (!condition) throw new Exception($"Quick View startup check failed: {name}");
    }
}
