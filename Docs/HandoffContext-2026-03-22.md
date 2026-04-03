HANDOFF CONTEXT
===============

USER REQUESTS (AS-IS)
---------------------
- 다음 페이즈에 대해 이야기 나누고 싶습니다.
- 1. 우선 adapter는 별도로 둡니다.
- 2. ECS + DOTS 환경이니 그에 맞는 Sphere, Line으로 갑니다 ( Monobehaviour 기반 x )
- 3. 충분할 것 같습니다.
- artifact 내용까지 포함해서 Handoff context를 작성해주세요. 앞으로 기록은 docs 폴더에 남기면 좋겠습니다.

GOAL
----
Continue from the approved Phase 5 plan and implement the Phase 5 visualization/UI slice without changing Phase 4 write-side ownership or breaking the adapter-only source ECS read boundary.

WORK COMPLETED
--------------
- I validated the Phase 2/3 runtime path in Unity: webcam started, runtime logs appeared, `Landmarks=21` was observed, and `Handedness` values `0/1` were confirmed by the user.
- I implemented the Phase 4 ECS runtime path and committed it as a set of atomic commits: `f7a84c5`, `9ae92e8`, `bbd065a`, `00f6bf7`, `6cbb348`.
- I added ECS runtime types and bridge logic in `MediaPipeUnityDOTS/Assets/MediaPipeUnityDots/Runtime/Ecs/` and extended `MediaPipeUnityDOTS/Assets/MediaPipeUnityDotsSamples/HandTracking/Scripts/WebcamFrameProvider.cs` so runtime snapshots are pushed into ECS singleton + `DynamicBuffer<LandmarkElement>`.
- I wired `MediaPipeUnityDOTS/Assets/Scenes/SampleScene.unity` so `WebcamFrameProvider` exists in-scene and Play Mode logs can exercise the current path.
- I fixed GitHub 429 build instability by updating `Native/Build/.bazelrc` and `Native/Build/BuildMacosEditor.sh` to prefill a distdir cache for Bazel repository downloads.
- I fixed two real ECS compile blockers that the first review missed: the DOTS source generator error in `MediaPipeUnityDOTS/Assets/MediaPipeUnityDots/Runtime/Ecs/HandTrackingSingletonUtil.cs` and the invalid `SystemAPI.GetBuffer(..., true)` call in `MediaPipeUnityDOTS/Assets/MediaPipeUnityDots/Runtime/Ecs/HandTrackingReadValidationSystem.cs`.
- I reduced noisy runtime validation logging in `MediaPipeUnityDOTS/Assets/MediaPipeUnityDots/Runtime/Ecs/HandTrackingReadValidationSystem.cs` and `MediaPipeUnityDOTS/Assets/MediaPipeUnityDotsSamples/HandTracking/Scripts/WebcamFrameProvider.cs`, and the user confirmed the logs became less noisy.
- I generated and updated Akane artifacts for Phase 4 and Phase 5.

CURRENT STATE
-------------
- Branch state: `main` is currently at `6694cfb up to unity 6.4`, tracking `origin/main`.
- Working tree is not clean. Current git status shows only two remaining non-doc changes outside this handoff write: `Native/Upstream/mediapipe` is modified as a submodule/worktree entry, and `.opencode/` is untracked.
- Phase 4 code exists and runtime validation has been manually observed in Unity. The strongest remaining evidence gap is that the Akane review artifacts still describe the absence of captured runtime evidence, because the logs were confirmed interactively after those artifacts were written.
- Akane current state is effectively Phase 5 planning complete. `/.opencode/akane/state.json` currently has `activeStage: plan-review`.
- Phase 4 artifact summary:
  - `/.opencode/akane/implementation-context.md` says Phase 4 ECS runtime path is implemented, `WebcamFrameProvider` pushes snapshots into ECS, and LSP errors were zero on changed files.
  - `/.opencode/akane/review-claude.md` says Phase 4 is approved with no P1 issues; the main remaining issue is runtime evidence capture.
  - `/.opencode/akane/review-codex.md` also says Phase 4 is approved and the remaining gap is Play Mode evidence.
- Phase 5 artifact summary:
  - `/.opencode/akane/plan.md` now defines the corrected Phase 5 architecture: one sample-side adapter as the only source ECS reader, DTO boundary via `HandTrackingFrameDto`, separate coordinate mapper, visualization-target ECS state, ECS/DOTS sphere+line visualizer, and minimal UI Toolkit panel.
  - `/.opencode/akane/plan-review.md` now approves the corrected Phase 5 plan. The only remaining notes are non-blocking: define a few `HandLandmarkVisualSettings` fields explicitly and choose a default invalid/reset hide policy, with `Scale = 0` recommended.

