#!/usr/bin/env python3
"""Refuse to publish players whose embedded build identity differs from the release."""

import argparse
import json
import re
import sys
from pathlib import Path
from zipfile import ZipFile


def release_notes(path: Path) -> tuple[int, str]:
    lines = path.read_text(encoding="utf-8-sig").splitlines()
    fields = {}
    for line in lines:
        key, separator, value = line.partition(":")
        if separator and key.strip().lower() in {"mia-updater", "commit"}:
            fields[key.strip().lower()] = value.strip()
    return int(fields["mia-updater"]), fields["commit"]


def check_archive(path: Path, stamp_path: str, platform: str, commit: str, protocol: int) -> None:
    with ZipFile(path) as archive:
        matches = [name for name in archive.namelist() if name.endswith("StreamingAssets/build.json")]
        if matches != [stamp_path]:
            raise ValueError(f"{path}: expected one stamp at {stamp_path}; found {matches}")
        stamp = json.loads(archive.read(stamp_path).decode("utf-8-sig"))
    if stamp.get("commit", "").lower() != commit.lower():
        raise ValueError(f"{path}: embedded commit {stamp.get('commit')!r} differs from release {commit}")
    if stamp.get("platform") != platform or stamp.get("protocol") != protocol:
        raise ValueError(f"{path}: embedded platform or updater protocol differs from release")
    print(f"{path.name}: {platform} {commit[:7]} protocol {protocol}")


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--commit", required=True)
    parser.add_argument("--windows", required=True, type=Path)
    parser.add_argument("--macos", required=True, type=Path)
    parser.add_argument("--notes", required=True, type=Path)
    args = parser.parse_args()
    if not re.fullmatch(r"[0-9a-fA-F]{40}", args.commit):
        raise ValueError("Release commit must be a full 40-character SHA")
    protocol, noted_commit = release_notes(args.notes)
    if protocol < 1 or noted_commit.lower() != args.commit.lower():
        raise ValueError("Release notes protocol or commit differs from the release")
    check_archive(args.windows, "Made in Arizona_Data/StreamingAssets/build.json", "Windows", args.commit, protocol)
    check_archive(args.macos, "Made in Arizona.app/Contents/Resources/Data/StreamingAssets/build.json", "macOS", args.commit, protocol)
    return 0


if __name__ == "__main__":
    try:
        sys.exit(main())
    except (OSError, KeyError, ValueError) as error:
        print(f"Release identity check failed: {error}", file=sys.stderr)
        sys.exit(1)
