# PocketDesk for macOS — reserved

The macOS implementation has **not been imported yet**. There is no macOS build target or release in this repository at present.

When integrating it:

- Keep native Swift/AppKit code and its platform build/test tooling in this directory.
- Preserve the original copyright and license of any imported source; update the root `NOTICE`.
- Use the PocketDesk product name and document any legacy data migration.
- Keep platform-specific monitor, accessibility and input implementations separate from Windows Win32 code.
- Add macOS CI when executable code and tests exist. Do not claim cross-platform support before verification.

See [the monorepo architecture](../../docs/architecture.md).
