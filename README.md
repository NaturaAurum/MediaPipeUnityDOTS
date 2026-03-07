# MediaPipeUnityDOTS

Unity DOTS (ECS + Jobs) 환경에서 MediaPipe를 성능 우선으로 통합하기 위한 실험용 저장소입니다.

현재 방향은 MediaPipe 자체를 DOTS-native로 재작성하는 것이 아니라, upstream MediaPipe 코어는 유지하면서 Unity 쪽 경계 비용을 줄이는 하이브리드 아키텍처를 구축하는 것입니다.

폴더명 컨벤션은 기본적으로 `PascalCase`를 사용합니다. 예외는 Unity 패키지명, 네이티브 툴체인 관례, 외부 서드파티 원본처럼 생태계 규칙을 따라야 하는 경우입니다.

## Current Status

- GitHub private repository 생성 완료
- Unity 프로젝트는 하위 폴더 [`MediaPipeUnityDOTS`](./MediaPipeUnityDOTS) 에 위치
- 현재 Unity 버전: `6000.3.10f1`
- 아직 MediaPipe 연동 코드는 시작 전이며, 프로젝트 구조와 통합 전략을 먼저 정리하는 단계

## Repository Layout

```text
.
├── Docs/                      # Architecture notes, plans, decisions
├── Native/                    # Native bridge source and build scripts
├── MediaPipeUnityDOTS/        # Unity project root
│   ├── Assets/
│   │   ├── MediaPipeUnityDots/
│   │   │   ├── Runtime/
│   │   │   └── EditorTool/
│   │   ├── Plugins/
│   │   ├── MediaPipeUnityDotsSamples/
│   │   └── Scenes/
│   ├── Packages/
│   └── ProjectSettings/
└── README.md
```

현재 저장소 루트는 문서와 네이티브 브리지 소스를 두는 공간으로 보고 있고, 실제 Unity 프로젝트는 별도 하위 폴더에서 관리합니다.

## Direction

핵심 목표는 아래 세 가지입니다.

1. MediaPipe 네이티브 그래프 실행은 유지
2. Unity <-> Native 경계의 복사, 마샬링, GC 비용 최소화
3. 결과 후처리와 게임플레이 연동은 DOTS 파이프라인으로 구성

예상 구현 축은 다음과 같습니다.

- `mediapipe` upstream 기반 네이티브 코어 사용
- `MediaPipeUnityPlugin` 에서 Unity 연동 노하우는 선별 재사용
- 얇은 C ABI 브리지 작성
- Unity 측에서는 `NativeArray` / unsafe pointer / Jobs 기반 후처리
- 초기 단계에서는 Unity embedded package 분리 없이, `MediaPipeUnityDOTS` 프로젝트 자체가 `MediaPipeUnityDots` 플러그인과 샘플을 함께 포함
- `MediaPipeUnityDots` 내부는 우선 `Runtime`, `EditorTool` 두 축으로만 시작

## Next Discussion

다음 단계에서는 폴더 구조를 아래 관점으로 정리할 예정입니다.

- Unity 프로젝트 내부 구조 (`Assets`, `Packages`, `ProjectSettings`)
- 네이티브 브리지 코드 위치
- 문서와 설계 산출물 위치
- PoC 단계와 패키지화 시점의 구조 분리 여부

구조 초안은 [`Docs/FolderStructure.md`](./Docs/FolderStructure.md) 에 정리합니다.

## Execution Docs

- Native intake and build path: [`Docs/MediaPipeNativeIntegrationPlan.md`](./Docs/MediaPipeNativeIntegrationPlan.md)
- First implementation slice: [`Docs/MediaPipePoCExecutionPlan.md`](./Docs/MediaPipePoCExecutionPlan.md)
