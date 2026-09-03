# MediaPipe Windows Editor Support Plan

Status: 계획만 확정, 미착수. macOS Editor (v1.0.0, `6d31f1e`) 정상 동작을 기준으로 Windows 포트 시 변경 범위를 고정한다.

## Goal

`mpud_bridge`를 Windows Editor (`x64`, CPU-only)에서 macOS와 동일한 C ABI로 빌드하고, `MediaPipeUnityDOTS/Assets/Plugins/x86_64/mpud_bridge.dll`로 로드한다. C ABI와 C# Interop은 변경하지 않는다.

## Check 결과 (2026-09-04 기준)

준비됨 — 수정 불필요:
- `Native/Bridge/Include/mpud_bridge.h:8-12`: `_WIN32 → __declspec(dllexport)` 분기 존재. `extern "C"` + POD + 고정배열이라 MSVC ABI 문제 없음.
- `MpudBridge.cs:12`: `DllName = "mpud_bridge"`는 Windows에서 `mpud_bridge.dll`로 자동 해석. `Cdecl`, `IntPtr`/`long(8B)`/`fixed float[105]` 매핑 정상.
- `mpud_bridge.cc`: POSIX 호출 없음 (`thread_local`, `strncpy`, `memset`, `memcpy`, lambda deleter만 사용).
- `macos_arm64_compat.diff`: `opencv_macos.BUILD`만 수정하므로 Windows 빌드에 무해.

막힘 — Windows 대응 작업 필요:
1. `Native/Bridge/BazelOverlay/BUILD:7,15-16`: `copts = ["-fvisibility=hidden"]`는 MSVC에서 거부됨. `select()` 분기 + `mpud_bridge.dll` 타깃 추가 필요.
2. `Native/Build/.bazelrc:6-8`: `--cpu=darwin_arm64`, `--apple_platform_type`, `--macos_minimum_os`는 mac 전용. `x64_windows` + MSVC `/std:c++20` config 필요.
3. `Native/Build/BuildMacosEditor.sh:85-192`: `LC_UUID`/`zlib fdopen`/`libmpud_bridge.dylib` 경로 전부 mac 가정. `BuildWindowsEditor.ps1` (또는 `.cmd`) twin 필요.
4. `Native/Build/CopyArtifactsToUnity.sh:22-31`: `install_name_tool`/`codesign`/`otool`은 mac 전용. Windows는 `Assets/Plugins/x86_64/` 복사만 (서명 생략) + `.meta` (`OS=Windows, CPU=x86_64`) 필요. 현재 `Assets/Plugins/` 하위에 `macOS/`만 존재.
5. `.gitignore:57-58`: `MacosEditor/*.dylib`, `Plugins/macOS/*.dylib`만 무시. `Artifacts/WindowsEditor/*.dll`, `Plugins/x86_64/*.dll` 항목 필요.
6. OpenCV Windows 경로 미확정: upstream `third_party/opencv_windows.BUILD`는 존재하나 로컬 `C:\opencv` vs vcpkg + 4.14 레이아웃 실측 필요.

진짜 리스크는 우리 코드가 아니라 upstream `HandLandmarker` 의존성 그래프가 Windows Bazel 7.4.1 + VS2022에서 컴파일되는지 여부. Windows 머신 실빌드 전에는 확언 불가.

## 변경 범위 (착수 시)

| # | 파일 | 변경 |
|---|------|------|
| 1 | `Native/Bridge/BazelOverlay/BUILD` | `copts`를 `select()`로 분리 (MSVC: `/W0` 또는 제거), `cc_binary(name = "mpud_bridge.dll", ...)` 추가 |
| 2 | `Native/Build/.bazelrc` | `build:windows` config (`--cpu=x64_windows`, `--define=MEDIAPIPE_DISABLE_GPU=1`, MSVC C++20) |
| 3 | `Native/Build/BuildWindowsEditor.ps1` | 신규. Sync → `bazel build --config=windows //mediapipe/mpud_bridge:mpud_bridge.dll` → `Artifacts/WindowsEditor/` 복사 |
| 4 | `Native/Build/CopyArtifactsToUnity.sh` 또는 신규 `CopyArtifactsToUnityWindows.ps1` | `Assets/Plugins/x86_64/mpud_bridge.dll` 복사, `install_name`/`codesign` 생략 |
| 5 | `.gitignore` | `Native/Artifacts/WindowsEditor/*.dll`, `MediaPipeUnityDOTS/Assets/Plugins/x86_64/*.dll` 추가 |
| 6 | `Assets/Plugins/x86_64/` | `mpud_bridge.dll.meta` (PluginImporter Windows x86_64) 추가. `.dll` 본체는 git 제외 |
| 7 | `Native/README.md` | Windows 전제조건 (VS2022, MSVC, Python 3.11, OpenCV 4.14 경로) + 빌드 순서追記 |

C/C#/`.task` 모델 경로는 변경 없음.

## 전제조건 (Windows 머신)

- Bazelisk → Bazel 7.4.1 (`.bazelversion` 일치 확인)
- Visual Studio 2022 + MSVC (C++20), Windows SDK
- Python 3.11 + numpy (`HERMETIC_PYTHON_VERSION=3.11`)
- OpenCV 4.14 (`opencv_windows.BUILD`의 `path`와 일치시킬 것)
- `MEDIAPIPE_DISABLE_GPU=1` 유지 (CPU-only)

## 검증 순서

1. `SyncBridgeIntoWorkspace.sh` 그대로 실행 (두 패치 `Already applied` 확인)
2. `bazel build --config=windows //mediapipe/mpud_bridge:mpud_bridge.dll -c opt`
3. `dumpbin /EXPORTS mpud_bridge.dll`에 6개 `mpud_*` 심볼 확인
4. Unity Windows Editor → **MediaPipe > Run Smoke Test** → `create_hand_tracker status: 0` + `Smoke Test Complete` 확인

## Explicit Non-Goals

- GPU/DirectML 경로 없음 (CPU-only 유지)
- Android/iOS/UWP 없음
- OpenCV 5 대응 없음 (당분간 `opencv@4` 고정)
- IL2CPP Player 빌드 검증은 Editor 통과 이후로 연기
