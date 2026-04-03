# MediaPipe PoC 구현 계획 및 현재 상태

> 이 문서는 `Docs/MediaPipePoCExecutionPlan.md`의 실행용 기준 문서입니다.
> 현재 baseline 복구 결과, 다음 PR 경계, Phase별 구현 계약을 함께 기록합니다.

## Goal

macOS Editor 환경에서 CPU-only single-hand landmark tracking PoC를 완성한다.
흐름은 `Unity webcam input -> native bridge submit -> runtime snapshot -> App/UI layer bridge push -> ECS singleton + dynamic buffer -> sample/app adapter -> sample visualization + UI Toolkit`으로 고정한다.

## Scope Lock

| 항목 | 값 |
|------|-----|
| Target platform | macOS Editor |
| Native mode | CPU only |
| Model scope | single-hand landmark tracking |
| Input | Unity webcam feed |
| Output | one hand, 21 normalized landmarks |
| Rendering | sample scene landmark visualizer |
| UI | UI Toolkit 상태/디버그 패널 |
| Excluded | multi-hand, face, pose, holistic, GPU, mobile, gesture recognition, avatar retargeting, package split |

## 현재 상태 요약

| Phase | 상태 | 메모 |
|------|------|------|
| Phase 0 - baseline 복구 | 완료 | Entities 설치, asmdef wiring, sample editor asmdef 분리, model/plugin 재생성 완료 |
| Phase 1 - smoke test 재검증 | 완료 | batchmode smoke test 통과, create/destroy evidence 확보 |
| Phase 2 - frame submit | 미착수 | 별도 PR로 분리 |
| Phase 3 - polling/snapshot | 미착수 | Phase 2와 같은 PR 권장 |
| Phase 4 - ECS runtime path | 미착수 | App/UI bridge push + singleton/dynamic buffer 기준 |
| Phase 5 - sample visualization/UI | 미착수 | adapter -> plain DTO 경계 고정 |
| Phase 6 - stability/profiling | 미착수 | Play Mode 10회, alloc/FPS 기록 |

현재 확정 사항:

- `com.unity.entities`는 `1.3.14`로 설치했고 `MediaPipeUnityDOTS/Packages/packages-lock.json`에 lock됨
- `MediaPipeUnityDOTS/Assets/MediaPipeUnityDots/Runtime/MediaPipeUnityDots.Runtime.asmdef`에 `Unity.Collections`, `Unity.Entities` 참조를 추가함
- `MediaPipeUnityDOTS/Assets/MediaPipeUnityDotsSamples/MediaPipeUnityDotsSamples.asmdef`에 `Unity.Collections`, `Unity.Entities` 참조를 추가하고 `allowUnsafeCode`를 `true`로 변경함
- `MediaPipeUnityDOTS/Assets/MediaPipeUnityDotsSamples/HandTracking/Scripts/Editor/MediaPipeUnityDotsSamples.HandTracking.Editor.asmdef`를 추가해 `NativeSmokeTestRunner.cs`를 editor 전용 assembly로 분리함
- `Native/Upstream/mediapipe` submodule을 `v0.10.33`으로 올림
- `MediaPipeUnityDOTS/Assets/StreamingAssets/MediaPipe/Models/hand_landmarker.task`를 로컬에 재생성함
- `MediaPipeUnityDOTS/Assets/Plugins/macOS/libmpud_bridge.dylib`를 로컬에 재생성함
- `MediaPipeUnityDOTS/Assets/Plugins/macOS/libmpud_bridge.dylib.meta`에 Editor용 `PluginImporter` 설정을 고정함
- `Native/Build/.bazelrc`를 MediaPipe v0.10.33 요구사항에 맞춰 C++20으로 상향함
- `Native/Build/BuildMacosEditor.sh`는 macOS 26의 zlib `fdopen` 블록 깨짐을 복구하도록 보강함
- `Native/Build/BuildMacosEditor.sh`는 `bazelisk`를 통해 `Native/Upstream/mediapipe/.bazelversion`의 Bazel 7.4.1을 강제하고, `HERMETIC_PYTHON_VERSION=3.12`를 기본값으로 사용하도록 고정함
- `Native/Patches/mediapipe/macos_arm64_compat.diff`를 v0.10.33 기준으로 재작성함
- `Native/Patches/mediapipe/tasks_logging_analytics_compat.diff`를 추가해 공개 OSS 태그에 없는 `mediapipe/util/analytics` 의존성을 제거함
- `Native/Build/CopyArtifactsToUnity.sh`는 기존 읽기 전용 dylib overwrite를 위해 `chmod u+w`를 수행함
- 2026-04-03 기준 Unity 실행 중 인스턴스에서 `MediaPipe/Run Smoke Test` 메뉴를 재실행했고 create/destroy 로그 증거를 다시 확보함

