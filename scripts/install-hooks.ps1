$ErrorActionPreference = 'Stop'
# Resolve Git for Windows bash from git itself. A bare `bash` can hit the WSL launcher in System32.
$execPath = & git --exec-path
if ($LASTEXITCODE -ne 0) { throw 'git must be on PATH.' }
$gitRoot = Split-Path (Split-Path (Split-Path $execPath -Parent) -Parent) -Parent
$bash = @('bin/bash.exe', 'usr/bin/bash.exe') | ForEach-Object { Join-Path $gitRoot $_ } | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $bash) { throw "Git for Windows bash.exe not found under $gitRoot. Run scripts/install-hooks.sh from Git Bash." }
& $bash (Join-Path $PSScriptRoot 'install-hooks.sh')
exit $LASTEXITCODE
