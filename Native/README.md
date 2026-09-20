# Native

네이티브 브리지 **개발/재빌드용** 문서입니다. UPM 소비자는 패키지에 동봉된 dylib를 사용하므로 Homebrew나 Bazel을 설치하지 않습니다.

## 빌드 기준

- MediaPipe submodule: `v1.0.0`, `6d31f1ebc3284db74d211d62bdc4f0a0c29ea120`.
- Bazel: upstream `.bazelversion`의 `7.4.1`. `BuildMacosEditor.sh`가 다른 버전을 거부합니다.
- 빌드 호스트: macOS Apple Silicon, Xcode/Command Line Tools, `python3`, `bazelisk`, Homebrew `opencv@4`.
- MediaPipe의 hermetic Python은 `3.11`로 고정합니다. 로컬 기본 Python 버전과 구분합니다.
- 현재 동봉 OpenCV는 `4.14.0_6`입니다. 정확한 의존 버전·소스 레시피는 생성된 `ThirdPartyLicenses`에 기록됩니다. `brew install`의 미래 최신 버전이 현재 번들과 같다고 가정하지 마세요.

```bash
git submodule update --init
Native/Build/BuildMacosEditor.sh
Native/Build/CopyArtifactsToUnity.sh
```

모델은 컴파일에 필요하지 않습니다. 추론 검증 전에 별도로 `Native/Build/DownloadModels.sh` 또는 Unity의 모델 메뉴를 실행합니다. 다운로드 기준은 패키지의 `EditorTool/ModelManifest.txt` 하나를 공유합니다.

## 산출물

```text
Native/Bridge + Patches → Native/Upstream/mediapipe/mediapipe/mpud_bridge
    → Bazel
Native/Artifacts/MacosEditor/libmpud_bridge.dylib
    → PackageMacosNative.py
MediaPipeUnityDOTS/Assets/MediaPipeUnityDots/Runtime/Plugins/macOS/
    libmpud_bridge.dylib + 비시스템 전이 의존 dylib
    native-dependency-manifest.txt
    THIRD_PARTY_LICENSES.txt + ThirdPartyLicenses/
```

빌드 중간 산출물은 Git에서 제외하고, **패키지 내 완성된 dylib와 importer metadata는 Git에 포함**합니다. 현재 번들은 12개 arm64 dylib, 45,401,664바이트입니다. `.task`·`.onnx` 모델은 Git에 포함하지 않습니다.

`CopyArtifactsToUnity.sh`는 `PackageMacosNative.py`를 호출합니다. 패키징은 다음을 검사합니다.

1. `otool` 의존 그래프에서 macOS 시스템 프레임워크를 제외한 dylib를 재귀 수집합니다.
2. 실제 symlink 대상을 복사하고 arm64 슬라이스, install name, 전이 의존 경로를 정리합니다.
3. 비시스템 로드 경로를 `@loader_path/<동봉 파일>`로 바꾸고 절대/Homebrew 경로나 누락 의존성을 거부합니다.
4. 모든 dylib를 ad-hoc 서명하고 서명·의존성·플랫폼을 검사합니다.
5. 명시적 macOS/ARM64 Unity PluginImporter를 만들고, 바이너리 해시·크기·최소 OS·빌드 입력 해시를 기록합니다.
6. Homebrew 라이브러리의 라이선스와 실제 설치 소스 레시피, Bazel C/C++ 의존 그래프의 정적 라이브러리 고지를 수집합니다.

브리지 자체는 macOS 11로 컴파일되지만 현재 OpenCV 등 의존성은 26.0을 요구합니다. **완성 번들의 실제 최소 OS는 macOS 26.0**입니다. 브리지 플래그 하나만 보고 지원 하한을 낮추지 마세요. 사용자가 배포하는 앱의 최종 서명·공증은 앱 배포자의 작업입니다.

## 패치와 빌드 보정

`SyncBridgeIntoWorkspace.sh`는 `Native/Bridge/`를 복사하고 다음 패치를 멱등 적용합니다.

| 패치 | 역할 |
| --- | --- |
| `macos_arm64_compat.diff` | Apple Silicon Homebrew 경로와 OpenCV include/layout |
| `module_compat.diff` | Apple toolchain 우선 등록, Bzlmod/Java 호환 |
| `opencv_minimal_macos.diff` | MediaPipe에서 실제 사용하는 OpenCV core/imgproc/calib3d 및 전이 의존으로 링크 범위 축소 |

`BuildMacosEditor.sh`는 fetch 후 macOS 26 toolchain의 누락된 `LC_UUID`, vendored zlib의 `fdopen` 매크로 충돌, XNNPACK의 `XNN_ENABLE_SRM_SME` 오타를 필요한 경우에만 보정합니다. `.bazelrc`의 `xnn_enable_arm_sme=false`, `xnn_enable_arm_sme2=false`는 Holistic에서 재현된 `SIGILL`을 피하며 XNNPACK CPU delegate와 다른 지원 커널은 유지합니다.

submodule HEAD는 고정하고 동기화 복사본/패치 적용 결과를 upstream 커밋으로 만들지 않습니다. 기존 작업 디렉터리에 사용자 수정이 있으면 별도 깨끗한 checkout에서 재현하세요.

## 검증

### 실제 결과가 필요한 릴리스 검증

저장소의 `Tools/ConsumerSmoke/consumer_smoke.py`로 **지정 Git 리비전**을 빈 프로젝트에 설치합니다. 실제 사진으로 Hand/Face/Pose/Holistic 검출, raw/world 복사, One Euro 평활화, 소비자 필터, reset·미검출·dispose, macOS Mono Player를 검사합니다. Player는 네트워크와 Homebrew 파일 읽기를 차단합니다. 명령은 [루트 README](../README.md)에 있습니다.

깨끗한 upstream checkout에서 이번 브리지를 재빌드·재패키징한 결과는 기존 번들과 동일한 SHA-256이었습니다.

```text
libmpud_bridge.dylib
72cd9fc49b3354f5198aa7383b2c11d3bba42115b18fd849cfb4261af6080a6c
```

이 기록은 동일 호스트에서의 재현 증거입니다. 별도 Mac에서 Homebrew/Bazel 없이 실행하는 검증과 혼동하지 않습니다. 컴파일러/SDK/Homebrew 버전을 바꾼 모든 빌드의 byte-for-byte 동일성을 보장하는 정책은 아닙니다.

### 네이티브 ABI 스모크

빌드와 모델 준비 후 upstream workspace에서 실행합니다.

```bash
bazelisk --bazelrc=../../Build/.bazelrc build -c opt //mediapipe/mpud_bridge:mpud_smoke_test
bazel-bin/mediapipe/mpud_bridge/mpud_smoke_test ../../../MediaPipeUnityDOTS/Assets/StreamingAssets/MediaPipe/Models
```

합성 입력의 create/submit/poll/destroy를 확인하며 성공 표시는 `SMOKE OK`입니다. 실제 랜드마크 검출 성공을 대신하는 검사는 아닙니다.

## 라이선스

[패키지 Third Party Notices](../MediaPipeUnityDOTS/Assets/MediaPipeUnityDots/Third%20Party%20Notices.md)를 확인하세요. 특히 OpenBLAS를 통해 동봉되는 GNU 런타임에는 GNU 라이선스와 해당 예외가 적용됩니다. 라이선스 원문·NOTICE·대응 소스 안내를 유지해야 하며 전체 번들을 MIT로 재표기하지 않습니다. 바이너리를 교체하면 의존 그래프, 고지, 대응 소스, 지원 하한과 소비자 검증을 함께 갱신합니다.
