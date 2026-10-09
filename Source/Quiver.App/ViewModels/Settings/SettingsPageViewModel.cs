using CommunityToolkit.Mvvm.ComponentModel;
using Quiver.Library.Models;
using Quiver.App.Services.Interfaces;


namespace Quiver.App.ViewModels;

public partial class SettingsPageViewModel : ObservableObject
{
    [ObservableProperty]
    public partial AppSettings AppSettings { get; set; }

    private readonly ISettingsService _settingsService;

    public SettingsPageViewModel(ISettingsService settingsService)
    {
        AppSettings = settingsService.LoadSettings().AppSettings;
        _settingsService = settingsService;
    }

    public bool Option_MinimizeOnFocusLoss
    {
        get => AppSettings.MinimizeOnFocusLoss;
        set
        {
            if (AppSettings.MinimizeOnFocusLoss != value)
            {
                AppSettings.MinimizeOnFocusLoss = value;
                _settingsService.UpdateAppSettings(AppSettings);

                OnPropertyChanged();
            }
        }
    }
}
