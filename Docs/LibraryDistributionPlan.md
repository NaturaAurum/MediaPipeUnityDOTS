# 라이브러리 배포 및 랜드마크 API 정비 계획

## 1. 목적과 범위

프로젝트의 주 목적은 다른 Unity 프로젝트가 GitHub Package URL로 설치하여 랜드마크를 활용할 수 있는 라이브러리를 제공하는 것이다. 샘플 시각화의 고도화보다 설치 완결성, 결과 데이터 계약, 렌더링에 독립적인 API를 우선한다.

목표 소비자 흐름:

1. Unity Package Manager에서 Git URL로 설치한다.
2. 사용할 모델을 준비하고 입력을 연결한다.
3. 원시 또는 필터링된 랜드마크를 읽는다.
4. 필요한 경우 소비자 고유의 필터를 적용한다.
5. UI, 제스처 처리, 게임 로직, 시각화 등은 소비자가 구성한다.

이 문서는 현 구현 검토와 후속 작업 계획이다. 아래 제안 API와 배포 기능이 이미 구현되어 있다는 의미는 아니다. 코드 수정, 릴리스 발행, Linear 티켓 생성은 이 문서 작성 범위에 포함하지 않는다.

## 2. 검토 기준과 실측 결과

- 검토 리비전: `09da6f8a3dbd5d78739cec45014663822f5720ba`
- 실행 환경: macOS Apple Silicon, Unity `6000.6.0f1`
- 패키지 루트: `MediaPipeUnityDOTS/Assets/MediaPipeUnityDots`
- 검토 범위: UPM 구성, 입력 → 추론 → 스냅샷 → ECS → 필터 → 렌더 경로, 네이티브 및 모델 배포, 소비자 API, 기존 테스트.
- 설치 실험은 개발 프로젝트와 분리한 임시 Unity 프로젝트에서 수행했다. 개발 프로젝트의 모델이나 네이티브 파일을 소비자 프로젝트로 복사하지 않았다.

| 검사 | 관측 결과 | 판단 |
| --- | --- | --- |
| 빈 프로젝트에서 현재 리비전의 Git URL 설치 | 다운로드·패키지 해석 성공, 컴파일 실패 | 패키지 위치는 유효하지만 의존성 누락 |
| 임시 소비자에 `com.unity.ai.inference@2.6.1`만 추가 | 컴파일 성공, 종료 코드 0 | 확인된 컴파일 차단 원인은 Inference 의존성 누락 |
| 소비자에서 `MpudBridge.mpud_get_last_error()` 호출 | `DllNotFoundException` | Git 배포물에 네이티브 바이너리 없음 |
| 소비자의 기본 손 모델 경로 확인 | 파일 없음 | 모델 준비 절차가 배포에 포함되지 않음 |
| 로컬 dylib의 `otool -L` 검사 | `/opt/homebrew/opt/opencv@4/lib/` 절대 경로 참조 | 바이너리만 복사해도 타 머신 실행은 보장되지 않음 |
| 기존 라이브러리 EditMode 검사 | 86개 중 81개 통과, 5개 실패 | 릴리스 전 회귀 기준 정리 필요 |

실패한 테스트는 모두 `OneEuroFilterSettingsPanelTests`에 속한다.

- `EnableDisableEnable_DoesNotAccumulateSubscriptions`
- `RenderModeToggle_TriggersSinglePush`
- `ResetButton_PushesDefaults`
- `ValueChange_TriggersPush_WhenBound`
- `VerboseLoggingToggle_FlipsLogServiceFlag`

실제 화면에서도 동일하게 실패한다고 단정하지 않는다. UI 동작과 테스트 실행 환경을 구분해 원인을 확인해야 한다. 이번 검토에서는 실제 네이티브 추론을 소비자 환경에서 끝까지 실행하거나 Player 빌드를 검증하지 못했다.

검토 당시 로컬 증거 파일은 다음과 같다. 임시 파일이므로 영구 CI 산출물은 아니다.

- `/tmp/mpud-upm-consumer.log`
- `/tmp/mpud-upm-consumer-inference.log`
- `/tmp/mpud-upm-consumer-native.log`
- `/tmp/mpud-library-review-tests.xml`

## 3. 유지할 기존 기반