## 현재 baseline 검증 기록

### Preflight - 통과

| 항목 | 결과 |
|------|------|
| MediaPipe submodule | `3987048d4b390aa9ae675c796f6421bbeece6511 (v0.10.33)` |
| Bazelisk | `1.28.1` |
| Bazel (`Native/Upstream/mediapipe` 기준) | `7.4.1` |
| Python numpy | `2.4.2` |
| OpenCV | `4.13.0` |
| Xcode CLT | `/Applications/Xcode.app/Contents/Developer` |

### 타겟 검증 - 통과

- `MediaPipeUnityDOTS/Packages/packages-lock.json`에 `com.unity.entities: 1.3.14` 기록
- `NativeSmokeTestRunner.cs`는 sample runtime asmdef가 아니라 editor 전용 asmdef에서 컴파일되도록 분리됨
- `MediaPipeUnityDOTS/Assets/StreamingAssets/MediaPipe/Models/hand_landmarker.task` 존재
- `Native/Artifacts/MacosEditor/libmpud_bridge.dylib` 존재
- `MediaPipeUnityDOTS/Assets/Plugins/macOS/libmpud_bridge.dylib` 존재
- `MediaPipeUnityDOTS/Assets/Plugins/macOS/libmpud_bridge.dylib.meta`에 Editor용 `PluginImporter` 설정 존재
- copied dylib의 install name이 `@loader_path/libmpud_bridge.dylib`로 고정됨
- copied dylib에 ad-hoc codesign이 적용됨
- Bazel target `//mediapipe/mpud_bridge:libmpud_bridge.dylib` 빌드 성공

### Phase 1 smoke test - 통과

- 2026-04-03 실행 방식:
  - 이미 열려 있던 Unity 6000.4.1f1 인스턴스에서 `MediaPipe/Run Smoke Test` 메뉴를 MCP `execute_menu_item`으로 실행
- 핵심 로그:
  - `[MPUD Smoke] create_hand_tracker status: 0`
  - `[MPUD Smoke] Tracker created successfully!`
  - `[MPUD Smoke] destroy_hand_tracker completed`
  - `[MPUD Smoke] === Smoke Test Complete (no crash) ===`
- 검증 결과:
  - `DllNotFoundException` 없음
  - plugin import/load, model path, create/destroy 기본 경로가 현재 baseline에서 동작함
  - 같은 프로젝트가 이미 열려 있어 별도 batchmode 실행은 Unity가 차단했으며, open-editor smoke 경로로 대체 검증함

### 현재 비차단 관찰 사항

- Unity batchmode 로그에 duplicate assembly 경고가 보이지만 이번 smoke test는 통과함
- `Unity.Properties.Internals.asmref` 관련 경고가 보이지만 이번 baseline 검증을 막지는 않음
- `libmpud_bridge.dylib` 링크 시 OpenCV dylib들이 macOS 26.0 타깃으로 빌드되었다는 경고가 남지만 현재 Editor smoke test는 통과함
- `Native/Upstream/mediapipe` dirty 상태는 계속 남으며 build 부산물과 upstream 수정이 혼동되지 않도록 주의가 필요함

## 구조 불변 규칙

이 문서의 규칙은 후속 구현에서 유지해야 한다.

