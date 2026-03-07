# MediaPipeUnityDOTS

Unity DOTS (ECS + Jobs) 환경에서 MediaPipe를 성능 우선으로 통합하기 위한 실험용 저장소입니다.

현재 방향은 MediaPipe 자체를 DOTS-native로 재작성하는 것이 아니라, upstream MediaPipe 코어는 유지하면서 Unity 쪽 경계 비용을 줄이는 하이브리드 아키텍처를 구축하는 것입니다.

폴더명 컨벤션은 기본적으로 `PascalCase`를 사용합니다. 예외는 Unity 패키지명, 네이티브 툴체인 관례, 외부 서드파티 원본처럼 생태계 규칙을 따라야 하는 경우입니다.

## Current Status

- Unity 버전: `6000.3.10f1`
- **Phase 1 (Native Bridge) 완료**: macOS Apple Silicon 전용 C ABI bridge 빌드 및 Unity Editor 검증 통과
- Phase 2 이후 (웹캠 캡처 → ECS 연동 → 시각화) 는 미착수

## Repository Layout

```text
.
├── Docs/                      # Architecture notes, plans, decisions
├── Native/                    # Native bridge source and build scripts
├── MediaPipeUnityDOTS/        # Unity project root
│   ├── Assets/
│   │   ├── MediaPipeUnityDots/
│   │   │   ├── Runtime/
│   │   │   └── EditorTool/
│   │   ├── Plugins/
│   │   ├── MediaPipeUnityDotsSamples/
│   │   └── Scenes/
│   ├── Packages/
│   └── ProjectSettings/
└── README.md
```

저장소 루트에 문서와 네이티브 브리지 소스를 두고, Unity 프로젝트는 별도 하위 폴더에서 관리합니다. 구조 상세는 [`Docs/FolderStructure.md`](./Docs/FolderStructure.md) 참고.

## Quick Start

```bash
# 1. clone + submodule
git clone --recurse-submodules <repo-url>

# 2. 모델 다운로드
Native/Build/DownloadModels.sh

# 3. 네이티브 빌드 (macOS Apple Silicon)
Native/Build/BuildMacosEditor.sh

# 4. dylib → Unity Plugins 복사
Native/Build/CopyArtifactsToUnity.sh

# 5. Unity Editor에서 프로젝트 열기 → MediaPipe > Run Smoke Test
```

빌드 상세 및 필수 도구는 [`Native/README.md`](./Native/README.md) 참고.

## Native → Unity 산출물 흐름

```
Bazel build (Native/Upstream/mediapipe)
    ↓
Native/Artifacts/MacosEditor/libmpud_bridge.dylib   ← 빌드 산출물
    ↓  CopyArtifactsToUnity.sh (install_name 수정 + ad-hoc codesign)
MediaPipeUnityDOTS/Assets/Plugins/macOS/libmpud_bridge.dylib  ← Unity가 인식하는 위치
    ↓
C# DllImport("mpud_bridge")  ← Unity가 lib 접두사와 .dylib 확장자를 자동 해석
```

| 산출물 | 경로 | git 추적 |
|--------|------|---------|
| dylib (빌드 결과) | `Native/Artifacts/MacosEditor/libmpud_bridge.dylib` | ✗ gitignore |
| dylib (Unity 플러그인) | `Assets/Plugins/macOS/libmpud_bridge.dylib` | ✗ gitignore |
| dylib .meta | `Assets/Plugins/macOS/libmpud_bridge.dylib.meta` | ✓ 추적 |
| 모델 파일 | `Assets/StreamingAssets/MediaPipe/Models/hand_landmarker.task` | ✗ gitignore |
| 모델 .meta | `Assets/StreamingAssets/MediaPipe/Models/hand_landmarker.task.meta` | ✓ 추적 |

> `.dylib`와 `.task`는 용량이 크므로 git에서 제외합니다. clone 후 빌드/다운로드 스크립트로 재생성합니다.

### Unity에서의 플러그인 인식

- `Assets/Plugins/macOS/` 경로에 `.dylib`를 두면 Unity가 macOS 전용 네이티브 플러그인으로 자동 인식합니다.
- C# 측에서는 `[DllImport("mpud_bridge")]` 로 참조합니다 — Unity가 `lib` 접두사와 `.dylib` 확장자를 플랫폼별로 자동 붙입니다.
- `CallingConvention.Cdecl`을 명시해야 합니다 (C ABI bridge).

### C# Interop 위치

```
Assets/MediaPipeUnityDots/Runtime/Interop/
├── NativeStructs.cs    ← C 구조체 미러 (MpudHandResult, MpudImageFrame 등)
└── MpudBridge.cs       ← DllImport 선언 (6개 함수)
```

## Direction

핵심 목표:

1. MediaPipe 네이티브 그래프 실행은 유지
2. Unity ↔ Native 경계의 복사, 마샬링, GC 비용 최소화
3. 결과 후처리와 게임플레이 연동은 DOTS 파이프라인으로 구성

구현 방식:

- MediaPipe upstream 기반 네이티브 코어 + 얇은 C ABI bridge
- Unity 측에서는 `NativeArray` / unsafe pointer / Jobs 기반 후처리
- `MediaPipeUnityDots` 플러그인과 샘플을 하나의 Unity 프로젝트에 포함 (PoC 단계)

## Execution Docs

- Native intake and build path: [`Docs/MediaPipeNativeIntegrationPlan.md`](./Docs/MediaPipeNativeIntegrationPlan.md)
- First implementation slice: [`Docs/MediaPipePoCExecutionPlan.md`](./Docs/MediaPipePoCExecutionPlan.md)