| 영역 | 현재 구현 | 방향 |
| --- | --- | --- |
| 추론 | Hand / Face / Pose / Holistic 서비스, 스냅샷, C ABI 연동 | 기존 서비스 유지 |
| 실행 수명주기 | 공통 `TrackerWorker<T>`의 전용 스레드, 제출·완료 슬롯, 리셋 | 재사용하고 공개 상태 계약 보완 |
| 입력 소유권 | 제출 입력 복사, `CaptureStamp`, 타임스탬프 매핑 | 계약을 소비자에게 명시 |
| 결과 읽기 | 스냅샷과 caller-owned 배열 복사 | 트래커별 사용 패턴 정리 |
| DOTS | 상태 컴포넌트, 랜드마크 버퍼, 작성자 소유권 | unmanaged 데이터 경계 유지 |
| 좌표 | 원시 normalized/world 데이터와 표시용 매핑 | 공개 API에서도 의미 분리 |
| 스무딩 | One Euro Filter와 타임라인 검사 | 렌더 독립 처리에 재사용 |
| 시각화 | 오버레이, 포인트, 튜닝 UI | 선택형 샘플로 분리 |

`HandTrackingService`는 이미 모델 경로 지정, `Color32[]` 제출, 완료 수신, normalized/world 결과 복사, reset/dispose를 제공한다. 비웹캠 입력을 지원하기 위해 새 입력 프레임워크를 먼저 만들 필요는 없다.

근거: `Runtime/Tracking/Hand/HandTrackingService.cs:27–205`.

이하 `Runtime/`, `EditorTool/`, `Sample/`, `Tests/`, `package.json` 경로는 패키지 루트 기준이며 줄 번호는 검토 리비전 기준이다.

## 4. 주요 격차

### 4.1. 설치 의존성과 지원 버전

`Runtime/MediaPipeUnityDots.Runtime.asmdef:9`는 `Unity.InferenceEngine`을 참조하지만 `package.json:7–13`에는 `com.unity.ai.inference`가 없다. 개발 프로젝트의 `Packages/manifest.json`에만 있어 로컬 프로젝트에서 문제가 가려진다.

우선 누락 의존성을 선언해 설치를 복구한다. Depth를 선택 모듈로 분리할지는 이후 결정한다. `package.json`의 Unity `6000.0` 선언과 실제 검증 버전 `6000.6.0f1`도 맞춰야 한다. 미검증 버전을 지원 완료로 표기하지 않는다.

### 4.2. 네이티브와 모델 배포

- dylib, Task 모델, Depth ONNX 모델은 Git에서 제외되어 있다.
- `Runtime/Plugins/macOS/libmpud_bridge.dylib.meta`에는 명시적인 플랫폼·CPU importer 설정이 없다. 이것만으로 로드 실패를 단정하지 않지만 배포 타깃은 명시적으로 설정해야 한다.
- `Native/Build/CopyArtifactsToUnity.sh:22–24`는 브리지의 install name과 서명만 처리한다. OpenCV 의존 라이브러리 동봉은 하지 않는다.
- 기본 FrameProvider는 `Application.streamingAssetsPath/MediaPipe/Models/*.task`를 사용한다. 패키지 설치만으로 해당 파일이 준비되지 않는다.
- `EditorTool/DownloadDepthModel.cs:106–110`은 개발 프로젝트의 `Assets/MediaPipeUnityDots/Models` 경로를 전제한다.

소비자에게 Bazel/Homebrew 설치를 요구하지 않는 바이너리와, 모델 준비·검증·설정 연결·Player 포함 경로가 필요하다.

### 4.3. 패키지와 샘플 경계

`Sample/`은 일반 폴더라 설치 시 함께 컴파일되지만 실제 `Assets/Scenes/SampleScene.unity`는 패키지 밖에 있다. `package.json`에는 `samples` 항목이 없고 테스트 어셈블리는 `MediaPipeUnityDots.Sample`을 참조한다.

샘플 코드·UI·씬·필요한 에셋을 `Samples~`로 함께 이동하고 핵심 테스트와 샘플 테스트를 분리해야 한다. 폴더명만 바꾸는 작업으로는 충분하지 않다.

### 4.4. 소비자 결과 API

서비스 계층의 결과 접근은 구현되어 있으나 ECS 간편 리더는 `HandTrackingAdapter`/`HandTrackingDto` 중심이다. 손 DTO는 world 랜드마크, 캡처 식별자·epoch, raw/filtered 구분 등을 충분히 전달하지 않는다.

근거:

- `Runtime/Tracking/Hand/HandTrackingDto.cs:10–24`
- `Runtime/Tracking/Hand/HandTrackingAdapter.cs:67–105`

