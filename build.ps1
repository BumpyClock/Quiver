param(
  [ValidateSet("x64", "arm64")]
  [string[]]$Platforms = @("x64", "arm64"),
  [string]$OutputPath = "./_Publish"
)

$ErrorActionPreference = "Stop"

$RustTargets = @{ x64 = "x86_64-pc-windows-msvc"; arm64 = "aarch64-pc-windows-msvc" }
$OutputPath = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($OutputPath)
$PackagesPath = Join-Path $OutputPath "packages"
Remove-Item -Recurse -Force $OutputPath -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force $PackagesPath | Out-Null

function Invoke-Native([scriptblock]$Command) {
  & $Command
  if ($LASTEXITCODE -ne 0) { throw "Command failed with exit code ${LASTEXITCODE}: $Command" }
}

foreach ($platform in $Platforms) {
  $rustTarget = $RustTargets[$platform]
  Write-Output "Building NativeMessagingHost ($rustTarget)...."
  Invoke-Native { rustup target add $rustTarget }
  Invoke-Native { cargo build --release --workspace --target $rustTarget }

  Write-Output "Building Quiver MSIX ($platform)...."
  Invoke-Native {
    # GenerateAppxPackageOnBuild is what makes the MSIX tooling package the NativeAOT publish output.
    dotnet build .\Source\Quiver.App\Quiver.App.csproj -c Release -r "win-$platform" -p:Platform=$platform `
      -p:GenerateAppxPackageOnBuild=true -p:AppxPackageSigningEnabled=false -p:AppxBundle=Never `
      -p:AppxPackageDir="$OutputPath\AppPackages\$platform\"
  }
  Get-ChildItem "$OutputPath\AppPackages\$platform" -Recurse -Filter *.msix | Copy-Item -Destination $PackagesPath
}

$version = ([xml](Get-Content .\Source\Quiver.App\Package.appxmanifest)).Package.Identity.Version
$nugetRoot = (dotnet nuget locals global-packages -l) -replace "^global-packages:\s*", ""
$buildToolsVersion = ([xml](Get-Content .\Directory.Packages.props)).Project.ItemGroup.PackageVersion |
  Where-Object Include -eq "Microsoft.Windows.SDK.BuildTools" | Select-Object -ExpandProperty Version
$makeAppx = Get-ChildItem (Join-Path $nugetRoot "microsoft.windows.sdk.buildtools\$buildToolsVersion\bin") -Recurse -Filter makeappx.exe |
  Where-Object { $_.Directory.Name -eq "x64" } | Select-Object -First 1
$bundlePath = Join-Path $OutputPath "Quiver_$version.msixbundle"

Write-Output "Bundling $bundlePath...."
Invoke-Native { & $makeAppx.FullName bundle /d $PackagesPath /p $bundlePath /bv $version /o }
