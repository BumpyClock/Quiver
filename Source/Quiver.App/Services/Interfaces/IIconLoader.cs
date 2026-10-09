using Quiver.Library.Models;
using Microsoft.UI.Xaml.Media.Imaging;
using System.Threading;
using System.Threading.Tasks;

namespace Quiver.App.Services.Interfaces;

public interface IIconLoader
{
    Task StopAsync();
    Task FlushCacheMaintenanceAsync();
    Task<BitmapImage?> LoadIconAsync(Browser browser, CancellationToken cancellationToken = default);
    Task<BitmapImage?> LoadIconFromExe(string exePath, CancellationToken cancellationToken = default);
    Task<BitmapImage?> LoadIconFromExe(string exePath, int iconIndex, CancellationToken cancellationToken = default);
    Task<int> GetExeIconCountAsync(string exePath, CancellationToken cancellationToken = default);
    Task<BitmapImage?> LoadIconFromIco(string icoPath, CancellationToken cancellationToken = default);
    Task<BitmapImage?> LoadIconFromImage(string imagePath, CancellationToken cancellationToken = default);
    Task<BitmapImage?> LoadIconFromURL(string url, CancellationToken cancellationToken = default);
}