공통 프레임 메타데이터와 트래커별 결과를 정리하되, 모든 트래커를 하나의 거대한 DTO로 합치지 않는다. 각 모델이 실제 제공하지 않는 품질 값이나 식별자를 만들어 제공하지 않는다.

### 4.5. 필터와 렌더링 결합

현재 기본 흐름:

```text
원시 스냅샷 → ECS 원시 버퍼 → 소비자 리더 → 원시 데이터
                         └→ 렌더 시스템 → One Euro Filter → 표시 Transform
```

`LandmarkRenderSystem`은 `LandmarkPoint`와 `LandmarkOverlayMapping`을 요구한다. 기본 필터 결과는 소비자용 버퍼가 아니라 표시 위치 계산에 사용된다.

근거:

- `Runtime/Ecs/Common/LandmarkRenderSystem.cs:29–33`
- `Runtime/Ecs/Common/LandmarkRender.cs:42–61`

목표는 렌더러 없이 raw/filtered 데이터를 읽고 소비자가 필터를 교체할 수 있는 구조다. 구체적인 인터페이스 계약은 태스크 6에서 다룬다.

### 4.6. 좌표와 상태의 의미

다음 좌표를 구분해야 한다.

| 종류 | 의미 |
| --- | --- |
| Image/Normalized | 이미지 기준 좌표와 모델의 상대 깊이 |
| Model World | MediaPipe가 반환하는 미터 단위 좌표 |
| Unity/Overlay | 카메라·배경·앵커 설정으로 변환한 표시 좌표 |

`HandWorldLandmarkElement`는 이미 원본 미터 좌표가 Unity 절대 월드 위치가 아니라고 명시한다. 이 원본 보존 방향은 유지하고 축·원점·단위·회전·미러링 계약을 공개 API로 확장한다.

invalid/reset 경로는 상태를 비우지만 이전 버퍼 값은 남을 수 있다(`Runtime/Ecs/Hand/HandTrackingSingletonUtil.cs:49–61`). 상태와 count를 지키는 한 가능한 설계이나, 외부 소비자가 버퍼 길이만 보고 오래된 결과를 새 결과로 오인하지 않도록 읽기 계약이 필요하다.

`TryTakeCompleted()`는 오류를 로그로 남기고 `false`를 반환하므로 결과 대기와 오류를 호출자가 구분하기 어렵다(`Runtime/Tracking/Hand/HandTrackingService.cs:142–161`). 또한 프레임 내 대상 인덱스를 영속적인 추적 ID로 보장하는 계약은 없다.

## 5. 권장 배포 정책

### 5.1. 초기에는 단일 UPM 패키지 유지

기존 패키지 하위 경로는 실제 Git 설치에서 정상 인식됐다. 당장 저장소나 패키지를 여러 개로 나누지 않는다.

향후 배포 리비전이 준비됐을 때 사용할 URL 형식:

```text
https://github.com/NaturaAurum/MediaPipeUnityDOTS.git?path=/MediaPipeUnityDOTS/Assets/MediaPipeUnityDots#<release-tag>
```

`<release-tag>`는 향후 생성할 릴리스 태그 자리표시자다. 현재 리비전이 설치·실행 완료 상태라는 뜻은 아니다.

| 구성 | 권장 정책 |
| --- | --- |
| C# API·ECS | 패키지 포함 |
| 네이티브 및 필수 동적 의존성 | 사전 빌드하여 배포 리비전에 포함 |
| 모델 | 필요한 모델을 명시적으로 준비하는 Editor 기능 제공 |
| 시각화·UI·데모 | `Samples~`로 선택 Import |
| 버전 | 개발 브랜치 대신 릴리스 태그 고정 |
| 플랫폼 | 우선 macOS Apple Silicon 검증 범위 명시 |
| 라이선스 | 프로젝트 코드, 네이티브 의존성, 모델 재배포 조건 구분 |

모델 별도 준비 방식은 “Git URL로 SDK 설치 후 모델 준비 1회”다. URL 추가만으로 추론까지 가능한 요구로 확정하면 모델도 배포물에 포함하고 실행 경로와 빌드 포함을 해결해야 한다.

GitHub Release 첨부 파일은 Git UPM 설치 시 자동으로 내려오지 않는다. Git LFS를 기본 전제로 삼으면 소비자에게 LFS 설치 조건이 추가된다. 바이너리 크기와 정책상 필요해지면 배포용 브랜치 또는 저장소를 생성한다.