- ECS 데이터(`IComponentData`, `IBufferElementData`, job struct)는 unmanaged only
- native-owned memory를 ECS/UI/sample 코드에 노출하지 않음
- native handle은 `HandTrackingService`만 소유함
- `HandTrackingService`는 App/UI layer의 managed singleton으로 존재하며 runtime snapshot 저장소를 mutate할 수 있는 유일한 소유자임
- `HandTrackingService`는 내부 배열 참조를 직접 반환하지 않음. caller-owned destination으로 복사하는 read-only snapshot copy API만 제공함
- copy API는 capacity 21의 caller-owned destination에 landmark를 복사하고 copied landmark count와 상태 필드를 함께 반환함
- App/UI layer bridge만 `HandTrackingService` copy API를 호출해 ECS에 push할 수 있음
- ECS write는 App/UI managed bridge가 메인 스레드에서 `EntityManager`로 직접 수행함. ECS system/job는 write를 담당하지 않음
- sample/app adapter만 `EntityManager` 또는 ECS query를 직접 사용할 수 있음
- sample visualizer와 UI presenter/viewmodel은 adapter가 준 plain DTO만 사용함
- UI Toolkit은 native를 직접 호출하지 않음
- 첫 working slice에서는 `create/start/submit/poll/get_last_error/destroy`를 같은 메인 스레드에서 동기 호출함

## Native 계약

- lifecycle 기본값: `create -> optional start(no-op) -> submit/poll -> destroy`
- `stop` API는 현재 없음. restart/reset은 Unity 쪽에서 `destroy + create`로 처리함
- `timestamp_us`는 strict monotonic increasing 이어야 함
- 허용 pixel format은 `SRGB(0)` 또는 `SRGBA(1)`만
- 첫 구현 기본값은 `Color32[] -> SRGBA`, `strideBytes = width * 4`
- `mpud_get_last_error()`는 thread-local이므로 에러가 발생한 동일 스레드에서 호출해야 함

## 상태/리셋 계약

- `FrameCount`는 poll로 새 latest snapshot이 확정될 때마다 1 증가함
- invalid latest snapshot도 `FrameCount` 증가 대상에 포함함
- reset/recreate 직후 `FrameCount`는 `0`으로 초기화함
- invalid runtime snapshot은 `IsValid = false`, `Handedness = -1`, `Score = 0`, `LandmarkCount = 0`, `TimestampUs = latest frame timestamp`로 정규화함
- reset/recreate 직후 runtime snapshot은 즉시 empty state로 재설정되며 `TimestampUs = 0`, `LandmarkCount = 0`, `IsValid = false`, `Handedness = -1`, `Score = 0`, `FrameCount = 0`을 사용함
- ECS singleton entity는 bridge 초기화 시 1회 생성 후 재사용함. reset/recreate 시 entity를 파괴하지 않고 status와 dynamic buffer만 empty state로 갱신함
- invalid ECS empty state는 `IsValid = false`, `Handedness = -1`, `Score = 0`, `LandmarkCount = 0`, `DynamicBuffer.Length = 0`, `TimestampUs = latest frame timestamp`로 고정함
- App/UI bridge는 `_pendingResetSnapshotPush == true`이면 timestamp dedupe를 우회해 현재 읽힌 snapshot을 1회 push하고, 직후 `_pendingResetSnapshotPush = false`로 되돌림
- 그 외에는 `snapshot.TimestampUs > _lastCopiedTimestamp`일 때만 ECS에 push함
- service reset/recreate 직후 bridge는 `_pendingResetSnapshotPush = true`와 `_lastCopiedTimestamp` 초기화를 함께 설정하고, 다음 update에서 reset snapshot(`TimestampUs = 0`) 또는 이후 첫 최신 snapshot 중 실제로 읽힌 첫 snapshot을 정확히 1회 push함

## 데이터 흐름

