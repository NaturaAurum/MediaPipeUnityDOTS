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
│   │   └── EditorTool/
│   ├── Plugins/
│   ├── MediaPipeUnityDotsSamples/
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
- `Assets/MediaPipeUnityDotsSamples`
  - 샘플용 MonoBehaviour
  - 시각화
  - 디버그 UI
  - 예제 데이터 흐름
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
3. `Assets/MediaPipeUnityDotsSamples`
   - 검증 및 데모 계층

이렇게 나누면 브리지 계층과 샘플 계층이 섞이지 않아 PoC 이후 패키지화가 쉬워집니다.

## Initial Recommendation

초기 PoC에서는 다음 순서로 진행하는 것이 안전합니다.

1. `Native` 에 최소 브리지 API 정의
2. `Assets/MediaPipeUnityDots/Runtime` 에 C# interop 레이어와 ECS 데이터 경로 작성
3. `Assets/MediaPipeUnityDotsSamples` 와 `Assets/Scenes` 에 단일 모델 검증 씬 구성
4. editor 지원이 필요해질 때만 `Assets/MediaPipeUnityDots/EditorTool` 확장
5. 구조가 안정되면 필요한 부분만 패키지화

## Notes

- 현재 `Assets/TutorialInfo` 는 Unity 템플릿 기본 자산으로 보이며, 추후 정리 대상입니다.
- `Assets/Scenes` 와 `Assets/Settings` 는 이미 존재하므로 그대로 유지하면서 `MediaPipeUnityDots`, `Plugins`, `MediaPipeUnityDotsSamples` 를 추가하는 쪽이 자연스럽습니다.
- `EditorTool` 은 폴더 이름일 뿐이라 editor-only 컴파일 분리는 자동으로 생기지 않습니다. 실제 editor 전용 코드가 들어가면 이후 asmdef 또는 `#if UNITY_EDITOR` 정리가 필요합니다.
- 패키지화는 브리지 계층 경계가 충분히 안정된 뒤 진행하는 것이 좋습니다.
