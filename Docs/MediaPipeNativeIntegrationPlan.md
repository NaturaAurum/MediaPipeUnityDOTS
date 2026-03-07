# MediaPipe Native Integration Plan

## Goal

Bring upstream MediaPipe into the repository as a native dependency without turning the Unity side into a thin wrapper around high-allocation C# bindings.

The intended result is a reproducible native build path and a small C ABI bridge that Unity can call from `MediaPipeUnityDots/Runtime`.

## Decision Summary

- Use **upstream `google-ai-edge/mediapipe`** as the native source of truth.
- Treat **`MediaPipeUnityPlugin` as a reference only**, not as a runtime dependency to vendor into this repository.
- Keep **MediaPipe build orchestration in Bazel** for the PoC.
- Add a **thin C ABI bridge** that exposes POD structs and opaque handles only.
- Build **CPU-only macOS Editor** first.
- Delay GPU, Android, iOS, Windows, Tasks multi-model composition, and package splitting until after the first PoC works.

## Why This Approach

- Upstream MediaPipe already solves graph execution, calculators, model plumbing, and dependency management.
- Recreating that stack in Unity DOTS would waste time and move effort away from the actual optimization target: the managed/native boundary.
- `MediaPipeUnityPlugin` contains useful know-how, but its managed wrapper style is not the runtime shape we want for this project.

## Repository Placement

The native integration work should live under `Native/`.

Recommended layout:

```text
Native/
├── README.md
├── Upstream/
│   └── mediapipe/              # git submodule pinned to a tested commit
├── Bridge/
│   ├── Include/
│   ├── Src/
│   └── BazelOverlay/
├── Build/
│   ├── SyncBridgeIntoWorkspace.sh
│   ├── BuildMacosEditor.sh
│   └── CopyArtifactsToUnity.sh
├── Patches/
│   └── mediapipe/
└── Artifacts/
    └── MacosEditor/
```

## Source Intake Rules

### 1. MediaPipe upstream

- Add upstream as a git submodule under `Native/Upstream/mediapipe`.
- Pin to a tested commit and record the SHA in this document after the first successful native build.
- Do not edit random upstream files inline without also creating a patch or scripted overlay step.

Suggested command:

```bash
git submodule add https://github.com/google-ai-edge/mediapipe.git Native/Upstream/mediapipe
git submodule update --init --recursive
```

### 2. MediaPipeUnityPlugin

- Do not vendor the whole repository into this project for the PoC.
- Use it only as an external reference for:
  - Unity plugin import settings
  - asset packaging patterns
  - graph/model file placement
  - platform build hints

## Build Strategy

### Chosen build system

- Use Bazel as the native source-of-truth build path.
- Do not attempt a full CMake migration for the first PoC.

### Practical integration method

- Keep bridge source in `Native/Bridge`.
- Use a sync/overlay script to copy bridge files into the upstream workspace before invoking Bazel.
- Keep all custom targets, BUILD fragments, and bridge-specific code inside our repository, not hidden inside ad hoc local edits.

Example flow:

1. Sync `Native/Bridge` into a deterministic path inside `Native/Upstream/mediapipe`.
2. Apply any required patch files from `Native/Patches/mediapipe`.
3. Build the bridge target with Bazel.
4. Copy the resulting library into `MediaPipeUnityDOTS/Assets/Plugins`.

## Native Bridge Contract

The bridge must be small and boring.

### ABI rules

- Export plain C functions only.
- Use opaque handles for native tracker instances.
- Return POD structs, fixed-size arrays, counts, and status codes.
- Do not expose STL containers, C++ classes, exceptions, or templates across the boundary.
- Do not pass ownership of heap objects to C#.

### Minimum surface for PoC

The first PoC only needs a hand-tracking bridge.

Recommended minimum API:

