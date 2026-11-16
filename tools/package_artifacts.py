#!/usr/bin/env python3
"""Development-package metadata and static checks. Never execute packaged apps."""
from __future__ import annotations

import argparse
import hashlib
import json
from pathlib import Path, PurePosixPath
import plistlib
import re
import shutil
import struct
import zipfile

ROOT = Path(__file__).resolve().parents[1]
WINDOWS_RUNTIME_FILES = {"coreclr.dll", "hostfxr.dll", "System.Private.CoreLib.dll", "PresentationFramework.dll"}


def require(condition: bool, message: str):
    if not condition:
        raise ValueError(message)


def prepare(folder: Path, platform: str, version: str, commit: str):
    require(platform in {"windows", "macos"}, "unsupported platform")
    require(bool(re.fullmatch(r"\d+\.\d+\.\d+", version)), "invalid version")
    require(bool(re.fullmatch(r"[0-9a-f]{40}", commit)), "invalid source commit")
    folder.mkdir(parents=True, exist_ok=True)
    require(not folder.is_symlink(), "package root cannot be a symlink")
    licenses = folder / "licenses"
    licenses.mkdir(exist_ok=True)
    for name in ["LICENSE", "NOTICE"]:
        shutil.copyfile(ROOT / name, folder / name)
    shutil.copyfile(ROOT / "licenses/snug-MIT.txt", licenses / "snug-MIT.txt")
    if platform == "macos":
        shutil.copyfile(ROOT / "apps/macos/Vendor/PermissionFlow/LICENSE", licenses / "PermissionFlow-MIT.txt")
    info = {
        "schema": 1, "product": "PocketDesk", "platform": platform, "version": version,
        "architecture": "x64" if platform == "windows" else "universal",
        "source_repository": "https://github.com/ErlichLiu/PocketDesk", "source_commit": commit,
        "development_build": True, "signed": "unsigned" if platform == "windows" else "ad-hoc",
        "notarized": False, "self_contained": platform == "windows",
    }
    (folder / "BUILD-INFO.json").write_text(json.dumps(info, indent=2) + "\n", encoding="utf-8")
    instructions = (
        "Extract this entire directory and run PocketDesk.exe. The .NET runtime is included.\n"
        "Windows 10/11 x64 and an independently installed working virtual display are required.\n"
        "No driver or installer is bundled; state is kept in your LocalAppData, not beside the executable.\n"
        "This executable is unsigned; Windows may show a reputation warning. Do not disable security protections.\n"
        if platform == "windows" else
        "Extract the inner ZIP, then deliberately move/open PocketDesk.app. macOS 14+ is required.\n"
        "The universal app includes Apple Silicon and Intel slices. It is ad-hoc signed, NOT notarized.\n"
        "macOS may block an unidentified app; review the source and use normal Privacy & Security controls.\n"
        "Never disable Gatekeeper or SIP. The current app needs Accessibility/device-control permission.\n"
    )
    (folder / "README.txt").write_text(
        "PocketDesk experimental development package\n\n" + instructions +
        "\nClose an existing instance safely before switching builds; preserve recovery state.\n"
        "Launching can monitor/control real displays and windows. Packaging did not launch or install the app.\n"
        "This is an Actions artifact, not a signed production release or proof of phone-side usability.\n"
        "Source version and exact commit are recorded in BUILD-INFO.json. Licenses are included.\n",
        encoding="utf-8")


