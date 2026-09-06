# Native

MediaPipe 네이티브 브리지 소스와 빌드 스크립트를 두는 위치입니다.

## 필요 도구

| 도구 | 설치 | 비고 |
|------|------|------|
| [bazelisk](https://github.com/bazelbuild/bazelisk) | `brew install bazelisk` | `BuildMacosEditor.sh`가 `Native/Upstream/mediapipe/.bazelversion`을 따라 현재 요구 Bazel 7.4.1인지 확인 후 아니면 즉시 실패 (v1.0.0 기준) |
| Xcode Command Line Tools | `xcode-select --install` | clang, ld, libtool 등 |
| Python 3.11 + numpy | `python3.11 -m venv /tmp/mp_build_venv && source /tmp/mp_build_venv/bin/activate && pip install numpy` | `BuildMacosEditor.sh`가 `HERMETIC_PYTHON_VERSION=3.11`를 기본값으로 사용 (MODULE.bazel toolchain) |
| OpenCV 4 | `brew install opencv@4` | HandLandmarker 런타임 의존성 (4.14.0, keg-only; `PREFIX=opt/opencv@4`) |

## Upstream

- `Native/Upstream/mediapipe/`: google-ai-edge/mediapipe git submodule
- Pinned tag: **v1.0.0** (SHA: `6d31f1ebc3284db74d211d62bdc4f0a0c29ea120`)

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

## 산출물과 모델

| 구분 | 경로 | 설명 |
|------|------|------|
| dylib (빌드 결과) | `Native/Artifacts/MacosEditor/libmpud_bridge.dylib` | Bazel 산출물 복사본 |
| dylib (Unity 플러그인) | `MediaPipeUnityDOTS/Assets/MediaPipeUnityDots/Runtime/Plugins/macOS/libmpud_bridge.dylib` | `CopyArtifactsToUnity.sh`가 복사, install name 보정, ad-hoc 서명 |
| 모델 | `MediaPipeUnityDOTS/Assets/StreamingAssets/MediaPipe/Models/` | 다운로드 대상. Git에는 `.meta`만 추적 |

`DownloadModels.sh`는 아래 공식 float16 task bundle을 내려받는다.

- `hand_landmarker.task`
- `face_landmarker.task`
- `pose_landmarker_full.task`
- `holistic_landmarker.task`

## 브리지 구성

| 종류 | 네이티브 브리지 | Unity 프레임 프로바이더 |
|------|------|------|
| Hand | `mpud_bridge.*` | `WebcamFrameProvider` |
| Face | `mpud_face_bridge.*` | `FaceFrameProvider` |
| Pose | `mpud_pose_bridge.*` | `PoseFrameProvider` |
| Holistic | `mpud_holistic_bridge.*` | `HolisticFrameProvider` |

`NativeStructs.cs`와 `MpudBridge.cs`는 네이티브 ABI를 C#에 미러한다. Holistic은 Face, Pose, Left/Right Hand 결과를 기존 ECS 추적 데이터로 전달한다.

## 검증

### Unity Editor

`CopyArtifactsToUnity.sh` 실행 후 Unity에서 **MediaPipe > Run Smoke Test**를 실행한다. 이 메뉴는 Hand tracker의 create/destroy와 플러그인 로딩을 확인한다.

### 네이티브 전체 스모크

합성 RGBA 프레임으로 Hand, Face, Pose, Holistic의 create/submit/poll/destroy를 확인한다. 먼저 `BuildMacosEditor.sh`로 bridge 동기화와 submodule 패치를 적용한 뒤 모든 모델을 내려받고 실행한다.

```bash
(cd Native/Upstream/mediapipe && \
  HERMETIC_PYTHON_VERSION=3.11 bazelisk \
    --bazelrc=../../Build/.bazelrc \
    build -c opt //mediapipe/mpud_bridge:mpud_smoke_test && \
  bazel-bin/mediapipe/mpud_bridge/mpud_smoke_test \
    ../../../MediaPipeUnityDOTS/Assets/StreamingAssets/MediaPipe/Models)
```

성공 기준: 마지막 줄이 `SMOKE OK`.

## 빌드 워크어라운드

`BuildMacosEditor.sh`는 fetch 뒤 아래 보정을 조건부로 적용한다.

1. **Hermetic Python 3.11** — 로컬 기본 Python과 무관하게 MediaPipe requirements lock에 맞춘다.
2. **macOS 26+ Bazel toolchain UUID** — `wrapped_clang` 및 `libtool_check_unique`에 `LC_UUID`가 없을 때만 재빌드한다.
3. **vendored zlib `fdopen` 매크로** — macOS 26 SDK와 충돌하는 정의를 제거한다.
4. **XNNPACK SME 오타** — 사용 중인 XNNPACK의 `XNN_ENABLE_SRM_SME`를 `XNN_ENABLE_ARM_SME`로 교정한다.

### Apple Silicon XNNPACK SME/SME2

`Native/Build/.bazelrc`는 다음 두 플래그를 항상 적용한다.

```text
--define=xnn_enable_arm_sme=false
--define=xnn_enable_arm_sme2=false
```

Holistic bundle의 양자화 추론이 Apple Silicon에서 SME/SME2 커널을 선택하면 `SIGILL`이 발생한다. 두 커널만 제외하며 XNNPACK CPU delegate와 NEON/I8MM/DotProd 경로는 유지한다. 위 오타 교정은 upstream XNNPACK이 수정되면 자동으로 건너뛴다.

## 패치와 동기화

`SyncBridgeIntoWorkspace.sh`는 `Native/Bridge/`의 헤더, 소스, BUILD overlay를 submodule의 `mediapipe/mpud_bridge/`로 복사하고 다음 패치를 멱등 적용한다.

| 패치 | 대상 | 역할 |
|------|------|------|
| `macos_arm64_compat.diff` | `WORKSPACE`, `opencv_macos.BUILD` | Apple Silicon Homebrew 경로와 OpenCV 4 include/layout |
| `module_compat.diff` | `MODULE.bazel` | Apple toolchain 우선 등록과 `rules_java` 호환 |

submodule HEAD는 v1.0.0에 고정하고, 동기화 복사본과 패치 적용 결과는 커밋하지 않는다.

## 디렉토리 구조

```text
Native/
├── Upstream/mediapipe/      # google-ai-edge/mediapipe submodule (v1.0.0)
├── Bridge/
│   ├── Include/             # Hand/Face/Pose/Holistic C ABI
│   ├── Src/                 # 브리지 구현과 mpud_smoke_test
│   └── BazelOverlay/        # libmpud_bridge와 스모크 BUILD
├── Build/                   # 다운로드, 동기화, 빌드, Unity 복사 스크립트
├── Patches/mediapipe/       # submodule에 멱등 적용할 호환 패치
└── Artifacts/MacosEditor/   # 생성된 dylib
```
