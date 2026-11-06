# Reproducible native checks

Rust is pinned to **1.97.1** in rust-toolchain.toml and both existing Windows workflows. Every existing build.ps1 Cargo target uses --locked. Windows x64/arm64 target order, packaging and explicit native exit checks remain intact. Both workflows stay dispatch-only on windows-latest. No Linux Boat replacement for native WinUI/MSIX and no release/cache-seeding run.

Portable NativeMessagingHost formatting, locked compilation and six framing tests passed in secret-free Linux Boat under the exact Rust pin. They do not execute the messaging host application or qualify Windows UI, settings, packaging, signing or architecture-specific behavior. Native package identity/cache comparisons remain blocked on an approved Windows host.

## Explicit hook setup

On an approved Windows host, prepare PowerShell 7, Git Bash, installed Rust 1.97.1 with rustfmt and .NET 10 per global.json. Resolve Cargo.lock dependencies and restore Source/Quiver.App, Tests/RuleMatchSmoke and Tests/SettingsSmoke explicitly. Before the App build, explicitly prepare the locked, offline NativeMessagingHost Release binary for the host architecture's Windows Rust target. The hook builds the App for the host architecture (`win-x64`/`x64` or `win-arm64`/`arm64`). The App project requires `target/<RustTarget>/release/NativeMessagingHost.exe`; `cargo check` does not produce it. Native checks must use existing Windows SDK/MSIX/VS prerequisites. No bootstrap occurs inside the hook.

Opt in with `pwsh -NoProfile -File scripts/install-hooks.ps1` or `bash scripts/install-hooks.sh`. Installer refuses existing unrelated active hooks/custom hooksPath and is idempotent. Relative repository-local hooksPath works within linked worktrees. No user hooks were installed during this work.

The hook fails explicitly off Windows. On Windows it verifies prepared assets, runs pinned cargo fmt/check with offline locked dependencies, builds the real App project in Debug with packaging/signing disabled, then runs existing RuleMatchSmoke and SettingsSmoke. Each external command's nonzero exit is propagated immediately. SettingsSmoke uses its bounded test fixture path, not real settings. The hook never calls build.ps1, UI smoke, release workflows, packaging, signing or app launch.

Every nondeleted pushed ref runs the full checks. There is no path-based skipping. Each such ref must resolve to checked-out HEAD from a clean checkout, including nonignored untracked files. Empty input also runs the full checks. Deleted refs alone skip. Multiple refs are checked without fetching, stashing or rewriting the checkout.

`python3 scripts/hooks/test-hooks.py` exercises actual executable range/installer behavior using stub payloads in disposable repositories. This is not Windows-native qualification. Native PowerShell/.NET/project behavior and timing remain unverified until the approved matching host runs it. No under-nine-second claim or cache optimization.
