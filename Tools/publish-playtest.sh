#!/usr/bin/env bash
# Builds both players on this Mac and publishes them as a GitHub Release that the in-game updater
# (Versions & Updates) can install. Requires Unity (see build.sh) and the GitHub CLI (`brew install gh`, `gh auth login`).
# SKIP_BUILD=1 publishes the players already in Builds/ (they must have been built from this commit).
set -euo pipefail
root="$(cd "$(dirname "$0")/.." && pwd)"
cd "$root"
command -v gh >/dev/null || { echo "Install the GitHub CLI (brew install gh) and run: gh auth login" >&2; exit 1; }
if [ -n "$(git status --porcelain -- Assets ProjectSettings Packages)" ]; then
  echo "Commit your changes first: a published build must match a commit on GitHub." >&2; exit 1
fi
commit="$(git rev-parse HEAD)"; short="${commit:0:7}"; tag="build-$short"
git fetch -q origin
if [ -z "$(git branch -r --contains "$commit")" ]; then echo "Push $short to GitHub first." >&2; exit 1; fi
if gh release view "$tag" >/dev/null 2>&1; then echo "$tag is already published."; exit 0; fi

[ "${SKIP_BUILD:-0}" = 1 ] || "$root/Tools/build.sh" all

dist="$root/Builds/dist"; rm -rf "$dist"; mkdir -p "$dist"
# ditto keeps the bundle's permissions and symlinks; --keepParent puts the .app at the zip root.
ditto -c -k --sequesterRsrc --keepParent "$root/Builds/macOS/Made in Arizona.app" "$dist/MadeInArizona-macOS.zip"
(cd "$root/Builds/Windows" && zip -q -r -X "$dist/MadeInArizona-Windows.zip" . -x '*_BurstDebugInformation_DoNotShip*' '*_BackUpThisFolder_ButDontShipItWithYourGame*')

subject="$(git log -1 --format=%s)"
protocol="$(grep -o 'Protocol = [0-9]*' Assets/MadeInArizona/Scripts/Core/GameUpdater.cs | head -n 1 | grep -o '[0-9]*')"
printf '%s\n\nmia-updater: %s\ncommit: %s\n' "$subject" "$protocol" "$commit" > "$dist/notes.md"
gh release create "$tag" "$dist/MadeInArizona-Windows.zip" "$dist/MadeInArizona-macOS.zip" \
  --target "$commit" --title "Playtest $short • $subject" --notes-file "$dist/notes.md"
echo "Published $tag. Testers see it under Versions & Updates."
