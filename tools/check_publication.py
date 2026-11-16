#!/usr/bin/env python3
"""Fail closed on unsuitable public files. Supplement, never replace, Gitleaks/review."""
from __future__ import annotations

import argparse
from pathlib import Path
import re
import subprocess
import sys

ROOT = Path(__file__).resolve().parents[1]
IGNORED_PARTS = {".git", "bin", "obj", "artifacts", "app-build", "__pycache__", ".vs", "x64", "x86"}
FORBIDDEN_SUFFIXES = {
    ".key", ".pem", ".pfx", ".p12", ".cer", ".crt", ".p7b", ".snk", ".cat",
    ".dll", ".exe", ".sys", ".pdb", ".zip", ".dmp", ".log", ".mobileprovision",
}
RUNTIME_NAMES = {"config.json", "phone-screen.json", "zoom.json", "mcp.json", "credentials.json"}
# The old project name is allowed ONLY for attribution and deliberate compatibility.
LEGACY_NAME_ALLOWLIST = {
    "NOTICE", "README.md", "README.zh-CN.md", "licenses/snug-MIT.txt",
    "apps/windows/README.md", "apps/windows/src/PocketDeskCore/LegacyDataMigration.cs",
    "apps/windows/src/PocketDeskApp/App.xaml.cs", "tools/check_publication.py",
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
        for path in sorted(ROOT.rglob("*")):
            rel = path.relative_to(ROOT)
            if path.is_file() and not set(rel.parts) & IGNORED_PARTS:
                yield rel.as_posix(), path.read_bytes()


def audit(name: str, data: bytes) -> list[str]:
    path = Path(name)
    problems: list[str] = []
    if set(path.parts) & IGNORED_PARTS or path.suffix.lower() in FORBIDDEN_SUFFIXES:
        problems.append("generated, binary or signing material must not be published")
    if path.name in RUNTIME_NAMES or path.name.startswith(".env"):
        problems.append("local runtime state or environment file must not be published")
    if "\0" in data.decode("utf-8", errors="ignore"):
        problems.append("unexpected binary content")
        return problems
    try:
        text = data.decode("utf-8-sig")
    except UnicodeDecodeError:
        return problems + ["file must be reviewable UTF-8 text"]
    if re.search(r"snug", name + "\n" + text, re.IGNORECASE) and name not in LEGACY_NAME_ALLOWLIST:
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
