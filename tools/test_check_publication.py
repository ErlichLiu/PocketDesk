"""Publication regression tests: native imports are not blanket exemptions."""
from pathlib import Path
import subprocess
import tempfile
import unittest
from unittest.mock import patch

import check_publication as checker


class PublicationTests(unittest.TestCase):
    def test_native_internal_names_are_allowed(self):
        for name in ["apps/macos/Package.swift", "apps/macos/Sources/SnugCore/Example.swift",
                     "apps/macos/Tests/SnugCoreTests/ExampleTests.swift"]:
            with self.subTest(name=name):
                self.assertEqual(checker.audit(name, b"import SnugCore\n"), [])

    def test_windows_legacy_rules_are_not_widened(self):
        self.assertTrue(checker.audit("apps/windows/src/NewProduct.cs", b"class Snug {}"))
        self.assertTrue(checker.audit("apps/macos/unreviewed.txt", b"Snug"))

    def test_approved_icons_are_allowed(self):
        for name in ["AppIcon-1024.png", "AppIcon.icns"]:
            path = "apps/macos/Assets/AppIcon/" + name
            with self.subTest(name=name):
                self.assertEqual(checker.audit(path, (checker.ROOT / path).read_bytes()), [])

    def test_modified_or_unapproved_binary_is_rejected(self):
        path = "apps/macos/Assets/AppIcon/AppIcon.icns"
        self.assertTrue(checker.audit(path, (checker.ROOT / path).read_bytes() + b"extra"))
        self.assertTrue(checker.audit("apps/macos/Assets/unknown.png", b"image\0data"))

    def test_native_source_still_checks_personal_paths_and_credentials(self):
        name = "apps/macos/Sources/SnugCore/Example.swift"
        for value in ["/Users/" + "someone/private/", "ghp_" + "Z" * 40]:
            with self.subTest(kind=value[:4]):
                self.assertTrue(checker.audit(name, value.encode()))

    def test_all_recovery_files_and_native_build_output_are_rejected(self):
        for name in ["apps/macos/last-phone-window.json", "apps/macos/phone-screen.json",
                     "apps/macos/.build/result.txt", "apps/macos/build/result.txt"]:
            with self.subTest(name=name):
                self.assertTrue(checker.audit(name, b"{}"))

    def test_non_utf8_source_remains_rejected(self):
        self.assertTrue(checker.audit("apps/macos/Sources/SnugCore/Example.swift", b"\xff\0"))

    def test_worktree_enumeration_respects_git_ignores_without_skipping_new_files(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            subprocess.run(["git", "init", "-q", str(root)], check=True)
            (root / ".gitignore").write_text("build/\n.env\nAGENTS.md\n")
            (root / "tracked.txt").write_text("tracked")
            subprocess.run(["git", "-C", str(root), "add", "tracked.txt"], check=True)
            (root / "new.txt").write_text("review me")
            (root / "build").mkdir()
            (root / "build/output").write_bytes(b"binary\0")
            (root / ".env").write_text("private local state")
            (root / "AGENTS.md").write_text("local-only map")
            with patch.object(checker, "ROOT", root):
                names = {name for name, _ in checker.files(False)}
            self.assertIn("tracked.txt", names)
            self.assertIn("new.txt", names)
            self.assertFalse(names & {".env", "AGENTS.md", "build/output"})

    def test_native_diagnostic_sources_are_not_ignored_but_windows_outputs_are(self):
        for name in ["Renderer.swift", "RenderSamples.swift", "UUDump.swift", "WindowBenchmark.swift"]:
            with self.subTest(name=name):
                result = subprocess.run(["git", "-C", str(checker.ROOT), "check-ignore", "--no-index",
                                         "apps/macos/Sources/Snug/Debug/" + name], capture_output=True)
                self.assertEqual(result.returncode, 1)
        result = subprocess.run(["git", "-C", str(checker.ROOT), "check-ignore", "--no-index",
                                 "apps/windows/src/Example/Debug/output.txt"], capture_output=True)
        self.assertEqual(result.returncode, 0)

    def test_staged_audit_reads_index_not_worktree(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            subprocess.run(["git", "init", "-q", str(root)], check=True)
            (root / "tracked.txt").write_text("staged")
            subprocess.run(["git", "-C", str(root), "add", "tracked.txt"], check=True)
            (root / "tracked.txt").write_text("different worktree")
            with patch.object(checker, "ROOT", root):
                self.assertEqual(dict(checker.files(True))["tracked.txt"], b"staged")


if __name__ == "__main__":
    unittest.main()
