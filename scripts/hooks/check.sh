#!/usr/bin/env bash
set -euo pipefail
command -v pwsh >/dev/null || { echo 'Prepare Windows PowerShell 7 and the README native prerequisites first.' >&2; exit 1; }
exec pwsh -NoProfile -File scripts/hooks/check.ps1