def verify(archive: Path, platform: str) -> dict:
    with zipfile.ZipFile(archive) as data:
        require(data.testzip() is None, "archive CRC failure")
        names = data.namelist()
        require(len(names) == len(set(names)), "duplicate archive paths")
        for name in names:
            parts = PurePosixPath(name).parts
            require(not name.startswith("/") and ".." not in parts and "\\" not in name and ":" not in name,
                    "unsafe archive path")
        manifests = [n for n in names if n.endswith("/BUILD-INFO.json") and not n.startswith("__MACOSX/")]
        require(len(manifests) == 1, "expected one package manifest")
        prefix = manifests[0].removesuffix("BUILD-INFO.json")
        info = json.loads(data.read(manifests[0]))
        require(info.get("schema") == 1 and info.get("product") == "PocketDesk", "invalid package manifest")
        require(info.get("platform") == platform, "wrong platform")
        require(bool(re.fullmatch(r"[0-9a-f]{40}", info.get("source_commit", ""))), "missing provenance")
        require(info.get("development_build") is True and info.get("notarized") is False, "invalid signing claims")

        def read(name: str) -> bytes:
            require(prefix + name in names, "missing package file: " + name)
            return data.read(prefix + name)

        for name in ["LICENSE", "NOTICE", "README.txt", "licenses/snug-MIT.txt"]:
            require(bool(read(name)), "empty license or instructions")
        if platform == "windows":
            require(info.get("architecture") == "x64" and info.get("self_contained") is True, "not self-contained x64")
            exe = read("PocketDesk.exe")
            require(len(exe) >= 128 and exe[:2] == b"MZ", "not a Windows executable")
            offset = struct.unpack_from("<I", exe, 0x3C)[0]
            require(offset + 6 <= len(exe) and exe[offset:offset+4] == b"PE\0\0", "invalid PE header")
            require(struct.unpack_from("<H", exe, offset + 4)[0] == 0x8664, "wrong Windows architecture")
            for name in WINDOWS_RUNTIME_FILES:
                require(bool(read(name)), "missing runtime")
            runtime = json.loads(read("PocketDesk.runtimeconfig.json"))["runtimeOptions"]
            included = {item["name"] for item in runtime.get("includedFrameworks", [])}
            require({"Microsoft.NETCore.App", "Microsoft.WindowsDesktop.App"} <= included, "runtime is framework-dependent")
            for name in ["dotnet-core", "dotnet-desktop"]:
                require(bool(read("licenses/" + name + "/LICENSE.TXT")), "missing runtime license")
        elif platform == "macos":
            require(info.get("architecture") == "universal" and info.get("signed") == "ad-hoc", "not an ad-hoc universal app")
            require(bool(read("licenses/PermissionFlow-MIT.txt")), "missing dependency license")
            plist = plistlib.loads(read("PocketDesk.app/Contents/Info.plist"))
            require(plist.get("CFBundleIdentifier") == "cool.proma.pocketdesk", "wrong bundle identity")
            require(plist.get("CFBundleShortVersionString") == info.get("version"), "version mismatch")
            executable = "PocketDesk.app/Contents/MacOS/PocketDesk"
            require(bool((data.getinfo(prefix + executable).external_attr >> 16) & 0o111), "executable mode lost")
            binary = read(executable)
            require(len(binary) >= 48 and binary[:4] == b"\xca\xfe\xba\xbe", "not a universal Mach-O")
            count = struct.unpack_from(">I", binary, 4)[0]
            require(count == 2 and len(binary) >= 8 + count * 20, "invalid universal header")
            cpus = {struct.unpack_from(">I", binary, 8 + i * 20)[0] for i in range(count)}
            require(cpus == {0x01000007, 0x0100000C}, "missing Intel or Apple Silicon slice")
        else:
            raise ValueError("unsupported platform")
        return info


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    sub = parser.add_subparsers(dest="command", required=True)
    prep = sub.add_parser("prepare")
    prep.add_argument("folder", type=Path)
    prep.add_argument("platform", choices=["windows", "macos"])
    prep.add_argument("version")
    prep.add_argument("commit")
    check = sub.add_parser("verify")
    check.add_argument("archive", type=Path)
    check.add_argument("platform", choices=["windows", "macos"])
    digest = sub.add_parser("checksum")
    digest.add_argument("archive", type=Path)
    args = parser.parse_args()
    if args.command == "prepare":
        prepare(args.folder, args.platform, args.version, args.commit)
    elif args.command == "verify":
        print(json.dumps(verify(args.archive, args.platform), sort_keys=True))
    else:
        with args.archive.open("rb") as stream:
            value = hashlib.file_digest(stream, "sha256").hexdigest()
        line = value + "  " + args.archive.name + "\n"
        args.archive.with_suffix(args.archive.suffix + ".sha256").write_text(line, encoding="utf-8")
        print(line, end="")


if __name__ == "__main__":
    main()
