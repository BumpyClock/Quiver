using System.Runtime.InteropServices;

namespace Quiver.Library;

public static partial class PackageIdentity
{
    private const int APPMODEL_ERROR_NO_PACKAGE = 15700;

    public static bool IsPackaged { get; } = HasPackageIdentity();

    private static bool HasPackageIdentity()
    {
        uint length = 0;
        return GetCurrentPackageFullName(ref length, IntPtr.Zero) != APPMODEL_ERROR_NO_PACKAGE;
    }

    [LibraryImport("kernel32.dll")]
    private static partial int GetCurrentPackageFullName(ref uint packageFullNameLength, IntPtr packageFullName);
}