### 5.2. 지원 범위

현재 검토에서 확인한 구현·실험 환경은 macOS Apple Silicon Editor 중심이다. macOS Player, Intel macOS, Windows, Linux, Android, iOS, WebGL을 지원 완료로 표시하지 않는다. 공개 라이선스는 소유자가 결정해야 하며 이 계획에서 임의로 선택하지 않는다.

## 6. 후속 태스크

### 태스크 1. UPM 의존성과 지원 Unity 버전 정합성 확보

- 우선순위: P0
- 작업: 누락된 Inference 의존성을 선언하고 package.json·asmdef·지원 버전의 정합성을 확보한다.
- 완료 기준: 빈 소비자 프로젝트에 Git URL만 추가해 컴파일 성공. 수동 의존성 보충 없이 선언한 지원 Unity 버전에서 검증한다.

### 태스크 2. macOS 네이티브 배포물 독립화

- 우선순위: P0
- 작업: Homebrew 절대 경로 의존성을 제거한다. 필요한 dylib 동봉·상대 경로 링크 또는 정적 링크를 선택하고 importer·서명·배포 포함 정책을 정리한다.
- 완료 기준: Homebrew/Bazel 없는 별도 머신에서 네이티브 로드, 트래커 생성·해제 성공. 깨끗한 checkout에서 배포물을 재생성할 수 있다.
- 참고: `SyncBridgeIntoWorkspace.sh`는 패치 적용과 브리지 복사로 서브모듈을 변경한다. dirty 상태 자체보다 문서화된 빌드의 재현성이 우선이다.

### 태스크 3. 소비자용 모델 준비·경로 설정·빌드 포함 구현

- 우선순위: P0
- 작업: 모델별 출처·버전·해시를 고정하고 명시적인 다운로드, 무결성 검사, Provider 연결, 소비자 지정 경로, Player 포함 경로를 제공한다. 패키지 캐시에는 쓰지 않는다.
- 완료 기준: 선택 모델 준비 후 추론 초기화 성공. 다운로드 실패·손상 파일을 명확히 보고한다. 네트워크 없는 재실행과 지원 Player의 모델 접근이 성공한다.

### 태스크 4. Samples~ 및 테스트 경계 정리

- 우선순위: P1
- 작업: 데모 코드·UI·씬·참조 에셋을 함께 선택형 샘플로 구성하고 package.json에 samples를 선언한다. 코어 테스트와 샘플 테스트를 분리한다. 기존 설정 패널 테스트 5개 실패를 조사한다.
- 완료 기준: 샘플 없이 코어 사용 가능. Import한 샘플은 개발 프로젝트의 외부 에셋 없이 실행된다. 실패 원인을 해결하고 관련 사용자 동작을 검증한다.

### 태스크 5. 공개 결과 계약과 읽기 API 정리

- 우선순위: P1
- 작업: 트래커별 결과에 좌표 종류, 유효 count, 시간, capture 정보, 제공 가능한 품질 값을 명확히 노출한다. 버퍼 소유권·유효 기간·호출 스레드와 프레임 인덱스의 의미를 정의한다.
- 완료 기준: Hand/Face/Pose/Holistic 결과를 일관된 사용 패턴으로 읽는다. normalized/model-world/표시 좌표를 혼동하지 않으며 읽기 과정에서 소비자가 네이티브 메모리 수명을 관리할 필요가 없다.

### 태스크 6. 렌더 독립 필터 파이프라인과 사용자 확장 인터페이스 제공

- 우선순위: P1
- 선행 계약: 태스크 5의 결과·좌표·시간·소유권 계약. 초기화 및 오류 계약은 태스크 7과 함께 맞춘다.
- 요구: 라이브러리 기본 필터뿐 아니라 사용하는 프로젝트에서 구현한 커스텀 필터를 인터페이스로 제공할 수 있어야 한다.

#### 목표 흐름

```text
추론 → 원시 결과 ───────────────────────────→ Raw 읽기 API
             └→ 선택한 필터 구현 → 필터 결과 → Filtered 읽기 API
                                                └→ 선택적 좌표 변환·렌더링
```

- 기존 One Euro 계산 코어를 기본 구현으로 재사용한다.
- 필터를 사용하지 않는 raw 경로를 제공한다.
- 소비자가 구현한 필터를 코드에서 명시적으로 선택·연결할 수 있게 한다.
- 필터가 카메라, Quad, Material, UI, 렌더 엔티티를 요구하지 않게 한다.
- 원시 버퍼는 필터 출력으로 덮어쓰지 않는다.

