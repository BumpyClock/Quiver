# Browsers

Manage browsers in **Quiver Settings > Browsers**, or edit the top-level `Browsers` array in [UserSettings.json](../README.md).
The settings UI supports creating, editing, deleting, and dragging browser entries to reorder them.

- `Id` - Stable UUID for this browser. Various other settings refer to this browser by this Id. Required.
- `Name` - Display name for browser. Required.
- `ExePath` - Path to the browser's main executable. Required.
- `Icon` - Optional icon configuration containing `Source`, `Path`, and `Index`. Omit it or use `null` to use the executable's default icon. See below.
- `LaunchArgs` - Default executable launch arguments. Use `%URL%` to insert the URL at a specific position. If `%URL%` is absent, the URL is placed before the arguments. Optional.
- `Hidden` - Set to **true** to hide the browser from the selector and Quick View's browser targets. Rules can still launch it. Defaults to **false**.
- `AlternateLaunches` - This is an array; See below. Optional.

## Browser icons

Click the icon preview beside **Name** and **Executable Path** to open **Select Browser Icon**:

- **Exe Icons** shows the icons embedded in the executable, labeled with their zero-based index. Selecting `icon 0` restores the default.
- **Next icons** and **Previous icons** browse executable icons 16 at a time. The selected icon stays selected when you change pages.
- **Local Image** opens a local file picker for ICO, PNG, JPEG, BMP, GIF, or TIFF images. The source file should be available all times.
- **From URL** loads a direct HTTP(S) image URL. Press Enter or the arrow button to preview it.

Quiver falls back to the executable's default icon if the override cannot be loaded. Image files and URL responses must be at most 8 MiB. Icons are decoded to fit within 256 × 256 pixels while keeping their proportions.

### Caching

Images are cached in `%LOCALAPPDATA%\Packages\<PackageFamilyName>\LocalState\cache\icons`. Background maintenance removes entries older than 30 days and trims the disk cache to 64 MiB after new writes; a burst of writes can temporarily exceed that budget. The icon service keeps up to 64 decoded images in memory. Browser settings load icons for realized rows and release row references when they are recycled.

The cache updates when a local source file or icon configuration changes. URL icons refresh after their cached entry expires.

### Or in the UserSettings.json

| Source      | Example                                                                |
| ----------- | ---------------------------------------------------------------------- |
| Executable  | `"Icon": { "Source": "Executable", "Index": 2 }`                       |
| Local image | `"Icon": { "Source": "LocalImage", "Path": "C:\\Icons\\browser.png" }` |
| URL         | `"Icon": { "Source": "Url", "Path": "https://example.com/icon.png" }`  |

## Launch Profiles (AlternateLaunches)

This is a way to launch the browser when you have multiple launch methods or launch targets, like incognito, browser profiles...

Suppose you have multiple chrome profiles like this:

![Example of Chrome profile in .lnk shortcut](../Images/ChromeProfiles.png)

Then you might want to use this feature, instead of totally adding a new browser entity for each profile in
the settings file. The following snippet demonstrates this feature.
Adding the `AlternateLaunches` field to the browser entry lets you right-click its icon in the selector to
choose an alternate launch. The icon's tooltip lists them. Add this property to the browser object:

```json
{
  "AlternateLaunches": [
    {
      "Id": "f81a1698-1488-461d-9294-4f4f8313178e",
      "ItemName": "Main Profile",
      "LaunchArgs": "--profile-directory=\"Default\""
    },
    {
      "Id": "e9e5df5a-1bc6-4bce-8f8d-e66950daa495",
      "ItemName": "Profile 2",
      "LaunchArgs": "--profile-directory=\"Profile 1\""
    },
    {
      "Id": "4bd80686-0a0b-4668-8ed0-063989e99c14",
      "ItemName": "Incognito",
      "LaunchArgs": "-incognito"
    }
  ]
}
```

Selecting an alternate launch includes the URL automatically. Its arguments replace the browser's default `LaunchArgs`; they are not combined.

![Alternate-launch menu in an older Quiver version](../Images/BrowserProfiles.png)

- `ItemName` - The name that shows up in the context menu for this launch
- `LaunchArgs` - Arguments for this alternate launch, such as an incognito flag or profile directory. Supports `%URL%` with the same behavior as the browser's default arguments.
- `Id` - Stable UUID for this alternate launch. Quiver generates one when absent. Preserve it when rulesets or Quick View target this profile.

## Refreshing Browsers list

Select **Refresh** on the **Browsers** page in Quiver Settings to choose between two modes:

- **Preserve existing** compares detected browsers by `ExePath` and appends only those without an existing
  match. Existing settings and IDs are kept.
- **Add all detected** appends every detected browser as a new entry with a new ID, irrespective of whether an
  entry with the same executable path already exists.

### Limitations

- Refresh does not hide or remove uninstalled browsers, update existing entries, or repair rules and Quick View targets that reference deleted entries.
