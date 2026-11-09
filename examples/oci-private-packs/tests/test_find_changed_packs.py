from pathlib import Path
import os
import shutil
import subprocess
import sys
import tempfile
import unittest


SCRIPT = Path(__file__).resolve().parents[1] / "scripts" / "find_changed_packs.py"


class ChangedPackDiscoveryTests(unittest.TestCase):
    def setUp(self) -> None:
        self.temporary_directory = tempfile.TemporaryDirectory()
        self.repository = Path(self.temporary_directory.name)
        self.git("init", "--initial-branch=main")
        self.git("config", "user.name", "LunaPack fixture")
        self.git("config", "user.email", "lunapack-fixture@example.invalid")

    def tearDown(self) -> None:
        self.temporary_directory.cleanup()

    def test_first_push_with_missing_or_zero_before_sha_lists_existing_packs(self) -> None:
        self.add_pack("packs/backend")
        revision = self.commit("initial packs")

        self.assertEqual(self.find_changed_packs("", revision), ["packs/backend"])
        self.assertEqual(self.find_changed_packs("0" * 40, revision), ["packs/backend"])

    def test_multiple_changed_files_and_packs_emit_each_folder_once_with_spaces(self) -> None:
        self.add_pack("packs/backend")
        self.add_pack("packs/security policies")
        before = self.commit("initial packs")
        (self.repository / "packs/backend/content/README.md").write_text(
            "updated backend\n", encoding="utf-8"
        )
        (self.repository / "packs/backend/content/second.md").write_text(
            "another file\n", encoding="utf-8"
        )
        (self.repository / "packs/security policies/content/README.md").write_text(
            "updated security\n", encoding="utf-8"
        )
        after = self.commit("update multiple packs")

        self.assertEqual(
            self.find_changed_packs(before, after),
            ["packs/backend", "packs/security policies"],
        )

    def test_rename_publishes_new_folder_and_deleted_pack_is_skipped(self) -> None:
        self.add_pack("packs/old-name")
        self.add_pack("packs/deleted-pack")
        before = self.commit("initial packs")
        self.git("mv", "packs/old-name", "packs/new name")
        shutil.rmtree(self.repository / "packs/deleted-pack")
        after = self.commit("rename and delete packs")

        self.assertEqual(self.find_changed_packs(before, after), ["packs/new name"])

    def test_merge_commit_uses_the_pushed_tree_difference(self) -> None:
        self.add_pack("packs/backend")
        before = self.commit("initial pack")
        self.git("checkout", "-b", "feature")
        (self.repository / "packs/backend/content/README.md").write_text(
            "feature change\n", encoding="utf-8"
        )
        self.commit("change on feature")
        self.git("checkout", "main")
        self.git("merge", "--no-ff", "--no-edit", "feature")
        after = self.git("rev-parse", "HEAD").decode("ascii").strip()

        self.assertEqual(self.find_changed_packs(before, after), ["packs/backend"])

    def test_squash_commit_publishes_changed_pack_once(self) -> None:
        self.add_pack("packs/backend")
        before = self.commit("initial pack")
        self.git("checkout", "-b", "feature")
        (self.repository / "packs/backend/content/README.md").write_text(
            "first feature change\n", encoding="utf-8"
        )
        self.commit("first change")
        (self.repository / "packs/backend/content/second.md").write_text(
            "second feature change\n", encoding="utf-8"
        )
        self.commit("second change")
        self.git("checkout", "main")
        self.git("merge", "--squash", "feature")
        after = self.commit("squashed changes")

        self.assertEqual(self.find_changed_packs(before, after), ["packs/backend"])

    def test_missing_revision_fails_instead_of_returning_no_packs(self) -> None:
        self.add_pack("packs/backend")
        after = self.commit("initial pack")

        result = subprocess.run(
            [sys.executable, str(SCRIPT), "missing-revision", after],
            cwd=self.repository,
            check=False,
            stdout=subprocess.PIPE,
            stderr=subprocess.PIPE,
        )

        self.assertNotEqual(result.returncode, 0)
        self.assertIn(b"Git could not compare", result.stderr)

    def add_pack(self, relative_directory: str) -> None:
        pack_directory = self.repository / relative_directory
        (pack_directory / "content").mkdir(parents=True)
        (pack_directory / "pack.yml").write_text(
            "id: example-pack\nversion: 1.0.0\n", encoding="utf-8"
        )
        (pack_directory / "content/README.md").write_text("initial\n", encoding="utf-8")

    def find_changed_packs(self, before: str, after: str) -> list[str]:
        result = subprocess.run(
            [sys.executable, str(SCRIPT), before, after],
            cwd=self.repository,
            check=True,
            stdout=subprocess.PIPE,
            stderr=subprocess.PIPE,
        )
        return [
            os.fsdecode(path)
            for path in result.stdout.split(b"\0")
            if path
        ]

    def commit(self, message: str) -> str:
        self.git("add", "-A")
        self.git("commit", "-m", message)
        return self.git("rev-parse", "HEAD").decode("ascii").strip()

    def git(self, *arguments: str) -> bytes:
        result = subprocess.run(
            ["git", *arguments],
            cwd=self.repository,
            check=True,
            stdout=subprocess.PIPE,
            stderr=subprocess.PIPE,
        )
        return result.stdout


if __name__ == "__main__":
    unittest.main()
