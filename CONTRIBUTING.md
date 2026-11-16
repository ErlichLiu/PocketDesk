# Contributing

PocketDesk is experimental. Start with [README](README.md), [architecture](docs/architecture.md) and the relevant platform README.

- Windows application code belongs in `apps/windows`; native macOS code belongs in `apps/macos`.
- Keep recovery and input safety changes covered by isolated tests. Never use real user windows, state files or injected input in a logic test.
- Do not change the primary display, trust stores, driver state or UAC settings as part of normal app startup or automated tests.
- Test actual native UI separately from fake platforms. Record what was and was not exercised; do not call a compile success an end-to-end validation.
- Windows changes: run `apps/windows/scripts/Test-PocketDesk.ps1` and a Release solution build on Windows.
- macOS changes: from `apps/macos`, run `swift test --jobs 4` and `scripts/pocketdesk app`. Packaging must not install, launch, request permissions or mutate real displays/windows. Keep platform identities and data isolated from upstream applications.
- Shared publication tooling: run `python -m unittest discover -s tools -p "test_*.py"` and `python tools/check_publication.py --staged`. Use Gitleaks on Git history as an additional check. The two pinned native icons are deliberate binary exceptions; review changed bytes before updating hashes.
- Keep internal native module names during this import; do not gratuitously rename the engine or introduce shared runtime packages. Root architecture docs describe the distinct Windows/macOS activation contracts.
- Do not commit credentials, private keys, signing certificates, runtime state, personal logs, absolute user paths or build output.
- Preserve upstream license notices. `drivers/windows` is MS-PL; other application code is MIT unless noted. New third-party code must bring its license and a `NOTICE` entry.

For security-sensitive reports, see [SECURITY.md](SECURITY.md). Do not attach unredacted logs or recovery ledgers to issues.