```c
typedef struct MpudHandTracker MpudHandTracker;

typedef struct MpudImageFrame {
    const unsigned char* data;
    int width;
    int height;
    int stride_bytes;
    int pixel_format;
    long long timestamp_us;
} MpudImageFrame;

typedef struct MpudNormalizedLandmark {
    float x;
    float y;
    float z;
    float visibility;
    float presence;
} MpudNormalizedLandmark;

typedef struct MpudHandResult {
    int is_valid;
    int landmark_count;
    int handedness;
    float score;
    long long timestamp_us;
    MpudNormalizedLandmark landmarks[21];
} MpudHandResult;

int mpud_create_hand_tracker(/* config */, MpudHandTracker** out_tracker);
int mpud_start_hand_tracker(MpudHandTracker* tracker);
int mpud_submit_frame(MpudHandTracker* tracker, const MpudImageFrame* frame);
int mpud_try_get_latest_result(MpudHandTracker* tracker, MpudHandResult* out_result);
void mpud_destroy_hand_tracker(MpudHandTracker* tracker);
const char* mpud_get_last_error(void);
```

The exact signature can change, but the shape should stay this simple.

## Unity Boundary Rules

- Unity C# code talks only to the bridge DLL.
- Unity C# code does not use `Packet<T>`-style managed wrappers.
- `MediaPipeUnityDots/Runtime` owns `DllImport`, marshaling structs, and polling logic.
- ECS systems consume copied snapshot data from native results, not native-owned memory.

## Asset and Model Placement

The first PoC should keep model files outside the native build output.

Recommended runtime asset path:

```text
MediaPipeUnityDOTS/Assets/StreamingAssets/MediaPipe/
└── Models/
```

Rules:

- Native library and model assets are separate concerns.
- The bridge receives model paths or asset paths through a config call.
- Do not bake model binaries into the Unity C# layer.

## Platform Scope for Phase 1

- Platform: macOS Editor
- Architecture: Apple Silicon / Editor local machine
- Inference mode: CPU only
- Input source: webcam frame or test image
- Output: one hand, 21 normalized landmarks

## Explicit Non-Goals

- No GPU path in PoC phase 1
- No Android or iOS
- No Windows build
- No multi-model orchestration
- No gesture classifier
- No avatar rigging
- No package split refactor

## Execution Checklist

1. Add upstream MediaPipe submodule under `Native/Upstream/mediapipe`.
2. Create `Native/Bridge/Include` and `Native/Bridge/Src`.
3. Create `Native/Build/SyncBridgeIntoWorkspace.sh`.
4. Create `Native/Build/BuildMacosEditor.sh`.
5. Add bridge BUILD overlay or patch so Bazel can compile the wrapper.
6. Build a dummy smoke-test library that exports at least `mpud_get_last_error`.
7. Replace the dummy with a single hand-tracking bridge.
8. Copy the built artifact into `MediaPipeUnityDOTS/Assets/Plugins`.
9. Add a tiny C# `DllImport` smoke test in `MediaPipeUnityDots/Runtime`.
10. Record the pinned upstream commit SHA and produced artifact path in this document.

## Deliverables

- Submodule entry for upstream MediaPipe
- Native bridge header and source
- Build scripts
- One reproducible build command for macOS Editor
- One native artifact copied into Unity plugins
- One C# smoke test that can call into the native bridge

## Acceptance Criteria

- A clean checkout can initialize the submodule and build the bridge.
- The resulting native library loads in Unity Editor.
- A C# smoke test can call the bridge without crashing.
- The project has a documented pinned upstream commit.

## Handoff Notes for Another Agent

- Do not start with Android or GPU.
- Do not optimize the whole graph pipeline yet.
- First prove the native build and the C ABI shape.
- Keep every change reproducible from a clean checkout.

## Pinned Upstream

- Repository: https://github.com/google-ai-edge/mediapipe
- Tag: v0.10.14
- SHA: `4cf89a70942ca3252e46ace7e4552f53be9bef2e`
- Submodule path: `Native/Upstream/mediapipe`
