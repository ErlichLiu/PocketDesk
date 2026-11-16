import json
from pathlib import Path
import plistlib
import struct
import tempfile
import unittest
import zipfile

import package_artifacts as packages


class PackageTests(unittest.TestCase):
    def archive(self, platform, mutate=None, executable=True):
        temp = tempfile.TemporaryDirectory()
        self.addCleanup(temp.cleanup)
        root = Path(temp.name) / "PocketDesk-package"
        root.mkdir()
        packages.prepare(root, platform, "0.2.4", "a" * 40)
        if platform == "windows":
            exe = bytearray(128)
            exe[:2] = b"MZ"
            struct.pack_into("<I", exe, 0x3C, 64)
            exe[64:68] = b"PE\0\0"
            struct.pack_into("<H", exe, 68, 0x8664)
            (root / "PocketDesk.exe").write_bytes(exe)
            for name in packages.WINDOWS_RUNTIME_FILES:
                (root / name).write_bytes(b"runtime")
            (root / "PocketDesk.runtimeconfig.json").write_text(json.dumps({"runtimeOptions": {
                "includedFrameworks": [{"name": "Microsoft.NETCore.App"}, {"name": "Microsoft.WindowsDesktop.App"}]}}))
            for name in ["dotnet-core", "dotnet-desktop"]:
                folder = root / "licenses" / name
                folder.mkdir()
                (folder / "LICENSE.TXT").write_text("runtime license")
        else:
            content = root / "PocketDesk.app" / "Contents"
            (content / "MacOS").mkdir(parents=True)
            (content / "Info.plist").write_bytes(plistlib.dumps({
                "CFBundleIdentifier": "cool.proma.pocketdesk", "CFBundleShortVersionString": "0.2.4",
                "LSMinimumSystemVersion": "14.0"}))
            binary = content / "MacOS" / "PocketDesk"
            binary.write_bytes(b"\xca\xfe\xba\xbe" + struct.pack(">I", 2) +
                               struct.pack(">IIIII", 0x01000007, 0, 0, 0, 0) +
                               struct.pack(">IIIII", 0x0100000C, 0, 0, 0, 0))
            binary.chmod(0o755)
        if mutate:
            mutate(root)
        archive = Path(temp.name) / "package.zip"
        with zipfile.ZipFile(archive, "w") as output:
            for path in root.rglob("*"):
                if path.is_file():
                    name = path.relative_to(root.parent).as_posix()
                    if platform == "macos" and name.endswith("/Contents/MacOS/PocketDesk"):
                        # Fixtures must carry Unix mode even when these tests run on Windows.
                        info = zipfile.ZipInfo.from_file(path, name)
                        info.create_system = 3
                        info.external_attr = (0o100755 if executable else 0o100644) << 16
                        output.writestr(info, path.read_bytes())
                    else:
                        output.write(path, name)
        return archive

    def test_windows_and_macos_packages(self):
        for platform in ["windows", "macos"]:
            with self.subTest(platform=platform):
                self.assertEqual(packages.verify(self.archive(platform), platform)["platform"], platform)

    def test_windows_requires_its_runtime(self):
        archive = self.archive("windows", lambda root: (root / "coreclr.dll").unlink())
        with self.assertRaises(ValueError): packages.verify(archive, "windows")

    def test_windows_requires_runtime_license(self):
        archive = self.archive("windows", lambda root: (root / "licenses/dotnet-core/LICENSE.TXT").unlink())
        with self.assertRaises(ValueError): packages.verify(archive, "windows")

    def test_macos_requires_both_architectures(self):
        archive = self.archive("macos", lambda root: (root / "PocketDesk.app/Contents/MacOS/PocketDesk").write_bytes(b"thin"))
        with self.assertRaises(ValueError): packages.verify(archive, "macos")

    def test_macos_requires_executable_permissions(self):
        archive = self.archive("macos", executable=False)
        with self.assertRaises(ValueError): packages.verify(archive, "macos")

    def test_wrong_platform_and_unsafe_paths_fail(self):
        archive = self.archive("windows")
        with self.assertRaises(ValueError): packages.verify(archive, "macos")
        with zipfile.ZipFile(archive, "a") as output: output.writestr("../escape", "unsafe")
        with self.assertRaises(ValueError): packages.verify(archive, "windows")

    def test_prepare_rejects_invalid_provenance(self):
        with tempfile.TemporaryDirectory() as folder:
            with self.assertRaises(ValueError): packages.prepare(Path(folder), "windows", "bad", "bad")


if __name__ == "__main__":
    unittest.main()
