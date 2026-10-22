using CommunityToolkit.Mvvm.ComponentModel;
using Quiver.Library;
using Quiver.Library.Models;
using Microsoft.UI.Xaml.Media.Imaging;
using System;
using System.Collections.ObjectModel;
using System.Globalization;
using WinRT;

namespace Quiver.App.ViewModels;

[GeneratedBindableCustomProperty]
public partial class BrowserItemViewModel(Browser model, BrowserProfile? profile = null) : ObservableObject
{
    public Browser Model { get; } = model;

    /// <summary>The detected browser profile this item launches, or null for the browser's default launch.</summary>
    public BrowserProfile? Profile { get; } = profile;

    internal (Guid BrowserId, string? ProfileDirectory) Key => (Model.Id, Profile?.Directory);

    public string Name => Profile is null ? Model.Name : $"{Model.Name} ({Profile.Name})";

    public string ProfileInitial => Profile is null || Profile.Name.Length == 0
        ? string.Empty
        : StringInfo.GetNextTextElement(Profile.Name).ToUpper(CultureInfo.CurrentCulture);

    public ObservableCollection<AlternateLaunch>? AlternateLaunches => Model.AlternateLaunches;

    [ObservableProperty]
    public partial BitmapImage? Icon { get; set; }

    [ObservableProperty]
    public partial BitmapImage? ProfileAvatar { get; set; }

    /// <summary>The launch that opens this item's profile, combined with the browser's default arguments.</summary>
    public AlternateLaunch? ProfileLaunch => Profile is null
        ? null
        : new AlternateLaunch(Profile.Name, string.IsNullOrWhiteSpace(Model.LaunchArgs)
            ? Profile.LaunchArgs
            : $"{Model.LaunchArgs} {Profile.LaunchArgs}");
}