#### 인터페이스의 최소 계약

가칭 `ILandmarkFilter`를 확장 지점으로 삼되 실제 타입명과 메서드 시그니처는 태스크 5의 데이터 계약에 맞춰 확정한다. 아래는 구현 요구사항이지 현재 존재하는 API가 아니다.

| 항목 | 계약 |
| --- | --- |
| 입력 | 읽기 전용 랜드마크 데이터, 유효 count, 좌표 종류, 타임스탬프, capture epoch, 트래커·대상 구분 |
| 출력 | 호출자가 소유·재사용하는 별도 버퍼에 기록. 랜드마크 순서와 의미를 유지하고 유효 count를 명시 |
| 버퍼 수명 | 호출 범위 밖으로 입력/출력 버퍼 참조를 보관하지 않음. 필요한 과거 상태는 구현 자체가 소유 |
| 상태 격리 | 독립된 스트림·대상·좌표 종류 사이에 필터 상태가 섞이지 않음 |
| 시간 | 같은 결과를 여러 번 읽어도 필터 상태는 한 번만 전진. 시간 역행과 epoch 변경 처리 명시 |
| 초기화 | reset, 추적 손실, 입력 스트림 변경, 좌표 종류 변경, 필터 교체 시 상태 초기화 규칙 제공 |
| 대상 식별 | 프레임 인덱스를 영속 ID로 간주하지 않음. 연속성을 확인하지 못한 대상에 과거 상태를 무조건 재사용하지 않음 |
| 오류 | 입력/출력 크기 및 유효성 위반을 명확히 보고. 실패한 출력을 정상 결과로 발행하지 않음 |
| 자원 해제 | 라이브러리가 생성한 기본 구현과 소비자가 전달한 구현의 소유권을 구분. 해제 책임 명시 |

랜덤한 매핑·리타게팅·검출 기능까지 포함하는 범용 처리 플러그인 시스템은 만들지 않는다. 우선 같은 landmark topology를 유지하는 필터 교체에 집중한다.

#### ECS / Jobs / Burst 경계

- managed 인터페이스 인스턴스를 `IComponentData`, buffer element, job 데이터에 저장하지 않는다.
- 소비자 C# 인터페이스 선택·호출은 관리 계층에서 처리하고, ECS에는 결과와 unmanaged 설정/상태만 전달한다.
- 기본 One Euro의 Burst 호환 계산 코어는 유지한다. 사용자 managed 구현이 자동으로 Burst/Jobs에서 실행된다고 보장하지 않는다.
- 기본 경로에 인터페이스 boxing, 프레임별 배열 생성, 불필요한 추가 복사를 도입하지 않는다.
- 커스텀 필터의 실행 스레드와 호출 순서를 명시한다. 초기에는 문서화한 단일 스레드 경로로 제한하고 Unity API의 워커 스레드 접근을 허용하지 않는다.
- Job/Burst 전용 사용자 필터 확장이 실제 요구될 경우 별도의 unmanaged 실행 계약을 검토한다. 초기 범위에 두 번째 확장 프레임워크를 선제 도입하지 않는다.

#### 완료 기준

1. 카메라·배경·렌더 엔티티 없는 환경에서 동일 입력의 raw/filtered 결과를 읽는다.
2. 기본 One Euro 구현이 기존 타임라인·리셋 의미를 유지한다.
3. 소비자 프로젝트에서 간단한 커스텀 필터를 구현해 라이브러리 소스 변경 없이 연결하고, 의도한 출력 차이를 확인한다.
4. 필터를 거쳐도 원시 결과가 보존된다.
5. 동일 타임스탬프 재조회, 시간 역행, epoch 변경, 추적 손실·재검출, 필터 교체에서 상태가 잘못 누적되지 않는다.
6. 서로 다른 대상·스트림·좌표 종류의 상태가 섞이지 않는다.
7. 기본 경로는 워밍업 이후 필터 자체의 프레임별 managed 할당이 없음을 확인한다. 사용자 구현의 성능 책임과 제한을 문서화한다.
8. 코어 ECS/Jobs 데이터에는 managed 참조가 없으며, 렌더러는 필터 결과를 소비할 뿐 동일 필터를 재적용하지 않는다.

### 태스크 7. 추적 손실·오류·종료 상태 계약 확립

