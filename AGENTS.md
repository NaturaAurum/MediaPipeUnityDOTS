# Global Guidelines

1. **Think Before Coding**: Explicit assumptions. No speculation. Ask if unclear.
2. **Simplicity First**: Minimum code. No unrequested features/abstractions.
3. **Surgical Changes**: Touch only necessary code. Clean up what you create.
4. **Goal-Driven**: Define verifiable success criteria (tests/checks) before coding.
5. **Short Feedback Loop**: Ship small changes, evaluate immediately, then iterate.

## Conversation

- **Lang**: Korean
- **Code Comments**: Korean
- **Commits/Requests**: Korean or English
- **Workflow**: Ignore LSP warnings unless errors.

## Tech Stack

- **Unity**: 6000.3.11f1 (`MediaPipeUnityDOTS/ProjectSettings/ProjectVersion.txt`)
- **Scene**: `MediaPipeUnityDOTS/Assets/Scenes/SampleScene.unity`
- **Lang**: C# (unsafe allowed when needed for interop)
- **UI**: UI Toolkit for new UI work
- **Libraries**: UniTask, R3, VContainer are allowed for UI / App layer code

## UI / ECS Boundary

- **UI Stack**: `UI Toolkit + MVVM + R3 + UniTask + VContainer` is allowed.
- **UI Definition**: Manage UI Toolkit layout and styles in `.uxml`/`.uss` files instead of C# code; keep C# for binding and behavior only.
- **Boundary Rule**: Use that stack only in the UI / App layer. Keep ECS core (`IComponentData`, job data, system data flow) as unmanaged pure data.
- **Do Not Put In ECS**: Do not place `ReactiveProperty`, `UniTask`, DI references, or ViewModel references inside ECS component data or job data.
- **Integration Rule**: UI/App -> ECS uses command or request push. ECS -> UI/App uses snapshot, presenter, or ViewModel update.
- **Verify**: For related changes, confirm there are no managed fields inside `IComponentData` / job structs, and confirm UniTask PlayerLoop startup does not conflict with Entities.

## Coding Guidelines

- **Naming**: `PascalCase` (Class/Method/Prop/Const), `camelCase` (Var/Param), `_camelCase` (Private), `IInterface`.
- **Format**: 4 spaces indent, LF style. See `MediaPipeUnityDOTS/.editorconfig` for details.
- **Patterns**: MVVM for UI/App code, data-oriented design for Unity DOTS / ECS runtime
- **Explicit Wiring**: Avoid `Find*`, `AddComponent`, and `GetComponent` in new code; wire references via `[SerializeField]` or VContainer instead. Verify: grep touched files for the avoided calls.
- **Structure**:
  - `MediaPipeUnityDOTS/Assets/MediaPipeUnityDots/Runtime/`: Plugin runtime code
  - `MediaPipeUnityDOTS/Assets/MediaPipeUnityDots/EditorTool/`: Editor-side utility code
  - `MediaPipeUnityDOTS/Assets/MediaPipeUnityDots/Sample/`: Sample and validation code
  - `Native/`: Native bridge source and build scripts
  - `Docs/`: Architecture notes and decisions

## Precautions & Best Practices

- **Safety**: No magic numbers. Dispose events properly. Use `Debug.LogError` for errors.
- **Serialized Rename Policy**: When renaming a `MonoBehaviour` field exposed in Inspector (`public` or `[SerializeField]`), do not use `FormerlySerializedAsAttribute`; update affected prefab serialized data in the same change.
- **Git**: Use Git Flow (`feature/`, `bugfix/`). Merge PRs with Squash only (`gh pr merge --squash`). Verify: merged PR adds exactly one commit to `main`.
- **Modifications**: Keep changes minimal. Update tests. Match existing styles.

## Evaluation Loop (Mandatory Per Task)

1. **Define**: Write one concrete success criterion before coding.
2. **Change**: Make the smallest possible code edit to satisfy it.
3. **Verify**: Run one targeted check first (test/filter/build/log check), then broader checks only if needed.
4. **Record**: Capture what passed/failed and the next action in the task summary.

- **Targeted First**: Prefer narrow checks (`-testFilter`) before full EditMode run.
- **Failure Rule**: If check fails, do not add new features. Fix failure or rollback the last change.
- **Done Rule**: A task is complete only when success criterion is satisfied by evidence.

## AGENTS.md Incremental Improvement Rule

- Update this file only when repeated friction is observed (same issue at least 2 times).
- Add/modify one rule at a time and keep wording testable.
- Avoid broad policy rewrites during feature work.
- Every new rule must include how it will be verified in real tasks.


<!-- headroom:rtk-instructions -->
# RTK (Rust Token Killer) - Token-Optimized Commands

When running shell commands, **always prefix with `rtk`**. This reduces context
usage by 60-90% with zero behavior change. If rtk has no filter for a command,
it passes through unchanged — so it is always safe to use.

## Key Commands
```bash
# Git (59-80% savings)
rtk git status          rtk git diff            rtk git log

# Files & Search (60-75% savings)
rtk ls <path>           rtk read <file>         rtk grep <pattern>
rtk find <pattern>      rtk diff <file>

# Test (90-99% savings) — shows failures only
rtk pytest tests/       rtk cargo test          rtk test <cmd>

# Build & Lint (80-90% savings) — shows errors only
rtk tsc                 rtk lint                rtk cargo build
rtk prettier --check    rtk mypy                rtk ruff check

# Analysis (70-90% savings)
rtk err <cmd>           rtk log <file>          rtk json <file>
rtk summary <cmd>       rtk deps                rtk env

# GitHub (26-87% savings)
rtk gh pr view <n>      rtk gh run list         rtk gh issue list

# Infrastructure (85% savings)
rtk docker ps           rtk kubectl get         rtk docker logs <c>

# Package managers (70-90% savings)
rtk pip list            rtk pnpm install        rtk npm run <script>
```

## Rules
- In command chains, prefix each segment: `rtk git add . && rtk git commit -m "msg"`
- For debugging, use raw command without rtk prefix
- `rtk proxy <cmd>` runs command without filtering but tracks usage
<!-- /headroom:rtk-instructions -->
