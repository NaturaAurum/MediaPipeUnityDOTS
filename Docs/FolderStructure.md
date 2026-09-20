# Folder Structure

이 문서는 현재 UPM 패키지 원본과 개발 프로젝트의 경계를 설명합니다. 기본 폴더명은 PascalCase이며 `Samples~`, UPM 이름, 외부 툴체인/서드파티 경로는 해당 생태계 규칙을 따릅니다.

```text
Native/
├── Bridge/                     # C ABI 구현과 Bazel overlay
├── Build/                      # 빌드·모델 다운로드·의존 번들 패키징
├── Patches/mediapipe/           # 고정 upstream에 적용하는 패치
├── Upstream/mediapipe/          # 고정 Git submodule, 사용자 작업 보존
└── Artifacts/MacosEditor/       # 로컬 빌드 중간 산출물, Git 제외
MediaPipeUnityDOTS/
├── Assets/
│   ├── MediaPipeUnityDots/      # UPM 패키지 원본
│   │   ├── package.json
│   │   ├── Runtime/
│   │   │   ├── Interop/        # C 구조체 및 DllImport
│   │   │   ├── Input/          # 입력 처리 유틸리티
│   │   │   ├── Models/         # 소비자 모델 경로 API (코드)
│   │   │   ├── Tracking/       # 서비스·스냅샷·Provider·이름 있는 topology
│   │   │   │   └── Filtering/  # managed 필터 계약/coordinator/기본 구현
│   │   │   ├── Ecs/            # unmanaged 원시/필터 버퍼·시스템·표시 매핑
│   │   │   └── Plugins/macOS/  # 배포용 dylib 전체 의존 그래프·고지·manifest
│   │   ├── EditorTool/         # 모델 준비·연결·빌드 검사
│   │   ├── Tests/EditMode/     # Samples 어셈블리를 참조하지 않는 코어 테스트
│   │   ├── Samples~/
│   │   │   ├── TrackingDemo/   # UI·씬·프로필·URP 설정·샘플 테스트
│   │   │   └── LandmarkApi/    # 렌더 없는 실제 입력 소비자 검증
│   │   └── Models/            # 개발 중 다운로드한 Depth 모델, Git 제외
│   ├── Samples/               # 선택 Import한 생성 복사본, Git 제외
│   └── StreamingAssets/
│       └── MediaPipe/Models/  # 다운로드한 Task 모델, Git 제외
├── Packages/                  # 개발 프로젝트의 UPM manifest/lock
└── ProjectSettings/           # 개발용 Unity 설정
Tools/
├── ImportSamples.py           # 개발 프로젝트에서 선언된 샘플 Import
└── ConsumerSmoke/             # 빈 Git 소비자 및 macOS Player 검증
Docs/                          # 설계·계획·검증 기록
```

## 패키지와 소비자 자산

- UPM URL의 하위 경로는 `/MediaPipeUnityDOTS/Assets/MediaPipeUnityDots`입니다. 루트 개발 프로젝트 전체를 의존성으로 설치하지 않습니다.
- 패키지 의존성과 최소 Unity 버전은 `package.json`이 기준입니다. Inference Engine을 포함한 코어 의존성은 패키지가 선언합니다.
- 네이티브 배포 dylib는 `Runtime/Plugins/macOS` 안에 Git 추적하며 소비자가 빌드하지 않습니다. `Native/Artifacts`의 중간 파일과 모델은 배포 Git에 넣지 않습니다.
- `Runtime/Models`는 코드이며, 패키지 루트의 다운로드 자산용 `Models`와 다릅니다. 패키징 시 이름이 같다는 이유로 둘 다 제외하면 안 됩니다.
- 다운로드 도구는 등록된 package resolvedPath에서 manifest를 **읽고**, 모델은 소비자 `Assets`에 **씁니다**. PackageCache는 수정하지 않습니다.

## Samples 경계

`Samples~`는 설치만으로 컴파일되지 않습니다. Package Manager 또는 `Tools/ImportSamples.py`로 선택 Import하면 `Assets/Samples/MediaPipe Unity DOTS/<version>/<displayName>` 아래 복사본이 만들어집니다. 개발 프로젝트의 Build Settings도 Import한 Tracking Demo 씬을 사용합니다.

샘플 원본 변경은 `Samples~`에서 하고, 개발용 복사본은 `Tools/ImportSamples.py --replace`로 갱신합니다. Tracking Demo의 씬·프로필·UI·테마·URP 자산은 샘플 안에 유지합니다. 소비자별 Graphics/Quality, 카메라 권한, 선택 모델 설정은 프로젝트가 소유합니다.

코어 테스트는 `MediaPipeUnityDots.Sample`을 참조하지 않습니다. UI 상호작용 테스트는 Tracking Demo의 Editor 테스트 어셈블리에 있으며, 실제 패널 이벤트가 필요하므로 `-nographics`로 검증하지 않습니다.

## 데이터/실행 경계

공개 서비스는 caller-owned 배열 복사와 상태·캡처 메타데이터를 제공합니다. managed `ILandmarkFilter`는 관리 계층의 동기 확장 지점입니다. ECS 컴포넌트·버퍼·Job에는 인터페이스, ViewModel, DI, UniTask, ReactiveProperty를 넣지 않습니다.

기본 `LandmarkFilterSystem`은 렌더 엔티티 없이 별도 필터 버퍼를 만듭니다. 렌더러는 그 결과의 표시 좌표만 계산하며 같은 필터를 다시 적용하지 않습니다. topology·좌표·수명·연속성 규칙은 [패키지 API 문서](../MediaPipeUnityDOTS/Assets/MediaPipeUnityDots/README.md)를 참고하세요.
