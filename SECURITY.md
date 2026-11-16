# Security

PocketDesk is an early experimental application, not a hardened security boundary. Do not run untrusted applications elevated merely to make them controllable by PocketDesk.

## Reporting

Do not publish a live credential, signing key, private recovery ledger or unredacted desktop log in an issue. If GitHub private vulnerability reporting is enabled, use the repository's Security → Report a vulnerability entry. Otherwise contact the repository maintainer through an available private channel before sharing sensitive details; a dedicated reporting address is not currently configured.

If a credential has already been published, revoke/rotate it immediately. Deleting a file or creating a later commit does not remove it from Git history.

## Publication checks

The repository excludes generated binaries, signing material, runtime user data and local machine notes. `tools/check_publication.py --staged` checks the actual index snapshot for disallowed paths, obvious credential patterns, unrenamed product identifiers and absolute user paths. Gitleaks should additionally scan staged content before the first push and full Git history after committing.

No scanner proves the complete absence of secrets. Review every staged path and diff before publishing, and re-check after any change. Keep scan reports outside the repository; even a redacted report can reveal private filenames.

The included IddCx source is unsupported. No driver certificates or signing private keys are distributed, and standard app installation does not modify Windows trust stores.
