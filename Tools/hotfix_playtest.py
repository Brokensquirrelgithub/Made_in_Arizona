"""Build and publish a verified two-platform playtest update for the MCP bridge.

Unity player C# cannot be replaced in memory. This job publishes both players so
their opt-in bridges can install the new build after leaving the co-op room.
"""

import json
import os
from pathlib import Path
import re
import shutil
import stat
import subprocess
import sys
import time
from uuid import uuid4
import zipfile


ROOT = Path(__file__).resolve().parent.parent
JOBS = ROOT / "Builds" / "hotfix-jobs"


def _run(args, *, output=None, check=True):
    return subprocess.run(args, cwd=ROOT, text=True, stdout=output or subprocess.PIPE,
                          stderr=subprocess.STDOUT, check=check)


def _git(*args):
    return _run(["git", *args]).stdout.strip()


def _state(folder: Path, phase: str, **fields):
    payload = {"jobId": folder.name, "phase": phase, "updatedAt": time.time(), **fields}
    temporary = folder / "status.tmp"
    temporary.write_text(json.dumps(payload, indent=2), encoding="utf-8")
    temporary.replace(folder / "status.json")


def _zip_tree(source: Path, archive: Path, prefix: str = ""):
    """Keep Unix modes and symlinks so the macOS app still launches after update."""
    with zipfile.ZipFile(archive, "w", zipfile.ZIP_DEFLATED, compresslevel=6,
                         allowZip64=True) as output:
        for path in sorted(source.rglob("*")):
            relative = path.relative_to(source).as_posix()
            if "_BurstDebugInformation_DoNotShip" in relative or "_BackUpThisFolder_ButDontShipItWithYourGame" in relative:
                continue
            name = prefix + relative
            mode = path.lstat().st_mode
            # Windows filesystems do not carry the executable bit that a macOS app needs.
            if not path.is_dir() and not path.is_symlink() and (
                relative.startswith("Contents/MacOS/") or path.suffix == ".sh"
            ):
                mode = (mode & ~0o777) | 0o755
            info = zipfile.ZipInfo(name + ("/" if path.is_dir() else ""))
            info.create_system = 3
            info.compress_type = zipfile.ZIP_DEFLATED
            info.external_attr = (mode & 0xFFFF) << 16
            if path.is_symlink():
                output.writestr(info, os.readlink(path))
            elif path.is_dir():
                output.writestr(info, b"")
            else:
                with path.open("rb") as content, output.open(info, "w", force_zip64=True) as target:
                    shutil.copyfileobj(content, target, 1024 * 1024)


def start_hotfix_job():
    dirty = _git("status", "--porcelain", "--untracked-files=no")
    if dirty:
        return {"ok": False, "message": "Commit tracked changes before publishing a code hotfix", "dirty": dirty[:1000]}
    commit = _git("rev-parse", "HEAD")
    if not _git("branch", "-r", "--contains", commit):
        return {"ok": False, "message": "Push the commit before publishing a code hotfix", "commit": commit}
    for folder in JOBS.glob("*/status.json") if JOBS.exists() else []:
        try:
            status = json.loads(folder.read_text(encoding="utf-8"))
            if status.get("phase") not in ("complete", "failed") and time.time() - status.get("updatedAt", 0) < 3600:
                return {"ok": False, "message": "A code hotfix job is already active", "jobId": status.get("jobId")}
        except (OSError, ValueError):
            continue
    job_id = uuid4().hex
    folder = JOBS / job_id
    folder.mkdir(parents=True)
    _state(folder, "queued", commit=commit)
    creationflags = subprocess.CREATE_NO_WINDOW if os.name == "nt" else 0
    subprocess.Popen([sys.executable, str(Path(__file__).resolve()), "worker", job_id],
                     cwd=ROOT, stdin=subprocess.DEVNULL, stdout=subprocess.DEVNULL,
                     stderr=subprocess.DEVNULL, creationflags=creationflags)
    return {"ok": True, "jobId": job_id, "commit": commit,
            "message": "Build and publish started; poll code_hotfix_status"}


