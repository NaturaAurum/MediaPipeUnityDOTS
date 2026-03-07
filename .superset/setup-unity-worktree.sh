#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd -P)"
. "$SCRIPT_DIR/unity-common.sh"

WORKSPACE_PROJECT_PATH="$(unity_workspace_project_path)"
ROOT_PROJECT_PATH="$(unity_repo_project_path)"
UNITY_COPY_DIRS="${UNITY_COPY_DIRS:-Library UserSettings}"

unity_assert_project_exists "$WORKSPACE_PROJECT_PATH"
unity_assert_project_exists "$ROOT_PROJECT_PATH"

if [ "$WORKSPACE_PROJECT_PATH" = "$ROOT_PROJECT_PATH" ]; then
  printf '[superset/unity] root repository detected; skipping cache copy.\n'
  exit 0
fi

copy_if_missing() {
  local dir_name="$1"
  local source_dir="$ROOT_PROJECT_PATH/$dir_name"
  local target_dir="$WORKSPACE_PROJECT_PATH/$dir_name"

  if [ ! -d "$source_dir" ]; then
    printf '[superset/unity] skip %s: source not found at %s\n' "$dir_name" "$source_dir"
    return 0
  fi

  if [ -d "$target_dir" ] && find "$target_dir" -mindepth 1 -print -quit | grep -q .; then
    printf '[superset/unity] skip %s: workspace already contains data\n' "$dir_name"
    return 0
  fi

  mkdir -p "$target_dir"

  if command -v rsync >/dev/null 2>&1; then
    rsync -a "$source_dir/" "$target_dir/"
  else
    cp -R "$source_dir/." "$target_dir/"
  fi

  printf '[superset/unity] copied %s into workspace\n' "$dir_name"
}

for dir_name in $UNITY_COPY_DIRS; do
  copy_if_missing "$dir_name"
done

printf '[superset/unity] Unity workspace prepared: %s\n' "$WORKSPACE_PROJECT_PATH"
