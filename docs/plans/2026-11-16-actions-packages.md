# Actions Development Packages Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Build downloadable Windows x64 self-contained and macOS universal development ZIPs in Actions, without installing/launching apps or creating formal Releases.

**Architecture:** Keep native CI and application versions independent. Add platform package scripts after isolated tests, a shared metadata/license/archive checker, checksums and Actions artifact uploads. macOS combines host and cross-compiled Intel/Apple Silicon executable slices, then signs ad-hoc; Windows uses dotnet publish with its runtime and runtime license notices. No signing credentials, drivers or machine data are bundled.

**Tech Stack:** PowerShell/.NET 8, Bash/Swift/lipo/codesign/ditto, Python unittest/zipfile/plistlib, upload-artifact v4.

1. Add tools/test_package_artifacts.py fixtures for metadata, architectures, runtime files, licenses, integrity and unsafe paths; observe missing implementation failure.
2. Implement tools/package_artifacts.py preparation/verification/checksum commands. Add apps/windows/scripts/Package-PocketDesk.ps1 and apps/macos/scripts/package. Preserve existing install/build behavior.
3. Update existing native workflows with workflow_dispatch, package and upload steps (ZIP + SHA256, 14-day retention, no Release permissions). Add exact publication-name exceptions only for attribution-aware tooling.
4. Run shared tests, publication checks, Bash syntax/native package validation and independent review; push/open PR. Obtain and statically verify both actual Actions artifacts without executing them.
5. Seek user agreement before merge; after merge verify/download the final main-run archives, retain user deliverables and clean task-only resources. Document unsigned/notarized and virtual-display prerequisites.
