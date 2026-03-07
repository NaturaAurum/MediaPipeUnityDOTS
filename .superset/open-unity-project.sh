#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd -P)"
. "$SCRIPT_DIR/unity-common.sh"

WORKSPACE_PROJECT_PATH="$(unity_workspace_project_path)"
EDITOR_VERSION="$(unity_editor_version)"
EDITOR_PATH="$(unity_find_editor "$EDITOR_VERSION" || true)"

unity_assert_project_exists "$WORKSPACE_PROJECT_PATH"

if [ -z "$EDITOR_PATH" ]; then
  printf '[superset/unity] Unity %s is not installed in a known location.\n' "$EDITOR_VERSION" >&2
  printf '[superset/unity] Install the matching editor in Unity Hub or set UNITY_EDITOR_PATH.\n' >&2
  printf '[superset/unity] Project path: %s\n' "$WORKSPACE_PROJECT_PATH" >&2
  exit 1
fi

if [ -d "$EDITOR_PATH" ]; then
  printf '[superset/unity] launching %s\n' "$EDITOR_PATH"
  open -na "$EDITOR_PATH" --args -projectPath "$WORKSPACE_PROJECT_PATH"
  exit 0
fi

LOG_FILE="${TMPDIR:-/tmp}/superset-unity-$(date +%Y%m%d-%H%M%S).log"
printf '[superset/unity] launching %s\n' "$EDITOR_PATH"
printf '[superset/unity] log file: %s\n' "$LOG_FILE"
nohup "$EDITOR_PATH" -projectPath "$WORKSPACE_PROJECT_PATH" >"$LOG_FILE" 2>&1 &
