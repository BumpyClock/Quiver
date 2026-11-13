using System;
using System.IO;

namespace Quiver.App.Helpers;

internal readonly record struct SelectorAddress(string Title, string Detail, bool IsSecure)
{
    public static SelectorAddress For(string? url, bool shortenHttp)
    {
        string full = url ?? string.Empty;
        if (!Uri.TryCreate(full, UriKind.Absolute, out var uri)) return new(full, full, false);

        bool shortened = shortenHttp && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
        string title = uri.IsFile ? Path.GetFileName(uri.LocalPath)
            : string.IsNullOrEmpty(uri.Host) ? full
            : uri.Host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? uri.Host[4..] : uri.Host;
        if (shortened && !uri.IsDefaultPort) title += ":" + uri.Port;
        return new(title, shortened ? uri.PathAndQuery + uri.Fragment : full, uri.Scheme == Uri.UriSchemeHttps);
    }
}
