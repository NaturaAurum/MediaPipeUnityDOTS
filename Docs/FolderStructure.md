# Folder Structure

이 문서는 `MediaPipeUnityDOTS` 저장소의 초기 폴더 구조 원칙을 정리합니다.

## Naming Convention

- 기본 폴더명은 `PascalCase` 를 사용합니다.
- 예외는 아래와 같습니다.
- Unity embedded package의 실제 패키지 `name` 필드
- Bazel, CMake, Gradle 같은 외부 툴체인 규칙
- 외부 원본 구조를 그대로 반영해야 하는 예외 경로

## Repository Root

```text
.
├── Docs/
├── Native/
├── MediaPipeUnityDOTS/
└── README.md
```

각 폴더의 역할은 다음과 같습니다.

- `Docs`
  - 아키텍처 문서
  - PoC 범위 정의
  - 프로파일링 결과
  - 의사결정 기록
- `Native`
  - MediaPipe 브리지용 C/C++ 코드
  - 플랫폼별 빌드 스크립트
  - Unity로 전달할 ABI 정의
- `MediaPipeUnityDOTS`
  - 실제 Unity 프로젝트 루트
  - 초기 단계에서는 브리지와 샘플을 함께 포함하는 메인 작업 공간

## Unity Project Structure

Unity 프로젝트 폴더명은 이미 준비된 상태를 유지하여 `MediaPipeUnityDOTS` 를 사용합니다.

```text
MediaPipeUnityDOTS/
├── Assets/
│   ├── MediaPipeUnityDots/
│   │   ├── Runtime/
│   │   ├── Sample/
│   │   └── EditorTool/
│   ├── Plugins/
│   ├── Scenes/
│   └── Settings/
├── Packages/
│   ├── manifest.json
│   └── packages-lock.json
└── ProjectSettings/
```

역할 분리는 아래 기준을 권장합니다.

- `Assets/MediaPipeUnityDots`
  - 플러그인 본체 루트
- `Assets/MediaPipeUnityDots/Runtime`
  - C# interop 레이어
  - runtime code
  - DOTS systems and jobs
  - bootstrap code
- `Assets/MediaPipeUnityDots/EditorTool`
  - editor utility code 후보 위치
  - 초반에는 최소한으로 유지
  - Unity의 특수 `Editor` 폴더를 바로 쓰지 않기 위한 완충 영역
- `Assets/Plugins`
  - 최종 배포용 네이티브 바이너리
  - 플랫폼별 플러그인 import 설정 대상
- `Assets/MediaPipeUnityDots/Sample`
  - 샘플용 MonoBehaviour (웹캠, 스포너, 디버그 UI)
  - ECS 시각화 지원 (스포너 + 렌더 시스템)
  - 예제 데이터 흐름 (adapter DTO)
- `Assets/Scenes`
  - 실행 씬과 테스트 씬
- `Assets/Settings`
  - 렌더링, 입력, 프로젝트별 ScriptableObject 설정 자산
- `Packages`
  - 현재는 Unity package manager 기본 관리 영역
  - 패키지화는 추후 단계에서 검토

## Why This Split

이 구조는 지금 필요한 세 층만 분리하기 위한 것입니다.

1. `Native`
   - MediaPipe 코어와 맞닿는 네이티브 계층
2. `Assets/MediaPipeUnityDots`
   - Unity 플러그인 계층
3. `Assets/MediaPipeUnityDots/Sample`

이렇게 나누면 브리지 계층과 샘플 계층이 섞이지 않아 PoC 이후 패키지화가 쉬워집니다.

## Initial Recommendation

초기 PoC에서는 다음 순서로 진행하는 것이 안전합니다.

1. `Native` 에 최소 브리지 API 정의
2. `Assets/MediaPipeUnityDots/Runtime` 에 C# interop 레이어와 ECS 데이터 경로 작성
3. `Assets/MediaPipeUnityDots/Sample` 와 `Assets/Scenes` 에 단일 모델 검증 씬 구성
5. 구조가 안정되면 필요한 부분만 패키지화

## Notes

- 현재 `Assets/TutorialInfo` 는 Unity 템플릿 기본 자산으로 보이며, 추후 정리 대상입니다.
- `Assets/Scenes` 와 `Assets/Settings` 는 이미 존재하므로 그대로 유지하면서 `MediaPipeUnityDots`, `Plugins` 를 추가하는 쪽이 자연스럽습니다.
- 패키지화는 브리지 계층 경계가 충분히 안정된 뒤 진행하는 것이 좋습니다.

## Runtime 승격 후보 (Sample → Runtime)

`Sample/HandTracking/Scripts` 중 플러그인 코어 체질이라 패키징 시점에
`Runtime`으로 옮길 파일. 지금은 단일 레포·단방향 참조라 이동하지 않는다.

- 승격: `Webcam/Face/Pose/HolisticFrameProvider`, `HandTrackingAdapter`,
  `HandTrackingDto` (네이티브→ECS 진입점, 의존성 없음).
- 조건부 승격: `Face/Hand/PoseLandmarkPointSpawner` — Entities Graphics
  의존을 `Runtime` asmdef로 끌고 들어가므로 렌더링 의존 정리 후.
- 잔류: `OneEuroFilterSettingsPanel`, `HandTrackingStatusPanel`
  (App 레이어 UI), `WebcamBackgroundRenderer`, `WebcamBackgroundToggle`
  (데모 씬 전용), `NativeSmokeTest`(+Editor 러너, 진단용).

## 패키지 목표와 외부 소비 계약

이 저장소의 목표는 `Runtime`의 Unity 패키지(UPM)화다. 패키지 경계가
성립하려면 브리지를 통해 얻은 값이 외부에서 가공하기 쉬워야 한다.

- 외부 소비자는 `Runtime` 어셈블리만으로 값을 읽는다.
  `Sample`/UI 어셈블리 참조 없이 접근 가능해야 한다.
- 읽기 API(`Get*Landmark` 접근자, 스냅샷 복사 API)는 `Runtime`에 둔다.
  가공용 DTO·어댑터는 소비 측(App 레이어) 책임이다.
- ABI 변경은 세 가드로 버전 관리한다:
  네이티브 `static_assert` + C# `ExpectedSize` + `NativeAbiTests`.
- `Runtime`에 UI(App 레이어) 의존을 넣지 않는다
  (R3/UniTask/VContainer/UI Toolkit 금지 — AGENTS.md UI/ECS 경계).

## package.json 계획 (UPM 분리 시점)

- 위치: `Assets/MediaPipeUnityDots/package.json` (임베디드 패키지 루트).
- `name` 가칭: `com.natura-aurum.mediapipe-unity-dots`, `unity` 최소 `6000.0`
  (검증 환경 `6000.6.0f1`).
- `dependencies` (잠금 버전 기준, `packages-lock.json` 실측):
  - `com.unity.burst`: `2.0.0`
  - `com.unity.collections`: `6.6.0`
  - `com.unity.entities`: `6.6.0`
  - `com.unity.mathematics`: `1.4.0`
  - `com.unity.entities.graphics`: `6.6.0` — 조건부. 현재 Runtime C#에서
    직접 사용하지 않으므로 스포너 승격 시에만 포함.
- 포함하지 않는다: R3/UniTask/VContainer (git URL 의존성 + App 전용),
  URP, 테스트·프로파일러·IDE 등 에디터 전용 패키지.
- 레이아웃 변경: `Sample/` → `Samples~/` (UPM 샘플 관례),
  `Assets/Plugins`의 네이티브 바이너리는 패키지 내
  `Runtime/Plugins/<platform>/` 로 이동.
