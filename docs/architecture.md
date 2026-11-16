# Architecture and platform boundaries

## Monorepo, native implementations

`apps/windows` currently contains the executable implementation. `apps/macos` reserves the future native macOS application. No cross-platform UI toolkit, Node workspace or shared runtime is implied.

The stable concepts are window identity, display profiles, durable recovery, window-picker state and safe session exit. These are design contracts, not a promise that Windows P/Invoke code can run on macOS. Introduce shared schemas or packages only when two implementations actually consume them.

## Windows layers

- `PocketDeskCore`: profiles, recovery ledger, pure session/gesture helpers and legacy data migration.
- `PocketDeskSystem`: Win32 monitor/window APIs and experimental input observation.
- `PocketDeskApp`: WPF settings, picker, bottom bar and a serial asynchronous workspace controller.
- `tests`: console-based logic/integration tests with fake platform interfaces; a separate manual UI probe.

## Recovery transaction

1. Validate the target HWND, PID and, where available, process start time.
2. Persist the original window placement before moving it.
3. Move, wait and read back. A mismatched placement is a failure, not a successful approximation.
4. On restore, verify identity and original placement before removing the record.
5. Wait for restoration before exit. Retain the record and cancel exit if restoration fails.

The recovery ledger is local user data, not source code or a sync format. Never commit a real ledger or enable two workspace processes to control it at the same time.

## Display and input safety

Only one recognized non-primary virtual monitor is selected. Changes are limited to driver-advertised modes. WPF panels are re-positioned in physical pixels after display/DPI events, and their bottom bar uses the monitor working area to avoid the taskbar.

The app does not make a monitor primary or control which monitor UU streams. Low-level injected-input flags do not identify UU; inactivity does not establish disconnection. The default application therefore uses manual persistence, native remote-client gestures and no global text-zoom injection.

## Driver boundary and licensing

`drivers/windows` is an unsupported IddCx experiment, separate from the regular app build and licensed under MS-PL. The working development setup used a separately installed third-party VDD. No signed driver, certificate, installation experiment or machine-specific VDD configuration is bundled here.

## Future macOS integration

Preserve native APIs, entitlements and accessibility requirements in the macOS app. Align product naming and recovery semantics where appropriate; document differences instead of assuming identical monitor/input behavior. Import upstream license notices together with code.