PENDING TASKS
-------------
- Implement Phase 5 from the approved Akane plan.
- Close the Step 0 compatibility gate for DOTS rendering before building the full visualizer: verify whether `com.unity.entities.graphics` is needed and which exact version is compatible with the current Unity/Entities combination.
- Implement the sample-side DTO and adapter boundary first, then the visualization-target ECS state, then the ECS/DOTS visualizer, then the minimal UI Toolkit panel.
- After Phase 5 implementation, capture runtime evidence into artifacts/docs: valid hand, invalid hand, reset behavior, visual entity hide/show, and UI field updates.
- Decide and document the default invalid/reset hide policy for visual entities. Current review recommendation is `Scale = 0`.
- Current todo state: no active todos remain from the handoff-writing task itself.

KEY FILES
---------
- Docs/HandoffContext-2026-03-22.md - This handoff record.
- .opencode/akane/implementation-context.md - Phase 4 implementation artifact and summary.
- .opencode/akane/review-claude.md - Most useful Phase 4 review verdict and residual risk summary.
- .opencode/akane/review-codex.md - Phase 4 secondary review summary.
- .opencode/akane/plan.md - Current approved Phase 5 plan.
- .opencode/akane/plan-review.md - Current approved Phase 5 plan review verdict.
- MediaPipeUnityDOTS/Assets/MediaPipeUnityDotsSamples/HandTracking/Scripts/WebcamFrameProvider.cs - Current write-side bridge plus scene/runtime integration point.
- MediaPipeUnityDOTS/Assets/MediaPipeUnityDots/Runtime/Ecs/HandTrackingSingletonUtil.cs - Phase 4 singleton lifecycle and invalid/reset write rules.
- MediaPipeUnityDOTS/Assets/MediaPipeUnityDots/Runtime/Ecs/HandTrackingReadValidationSystem.cs - Debug-only ECS read validation path and current logging policy.
- MediaPipeUnityDOTS/Assets/Scenes/SampleScene.unity - Current sample scene that already contains `WebcamFrameProvider`.

IMPORTANT DECISIONS
-------------------
- Phase 5 will use a separate adapter. The adapter is the only new sample-side code allowed to read source ECS directly.
- The Phase 5 visualizer must not be MonoBehaviour-based. The plan is for ECS/DOTS sphere + line visualization.
- The visualizer must not read source ECS directly. It should read visualization-target ECS state written by the adapter.
- UI must remain minimal and consume DTOs only: tracker state, frame count, timestamp, handedness, confidence, plus reset if needed.
- Coordinate conversion responsibility was intentionally split. The adapter emits normalized DTO snapshots, and a separate mapper converts them into scene-space visualization data.
- `WebcamFrameProvider` remains write-side only. Phase 5 must not move source ECS write ownership away from it.
- For native build reliability, `Native/Build/BuildMacosEditor.sh` now pre-downloads Bazel archives into a distdir cache and `Native/Build/.bazelrc` enables repository downloader retries.
- For Phase 4 visuals/debugging, noisy logs were intentionally reduced to state-change/interval logging rather than per-frame spam.

EXPLICIT CONSTRAINTS
--------------------
- 1. 우선 adapter는 별도로 둡니다.
- 2. ECS + DOTS 환경이니 그에 맞는 Sphere, Line으로 갑니다 ( Monobehaviour 기반 x )
- 앞으로 기록은 docs 폴더에 남기면 좋겠습니다.
- UI/App -> ECS uses command or request push. ECS -> UI/App uses snapshot, presenter, or ViewModel update.
- Do not place ReactiveProperty, UniTask, DI references, or ViewModel references inside ECS component data or job data.

CONTEXT FOR CONTINUATION
------------------------
- The next sensible task is Phase 5 implementation, not replanning Phase 4.
- Start from `/.opencode/akane/plan.md` and `/.opencode/akane/plan-review.md`; they are the current approved source of truth.
- If you update Akane artifacts again, keep them in sync with the actual runtime evidence. The current artifacts still understate runtime validation because the user confirmed log evidence after the reviews were written.
- Be careful not to accidentally commit `.opencode/` or the modified `Native/Upstream/mediapipe` entry unless that is explicitly intended.
- Before full Phase 5 visualizer work, verify the DOTS rendering compatibility gate and sample asmdef dependency needs.
- Prefer leaving future status/handoff records in `Docs/` unless the user says otherwise.
