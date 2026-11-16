# Actions development packages

The Windows and macOS workflows build downloadable development ZIPs after their isolated tests succeed. Pushes and manual `workflow_dispatch` runs upload artifacts; pull-request checks do not upload executables. These are **Actions artifacts, retained for 14 days**, not GitHub Releases. Downloading via GitHub may require signing in.

Open [Actions](https://github.com/ErlichLiu/PocketDesk/actions), choose a successful Windows or macOS run for the desired commit, and download its artifact. The artifact contains a platform ZIP and its `.sha256` file. Extract the inner ZIP before use. Each package contains `BUILD-INFO.json` with the independent app version and exact source commit.

| Package | Contents | Requirements and trust |
| --- | --- | --- |
| Windows x64 | Self-contained WPF app, .NET runtime, notices and instructions | Windows 10/11 x64; unsigned executable; working virtual display installed independently |
| macOS universal | Intel + Apple Silicon app, licenses and instructions | macOS 14+; ad-hoc signed, **not notarized**; current app permission required |

Windows uses the current project's .NET 8 target, without changing its framework or app behavior. Extract the whole directory and deliberately run `PocketDesk.exe`; do not move only the executable away from its DLLs. No SDK/Desktop Runtime installation is needed for this self-contained package. It does not include or install a display driver, modify trust stores or act as a signed installer. Application data remains in LocalAppData, not in the portable package.

The macOS ZIP preserves executable permissions and the application bundle. It may be blocked as an unidentified/unnotarized app; use normal system Privacy & Security controls after reviewing the source. Do not disable Gatekeeper or SIP. Rebuilding/replacing an ad-hoc app can require renewing Accessibility/device-control approval.

**Close the existing app safely before switching builds**, preserving pending restoration state. Packaging and CI never install or launch the app. Actual launch can monitor/control real windows and displays; a successful package is not phone-side acceptance.

## Build locally

Python 3.11+ is used for metadata, static archive checks and SHA256. Native SDK prerequisites remain in the platform READMEs.

```powershell
# Windows, from repository root; does not install or run
./apps/windows/scripts/Package-PocketDesk.ps1
```

```bash
# macOS, from repository root; cross-compiles the other CPU architecture
apps/macos/scripts/package
```

Outputs are in the platform's ignored `artifacts/packages/` directory. Windows includes runtime license/third-party texts from the exact restored runtime packs. macOS verifies both CPU slices and the complete ad-hoc signature before archiving. Both include repository/upstream license notices, a source manifest, instructions and a checksum; no user config, logs, signing keys or machine profile is read for packaging.

## Verify without launching

```bash
python3 tools/package_artifacts.py verify PATH_TO_ZIP windows
python3 tools/package_artifacts.py verify PATH_TO_ZIP macos
```

The checker validates CRCs, safe archive paths, provenance, required licenses, self-contained runtime/PE architecture or universal Mach-O architecture and executable permissions. It does not execute an app or replace platform signature verification. After extracting a macOS package with `ditto`, additionally use `codesign --verify --strict --deep --all-architectures` on the app.

Formal release assets, trusted signing/notarization, Windows signing and driver delivery remain separate decisions; these workflows have read-only repository permissions and do not create Releases.