def hotfix_job_status(job_id: str):
    if not re.fullmatch(r"[0-9a-f]{32}", job_id):
        return {"ok": False, "message": "Invalid job ID"}
    folder = JOBS / job_id
    status_file = folder / "status.json"
    if not status_file.exists():
        return {"ok": False, "message": "Unknown job ID"}
    status = json.loads(status_file.read_text(encoding="utf-8"))
    log = folder / "build.log"
    if log.exists():
        with log.open("rb") as stream:
            stream.seek(max(0, log.stat().st_size - 10000))
            status["logTail"] = stream.read().decode("utf-8", errors="replace")[-6000:]
    status["ok"] = status["phase"] != "failed"
    return status


def _worker(job_id: str):
    folder = JOBS / job_id
    commit = _git("rev-parse", "HEAD")
    log_path = folder / "build.log"
    try:
        with log_path.open("w", encoding="utf-8") as log:
            _state(folder, "building", commit=commit)
            if os.name == "nt":
                command = ["powershell", "-NoProfile", "-ExecutionPolicy", "Bypass", "-File",
                           str(ROOT / "Tools" / "build.ps1"), "-Target", "All"]
                if os.environ.get("MIA_UNITY_EDITOR"):
                    command += ["-UnityEditor", os.environ["MIA_UNITY_EDITOR"]]
            else:
                command = ["bash", str(ROOT / "Tools" / "build.sh"), "all"]
            _run(command, output=log)
            windows = ROOT / "Builds" / "Windows"
            mac = ROOT / "Builds" / "macOS" / "Made in Arizona.app"
            if not (windows / "Made in Arizona.exe").is_file() or not mac.is_dir():
                raise RuntimeError("Both platform players are required for a code hotfix")
            _state(folder, "packaging", commit=commit)
            dist = folder / "dist"
            dist.mkdir()
            windows_zip = dist / "MadeInArizona-Windows.zip"
            mac_zip = dist / "MadeInArizona-macOS.zip"
            _zip_tree(windows, windows_zip)
            _zip_tree(mac, mac_zip, "Made in Arizona.app/")
            protocol_source = (ROOT / "Assets/MadeInArizona/Scripts/Core/GameUpdater.cs").read_text(encoding="utf-8")
            protocol = re.search(r"const int Protocol = (\d+)", protocol_source).group(1)
            subject = _git("log", "-1", "--format=%s")
            notes = dist / "notes.md"
            notes.write_text(f"{subject}\n\nmia-updater: {protocol}\ncommit: {commit}\n", encoding="utf-8")
            _state(folder, "verifying", commit=commit)
            _run([sys.executable, str(ROOT / "Tools/verify-release-stamps.py"), "--commit", commit,
                  "--windows", str(windows_zip), "--macos", str(mac_zip), "--notes", str(notes)], output=log)
            tag = "build-" + commit[:7]
            _state(folder, "publishing", commit=commit, tag=tag)
            existing = _run(["gh", "release", "view", tag, "--json", "url"], check=False)
            if existing.returncode == 0:
                url = json.loads(existing.stdout)["url"]
            else:
                result = _run(["gh", "release", "create", tag, str(windows_zip), str(mac_zip),
                               "--target", commit, "--title", f"Playtest {commit[:7]} • {subject}",
                               "--notes-file", str(notes)], output=log)
                url = f"https://github.com/Brokensquirrelgithub/Made_in_Arizona/releases/tag/{tag}"
            _state(folder, "complete", commit=commit, tag=tag, releaseUrl=url)
    except Exception as error:
        with log_path.open("a", encoding="utf-8") as log:
            log.write("\nHOTFIX FAILED: " + str(error) + "\n")
        _state(folder, "failed", commit=commit, error=str(error))


if __name__ == "__main__" and len(sys.argv) == 3 and sys.argv[1] == "worker":
    _worker(sys.argv[2])
