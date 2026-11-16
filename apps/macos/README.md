# PocketDesk for macOS

Native Swift / AppKit / SwiftUI companion for UU Remote, **0.2.5**. It offers an isolated phone-sized display, window placement/restoration and explicit Find N5 cover/unfolded layouts. It is not a remote transport or a phone/Windows client.

[简体中文](README.zh-CN.md) · [Development](docs/development.md) · [Architecture](docs/architecture.md) · [Monorepo](../../README.md)

> Experimental source build. Logic tests and a successful package do not establish phone-side usability, reliable connection detection on every UU version, or full end-to-end acceptance.

## Build and test

Requires macOS 14+, Swift 6 and full Xcode. From this directory:

```bash
swift test --jobs 4
scripts/pocketdesk check
scripts/pocketdesk app
```

`app` produces `build/PocketDesk.app`, for the host architecture with ad-hoc signing. It does **not** install, launch, request permission, register login items, quit applications, create a display, move windows or publish anything. The app is not Developer-ID signed or notarized. Rebuilding may invalidate prior permission approval.

After deliberately installing/launching it, grant the current app in macOS Privacy & Security → Accessibility/device-control access. Normal launch starts real monitoring; permission and fresh automatic or explicit manual authorization can then cause display/window changes. Coordinate real-device testing rather than treating build commands as permission to perform it.

## Actions package

Successful push/manual macOS Actions runs provide a universal Intel/Apple Silicon ZIP, ad-hoc signed and not notarized. `scripts/package` builds it without installing or launching; `scripts/pocketdesk app` still builds only the host architecture. See [development packages](../../docs/development-packages.md).

## Behavior and boundaries

- Unknown controllers, outgoing Windows-device tables and historical or standalone streaming are not automatic display authority. Current local-controlled phone evidence and the current UU server identity are required. Unrecognized UI layouts fail closed.
- Manual hold is explicit and nonpersistent, with connection/loss and session time limits. Stop, the master switch, permissions or an identity change can revoke it.
- First-time and empty phone desktops retain the bottom bar instead of forcing the application list. Tap the bar to choose/switch a window; return to the existing window or desktop from the list.
- A successful selection is remembered separately. Only the same still-valid window instance can resume after fresh screen authorization; closed apps are not launched and same-title replacements are not guessed.
- General portrait **540×950**, Find N5 cover **570×1308** and unfolded **1080×1191** are logical layouts, selected explicitly. A device name does not establish fold state.
- Restore records precede window mutations; unresolved restore work remains recoverable. Do not run two controllers against the same local state.
- The app does not auto-quit Snug or import its state. Login-item registration is not automatic. Upstream checking/downloading/installing and all legacy `scripts/snug` publishing routes are disabled.

Private macOS display APIs and UU accessibility structures are compatibility risks. Long-running/disconnect behavior, first-use bar interaction, actual restoration and phone→Windows negative cases require real-device acceptance; do not infer these from tests or CI.

## Identity and local data

Product/bundle: `PocketDesk` / `cool.proma.pocketdesk`. User data: `~/Library/Application Support/PocketDesk/`; log: `~/Library/Logs/PocketDesk.log`. `config.json`, `phone-screen.json`, `zoom.json` and `last-phone-window.json` are local runtime state, not source files. Display identifiers and data are isolated from Snug.

## Attribution

Derived from [Snug](https://github.com/zephyr4123/snug) 2.2.1, baseline `1211dc24555099b05baccc06fe3e6e9477b906d1`, MIT. Copyrights by AoyamaShou and zephyr4123 remain in [LICENSE](LICENSE). The original icon and internal `Snug*` module names are retained deliberately, not as product branding or a shared Windows runtime.

Vendored [PermissionFlow](Vendor/PermissionFlow/VENDORED.md) retains its [MIT license](Vendor/PermissionFlow/LICENSE). Existing source/About credits are preserved. See the root [NOTICE](../../NOTICE). No private machine configuration, runtime logs or unrelated upstream development memory is part of this import.
