# GameHome release handoff — 2026-11-14

## Starting point

- Repository: `Kolbi007/gamehome`
- Branch: `fix/m2-toggle-r1-and-slideshow`
- Starting commit: `f842153`
- PR: [#48](https://github.com/Kolbi007/gamehome/pull/48), targeting `main`
- GitHub CLI is authenticated to `Kolbi007`. Release signing secrets are present; their values were not read.

## Changes in this handoff

- `MainWindow.Feel.cs`: fade the first attract slide as well as later slides; restore the hero after interrupted transitions; stop the ambient animation on input; check window/session/overlay/input state after waits; catch and log dispatcher timer errors; throttle retries when no local art is available.
- `MainWindow.Trailer.cs`: keep one fade storyboard per element and stop an older fade before starting another.
- `MainWindow.xaml.cs`: track and stop the ambient Ken Burns storyboard, reset its scale, stop it on window deactivation/game launch, stop the first Steam-sync timer at launch, guard Steam sync and metrics while a session is active, and send a best-effort timestamped Moonlight signal to `\\<host>\GameHomeHost\gamehome-stream.flag` before connecting.
- `MainWindow.Discover.cs` and `SteamLocalLibrary.cs`: cancel local Steam cache reads when launch begins and retry later if the read was interrupted.
- `tools/host/Set-StreamDesktop.ps1`: skip Desktop and require/consume a signal less than 45 seconds old, wait for an in-flight Hide to finish before Restore, save state atomically, restore the original taskbar visibility, and leave taskbar state unchanged when no Hide state exists.

The SMB share must be configured on the host as `GameHomeHost` pointing to the per-user `GameHome\host` directory. This repository change does not create or change the share. The optional signal fails open: unavailable SMB does not block Moonlight launch.

## Existing host-side setup inspected read-only

- The host copy of `Set-StreamDesktop.ps1` and `Start-WoWForever.ps1` exist locally and both parse as PowerShell.
- Apollo's `World of Warcraft Forever` entry points to the wrapper as a detached PowerShell command.
- The host copy's Desktop guard force-stops processes based on broad window-title/process-name matches. This can affect unrelated processes; review and narrow/remove that behavior before deploying host-script changes. The host copy was not modified or executed.
- No Legion Go or live Moonlight gameplay smoke test was run.

## Validation

- `dotnet build src/GameHome.App/GameHome.App.csproj -c Release -p:Platform=x64 -p:GameHomeAot=false`: passed, 0 warnings, 0 errors.
- `dotnet test --project tests/GameHome.Core.Tests -c Release`: 147 passed, 0 failed.
- PowerShell parser: repository host script, host-installed script, and WoW wrapper parse successfully.
- `git diff --check`: passed.
- The current PR's x64/ARM64 AOT and JIT CI was green before this handoff. A fresh CI run is still required for these uncommitted changes.

## Release gate

The repository release workflow creates a signed prerelease automatically on a push to `main`, assigning tag `v0.2.<workflow run number>`. Merge only after the updated PR checks pass. After merge, verify the Release workflow completed and the GitHub Release has x64 and ARM64 MSIX files, certificate, and installer script.

## Audit/backup

A pre-release bundle, binary patch, changed-file snapshot, and manifest are stored separately from the repository in the Codex outputs directory. The bundle preserves committed refs; the patch and snapshots preserve this working-tree handoff. Existing untracked review inputs and backup artifacts were left untouched and must not be staged accidentally.