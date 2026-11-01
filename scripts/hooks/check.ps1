$ErrorActionPreference = 'Stop'
if (-not $IsWindows) { throw 'Quiver .NET/WinUI checks require an explicitly approved Windows host. Linux is not native acceptance.' }
function Invoke-Checked([string]$Program, [string[]]$Arguments) {
    & $Program @Arguments
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}
$env:RUSTUP_AUTO_INSTALL = '0'
$env:CARGO_NET_OFFLINE = 'true'
foreach ($tool in @('rustup', 'dotnet')) {
    if (-not (Get-Command $tool -ErrorAction SilentlyContinue)) {
        throw "Missing $tool. Prepare installed Rust 1.97.1 and .NET 10 explicitly first."
    }
}
$sdk = & dotnet --version
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
if ($sdk -notlike '10.*') { throw 'Prepare .NET 10 as required by global.json.' }
foreach ($project in @('Source/Quiver.App', 'Tests/RuleMatchSmoke', 'Tests/SettingsSmoke')) {
    if (-not (Test-Path "$project/obj/project.assets.json")) {
        throw "Prepare explicit NuGet restore for $project before pushing. No hook-time restore."
    }
}
Invoke-Checked 'rustup' @('run', '1.97.1', 'cargo', 'fmt', '--all', '--', '--check')
Invoke-Checked 'rustup' @('run', '1.97.1', 'cargo', 'check', '--workspace', '--locked', '--offline')
Invoke-Checked 'dotnet' @('build', 'Source/Quiver.App/Quiver.App.csproj', '--no-restore', '-c', 'Debug', '-p:GenerateAppxPackageOnBuild=false', '-p:AppxPackageSigningEnabled=false')
Invoke-Checked 'dotnet' @('run', '--project', 'Tests/RuleMatchSmoke/RuleMatchSmoke.csproj', '--no-restore')
Invoke-Checked 'dotnet' @('run', '--project', 'Tests/SettingsSmoke/SettingsSmoke.csproj', '--no-restore')
