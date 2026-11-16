# PocketDesk for Windows

Experimental .NET 8 / WPF / Win32 implementation. Run commands below from the repository root.

## Build, test and launch

```powershell
dotnet build apps/windows/PocketDesk.Windows.sln -c Release
powershell -NoProfile -File apps/windows/scripts/Test-PocketDesk.ps1
powershell -NoProfile -File apps/windows/scripts/Build-PocketDesk.ps1
powershell -NoProfile -File apps/windows/scripts/Start-PocketDesk.ps1
```

`Build-PocketDesk.ps1` writes a framework-dependent build to `apps/windows/artifacts/stable`. The .NET 8 Desktop Runtime is needed on the destination computer. This is not a signed standalone installer.

`Install-PocketDesk.ps1` copies that build to `%LOCALAPPDATA%\Programs\PocketDesk`, verifies file hashes and creates a Start Menu shortcut. It refuses to overwrite a running installed executable. It does not install or restart drivers. Use `-NoLaunch` to stop after installation.

## Use

1. Ensure exactly one supported, non-primary virtual display is active. VDD's generic monitor name may differ from the app name.
2. Start PocketDesk; settings appear on the primary screen, the window picker on the virtual display.
3. On the **phone**, use UU's monitor selector to view that virtual display. This phone-side setting is not controlled by PocketDesk.
4. Choose a window. The bottom bar offers the picker and restore actions.
5. Exit through settings or the tray. `PocketDesk.exe --quit` sends a safe shutdown request.

Modes are selected only from resolutions advertised by the driver. A profile change may briefly interrupt the remote video stream. The app does not change the primary monitor, system DPI or driver configuration.

Use a regular process for regular windows. To move an elevated app, first exit PocketDesk and explicitly run it as administrator, e.g. `Start-PocketDesk.ps1 -Elevated`. Never disable UAC to work around a permission boundary.

## State and legacy migration

| Item | Location |
| --- | --- |
| Settings | `%LOCALAPPDATA%\PocketDesk\config.json` |
| Recovery ledger | `%LOCALAPPDATA%\PocketDesk\phone-screen.json` |
| Legacy zoom settings (not automatically applied) | `%LOCALAPPDATA%\PocketDesk\zoom.json` |
| Log | `%LOCALAPPDATA%\PocketDesk\pocketdesk.log` |

The previous Windows prototype was named **Snug**. Exit it safely before launching PocketDesk. On the first launch, PocketDesk can copy validated `config.json` and `zoom.json` from `%LOCALAPPDATA%\Snug` without modifying those originals. Pending legacy window records block migration: restore them in the old app first. Logs, certificates and active recovery transactions are never migrated.

Existing PocketDesk settings are never overwritten by legacy settings. Keep the old installation until you have confirmed the new one works; this repository does not silently uninstall it or alter its driver.

## Tests and limits

- `DisplayTests`: modes, profiles, working area and config serialization.
- `WindowSafetyTests`: durable recovery, identity checks, FIFO scheduling, shutdown and same-width aspect changes, using fake platforms.
- `GestureTests`: pure translation/input-capture logic; no native hook or injected input.
- `MigrationTests`: isolated legacy migration and refusal cases.
- `WindowProbe`: manually launched, disposable native UI fixture for real desktop tests; not run by the automated suite.

The app does not reliably infer UU connection state or input origin. Gesture remapping and automatic zoom remain disabled. Neither native UI testing nor logic tests establishes actual phone touch behavior; verify on your device.
