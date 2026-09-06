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
  - C# interop 레이어 (`Interop`), 입력 유틸 (`Input`), 로깅 (`Logging`)
  - `Ecs/Common|Hand|Face|Pose`: 컴포넌트·매핑·필터·스포너·렌더 시스템
  - `Tracking/Hand|Face|Pose|Holistic`: 서비스·스냅샷·프로바이더
    (+`Tracking` 루트의 공유 `WebcamFrameProvider`, `Hand`의 읽기 API)
  - `Plugins/<platform>`: 네이티브 바이너리
  - 폴더만 나누고 네임스페이스는 `Runtime.Ecs`/`Runtime.Tracking` 유지
- `Assets/MediaPipeUnityDots/EditorTool`
  - editor utility code 후보 위치
  - 초반에는 최소한으로 유지
  - Unity의 특수 `Editor` 폴더를 바로 쓰지 않기 위한 완충 영역
- `Assets/Plugins`
  - 비어 있음. 네이티브 바이너리는 `Runtime/Plugins/<platform>/` 로 이동 완료.
- `Assets/MediaPipeUnityDots/Sample`
  - 데모 씬 전용 MonoBehaviour (디버그 UI, 웹캠 배경, 스모크 테스트)
  - 프레임 프로바이더·스포너·어댑터/DTO는 `Runtime`으로 승격 완료
    (아래 "Runtime 승격" 참조)
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

## Runtime 승격 (완료)

`Sample/HandTracking/Scripts`에서 플러그인 코어 체질을 `Runtime`으로 이동했다.

- 승격됨: `Webcam/Face/Pose/HolisticFrameProvider`, `HandTrackingAdapter`,
  `HandTrackingDto` → `Tracking/Hand|Face|Pose|Holistic` (+공유 웹캠은 `Tracking` 루트).
  표시층은 `Ecs/Common`으로 통일: `LandmarkPointSpawner`·`LandmarkRenderSystem`·`LandmarkRender`
  (`LandmarkPoint`/`LandmarkTracker`/`IPointSource`). 트래커별 포인트 태그·스포너·렌더 3종은 삭제.
- 잔류: `OneEuroFilterSettingsPanel`, `HandTrackingStatusPanel`
  (App 레이어 UI), `WebcamBackgroundRenderer`, `WebcamBackgroundToggle`
  (데모 씬 전용), `NativeSmokeTest`(+Editor 러너, 진단용).

## 패키지 목표와 외부 소비 계약

이 저장소의 목표는 `Runtime`의 Unity 패키지(UPM)화다. 패키지 경계가
성립하려면 브리지를 통해 얻은 값이 외부에서 가공하기 쉬워야 한다.

- 외부 소비자는 `Runtime` 어셈블리만으로 값을 읽는다.
  공식 읽기 API는 `HandTrackingAdapter`/`HandTrackingDto`
  (`Runtime/Tracking`)이며, `Sample`/UI 어셈블리 참조 없이 접근 가능하다.
- 읽기 API(`Get*Landmark` 접근자, 스냅샷 복사 API, 어댑터/DTO)는 `Runtime`에 둔다.
- `Runtime`에 UI(App 레이어) 의존을 넣지 않는다
  (R3/UniTask/VContainer/UI Toolkit 금지 — AGENTS.md UI/ECS 경계).

## package.json (생성됨)

- 위치: `Assets/MediaPipeUnityDots/package.json` (임베디드 패키지 루트).
- `name` 가칭: `com.natura-aurum.mediapipe-unity-dots` (`0.1.0`),
  `unity` 최소 `6000.0` (검증 환경 `6000.6.0f1`).
- `dependencies` (잠금 버전 기준, `packages-lock.json` 실측):
  - `com.unity.burst`: `2.0.0`
  - `com.unity.collections`: `6.6.0`
  - `com.unity.entities`: `6.6.0`
  - `com.unity.mathematics`: `1.4.0`
  - `com.unity.entities.graphics`: `6.6.0` — 스포너가 `Unity.Rendering`을
    사용하므로 필수.
- 레이아웃: `Sample/`은 유지한다. 이 저장소가 패키지 원본과 데모 프로젝트를
  겸하므로 UPM 관례 `Samples~/` 전환은 저장소 분리 시점으로 연기한다.
  네이티브 바이너리는 패키지 내 `Runtime/Plugins/<platform>/` 로 이동 완료.
