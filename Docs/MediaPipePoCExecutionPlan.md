# MediaPipe PoC Execution Plan

## Goal

Deliver the first working MediaPipe + Unity DOTS proof of concept that another agent can execute without redefining scope.

The PoC is not a product slice. It is a boundary-validation slice.

## Chosen PoC Scope

- Target platform: **macOS Editor**
- Native mode: **CPU only**
- Model scope: **single-hand landmark tracking**
- Input: **Unity webcam feed**
- Output: **21 normalized landmarks for one hand**
- Unity rendering: **simple sample visualization**
- UI stack: **UI Toolkit for status/debug UI**
- DOTS scope: **results copied into ECS-friendly runtime data and consumed by a DOTS system**

## Why Hand Tracking

- It is visually obvious when it works.
- The output shape is fixed and easy to validate.
- It exercises frame input, native inference, result polling, and Unity-side visualization without pulling in the complexity of a full avatar pipeline.

## Explicit Non-Goals

- Multi-hand support beyond a single best result
- Face, pose, or holistic models
- GPU acceleration
- Mobile deployment
- Gesture recognition
- Animation retargeting
- Full package split or public SDK polish

## Success Criteria

The PoC is considered successful only when all of the following are true:

1. Unity Editor can start the native hand tracker from a sample scene.
2. Webcam frames reach the native layer and produce valid landmarks.
3. The sample scene visualizes the 21 landmarks in real time.
4. Native polling does not allocate new managed wrappers each frame.
5. Entering and leaving Play Mode repeatedly does not crash the plugin.

## Required Preconditions

Before the PoC starts, the project must have:

- The native bridge build path from `Docs/MediaPipeNativeIntegrationPlan.md`
- `com.unity.entities` installed in a Unity 6 compatible version
- The current runtime dependencies already in place:
  - UniTask
  - R3
  - VContainer
  - UI Toolkit

## Working Structure

The PoC should stay inside the current repository shape.

Recommended Unity-side layout:

```text
MediaPipeUnityDOTS/Assets/MediaPipeUnityDots/
├── Runtime/
│   ├── Interop/
│   ├── Input/
│   ├── Tracking/
│   └── Ecs/
└── EditorTool/

MediaPipeUnityDOTS/Assets/MediaPipeUnityDotsSamples/
├── HandTracking/
│   ├── Scripts/
│   ├── UI/
│   └── Uxml/
```

The exact folders can evolve, but the ownership split should stay:

- `Runtime`: plugin runtime, interop, ECS data flow
- `Samples`: webcam glue, visualization, sample UI, sample scene assets

## Execution Phases

## Phase 0 - Environment and Scope Lock

### Tasks

1. Install the Unity Entities package compatible with the current Editor version.
2. Confirm the native bridge library loads in Editor.
3. Confirm model files are present in the chosen runtime path.
4. Freeze the PoC scope at one-hand CPU tracking.

### Exit condition

- The repository can build native code and Unity can see the plugin artifact.

## Phase 1 - Native Smoke Test

### Tasks

1. Add a C# interop layer under `Runtime/Interop`.
2. Call `mpud_create_hand_tracker`.
3. Call `mpud_destroy_hand_tracker`.
4. Surface native error text into Unity logs.

### Exit condition

- A Unity play mode smoke test can create and destroy the tracker without a crash.

## Phase 2 - Frame Input Path

### Tasks

1. Create a webcam frame provider in sample code.
2. Convert the incoming frame into the pixel format expected by native.
3. Submit timestamped frames to the native bridge.
4. Verify width, height, stride, and orientation assumptions.

### Exit condition

- Native receives real frames from Unity and reports no submit error.

## Phase 3 - Result Polling Path

### Tasks

1. Add a polling call in `Runtime/Tracking`.
2. Copy the latest native result into a managed runtime snapshot struct.
3. Do not expose native-owned memory to DOTS or sample code.
4. Log confidence, handedness, and landmark count for validation.

### Exit condition

- Unity logs show valid single-hand results with `landmark_count == 21` when a hand is visible.

## Phase 4 - ECS Runtime Path

### Tasks

