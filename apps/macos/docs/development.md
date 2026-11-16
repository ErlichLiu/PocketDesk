# macOS development and verification

From the monorepo root, enter `apps/macos`. Requires macOS 14+, Swift 6 and full Xcode. The dependency is vendored; do not install global packages or restore upstream publishing tooling.

```bash
swift test --jobs 4
scripts/pocketdesk check
scripts/pocketdesk app
codesign --verify --strict --deep build/PocketDesk.app
plutil -lint build/PocketDesk.app/Contents/Info.plist
```

The test targets use Swift Testing and isolated fake platform services. They must not create real displays or move user windows. `scripts/pocketdesk app` performs a Release build, bundles the local dependency and licenses, validates metadata, and signs ad-hoc for the host architecture. It never installs or launches. `scripts/snug` is a refusal stub; legacy installation/distribution commands are not supported.

Normal launch is different: the installed app monitors UU and may mutate displays/windows when authority is satisfied. Installation, permission changes, login registration and actual phone/desktop testing require explicit coordination. Do not toggle user permissions as part of a build or automated test. Preserve existing applications and user state.

## Verification levels

- Unit tests validate policy and fake-platform orchestration, not actual phone interaction.
- `--render-settings`, `--render-welcome` and `--render-phone` render native view samples; they do not prove the real app has permissions or that first-screen/bar interactions work.
- Read-only `--dump-uu` and `--benchmark-windows` diagnostics run in the invoking host's context. Host Accessibility permission is not evidence that the installed app is authorized.
- Real-device acceptance separately checks phone→Mac activation, phone→Windows rejection, first-use bar/open/close, valid-window resume, invalid-target fallback, cancellation, transport interruptions and full restoration.

Do not commit actual diagnostic output or a recovery ledger. Redact account names, app titles and machine identifiers before sharing reproducible examples. The checked-in capture parser fixtures are inherited test data, not current user logs.

## Public import discipline

The 0.2.4 import preserves production/vendor/resource bytes and executable modes. Two test-only portability adjustments cover older Foundation: an explicit raw-buffer type annotation and normalization of equivalent about-URL encoding, retaining assertions for an inert scheme and no host. Application behavior and the byte-generation algorithm are unchanged. No developer memory, private validation journal, machine environment file, generated bundle or unrelated development Git history is imported. The native CI builds without launching the app.

Publication checks run from the monorepo root:

```bash
python3 -m unittest discover -s tools -p 'test_*.py'
python3 tools/check_publication.py
python3 tools/check_publication.py --staged
gitleaks git --redact --no-banner
```

Keep upstream [MIT attribution](../LICENSE), [dependency license](../Vendor/PermissionFlow/LICENSE), asset provenance and root [NOTICE](../../../NOTICE). The two binary icon files are pinned by digest in the publication checker; any change requires explicit byte review and a deliberate pin update, not a broad binary exemption.
