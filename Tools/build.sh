#!/usr/bin/env bash
set -euo pipefail
project_root="$(cd "$(dirname "$0")/.." && pwd)"
unity_editor="${UNITY_EDITOR:-/Applications/Unity/Hub/Editor/6000.5.5f1-arm64/Unity.app/Contents/MacOS/Unity}"
project_path="${MIA_PROJECT_PATH:-$project_root}"
case "${1:-all}" in
  mac) method=BuildMac ;;
  windows) method=BuildWindows ;;
  all) method=BuildAll ;;
  prepare) method=Prepare ;;
  validate) method=Validate ;;
  *) echo 'Usage: build.sh [mac|windows|all|prepare|validate]' >&2; exit 2 ;;
esac
mkdir -p "$project_root/Builds"
export MIA_BUILD_ROOT="${MIA_BUILD_ROOT:-$project_root}"
build_status=0
"$unity_editor" -batchmode -nographics -quit -projectPath "$project_path" -executeMethod "MadeInArizona.Editor.BuildGame.$method" -logFile "$project_root/Builds/unity-$method.log" || build_status=$?
if [[ "$method" == Build* ]]; then
  python3 "$project_root/Tools/cleanup_builds.py" --apply || echo 'Local build cleanup failed. See Docs/UPDATES.md.' >&2
fi
exit "$build_status"
