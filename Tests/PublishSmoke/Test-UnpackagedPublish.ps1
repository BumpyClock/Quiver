[CmdletBinding()]
param(
    [string]$PublishDirectory,
    [string]$NativeMessagingHostPath,
    [switch]$VerifyOnly,
    [int]$ObserveProcessId
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$project = Join-Path $repoRoot 'Source/Quiver.App/Quiver.App.csproj'
if (-not $PublishDirectory) {
    if ($VerifyOnly) { throw 'VerifyOnly requires PublishDirectory.' }
    $PublishDirectory = Join-Path $repoRoot "_Publish/publish-smoke-$([Guid]::NewGuid().ToString('N'))"
}
$PublishDirectory = [IO.Path]::GetFullPath($PublishDirectory)
$properties = @(
    '-p:Configuration=Release', '-p:Platform=x64', '-p:RuntimeIdentifier=win-x64',
    '-p:SelfContained=true', '-p:PublishAot=true', '-p:WindowsPackageType=None',
    '-p:EnableMsixTooling=false', '-p:GenerateAppxPackageOnBuild=false'
)
if ($NativeMessagingHostPath) { $properties += "-p:NativeMessagingHostPath=$NativeMessagingHostPath" }
if (-not $VerifyOnly) {
    if (Test-Path -LiteralPath $PublishDirectory) {
        throw 'PublishDirectory must not exist. Use a fresh path to rule out stale resources.'
    }
    & dotnet publish $project @properties -o $PublishDirectory -v:q
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE." }
}
$evaluation = & dotnet msbuild $project -nologo @properties '-getProperty:ProjectPriFileName' '-getItem:ApplicationDefinition,Page'
if ($LASTEXITCODE -ne 0) { throw "MSBuild evaluation failed with exit code $LASTEXITCODE." }
$model = ($evaluation -join "`n") | ConvertFrom-Json
$expected = @('Quiver.exe', $model.Properties.ProjectPriFileName)
foreach ($item in @($model.Items.ApplicationDefinition) + @($model.Items.Page)) {
    if ($item.PSObject.Properties['ExcludedFromBuild'] -and $item.ExcludedFromBuild -eq 'true') { continue }
    $relative = if ($item.PSObject.Properties['Link'] -and $item.Link) { $item.Link } else { $item.Identity }
    $expected += [IO.Path]::ChangeExtension($relative, '.xbf')
}
foreach ($relative in $expected) {
    $file = Join-Path $PublishDirectory $relative
    if (-not (Test-Path -LiteralPath $file -PathType Leaf) -or (Get-Item -LiteralPath $file).Length -eq 0) {
        throw "Missing or empty published resource $relative."
    }
    [pscustomobject]@{ Resource = $relative; Bytes = (Get-Item -LiteralPath $file).Length }
}
Write-Host "PASS. Published executable, application PRI and $($expected.Count - 2) compiled XAML resources in $PublishDirectory."

if ($ObserveProcessId) {
    $expectedExe = Join-Path $PublishDirectory 'Quiver.exe'
    $process = Get-Process -Id $ObserveProcessId
    if ($process.Path -ne $expectedExe) { throw "Process $ObserveProcessId is not $expectedExe." }
    $clock = [Diagnostics.Stopwatch]::StartNew()
    $observed = $false
    while ($clock.Elapsed.TotalSeconds -lt 10) {
        $process.Refresh()
        if ($process.HasExited) { throw "Published process exited with code $($process.ExitCode)." }
        if ($process.MainWindowHandle -ne [IntPtr]::Zero -and $process.MainWindowTitle -eq 'Quiver - Select') {
            $observed = $true
            break
        }
        Start-Sleep -Milliseconds 100
    }
    if (-not $observed) { throw 'Published process did not expose its selector window. Check instance redirection and startup logs.' }
    Start-Sleep -Seconds 2
    $process.Refresh()
    if ($process.HasExited -or $process.MainWindowHandle -eq [IntPtr]::Zero -or $process.MainWindowTitle -ne 'Quiver - Select') {
        throw 'Published selector did not remain visible through the startup observation.'
    }
    Write-Host "PASS. Observed published selector in process $ObserveProcessId. Process remains running."
}
