HANDOFF CONTEXT
===============

USER REQUESTS (AS-IS)
---------------------
- mediapipe를 최신화 할 수 있나요?
- 네 부탁드립니다.

GOAL
----
`Native/Upstream/mediapipe`를 최신 안정 태그로 올리고, 현재 macOS Editor baseline에서 native bridge 빌드와 smoke 검증까지 다시 통과시킨다.

WORK COMPLETED
--------------
- MediaPipe submodule을 `v0.10.14`에서 `v0.10.33`으로 업데이트했다.
- `Native/Patches/mediapipe/visibility.diff`는 최신 upstream에서 불필요해 삭제했다.
- `Native/Patches/mediapipe/macos_arm64_compat.diff`를 `v0.10.33` 기준으로 재작성했다.
- `Native/Patches/mediapipe/tasks_logging_analytics_compat.diff`를 추가해 공개 OSS 태그에 없는 `mediapipe/util/analytics` 의존성을 제거했다.
- `Native/Build/.bazelrc`의 C++ 표준을 `c++20`으로 올렸다.
- `Native/Build/BuildMacosEditor.sh`를 최신 upstream 기준 distdir archive(`rules_cc 0.1.4`, `rules_foreign_cc 0.12.0`, `rules_proto_grpc 4.2.0`)와 `HERMETIC_PYTHON_VERSION=3.12` 기본값을 사용하도록 수정했다.
- `Native/Build/BuildMacosEditor.sh`로 `libmpud_bridge.dylib`를 재빌드했다.
- `Native/Build/CopyArtifactsToUnity.sh`로 Unity 플러그인 경로에 dylib를 다시 복사했다.
- 열려 있는 Unity 6000.4.1f1 인스턴스에서 `MediaPipe/Run Smoke Test`를 실행해 `create_hand_tracker status: 0`, `Tracker created successfully!`, `destroy_hand_tracker completed`, `Smoke Test Complete` 로그를 확인했다.

CURRENT STATE
-------------
- submodule HEAD: `3987048d4b390aa9ae675c796f6421bbeece6511 (v0.10.33)`
- `.bazelversion`: `7.4.1`
- Unity project는 현재 열려 있어 별도 batchmode smoke는 Unity가 차단한다. 검증은 open-editor 경로로 수행했다.
- `Native/Upstream/mediapipe`는 계속 dirty 상태로 남는다. 이유는 `SyncBridgeIntoWorkspace.sh`가 bridge 파일과 patch를 submodule working tree에 적용하기 때문이다.

VERIFICATION
------------
- `bash -n Native/Build/BuildMacosEditor.sh Native/Build/SyncBridgeIntoWorkspace.sh Native/Build/CopyArtifactsToUnity.sh`
- `git diff --check`
- `Native/Build/BuildMacosEditor.sh`
- `Native/Build/CopyArtifactsToUnity.sh`
- open-editor smoke via MCP:
  - `execute_menu_item("MediaPipe/Run Smoke Test")`
  - `read_console`에서 smoke success 로그 확인

KNOWN RISKS / FOLLOW-UP
-----------------------
- link 단계에서 OpenCV dylib들이 macOS 26.0 타깃으로 빌드되었다는 경고가 남는다. 현재 Editor smoke는 통과했지만 runtime/배포 검증은 별도다.
- open-editor smoke는 성공했지만, 프로젝트가 닫힌 상태에서의 batchmode smoke는 이번 턴에서 재검증하지 못했다.
- `tasks_logging_analytics_compat.diff`는 upstream `v0.10.33` 공개 태그의 누락 의존성 회피용 패치다. 차후 upstream에서 공식 수정되면 제거 가능성을 먼저 검토해야 한다.

KEY FILES
---------
- `Native/Build/.bazelrc`
- `Native/Build/BuildMacosEditor.sh`
- `Native/Patches/mediapipe/macos_arm64_compat.diff`
- `Native/Patches/mediapipe/tasks_logging_analytics_compat.diff`
- `Native/README.md`
- `Docs/MediaPipeNativeIntegrationPlan.md`
- `Docs/MediaPipePoCImplementationPlan.md`
