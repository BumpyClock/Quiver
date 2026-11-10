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

    public int Option_SelectorLayout
    {
        get => AppSettings.SelectorLayout == SelectorLayout.List ? 1 : 0;
        set
        {
            if (value is not (0 or 1)) return;
            var layout = (SelectorLayout)value;
            if (AppSettings.SelectorLayout == layout)
            {
                return;
            }

            AppSettings.SelectorLayout = layout;
            _settingsService.UpdateAppSettings(AppSettings);
            OnPropertyChanged();
        }
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
