# Native

MediaPipe 네이티브 브리지 소스와 빌드 스크립트를 두는 위치입니다.

## 필요 도구

| 도구 | 설치 | 비고 |
|------|------|------|
| [bazelisk](https://github.com/bazelbuild/bazelisk) | `brew install bazelisk` | `BuildMacosEditor.sh`가 `Native/Upstream/mediapipe/.bazelversion`을 따라 현재 요구 Bazel 7.4.1인지 확인 후 아니면 즉시 실패 |
| Xcode Command Line Tools | `xcode-select --install` | clang, ld, libtool 등 |
| Python 3.12 + numpy | `python3.12 -m venv /tmp/mp_build_venv && source /tmp/mp_build_venv/bin/activate && pip install numpy` | `BuildMacosEditor.sh`가 `HERMETIC_PYTHON_VERSION=3.12`를 기본값으로 사용 |
| OpenCV 4 | `brew install opencv` | HandLandmarker 런타임 의존성 |

## Upstream

- `Native/Upstream/mediapipe/`: google-ai-edge/mediapipe git submodule
- Pinned tag: **v0.10.33** (SHA: `3987048d4b390aa9ae675c796f6421bbeece6511`)

## 빌드 순서

```bash
# 1. submodule 초기화 (최초 1회)
git submodule update --init

# 2. 모델 파일 다운로드
Native/Build/DownloadModels.sh

# 3. numpy venv 활성화 (빌드 시 필요)
source /tmp/mp_build_venv/bin/activate

# 4. bridge 빌드 (bridge sync + 패치 + Bazel 빌드 + artifact 복사)
Native/Build/BuildMacosEditor.sh

# 5. Unity Plugins 경로로 복사 (install_name 수정 + ad-hoc codesign)
Native/Build/CopyArtifactsToUnity.sh
```

## 산출물 경로

| 산출물 | 경로 | 설명 |
|--------|------|------|
| dylib (빌드 결과) | `Native/Artifacts/MacosEditor/libmpud_bridge.dylib` | Bazel 빌드 산출물 복사본 |
| dylib (Unity 플러그인) | `MediaPipeUnityDOTS/Assets/Plugins/macOS/libmpud_bridge.dylib` | `CopyArtifactsToUnity.sh`가 복사 + fixup |
| 모델 파일 | `MediaPipeUnityDOTS/Assets/StreamingAssets/MediaPipe/Models/hand_landmarker.task` | `DownloadModels.sh`가 다운로드 |

> dylib와 모델 파일은 `.gitignore`에 등록되어 있습니다. clone 후 빌드/다운로드 스크립트로 재생성합니다.

## Unity에서의 플러그인 임포트

`CopyArtifactsToUnity.sh` 실행 후 Unity Editor를 열면 (또는 이미 열려있으면 자동 refresh) 플러그인이 인식됩니다.

### 동작 원리

1. `Assets/Plugins/macOS/` 디렉토리에 `.dylib`를 두면 Unity가 **macOS 전용 네이티브 플러그인**으로 자동 인식
2. C# 코드에서 `[DllImport("mpud_bridge")]`로 참조 — Unity가 플랫폼별로 `lib` 접두사 + `.dylib` 확장자를 자동 해석
3. `CopyArtifactsToUnity.sh`가 `install_name_tool -id @loader_path/libmpud_bridge.dylib`을 적용하여 Unity의 로딩 경로와 맞춤
4. macOS Gatekeeper를 위해 `codesign --force -s -` (ad-hoc 서명) 적용
5. `MediaPipeUnityDOTS/Assets/Plugins/macOS/libmpud_bridge.dylib.meta`에 Editor용 `PluginImporter` 설정을 저장해 fresh import drift를 줄임

### C# Interop 파일

```
Assets/MediaPipeUnityDots/Runtime/Interop/
├── NativeStructs.cs    ← C 구조체 미러 (MpudHandResult, MpudImageFrame, MpudHandTrackerConfig)
└── MpudBridge.cs       ← DllImport 선언 (6개 함수, CallingConvention.Cdecl)
```

### 검증 방법

Unity Editor 메뉴 → **MediaPipe > Run Smoke Test** 실행. Console에 아래 로그가 나오면 정상:

```
[MPUD Smoke] create_hand_tracker status: 0
[MPUD Smoke] Tracker created successfully!
[MPUD Smoke] destroy_hand_tracker completed
[MPUD Smoke] === Smoke Test Complete (no crash) ===
```

## 패치 파일

`Native/Patches/mediapipe/` 아래 패치들이 있으며, `SyncBridgeIntoWorkspace.sh`가 빌드 시 자동 적용합니다.

| 패치 | 대상 | 역할 |
|------|------|------|
| `macos_arm64_compat.diff` | WORKSPACE, `opencv_macos.BUILD` | Apple Silicon Homebrew 경로 + OpenCV 4 include/layout 반영 |
| `tasks_logging_analytics_compat.diff` | `tasks/cc/core/*`, `tasks/cc/core/logging/*` | 공개 OSS 태그에 없는 `mediapipe/util/analytics` 의존성을 제거하여 Hand Landmarker 빌드 복구 |

## macOS 26+ 빌드 워크어라운드

`BuildMacosEditor.sh`는 `bazelisk`를 통해 Bazel 7.4.1을 사용하며, 아래 보정을 조건부로 적용합니다:

1. **Hermetic Python 3.12 기본값** — 로컬 기본 `python3`가 3.14 이상이어도 `HERMETIC_PYTHON_VERSION=3.12`를 기본 적용해 MediaPipe의 requirements lock과 맞춥니다.
2. **`wrapped_clang` / `libtool_check_unique` LC_UUID 누락** — Bazel toolchain 바이너리에 `LC_UUID`가 없을 때만 소스에서 `-Wl,-random_uuid`를 붙여 재컴파일합니다.
3. **vendored zlib `fdopen` 매크로 충돌** — fetched `zutil.h`에 대상 블록이 존재할 때만 `fdopen` 재정의 블록을 안전하게 치환합니다. 이 보정은 macOS 26 SDK 충돌 대응을 위해 추가되었습니다.

> LC_UUID 보정은 이미 UUID가 있으면 skip되고, zlib 보정은 `zutil.h`에 대상 블록이 없으면 skip됩니다.

## 디렉토리 구조

```
Native/
├── Upstream/
│   └── mediapipe/          # git submodule (v0.10.33)
├── Bridge/
│   ├── Include/            # C ABI 헤더 (mpud_bridge.h)
│   ├── Src/                # C++ 구현 (mpud_bridge.cc)
│   └── BazelOverlay/       # BUILD 파일
├── Build/
│   ├── .bazelrc            # macOS Apple Silicon 빌드 설정
│   ├── SyncBridgeIntoWorkspace.sh
│   ├── BuildMacosEditor.sh
│   ├── CopyArtifactsToUnity.sh
│   └── DownloadModels.sh
├── Patches/
│   └── mediapipe/
│       ├── macos_arm64_compat.diff
│       └── tasks_logging_analytics_compat.diff
└── Artifacts/
    └── MacosEditor/        # 빌드 산출물 (.dylib)
```

## PoC 이후 전환 검토

현재 git submodule + file sync 방식을 사용합니다. 안정화 이후 `http_archive` + patches 방식(MediaPipeUnityPlugin과 동일)으로의 전환을 검토합니다.

- **submodule**: 디버깅 용이성, 로컬 수정 즉시 반영
- **http_archive**: 재현성과 패치 관리 통합, CI 환경에서 유리
