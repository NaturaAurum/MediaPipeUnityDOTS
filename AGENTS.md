# Project Guidelines

## Communication & Workflow

- Use Korean for conversation and code comments; Korean or English for commits/requests.
- Ignore LSP warnings, not errors.
- State assumptions; inspect available evidence before asking about unresolved ambiguity.
- Before editing, state one concrete, verifiable success criterion.
- Make small, necessary changes only; reuse existing styles, avoid unrequested features/abstractions, and clean up what you create.
- Update affected tests. Run a targeted check first (test/filter/build/log; prefer `-testFilter` over full EditMode), then broaden only if needed.
- If verification fails, fix it or roll back your last change before adding features. Complete only with evidence; report results and next action.

## Linear

- All Linear-related operations MUST use only the Linear MCP tools; agents HAVE TO avoid Linear CLI, direct API/HTTP calls, browser automation, and every non-MCP path.
- Verify by confirming that Linear work used only Linear MCP tool calls.

## Project Map

- Unity version: `MediaPipeUnityDOTS/ProjectSettings/ProjectVersion.txt` (source of truth).
- Scene: `MediaPipeUnityDOTS/Assets/Scenes/SampleScene.unity`.
- C#; `unsafe` allowed for interop when needed.
- Under `MediaPipeUnityDOTS/Assets/MediaPipeUnityDots/`:
  - `Runtime/`: plugin runtime.
  - `EditorTool/`: editor utilities.
  - `Sample/`: samples and validation.
- `Native/`: native bridge and build scripts. `Docs/`: architecture notes and decisions.

## UI / ECS Boundary

- New UI: UI Toolkit + MVVM. UniTask, R3, and VContainer are allowed only in UI/App code.
- Define layout/styles in `.uxml`/`.uss`; use C# for binding and behavior.
- ECS core (`IComponentData`, job data, system data flow) stays unmanaged pure data and data-oriented. No `ReactiveProperty`, `UniTask`, DI, or ViewModel references in component/job data.
- UI/App → ECS: command/request push. ECS → UI/App: snapshot, presenter, or ViewModel update.
- For related changes, verify no managed fields in component/job structs and no UniTask PlayerLoop startup conflict with Entities.

## Coding & Safety

- Follow `MediaPipeUnityDOTS/.editorconfig`: 4 spaces, LF. Naming: `PascalCase` for classes/methods/properties/constants, `camelCase` for locals/parameters, `_camelCase` for private fields, `IInterface`.
- Wire references via `[SerializeField]` or VContainer; avoid `Find*`, `AddComponent`, and `GetComponent` in new code. Verify by searching touched files.
- Avoid magic numbers, dispose event subscriptions properly, and use `Debug.LogError` for errors.
- When renaming public or `[SerializeField]` Inspector fields on `MonoBehaviour`, update affected prefab serialized data in the same change; do not use `FormerlySerializedAsAttribute`.

## Branch & PR

- Follow `BRANCH_RULE.md` for branch names, PR targets/title prefixes, and squash merges. Branch from `develop` by default.
- Before committing, verify `git branch --show-current` is neither `main` nor `develop`.

## Maintaining These Guidelines

- During feature work, change this file only after the same friction occurs at least twice; change one testable rule at a time, not broad policy.
- Each new rule must state how to verify it in real tasks.

<!-- headroom:rtk-instructions -->
## Shell Commands (RTK)

- Prefer dedicated file/search tools when available. For shell commands, use `rtk <command>` for supported commands; use `rtk proxy <command>` for unfiltered execution with usage tracking.
- Check `rtk --help` for supported commands; prefix each command in a chain separately.
- For debugging, raw commands without the `rtk` prefix are allowed.
<!-- /headroom:rtk-instructions -->
