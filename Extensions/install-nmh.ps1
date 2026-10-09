param(
  [Parameter(Mandatory = $true)]
  [string]$ExtensionId,

  # Path to NativeMessagingHost.exe. Defaults to the alias the Store package installs.
  [string]$HostPath = (Join-Path $env:LOCALAPPDATA "Microsoft\WindowsApps\QuiverNativeMessagingHost.exe")
)

if (-not (Test-Path $HostPath)) {
  throw "Native messaging host not found at $HostPath. Install Quiver from the Microsoft Store, or pass -HostPath."
}

# The package install folder is read-only, so keep the manifest in a user folder.
$ManifestDir = Join-Path $env:LOCALAPPDATA "Quiver"
$ManifestPath = Join-Path $ManifestDir "nmh-manifest.json"
New-Item -ItemType Directory -Force $ManifestDir | Out-Null
Write-Output "Creating Native Messaging Host manifest file at $ManifestPath"

$NMH_MANIFEST = [ordered]@{
  name            = "com.bumpyclock.quiver"
  description     = "Quiver Proxy Native Messaging Host"
  path            = $HostPath
  type            = "stdio"
  allowed_origins = @("chrome-extension://$ExtensionId/")
} | ConvertTo-Json

$NMH_MANIFEST | Out-File -FilePath $ManifestPath -Encoding utf8

$REG_PATH = "HKCU\Software\Google\Chrome\NativeMessagingHosts"
REG ADD "$REG_PATH\com.bumpyclock.quiver" /ve /t REG_SZ /d "$ManifestPath" /f