```text
WebCamTexture
-> WebcamFrameProvider
-> HandTrackingService submit/poll
-> runtime snapshot
-> App/UI layer bridge push
-> ECS singleton + DynamicBuffer<LandmarkElement>
-> sample/app adapter
-> sample visualizer + UI presenter/viewmodel
```

프레임 내 순서는 다음과 같이 고정한다.

```text
poll -> snapshot copy -> ECS push -> adapter read -> visualizer/presenter 반영
```

## Phase별 작업 계획

### Phase 0 - baseline 복구 (완료)

완료한 항목:

1. `com.unity.entities` 설치 및 lock
2. runtime/sample asmdef에 ECS 참조 추가
3. `Native/Build/DownloadModels.sh` 실행
4. `Native/Build/BuildMacosEditor.sh` 실행
5. `Native/Build/CopyArtifactsToUnity.sh` 실행
6. macOS 26 zlib workaround 보강

남긴 산출물:

- `MediaPipeUnityDOTS/Assets/StreamingAssets/MediaPipe/Models/hand_landmarker.task`
- `MediaPipeUnityDOTS/Assets/Plugins/macOS/libmpud_bridge.dylib`
- `MediaPipeUnityDOTS/Packages/packages-lock.json`의 resolved version

주의:

- `Native/Build/SyncBridgeIntoWorkspace.sh` 특성상 `Native/Upstream/mediapipe` working tree는 dirty 상태로 남을 수 있음
- 이는 build 부산물로 취급하며, 별도 요청 없이 destructive git 정리를 하지 않음

### Phase 1 - smoke test 재검증 (완료)

목표:

- 기존 smoke test가 현재 baseline에서 create/destroy를 통과하고 실제 plugin load까지 증명함

작업:

1. `MediaPipe/Run Smoke Test` 실행
2. 필요 시 Play Mode용 `NativeSmokeTest` 실행
3. Play Mode 3회 반복으로 최소 lifecycle 안정성 확인
4. 실패 시 triage 범위는 `dylib` load/import settings, model path, bridge config/version mismatch, thread-local error capture로 제한
5. smoke test 자체를 막는 compile/package/import 오류는 즉시 Phase 0 blocker로 되돌림

확보한 증거:

- Console에 `[MPUD Smoke] create_hand_tracker status: 0`
- Console에 `[MPUD Smoke] destroy_hand_tracker completed`
- `DllNotFoundException` 없음
- 실패 경로에서도 `GetLastError()`가 비어 있지 않음
- batchmode 종료 로그가 정상적으로 남음

### Phase 2 - frame submit path (별도 PR)

목표:

- Unity webcam frame을 native submit까지 안정적으로 연결함

작업:

1. `Runtime/Input/`에 frame conversion 유틸 추가
2. sample 쪽에 `WebCamTexture` 기반 `WebcamFrameProvider` 추가
3. 첫 구현은 메인 스레드 동기 submit으로 고정
4. `Color32[]` 재사용 버퍼를 사용하고 필요 시 pin 후 `MpudImageFrame` 구성
5. `timestamp_us`는 `Stopwatch` 기반 microseconds로 만들고 이전 값 이하이면 +1 보정
6. `pixelFormat = SRGBA`, `strideBytes = width * 4`로 시작
7. orientation, dimensions, submit status를 임시 로그로 검증
8. restart/recreate 시 `_lastSubmittedTimestampUs`와 관련 submit state를 reset

검증 기준:

- 연속 submit에서 monotonic timestamp 에러가 없음
- submit 성공 로그가 반복 출력됨
- 손 인식 이전 단계에서도 submit 자체는 안정적으로 성공함

### Phase 3 - polling과 runtime snapshot (Phase 2와 같은 PR 권장)

목표:

- native 결과를 Unity-owned snapshot으로 복사하고 native memory 의존을 끊음

작업:

1. `Runtime/Tracking/`에 tracker lifecycle service 추가
2. `mpud_try_get_latest_result` 결과를 runtime snapshot으로 복사
3. snapshot은 one hand, 21 landmarks 고정 shape를 유지
4. warm-up 이후 per-frame wrapper/new array 생성 없이 재사용 가능한 저장소 사용
5. reset/recreate 시 tracker handle, `_lastSubmittedTimestampUs`, snapshot 상태, `FrameCount`를 함께 초기화

