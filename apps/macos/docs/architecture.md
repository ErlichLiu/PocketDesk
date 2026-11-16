# Native macOS architecture

## Layers

- `Sources/SnugCore`: pure session and authorization policy, phone-screen orchestration, window state and persistence.
- `Sources/SnugSystem`: WindowServer/Accessibility adapters, display mode helpers, event translation and single-instance locking.
- `Sources/Snug`: AppKit/SwiftUI UI, scoped UU observation and dependency assembly.
- `Sources/CGVirtualDisplayBridge`: Objective-C declarations for private virtual-display APIs.
- `Vendor/PermissionFlow`: licensed native permission guidance, adapted locally.

Internal upstream names remain to minimize import risk. They do not imply shared application identity or a shared runtime with Windows.

## Authority is not presence

UU process/capture presence and historical log replay do not authorize a phone display. Automatic authority additionally requires a recognized mobile controller in the current local-controlled UI with fresh evidence bound to the current server process. A supported already-confirmed session may tolerate a bounded transport restart only while local identity evidence stays fresh. Unknown UI/controller identity is rejected, including outgoing Windows-device tables.

Explicit manual authorization is session-only and bounded. It cannot override disabled master state or missing permission. Stop, identity replacement and timeout revoke it. Layout selection and remembered windows are not authorization.

## Phone workspace

`PhoneDesk` serializes display creation/mode changes, placement and restoration. Recovery data is persisted before mutations. Shutdown stops sensing before draining restoration/removal. Failed or partial work must not silently discard restoration records.

The initial phone screen is a desktop with a bottom bar, not a forced picker. `listVisible` reflects explicit UI intent; the bar is available without a placement. The picker can return to the existing window or empty desktop. Screen-mode compensation also runs when there is no placement.

A successful selection persists a window key and process-instance identity separately from the recovery ledger. After new authorization, only that same live window can resume. Explicit picker/desktop intent wins over queued resume work, and cancellation checks prevent further forward operations after authority is revoked.

## Boundaries

Do not launch closed apps, infer folding state from model names, activate from unknown connections, merge Snug data, register login items automatically or use upstream update/publishing channels. Native virtual-display creation, window operations, permission prompts and normal monitoring are real desktop actions, not unit tests.

Platform details intentionally differ from [Windows](../../../docs/architecture.md). This import is a reviewed source snapshot, not a common UI or transport implementation.
