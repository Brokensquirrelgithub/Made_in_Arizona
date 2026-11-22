"""Prune generated local build archives while retaining recent rollback copies."""

import argparse
import json
import math
from pathlib import Path
import re
import shutil
import stat
import time


BUILD_ROOT = Path(__file__).resolve().parents[1] / "Builds"
KEEP = 3
GRACE_SECONDS = 24 * 60 * 60
RELEASE_FOLDER = re.compile(r"build-[0-9a-f]{7,40}")
HOTFIX_FOLDER = re.compile(r"[0-9a-f]{32}")


def directory_info(path):
    """Only recognize real directories, including on Windows with junctions."""
    try:
        info = path.lstat()
    except FileNotFoundError:
        return None
    if stat.S_ISDIR(info.st_mode) and not (
        getattr(info, "st_file_attributes", 0) & stat.FILE_ATTRIBUTE_REPARSE_POINT
    ):
        return info
    return None


def hotfix_finished_at(path):
    try:
        status_path = path / "status.json"
        info = status_path.lstat()
        if not stat.S_ISREG(info.st_mode) or (
            getattr(info, "st_file_attributes", 0) & stat.FILE_ATTRIBUTE_REPARSE_POINT
        ):
            return None
        status = json.loads(status_path.read_text(encoding="utf-8"))
        timestamp = status.get("updatedAt")
        if (status.get("phase") in ("complete", "failed")
                and isinstance(timestamp, (int, float)) and math.isfinite(timestamp) and timestamp > 0):
            return timestamp
    except (OSError, ValueError, OverflowError, AttributeError):
        pass  # A missing or unreadable job status is treated as an active job.
    return None


def expired_builds(root, now):
    if directory_info(root) is None:
        return
    for folder, name_pattern in (("archive", None), ("dist", RELEASE_FOLDER), ("hotfix-jobs", HOTFIX_FOLDER)):
        group = root / folder
        if directory_info(group) is None:
            continue
        candidates = []
        for entry in group.iterdir():
            if name_pattern and not name_pattern.fullmatch(entry.name):
                continue
            info = directory_info(entry)
            if info is None:
                continue
            timestamp = hotfix_finished_at(entry) if folder == "hotfix-jobs" else info.st_mtime
            if timestamp is not None:
                candidates.append((timestamp, entry))
        for timestamp, entry in sorted(candidates, reverse=True)[KEEP:]:
            if now - timestamp >= GRACE_SECONDS:
                yield entry


def cleanup(root, apply=False):
    expired = list(expired_builds(root, time.time()))
    if not expired:
        print("No expired local build archives.")
    for entry in expired:
        if apply:
            if (directory_info(entry) is None or directory_info(entry.parent) is None
                    or directory_info(root) is None or entry.resolve() != entry):
                raise OSError(f"Build archive path changed during cleanup: {entry}")
            shutil.rmtree(entry)
        print(f"{'Removed' if apply else 'Would remove'} {entry.relative_to(root.parent)}")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--apply", action="store_true", help="Delete expired archives. The default previews the paths.")
    args = parser.parse_args()
    try:
        cleanup(BUILD_ROOT, args.apply)
    except OSError as error:
        parser.exit(1, f"Build cleanup failed: {error}\n")


if __name__ == "__main__":
    main()
