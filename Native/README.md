# Native

MediaPipe 네이티브 브리지 소스와 빌드 스크립트를 두는 위치입니다.

## 필요 도구

- **Bazel**: [bazelisk](https://github.com/bazelbuild/bazelisk) 권장 (`brew install bazelisk`)
  - upstream `.bazelversion` 파일 기준: Bazel 6.1.1
- **Xcode Command Line Tools**: `xcode-select --install`
- **Python 3**: MediaPipe 빌드 의존성

## Upstream

- `Native/Upstream/mediapipe/`: google-ai-edge/mediapipe git submodule
- Pinned tag: **v0.10.14** (SHA: `4cf89a70942ca3252e46ace7e4552f53be9bef2e`)

## 빌드 순서

```bash
# 1. submodule 초기화 (최초 1회)
git submodule update --init

# 2. 모델 파일 다운로드
Native/Build/DownloadModels.sh

# 3. bridge 빌드 (bridge sync + Bazel 빌드 + artifact 복사)
Native/Build/BuildMacosEditor.sh

# 4. Unity Plugins 경로로 복사
Native/Build/CopyArtifactsToUnity.sh
```

## 산출물 경로

- 빌드 결과: `Native/Artifacts/MacosEditor/libmpud_bridge.dylib`
- Unity 복사 대상: `MediaPipeUnityDOTS/Assets/Plugins/macOS/libmpud_bridge.dylib`
- 모델 파일: `MediaPipeUnityDOTS/Assets/StreamingAssets/MediaPipe/Models/hand_landmarker.task`

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
│       └── visibility.diff # upstream BUILD visibility 패치
└── Artifacts/
    └── MacosEditor/        # 빌드 산출물 (.dylib)
```

## PoC 이후 전환 검토

현재 git submodule + file sync 방식을 사용한다. 안정화 이후 `http_archive` + patches 방식(MediaPipeUnityPlugin과 동일)으로의 전환을 검토한다.

- **submodule**: 디버깅 용이성, 로컬 수정 즉시 반영
- **http_archive**: 재현성과 패치 관리 통합, CI 환경에서 유리