검증 기준:

- 손이 보일 때 `landmark_count == 21`
- 손이 없을 때도 latest frame timestamp는 갱신됨
- invalid result가 정규화된 empty state를 유지함
- steady-state poll 경로 `GC Alloc`가 0B이거나 측정값이 기록됨

### Phase 4 - ECS runtime path

목표:

- App/UI layer bridge가 runtime snapshot copy를 ECS unmanaged data로 push하고, ECS는 그 결과만 소비함

고정 선택:

- singleton entity 1개
- status component 1개
- `DynamicBuffer<LandmarkElement>` 1개

작업:

1. `Runtime/Ecs/`에 status component와 landmark buffer element 정의
2. 모든 ECS 타입에서 managed field 금지 확인
3. App/UI layer bridge 1개가 runtime snapshot copy API -> singleton/buffer push를 담당
4. `timestamp` 기반으로 동일 snapshot 중복 push 방지
5. reset/recreate 시 bridge의 ECS copy cache state 초기화
6. sample 쪽 ECS read는 sample/app adapter 1곳으로 제한

검증 기준:

- singleton entity 존재
- valid hand일 때 buffer length `21`
- invalid hand일 때 `DynamicBuffer.Length = 0`, `LandmarkCount = 0`, `Score = 0`, `Handedness = -1`
- reset 직후 entity는 유지되고 empty state만 반영됨

### Phase 5 - sample visualization과 UI Toolkit

목표:

- PoC가 눈으로 확인 가능하고 디버깅 가능한 상태가 됨

작업:

1. hand-tracking 전용 sample scene 구성
2. sample/app adapter가 ECS singleton + buffer를 읽어 plain DTO로 변환
3. landmark visualizer는 adapter가 제공한 plain DTO만 사용
4. UI presenter/viewmodel은 adapter가 제공한 plain DTO만 받음
5. UI Toolkit UXML/USS와 상태 view/viewmodel 추가
6. restart/reset 버튼이 필요하면 service reset을 호출하도록 연결

검증 기준:

- Game View에서 21개 landmark가 실시간으로 움직임
- UI에 tracker state, frame count, timestamp, handedness, confidence가 보임
- sample visualizer와 UI presenter/viewmodel 모두 `EntityManager` 또는 ECS query를 직접 사용하지 않음

### Phase 6 - stability와 profiling

목표:

- baseline으로 넘길 수 있는 최소 안정성과 할당 특성을 기록함

작업:

1. Play Mode 10회 반복 진입/퇴장
2. create/destroy 재진입 안전성 확인
3. poll path warm-up 후 allocation 확인
4. FPS와 고수준 CPU cost 기록
5. 결과를 `Docs/`에 기록

검증 기준:

- Play Mode 10회에서 crash 0회
- steady-state poll 경로 `GC Alloc` 0B 또는 측정값 기록
- 반복 경고/에러 누적 없음

## PR 분리 기준

- 현재 PR 범위는 Phase 0 baseline 복구와 Phase 1 smoke-test 재검증 준비 상태 확보까지로 제한함
- `Runtime/Input` + webcam submit path 구현은 이번 PR에 포함하지 않음
- 다음 기능 PR은 Phase 2와 Phase 3을 한 묶음으로 진행하는 것을 권장함
- Phase 4 이후 ECS/UI 작업은 Phase 2/3이 안정화된 뒤 별도 PR로 진행함

## 즉시 다음 액션

1. 별도 PR에서 Phase 2/3 (`Runtime/Input` + webcam submit + polling snapshot) 구현을 시작
2. native baseline을 다시 건드릴 때만 `MediaPipe/Run Smoke Test` 또는 batchmode smoke test를 재실행
3. Phase 2/3이 안정화되면 그 다음 PR에서 ECS runtime path와 sample visualization/UI를 진행
