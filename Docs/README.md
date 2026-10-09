# User settings

## Feature Documentation

- [Browser Configuration](./Features/BrowserConfiguration.md)
- [Rule Matching](./Features/RuleMatching.md)
- [Quick View](./Features/QuickView.md)

Developer verification: [Performance and memory changes](./Dev/Performance.md).

When Quiver is launched for the first time, it automatically detects the installed browsers and creates a _UserSettings.json_ file in the package's LocalState folder (`%LOCALAPPDATA%\Packages\<PackageFamilyName>\LocalState\UserSettings.json`), filling it with browsers it detected. The easiest way to open it is **Quiver Settings** > **Settings** page > **edit JSON** button. A typical UserSettings.json file looks like this:

```json
{
  "AppSettings": {
    "MinimizeOnFocusLoss": true,
    "RuleMatching": false
  },
  "Browsers": [
    {
      "Id": "2b36a1fe-97f7-4509-ae6e-5c2c61602af4",
      "Name": "Brave",
      "ExePath": "C:\\Program Files\\BraveSoftware\\Brave-Browser\\Application\\brave.exe",
      "Hidden": false
    },
    {
      "Id": "e48b823f-c4b0-4218-a7e2-a8c80231228a",
      "Name": "Google Chrome Dev",
      "ExePath": "C:\\Program Files\\Google\\Chrome Dev\\Application\\chrome.exe",
      "AlternateLaunches": [
        {
          "Id": "f81a1698-1488-461d-9294-4f4f8313178e",
          "ItemName": "Profile 1",
          "LaunchArgs": "--profile-directory=\"Default\""
        },
        {
          "Id": "4bd80686-0a0b-4668-8ed0-063989e99c14",
          "ItemName": "Incognito",
          "LaunchArgs": "-incognito"
        }
      ]
    }
  ]
}
```

## App settings

The following snippet shows the default options:

```json
"AppSettings": {
    "MinimizeOnFocusLoss": true,
    "RuleMatching": false
}
```

### Available options

- `MinimizeOnFocusLoss` defaults to **true** and hides the picker when you click outside it.
- `RuleMatching` defaults to **false**. Enable it in **Quiver Settings > Rulesets** to automatically open links using [rule matching](./Features/RuleMatching.md).

## Using the selector

The selector opens centered on the mouse cursor. Each browser is an acrylic petal around the link in the middle, so every browser is the same short move away. Two browsers sit side by side above the link; three or more are spread evenly around it, starting at the top.

- Click a petal, or press its number (**1**-**9**), to open the link in that browser. Hover a petal to see the browser's name.
- Right-click a petal to pick one of its [alternate launches](./Features/BrowserConfiguration.md).
- Use the arrow keys to move between petals and **Enter** to open.
- Click the link in the middle to copy it (**C**), edit it (**Alt+E**), open it in [Quick View](./Features/QuickView.md), or open **Rules** (**R**) and **Settings**.
- Press **Esc** or click outside the selector to close it.
