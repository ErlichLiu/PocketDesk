# PocketDesk

A phone-friendly remote desktop workspace, built as a cross-platform monorepo.

[简体中文](README.zh-CN.md) · [Architecture](docs/architecture.md) · [Contributing](CONTRIBUTING.md) · [License scope](NOTICE)

> **Experimental software.** Windows and macOS have separate native implementations; neither is a production-ready cross-platform release. PocketDesk is not a remote streaming client and does not replace UU Remote/GameViewer.

## What it does

On Windows, PocketDesk places a touch-sized window picker on an existing virtual monitor. Choose a desktop window, fill the monitor's usable area, return to the picker, or restore the window to its original location and state.

- Foldable profile: **2248:2480**, with a **1080×1191** base mode (OPPO Find N5 unfolded).
- Portrait profile: **1080×1900**.
- Persistent manual workspace: inactivity is not treated as a disconnect.
- Recovery records are saved before moving a window; restoration is verified before the record is removed.
- Shutdown waits for restoration. An unresolved restoration cancels exit and retains its record.
- Native UU input and application text size are left unchanged by default.

**Select the virtual monitor in your phone's remote client.** Starting PocketDesk does not automatically switch UU's view or change the Windows primary display.

## Repository layout

```text
apps/
  windows/       .NET 8 / WPF app, Win32 layer, tests and user-level installer
  macos/         Swift 6 / AppKit + SwiftUI app, system adapters, tests and local packager
drivers/
  windows/       Unsupported experimental IddCx source (MS-PL, not MIT)
docs/            Cross-platform boundaries and user-facing documentation
tools/           Publication hygiene checks
licenses/        Third-party license notices
.github/         CI
```

There is no JavaScript workspace or shared runtime package to install. Platform code remains native. A shared package should be added only when a real cross-platform contract exists.

## Download development packages

Windows x64 self-contained and macOS universal ZIPs are uploaded by successful push/manual Actions runs. They include checksums and source metadata, expire after 14 days, and are not signed/notarized production Releases. See [package downloads and requirements](docs/development-packages.md).

## Windows quick start

Prerequisites: Windows 10/11 x64, **.NET 8 SDK** (or the .NET 8 Desktop Runtime for a built app), and an independently installed, working virtual display such as [VDD](https://github.com/VirtualDrivers/Virtual-Display-Driver).

From the repository root:

```powershell
dotnet build apps/windows/PocketDesk.Windows.sln -c Release
powershell -NoProfile -File apps/windows/scripts/Test-PocketDesk.ps1
powershell -NoProfile -File apps/windows/scripts/Build-PocketDesk.ps1
powershell -NoProfile -File apps/windows/scripts/Install-PocketDesk.ps1
```

The user-level installer copies the app to `%LOCALAPPDATA%\Programs\PocketDesk`, adds a Start Menu shortcut, and launches it. It does not install a display driver, change trust stores, enable autostart, or require administrator privileges. `-NoLaunch` installs without launching. If that installed executable is running, exit it safely before updating.

See [Windows details](apps/windows/README.md) for monitor selection, permissions, settings and legacy data migration.

## macOS quick start

Requires macOS 14+, Swift 6 and full Xcode. PermissionFlow is vendored with its MIT license; there is no remote package fetch or global dependency installation.

```bash
cd apps/macos
swift test --jobs 4
scripts/pocketdesk app
# Output: apps/macos/build/PocketDesk.app (relative to the repository root)
```

The build command does **not** install, launch, request permissions or create a display. It produces a host-architecture, ad-hoc-signed development app, not a notarized release. A rebuild may require renewed macOS permission approval.

The imported macOS version is **0.2.4**. It creates an isolated phone display only after explicit manual intent or fresh, scoped evidence that a recognized phone controls this Mac. Historical streaming and a phone controlling Windows are not activation authority. First-time/empty desktops retain the bottom bar; the window picker opens explicitly, and a valid remembered window can resume without showing it.

See [macOS details](apps/macos/README.md) for permissions, data isolation, foldable layouts and real-device validation limits. The Windows manual-monitor workflow is intentionally different.

## Current boundaries

- **Windows:** A working third-party virtual display is required. The included experimental IddCx driver has not successfully loaded in the development environment (Code 31); it is **not a supported installer**.
- **Windows:** Reliable UU connection/phone identity detection is not available. There is no automatic disconnect restoration or primary-monitor switching.
- **Windows:** The experimental gesture translator and text-zoom helpers are not enabled in the default app.
- **macOS:** Accessibility/device-control permission and a compatible UU interface are required. Virtual displays use private macOS APIs and may break after OS changes; unknown controller layouts fail closed.
- Logic tests do not establish phone-side touch usability. Validate on your actual phone and remote client.
- Do not restart a display driver, UU, Explorer or the computer while depending on the live remote connection.

## Development and checks

The Windows tests are dependency-free console executables. They use isolated data and fake window/display platforms where appropriate; `WindowProbe` is a separately launched native UI fixture, not an automated desktop test.

```powershell
python -m unittest discover -s tools -p "test_*.py"
python tools/check_publication.py
# After staging, audit the actual index snapshot:
python tools/check_publication.py --staged
# Install Gitleaks separately; do not commit a downloaded scanner binary.
gitleaks git --redact --no-banner
```

Do not commit runtime state, logs, certificates, private keys, local paths or build output. See [SECURITY.md](SECURITY.md).

## License and attribution

Application code is **MIT**. `drivers/windows` is separately **MS-PL** because it adapts Microsoft's IndirectDisplay sample. See [LICENSE](LICENSE), [NOTICE](NOTICE), and [drivers/windows/LICENSE](drivers/windows/LICENSE).

The macOS code is derived from, and Windows workspace behavior was developed with reference to [Snug](https://github.com/zephyr4123/snug), with upstream MIT notices retained. VDD and UU remain separate projects/products; their binaries are not included.