- 우선순위: P1
- 작업: 결과 대기, 새 미검출 결과, 오류, reset/dispose, 오래된 결과를 구분한다. 내부 버퍼 유지 여부와 무관하게 공개 읽기 경로의 유효 범위를 보장한다. 필터 초기화 규칙도 함께 맞춘다.
- 완료 기준: 정상 → 미검출 → 재검출, 오류, reset, dispose 전환에서 오래된 데이터가 새 결과로 노출되지 않는다. 호출자가 오류와 결과 대기를 구분하고 자원을 중복 해제하지 않는다.

### 태스크 8. 이름 있는 랜드마크·연결 정보와 최소 소비 예제 제공

- 우선순위: P1
- 작업: Hand/Pose 인덱스 이름, 기본 연결선, Face blendshape 이름 대응을 제공한다. 직접 결과 읽기와 커스텀 필터 연결 예제를 포함한다.
- 완료 기준: 손가락 끝, 포즈 관절, 얼굴 blendshape 접근을 매직 넘버 없이 작성한다. 원본 SampleScene 없이 실행된다. 얼굴 전체 점에 임의의 이름을 붙이는 작업은 하지 않는다.

### 태스크 9. 외부 소비자 스모크 검증 자동화

- 우선순위: 릴리스 게이트
- 작업: 개발 프로젝트가 아닌 빈 소비자에서 지정 Git 리비전을 설치하고 실제 입력 추론을 검증한다. 네이티브 빌드 재현과 지원 Player 검증을 연결한다.
- 완료 기준: 설치 → 컴파일 → 모델 준비 → 입력 제출 → 결과 읽기 → 필터 적용 → 해제를 검증한다. 개발 머신의 에셋·Homebrew·설정에 의존하지 않는다. 첫 경로를 손으로 입증한 뒤 동일 기준을 Face/Pose/Holistic에도 적용한다.

### 태스크 10. 릴리스 메타데이터·라이선스·플랫폼 검증 정리

- 우선순위: 공개 배포 전 필수
- 작업: 코드 라이선스 선택, third-party 및 모델 고지, 설치/API 문서, 변경 내역, 버전 태그, 지원표를 정리한다.
- 완료 기준: 사용자가 네이티브 빌드 없이 설치 절차를 따라갈 수 있다. 재배포 조건과 모델 출처를 확인할 수 있다. 지원으로 표기한 Editor/Player 환경의 실행 증거가 있다.

## 7. 권장 실행 순서와 보류 범위

첫 목표는 “외부 빈 프로젝트에서 Git URL로 설치하고, 모델 준비 후 샘플 렌더러 없이 손 랜드마크를 읽는다”이다.

권장 순서:

1. 태스크 1–3으로 설치·네이티브·모델의 차단을 해결한다.
2. 태스크 4로 소비자와 샘플 경계를 정리한다.
3. 태스크 5·7의 결과/상태 계약을 바탕으로 태스크 6의 필터 인터페이스를 구현한다.
4. 태스크 8로 실제 소비 방법을 제공한다.
5. 태스크 9·10을 충족한 리비전만 배포한다. 라이선스 확인은 바이너리·모델 배포 방식 결정부터 병행한다.

지금 선제적으로 하지 않을 작업:

- 여러 UPM 패키지로 세분화
- 모든 입력 종류를 위한 범용 팩토리·소스 프레임워크
- 필터 레지스트리, 자동 검색, DI 컨테이너 강제, 범용 그래프 편집기
- 제스처 인식·아바타 리타게팅 등 상위 기능
- 재현된 문제 없이 `link.xml`이나 범용 오류 복구 계층 추가
- Windows·모바일·WebGL 동시 지원

소비자가 커스텀 필터를 구현하는 인터페이스는 명시적인 요구이므로 이번 계획에 포함한다. 다만 그 요구를 이유로 ECS의 unmanaged 경계를 깨거나 불필요한 플러그인 프레임워크까지 확장하지 않는다.

## 8. 참고

- [Unity Git dependencies](https://docs.unity3d.com/6000.0/Documentation/Manual/upm-git.html): 하위 경로·리비전 URL, Git LFS 조건, package.json 간 Git 의존성 제한.
- [Unity 패키지 샘플 구성](https://docs.unity3d.com/6000.0/Documentation/Manual/cus-samples.html): `Samples~`와 package.json의 samples 선언.
- [프로젝트 폴더 구조](FolderStructure.md)
- [Unity 월드 표시 매핑](transform-to-unity-world.md)
- [랜드마크 노이즈 필터링](landmark-noise-filtering.md)
