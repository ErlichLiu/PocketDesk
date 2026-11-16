#!/usr/bin/env python3
"""Fail closed on unsuitable public files. Supplement, never replace, Gitleaks/review."""
from __future__ import annotations

import argparse
import hashlib
from pathlib import Path
import re
import subprocess
import sys

ROOT = Path(__file__).resolve().parents[1]
IGNORED_PARTS = {".git", "bin", "obj", "artifacts", "app-build", "__pycache__", ".vs", "x64", "x86", ".build", ".swiftpm", "build", "DerivedData", ".memory", ".context"}
FORBIDDEN_SUFFIXES = {
    ".key", ".pem", ".pfx", ".p12", ".cer", ".crt", ".p7b", ".snk", ".cat",
    ".dll", ".exe", ".sys", ".pdb", ".zip", ".dmp", ".log", ".mobileprovision",
}
RUNTIME_NAMES = {"config.json", "phone-screen.json", "zoom.json", "mcp.json", "credentials.json", "last-phone-window.json"}
# The old name is allowed only for attribution, compatibility and retained native namespaces.
LEGACY_NAME_ALLOWLIST = {
    ".gitignore", "NOTICE", "README.md", "README.zh-CN.md", "licenses/snug-MIT.txt",
    "docs/architecture.md", "tools/test_check_publication.py",
    "apps/macos/Package.swift", "apps/macos/LICENSE", "apps/macos/README.md",
    "apps/macos/README.zh-CN.md", "apps/macos/scripts/pocketdesk", "apps/macos/scripts/snug",
    "apps/macos/Assets/AppIcon/README.md", "apps/macos/Assets/AppIcon/AppIcon.svg",
    "apps/macos/docs/architecture.md", "apps/macos/docs/development.md",
    "apps/macos/Tests/SnugCoreTests/Fixtures/uuremoteserver-1755.ndjson",
    "apps/macos/Tests/SnugCoreTests/Fixtures/uuremoteserver-1901.ndjson",
    "apps/macos/Tests/SnugCoreTests/Fixtures/uuremoteserver-2000.ndjson",
    "apps/macos/Tests/SnugCoreTests/Fixtures/uuremoteserver-2257.ndjson",
    "apps/windows/README.md", "apps/windows/src/PocketDeskCore/LegacyDataMigration.cs",
    "apps/windows/src/PocketDeskApp/App.xaml.cs", "tools/check_publication.py",
}
# Retained internal Swift module/type names are intentional, not product identity.
MACOS_NATIVE_PREFIXES = ("apps/macos/Sources/", "apps/macos/Tests/")
MACOS_NATIVE_SUFFIXES = {".swift", ".m", ".h"}
# Exact reviewed upstream icon bytes only; this is NOT a binary-directory exemption.
APPROVED_BINARY_SHA256 = {
    "apps/macos/Assets/AppIcon/AppIcon-1024.png": "650577ae9022a88483cc83994b1b27ce5a7c21ddb57e7cd5b20d7f196358fa5f",
    "apps/macos/Assets/AppIcon/AppIcon.icns": "2f862221ebcfded74a015a68f4190848fb81f7dab2341342c4abec4f41c400f1",
}
PATTERNS = {
    "private-key material": re.compile(r"-----BEGIN (?:RSA |EC |OPENSSH |DSA |ENCRYPTED )?PRIVATE KEY-----"),
    "GitHub credential": re.compile(r"(?:gh[pousr]_[A-Za-z0-9]{30,}|github_pat_[A-Za-z0-9_]{30,})"),
    "cloud access key": re.compile(r"\b(?:AKIA|ASIA)[A-Z0-9]{16}\b"),
    "provider API credential": re.compile(r"\bsk-(?:proj-|ant-)?[A-Za-z0-9_-]{24,}\b"),
    "absolute personal directory": re.compile(r"(?i)(?:[a-z]:[\\/]+Users[\\/]+|/Users/|/home/)[A-Za-z0-9_.-]+[\\/]"),
    "embedded URL credentials": re.compile(r"https?://[^\s/:'\"]+:[^\s/@'\"]+@"),
}


def git(*args: str) -> bytes:
    return subprocess.check_output(["git", "-C", str(ROOT), *args], stderr=subprocess.PIPE)


def files(staged: bool):
    if staged:
        for raw in git("ls-files", "--cached", "-z").split(b"\0"):
            if not raw:
                continue
            name = raw.decode("utf-8")
            yield name, git("show", ":" + name)
    else:
        # Honor Git excludes for local files; still inspect tracked forbidden files.
        for raw in git("ls-files", "--cached", "--others", "--exclude-standard", "-z").split(b"\0"):
            if not raw:
                continue
            name = raw.decode("utf-8")
            path = ROOT / name
            if path.is_file():
                yield name, path.read_bytes()


def audit(name: str, data: bytes) -> list[str]:
    path = Path(name)
    problems: list[str] = []
    if name in APPROVED_BINARY_SHA256:
        if hashlib.sha256(data).hexdigest() != APPROVED_BINARY_SHA256[name]:
            return ["approved icon changed; review bytes and update its pinned digest"]
        return []
    if set(path.parts) & IGNORED_PARTS or path.suffix.lower() in FORBIDDEN_SUFFIXES:
        problems.append("generated, binary or signing material must not be published")
    if path.name in RUNTIME_NAMES or (path.name.startswith(".env") or path.name.startswith(".machine.env")):
        problems.append("local runtime state or environment file must not be published")
    if "\0" in data.decode("utf-8", errors="ignore"):
        problems.append("unexpected binary content")
        return problems
    try:
        text = data.decode("utf-8-sig")
    except UnicodeDecodeError:
        return problems + ["file must be reviewable UTF-8 text"]
    native_internal = name.startswith(MACOS_NATIVE_PREFIXES) and path.suffix in MACOS_NATIVE_SUFFIXES
    if re.search(r"snug", name + "\n" + text, re.IGNORECASE) and name not in LEGACY_NAME_ALLOWLIST and not native_internal:
        problems.append("old product name outside attribution/migration allowlist")
    for label, pattern in PATTERNS.items():
        for match in pattern.finditer(text):
            line = text.count("\n", 0, match.start()) + 1
            problems.append(f"line {line}: {label} (value withheld)")
    return problems


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--staged", action="store_true", help="Read EVERY file from the actual Git index, not the worktree")
    args = parser.parse_args()
    failures = []
    count = 0
    try:
        for name, data in files(args.staged):
            count += 1
            failures.extend(f"{name}: {problem}" for problem in audit(name, data))
    except (OSError, subprocess.CalledProcessError, UnicodeDecodeError) as error:
        print(f"Audit could not complete: {type(error).__name__}", file=sys.stderr)
        return 2
    if count == 0:
        print("No files checked; refusing an empty audit.", file=sys.stderr)
        return 2
    if failures:
        print("\n".join(failures), file=sys.stderr)
        return 1
    print(f"PASS: {count} {'index' if args.staged else 'working-tree'} files checked. Run Gitleaks and review the diff too.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