1. Define an ECS-friendly data contract for the latest hand result.
2. Decide between:
   - singleton component plus fixed buffer
   - singleton entity plus dynamic buffer
3. Create one system that copies the latest runtime snapshot into ECS data.
4. Create one system that reads ECS data for sample visualization.

### Guidance

- Keep the first implementation simple.
- Prefer one singleton result owner over per-hand entity modeling in the first PoC.
- Do not design a general multi-tracker architecture yet.

### Exit condition

- A DOTS system can read the latest hand landmark data every frame.

## Phase 5 - Sample Visualization and UI Toolkit

### Tasks

1. Create one sample scene dedicated to hand tracking.
2. Add a simple landmark visualizer.
3. Add a minimal UI Toolkit panel showing:
   - tracker state
   - frame submit count
   - last result timestamp
   - handedness
   - confidence
4. Add one restart/reset button if needed.

### Exit condition

- The scene shows landmarks and basic runtime state while the tracker is running.

## Phase 6 - Stability and Profiling

### Tasks

1. Enter and exit Play Mode at least 10 times.
2. Confirm native shutdown does not crash or leak obvious handles.
3. Check managed allocations in the poll path after warm-up.
4. Record baseline FPS and high-level CPU cost.

### Exit condition

- The PoC is stable enough to serve as the baseline for the next iteration.

## Minimum File Set Another Agent Should Create

The next agent should expect to create or modify files in these areas:

- `MediaPipeUnityDOTS/Assets/MediaPipeUnityDots/Runtime/Interop/`
- `MediaPipeUnityDOTS/Assets/MediaPipeUnityDots/Runtime/Tracking/`
- `MediaPipeUnityDOTS/Assets/MediaPipeUnityDots/Runtime/Ecs/`
- `MediaPipeUnityDOTS/Assets/MediaPipeUnityDotsSamples/HandTracking/Scripts/`
- `MediaPipeUnityDOTS/Assets/MediaPipeUnityDotsSamples/HandTracking/Uxml/`
- `MediaPipeUnityDOTS/Assets/Scenes/`
- `MediaPipeUnityDOTS/Assets/StreamingAssets/MediaPipe/`

## Data Flow Contract

The PoC should use this directional flow:

```text
Webcam -> Sample input provider -> Native bridge submit
Native bridge -> Runtime snapshot -> ECS singleton/buffer
ECS data -> Sample visualization + UI Toolkit status panel
```

Avoid direct UI calls from the native bridge and avoid direct native memory access from ECS code.

## Risks and Mitigations

- **Plugin load failure**
  - Mitigation: add a startup smoke test before webcam integration.
- **Pixel format mismatch**
  - Mitigation: validate static image first, then webcam.
- **Domain reload / Play Mode crash**
  - Mitigation: make tracker lifecycle explicit and idempotent.
- **Scope creep**
  - Mitigation: one hand, one platform, one sample scene only.
- **DOTS overdesign**
  - Mitigation: use one singleton result owner first.

## Definition of Done

The PoC is done when:

- the sample scene runs in Editor,
- a real hand produces stable landmarks,
- those landmarks reach DOTS-readable runtime data,
- the sample visualization updates from that data,
- and the setup is documented enough for the next agent to continue without rediscovery.

## Immediate Next Agent Checklist

1. Read `Docs/MediaPipeNativeIntegrationPlan.md`.
2. Read `Docs/MediaPipePoCImplementationPlan.md` for the current baseline status, verified contracts, and PR split.
3. Start a separate PR for Phase 2/3 (`Runtime/Input` + webcam submit + polling snapshot).
4. Re-run the smoke test only if the native baseline changes.
5. Proceed to Phase 4-6 only after Phase 2/3 is stable.

## Current Handoff Note

- The current canonical implementation status lives in `Docs/MediaPipePoCImplementationPlan.md`.
- Phase 0 baseline recovery is complete there: Entities wiring, local model regeneration, local native plugin regeneration.
- Phase 1 smoke-test evidence is already captured there via batchmode create/destroy verification.
- The webcam submit slice is intentionally split into a follow-up PR.
