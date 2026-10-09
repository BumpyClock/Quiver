# Quick View

Quick View is enabled by default. Hold **Alt** while opening an HTTP or HTTPS link through Quiver to open it in
the built-in Edge WebView2 window. You can also use the **Quick View** button beside the URL text box in the
selector. A successful Quick View shortcut takes precedence over rule matching.

Configure it in **Quiver Settings > Quick View**, or add the following top-level property to `UserSettings.json`:

```json
{
  "QuickView": {
    "Enabled": true,
    "LaunchMode": "WebView",
    "ModifierKeys": "Alt",
    "BrowserId": null,
    "AlternateLaunchId": null,

    "AdditionalBrowserArguments": "",
    "BrowserExtensionsEnabled": false,
    "TrackingPrevention": "Balanced"
  }
}
```

- `Enabled` controls both the shortcut and the **Quick View** item in the selector's center menu.
- `LaunchMode` is `WebView` for the built-in preview or `Browser` to launch a configured browser directly. The selector's **Quick View** item uses this setting too.
- `ModifierKeys` supports `Alt`, `CtrlAlt`, or `Ctrl`.
- `BrowserId` selects a browser by its `Id` when `LaunchMode` is `Browser`. 
- `AlternateLaunchId` selects one of that browser's alternate launches, `null` uses its default launch.

### WebView2 Configuration
- Additional browser arguments are saved when you leave the text field, navigate to another settings page,
  close the settings window, exit, or restart Quiver. Other Quick View options queue a save when changed.
  Settings writes run in the background and finish before normal close, exit, or restart. If a save fails,
  Quiver keeps the settings window or app open so you can retry; forced termination can interrupt a queued save.
- `AdditionalBrowserArguments` and `BrowserExtensionsEnabled` configure the WebView2 environment.
  Restart Quiver after changing them if you have already opened a Quick View window.
- `TrackingPrevention` supports `None`, `Basic`, `Balanced` (default), or `Strict` for the WebView2 profile.

Closing a Quick View window closes its WebView2 control and stops pending navigation for that window.
The shared WebView2 environment remains available for other Quick View windows.
