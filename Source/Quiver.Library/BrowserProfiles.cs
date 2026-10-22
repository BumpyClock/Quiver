using System.Text.Json;

namespace Quiver.Library;

public sealed record BrowserProfile(string Directory, string Name, string? AvatarPath)
{
    public string LaunchArgs => $"--profile-directory=\"{Directory}\"";
}

public static class BrowserProfiles
{
    // Chromium browsers whose profiles live in %LOCALAPPDATA%\<Vendor>\<Channel>\User Data,
    // next to an install at ...\<Vendor>\<Channel>\Application\<exe>.
    private static readonly HashSet<string> SupportedExecutables = new(StringComparer.OrdinalIgnoreCase)
    {
        "msedge.exe",
    };

    /// <summary>
    /// Returns the browser's profiles in the browser's own order, or an empty list when it is not a
    /// supported browser or its profile list cannot be read.
    /// </summary>
    public static IReadOnlyList<BrowserProfile> Detect(string exePath)
    {
        try
        {
            return UserDataDirectory(exePath) is string userData ? Read(userData) : [];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return [];
        }
    }

    private static string? UserDataDirectory(string exePath)
    {
        if (!SupportedExecutables.Contains(Path.GetFileName(exePath)))
        {
            return null;
        }

        var application = Directory.GetParent(exePath);
        var channel = application?.Parent;
        var vendor = channel?.Parent;
        if (application is null || channel is null || vendor is null
            || !application.Name.Equals("Application", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            vendor.Name, channel.Name, "User Data");
    }

    private static List<BrowserProfile> Read(string userData)
    {
        string localState = Path.Combine(userData, "Local State");
        if (!File.Exists(localState))
        {
            return [];
        }

        using var stream = new FileStream(localState, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var document = JsonDocument.Parse(stream);
        if (!document.RootElement.TryGetProperty("profile", out var profile)
            || !profile.TryGetProperty("info_cache", out var infoCache)
            || infoCache.ValueKind != JsonValueKind.Object)
        {
            return [];
        }

        var order = new List<string>();
        if (profile.TryGetProperty("profiles_order", out var profilesOrder) && profilesOrder.ValueKind == JsonValueKind.Array)
        {
            order.AddRange(profilesOrder.EnumerateArray()
                .Where(entry => entry.ValueKind == JsonValueKind.String)
                .Select(entry => entry.GetString()!));
        }

        order.AddRange(infoCache.EnumerateObject().Select(entry => entry.Name).Where(name => !order.Contains(name)));

        var profiles = new List<BrowserProfile>();
        foreach (string directory in order)
        {
            if (!infoCache.TryGetProperty(directory, out var info)
                || info.ValueKind != JsonValueKind.Object
                || GetBool(info, "is_ephemeral")
                || directory.IndexOfAny(['"', '\\', '/']) >= 0)
            {
                continue;
            }

            string? picture = GetString(info, "gaia_picture_file_name");
            string? avatarPath = string.IsNullOrEmpty(picture) ? null : Path.Combine(userData, directory, picture);
            profiles.Add(new BrowserProfile(directory, DisplayName(info, directory), avatarPath));
        }

        return profiles;
    }

    // Edge keeps "Profile 1" in `name` and shows `shortcut_name` ("Work", "Personal") until the user renames it.
    private static string DisplayName(JsonElement info, string directory)
    {
        string? shortcutName = GetString(info, "shortcut_name");
        if (GetBool(info, "is_using_default_name") && !string.IsNullOrWhiteSpace(shortcutName))
        {
            return shortcutName;
        }

        return new[] { GetString(info, "name"), shortcutName, GetString(info, "user_name") }
            .FirstOrDefault(name => !string.IsNullOrWhiteSpace(name)) ?? directory;
    }

    private static string? GetString(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static bool GetBool(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.True;
}
