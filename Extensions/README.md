# Quiver browser extension

This browser extension uses Native Messaging API to communicate with the Quiver Application.
The native host accepts messages up to 1 MiB. Larger messages are rejected before the body is read.

## Installation

To use it in Chrome, follow the steps below:

- Enable the Developer Mode for Extensions in Chrome ([instructions on Chromium blog](https://blog.chromium.org/2009/06/developer-tools-for-google-chrome.html))
- Select **Load Unpacked**
- Choose the folder `Extensions/Chrome` from a checkout of this repository

Then run `install-nmh.ps1` with the extension id from the extension page in Chrome. The Store package installs the native messaging host as the `QuiverNativeMessagingHost.exe` app execution alias, and the script points Chrome at it by default.

```powershell
.\Extensions\install-nmh.ps1 -ExtensionId {EXTENSION_ID}
```

The script writes `nmh-manifest.json` to `%LOCALAPPDATA%\Quiver` and registers it under `HKCU`; it does not need admin rights.

You can modify the script to use it for other chromium-based browsers by editing the REG command in it and pointing to appropriate browser's registry key for Native Messaging Hosts.

During **Development**, deploy the package from Visual Studio (the `Quiver.App (Package)` profile) and run the script without `-HostPath`. The host starts the _Quiver.exe_ in its own folder, so a host run from `target/` cannot open links.
