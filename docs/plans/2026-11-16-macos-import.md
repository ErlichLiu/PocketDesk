# macOS Monorepo Import Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Import the existing native macOS 0.2.4 implementation into the reserved apps/macos directory without altering Windows or importing private development history.

**Architecture:** Keep platform runtimes independent and preserve the Swift package's directory structure and internal module names. Import a reviewed source snapshot, licensed assets and vendored PermissionFlow; do not import build output, runtime state, agent memory, private validation notes or machine configuration. Extend publication checks with narrowly scoped source-name and pinned-asset exceptions, retaining secret and personal-path checks.

**Tech Stack:** Swift 6 / AppKit / SwiftUI, .NET 8 / WPF, Python unittest, GitHub Actions.

## Task 1: Source import and provenance

- Import tracked Sources, Tests, Assets, Vendor, Package.swift, VERSION, LICENSE and the safe scripts into apps/macos.
- Preserve original bytes and executable modes; record MIT attribution in NOTICE and native README files. Do not combine unrelated Git histories.
- Add public native build/permission/recovery documentation. Preserve distinctions between model tests, local builds and actual phone acceptance.

## Task 2: Publication safety

- Add tools/test_check_publication.py regression cases for retained internal Swift names, approved icon bytes, modified/unapproved binaries, personal paths, credentials and runtime state.
- Run `python3 -m unittest discover -s tools -p 'test_*.py'` before implementation; expect failures under the old Windows-only rules.
- Extend tools/check_publication.py only for identified macOS paths/resources; use Git-aware enumeration for the worktree, keep the index audit complete. Add ignores for native build output and last-window state.
- Repeat tests, working-tree audit and `python3 tools/check_publication.py --staged`; expect all to pass.

## Task 3: Monorepo documentation and CI

- Update README.md, README.zh-CN.md, CONTRIBUTING.md, NOTICE and docs/architecture.md to describe both implemented native platforms and their different authorization/display contracts.
- Add .github/workflows/macos.yml on a macOS runner: report toolchain, audit publication, run isolated tests and build an ad-hoc app without launching it. Keep existing Windows and security workflows.

## Task 4: Verify and publish for review

- Run `cd apps/macos && swift test --jobs 4 && scripts/pocketdesk app`; inspect plist and signature. No installation or live desktop operations.
- Confirm imported native source bytes still match the source snapshot and Windows/driver files are unchanged.
- Scan staged files and the new commit with Gitleaks; review licenses and public links.
- Push only feat/macos-monorepo to ErlichLiu/PocketDesk, open a PR, and inspect cloud checks. Do not merge without separate user agreement.
