# Native

MediaPipe 네이티브 브리지 소스와 빌드 스크립트를 두는 위치입니다.

## 필요 도구

| 도구 | 설치 | 비고 |
|------|------|------|
| [bazelisk](https://github.com/bazelbuild/bazelisk) | `brew install bazelisk` | upstream `.bazelversion` 기준 Bazel 6.1.1 자동 선택 |
| Xcode Command Line Tools | `xcode-select --install` | clang, ld, libtool 등 |
| Python 3 + numpy | `python3 -m venv /tmp/mp_build_venv && source /tmp/mp_build_venv/bin/activate && pip install numpy` | MediaPipe 빌드 의존성 |
| OpenCV 4 | `brew install opencv` | HandLandmarker 런타임 의존성 |

## Upstream

- `Native/Upstream/mediapipe/`: google-ai-edge/mediapipe git submodule
- Pinned tag: **v0.10.14** (SHA: `4cf89a70942ca3252e46ace7e4552f53be9bef2e`)

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

`Native/Patches/mediapipe/` 아래 두 개의 패치가 있으며, `SyncBridgeIntoWorkspace.sh`가 빌드 시 자동 적용합니다.

| 패치 | 대상 | 역할 |
|------|------|------|
| `visibility.diff` | 9개 upstream BUILD 파일 | `cc_library` visibility를 `public`으로 변경 (bridge 빌드에 필요) |
| `macos_arm64_compat.diff` | WORKSPACE, opencv_macos.BUILD | Apple Silicon Homebrew 경로 + OpenCV 4 + `rules_cc` sha256 |

## macOS 26+ 빌드 워크어라운드

`BuildMacosEditor.sh`는 Bazel 6.1.1 + macOS 26 조합에서 발생하는 문제 두 가지를 자동으로 감지하고 패치합니다:

1. **`wrapped_clang` / `libtool_check_unique` LC_UUID 누락** — Bazel 6이 생성하는 toolchain 바이너리에 `LC_UUID`가 없어 macOS 26의 `dyld`가 로딩을 거부합니다. 소스에서 `-Wl,-random_uuid`를 붙여 재컴파일합니다.
2. **vendored zlib `fdopen` 매크로 충돌** — MediaPipe가 포함하는 zlib의 `zutil.h`에서 `fdopen`을 NULL로 재정의하는 매크로가 SDK 26.2의 `_stdio.h` 선언과 충돌합니다. 해당 매크로를 제거합니다.

> 이 워크어라운드는 macOS 25 이하에서는 실행되지 않습니다 (바이너리에 이미 LC_UUID가 있으면 skip).

## 디렉토리 구조

```
Native/
├── Upstream/
│   └── mediapipe/          # git submodule (v0.10.14)
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
│       ├── visibility.diff
│       └── macos_arm64_compat.diff
└── Artifacts/
    └── MacosEditor/        # 빌드 산출물 (.dylib)
```

## PoC 이후 전환 검토

현재 git submodule + file sync 방식을 사용합니다. 안정화 이후 `http_archive` + patches 방식(MediaPipeUnityPlugin과 동일)으로의 전환을 검토합니다.

- **submodule**: 디버깅 용이성, 로컬 수정 즉시 반영
- **http_archive**: 재현성과 패치 관리 통합, CI 환경에서 유리
