# Contributing

PocketDesk is experimental. Start with [README](README.md), [architecture](docs/architecture.md) and the relevant platform README.

- Windows application code belongs in `apps/windows`; future native macOS code belongs in `apps/macos`.
- Keep recovery and input safety changes covered by isolated tests. Never use real user windows, state files or injected input in a logic test.
- Do not change the primary display, trust stores, driver state or UAC settings as part of normal app startup or automated tests.
- Test actual native UI separately from fake platforms. Record what was and was not exercised; do not call a compile success an end-to-end validation.
- Run `apps/windows/scripts/Test-PocketDesk.ps1`, a Release solution build, and `python tools/check_publication.py --staged` before proposing changes. Use Gitleaks on the Git history as an additional check.
- Do not commit credentials, private keys, signing certificates, runtime state, personal logs, absolute user paths or build output.
- Preserve upstream license notices. `drivers/windows` is MS-PL; other application code is MIT unless noted. New third-party code must bring its license and a `NOTICE` entry.

For security-sensitive reports, see [SECURITY.md](SECURITY.md). Do not attach unredacted logs or recovery ledgers to issues.
