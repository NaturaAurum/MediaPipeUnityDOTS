#!/usr/bin/env bash

UNITY_PROJECT_RELATIVE_PATH="${UNITY_PROJECT_RELATIVE_PATH:-MediaPipeUnityDOTS}"

superset_workspace_root() {
  pwd -P
}

superset_repo_root() {
  if [ -n "${SUPERSET_ROOT_PATH:-}" ]; then
    printf '%s\n' "$SUPERSET_ROOT_PATH"
  else
    pwd -P
  fi
}

unity_workspace_project_path() {
  printf '%s/%s\n' "$(superset_workspace_root)" "$UNITY_PROJECT_RELATIVE_PATH"
}

unity_repo_project_path() {
  printf '%s/%s\n' "$(superset_repo_root)" "$UNITY_PROJECT_RELATIVE_PATH"
}

unity_assert_project_exists() {
  local project_path="$1"

  if [ ! -d "$project_path/Assets" ] || [ ! -d "$project_path/Packages" ] || [ ! -d "$project_path/ProjectSettings" ]; then
    printf '[superset/unity] Unity project not found at %s\n' "$project_path" >&2
    return 1
  fi
}

unity_editor_version() {
  local version_file
  version_file="$(unity_workspace_project_path)/ProjectSettings/ProjectVersion.txt"

  if [ ! -f "$version_file" ]; then
    printf '[superset/unity] ProjectVersion.txt not found: %s\n' "$version_file" >&2
    return 1
  fi

  awk -F': ' '/^m_EditorVersion: / { print $2; exit }' "$version_file"
}

unity_find_editor() {
  local version="$1"
  local candidate

  if [ -n "${UNITY_EDITOR_PATH:-}" ]; then
    if [ -d "$UNITY_EDITOR_PATH" ] || [ -x "$UNITY_EDITOR_PATH" ]; then
      printf '%s\n' "$UNITY_EDITOR_PATH"
      return 0
    fi

    printf '[superset/unity] UNITY_EDITOR_PATH does not exist: %s\n' "$UNITY_EDITOR_PATH" >&2
    return 1
  fi

  for candidate in \
    "/Applications/Unity/Hub/Editor/$version/Unity.app" \
    "$HOME/Applications/Unity/Hub/Editor/$version/Unity.app" \
    "/Applications/Unity/Hub/Editor/$version/Unity.app/Contents/MacOS/Unity" \
    "$HOME/Applications/Unity/Hub/Editor/$version/Unity.app/Contents/MacOS/Unity"
  do
    if [ -d "$candidate" ] || [ -x "$candidate" ]; then
      printf '%s\n' "$candidate"
      return 0
    fi
  done

  return 1
}
