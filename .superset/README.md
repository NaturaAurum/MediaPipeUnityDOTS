# Superset + Unity

This repository contains a Unity project at:

`MediaPipeUnityDOTS`

## Included files

- `config.json`: runs Unity workspace setup automatically when Superset creates a worktree
- `setup-unity-worktree.sh`: copies `Library` and `UserSettings` from the root repo into a new worktree when those folders exist
- `open-unity-project.sh`: launches the Unity project with the editor version declared in `MediaPipeUnityDOTS/ProjectSettings/ProjectVersion.txt`

## Default behavior

The committed Superset config prepares the workspace cache and opens Unity.

```json
{
  "setup": [
    "./.superset/setup-unity-worktree.sh",
    "./.superset/open-unity-project.sh"
  ],
  "teardown": []
}
```

## Open Unity manually

Run this from the repository root or any Superset worktree:

```bash
./.superset/open-unity-project.sh
```

The project currently expects Unity `6000.3.10f1`, which is read from `MediaPipeUnityDOTS/ProjectSettings/ProjectVersion.txt`.

## User override

Superset user overrides replace the repo config entirely, so if you create a user override, include both commands there as well:

`~/.superset/projects/<project-id>/config.json`

You can copy the example from `./.superset/config.auto-open.example.json`.

```json
{
  "setup": [
    "./.superset/setup-unity-worktree.sh",
    "./.superset/open-unity-project.sh"
  ],
  "teardown": []
}
```

## Environment variables

- `UNITY_PROJECT_RELATIVE_PATH`: override the relative Unity project path if the repository layout changes
- `UNITY_COPY_DIRS`: override cache folders to copy; default is `Library UserSettings`
- `UNITY_EDITOR_PATH`: point to a specific `Unity.app` bundle or the editor binary if it is installed outside the default Unity Hub path
