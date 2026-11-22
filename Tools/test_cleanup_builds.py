import contextlib
import io
import json
import os
from pathlib import Path
import tempfile
import time
import unittest

from cleanup_builds import GRACE_SECONDS, cleanup, expired_builds


class CleanupBuildsTests(unittest.TestCase):
    def setUp(self):
        directory = tempfile.TemporaryDirectory()
        self.addCleanup(directory.cleanup)
        self.root = Path(directory.name).resolve() / "Builds"
        self.now = time.time()

    def build(self, group, name, age):
        path = self.root / group / name
        path.mkdir(parents=True)
        (path / "payload").write_text("build", encoding="utf-8")
        timestamp = self.now - age
        os.utime(path, (timestamp, timestamp))
        return path

    def job(self, index, phase, age):
        path = self.build("hotfix-jobs", f"{index:032x}", 0)
        status = {"phase": phase, "updatedAt": self.now - age}
        (path / "status.json").write_text(json.dumps(status), encoding="utf-8")
        return path

    def test_preview_and_apply_only_expired_archives(self):
        for index in range(4):
            self.build("archive", f"snapshot-{index}", 60 + index)
            self.build("dist", f"build-{index:07x}", GRACE_SECONDS + index)
        stale_archive = self.build("archive", "stale", GRACE_SECONDS + 60)
        stale_release = self.root / "dist" / "build-0000003"
        custom = self.build("dist", "test-evidence", GRACE_SECONDS + 60)
        player = self.build("Windows", "Made in Arizona_Data", GRACE_SECONDS + 60)

        printed = io.StringIO()
        with contextlib.redirect_stdout(printed):
            cleanup(self.root)
        self.assertEqual(printed.getvalue().count("Would remove"), 2)
        self.assertTrue(stale_archive.exists() and stale_release.exists())

        with contextlib.redirect_stdout(io.StringIO()):
            cleanup(self.root, apply=True)
        self.assertFalse(stale_archive.exists())
        self.assertFalse(stale_release.exists())
        self.assertTrue(custom.exists() and player.exists())
        self.assertEqual(len(list((self.root / "archive").iterdir())), 4)

    def test_hotfix_cleanup_requires_terminal_status(self):
        for index in range(3):
            self.job(index, "complete", 60 + index)
        expired = self.job(3, "failed", GRACE_SECONDS + 1)
        self.job(4, "publishing", GRACE_SECONDS + 600)
        malformed = self.job(5, "failed", GRACE_SECONDS + 600)
        (malformed / "status.json").write_text("[]", encoding="utf-8")
        self.build("hotfix-jobs", f"{6:032x}", GRACE_SECONDS + 600)
        self.assertEqual(list(expired_builds(self.root, self.now)), [expired])

    def test_symlink_to_external_directory_is_ignored(self):
        for index in range(3):
            self.build("archive", f"snapshot-{index}", 60)
        external = self.build("private", "outside-archive", GRACE_SECONDS + 60)
        link = self.root / "archive" / "link"
        try:
            link.symlink_to(external, target_is_directory=True)
        except (NotImplementedError, OSError) as error:
            self.skipTest(f"Cannot create directory symlink: {error}")
        self.assertEqual(list(expired_builds(self.root, self.now)), [])
        self.assertTrue((external / "payload").is_file())


if __name__ == "__main__":
    unittest.main()
