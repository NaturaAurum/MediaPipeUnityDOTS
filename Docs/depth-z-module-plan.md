# 단안 깊이(DA-V2) 기반 Z 보정 모듈 구현 계획

`Docs/monocular-depth-rnd.md`의 후보 조사를 실제 Unity 환경에서 검증하는 계획이다.
성능 표와 MediaPipe Z와의 상관만으로 도입을 결정하지 않는다. 모델·시간 정합·보정 효과를
먼저 검증하고, 통과한 기능만 UI와 샘플 씬에 통합한다.

## 0. 결정 사항

| 항목 | 결정 |
|---|---|
| 원본 보존 | Hand/Pose 상태·랜드마크 버퍼는 변경하지 않고 렌더 단계에만 적용 |
| 1차 보정 | 대상별 공통 Z 오프셋. 관절별 MediaPipe 상대 깊이를 교체하지 않음 |
| 적용 범위 | 손 최대 2개, Pose는 활성 프로바이더의 실제 대상 수. 얼굴은 제외 |
| 런타임 | `com.unity.ai.inference` 2.6.1, 어셈블리·네임스페이스 `Unity.InferenceEngine` |
| 추론 처리 | GPU 우선, 진행 중 작업 1건 + 비동기 리드백 완료 폴링. 더블버퍼는 측정 후 판단 |
| UI | UI Toolkit + MVVM + R3. 효과 검증 후 구현 |
| 모델 배포 | EditorTool 다운로드 + 모델 파일 .gitignore. 출처·버전·SHA-256·라이선스 고정 |
| 브랜치 | develop에서 feature/depth-z-module 분기, PR base develop, 제목 feat: |

절대 거리 복원, 관절별 깊이 overwrite, 다중 카메라, 보정 Z의 DTO 노출은 이번 범위 밖이다.
대상별 오프셋은 시각화 보정이며 미터 단위 정확도를 주장하지 않는다.

## 1. 단계와 성공 기준

| 단계 | 작업 | 통과 증거 |
|---|---|---|
| P0 모델·성능 | ONNX 확보, 정지 이미지, 웹캠 동시 추론 | 전처리·좌표·출력 규약 확정, 비동기 완료 확인, 성능 측정 |
| P1 시간 정합·샘플링 | 캡처 스탬프 전달, 대응 랜드마크 보관, 대상별 샘플 | 오래된 맵·다른 대상·다른 실행 세대의 결과를 사용하지 않음 |
| P2 Z 결합 실험 | 대상별 오프셋, 기존 출력과 동일 영상 비교 | 깊이 순서와 형태를 유지하면서 목표 동작을 개선, OFF 즉시 원복 |
| P3 UI·씬 통합 | 설정·상태 UI와 명시적 배선 | 재바인딩·비활성화·실패 복구·자원 해제 확인 |

각 게이트 실패 시 다음 단계를 구현하지 않고 원인과 결과를 기록한다.
이는 실험 중단 기준이며, 구현하지 않은 후속 단계를 완료로 처리하지 않는다.

### 측정 계약

- P0에서 장비, Unity/패키지 버전, 모델 해시, 입력 크기, 백엔드, 워밍업과 측정 구간을 고정한다.
- 동일 녹화 입력의 Depth OFF/ON을 비교한다. 정지, 전후 이동, 빠른 횡이동,
  양손 교차·재검출, 배경 변화, 대상 이탈을 포함한다.
- 깊이 결과 완료율, 캡처→리드백 지연 p50/p95, Unity 프레임 시간 p50/p95,
  MediaPipe의 실제 새 결과 처리율, 할당량을 기록한다. Submit 호출 수를 처리율로 세지 않는다.
- 실시간 목표는 GPU 깊이 결과 15Hz 이상, MediaPipe 30Hz다. OFF 기준선이 이미 30Hz 미만이면
  그 사실을 분리 보고하며, ON/OFF 저하율도 함께 기록한다.
- P0에서 기준선 측정 후 허용 지연·프레임 시간 저하·신선도 상한을 수치로 기록하고 P1 전에 고정한다.
  기준을 충족하지 못하면 지원 입력 크기·추론 주기를 조정하고 재측정한다.
- P2 평가는 대상 기준선 떨림, 전후 이동 응답 지연, 깊이 순서 역전, 관절 형태 보존을 함께 본다.
  허용값은 P2 비교 실행 전에 고정한다. Z 분산 감소 또는 MediaPipe와의 상관만으로 통과시키지 않는다.
- 근거 있는 거리 정답이 없는 실험은 안정성·순서·지연 평가로 한정하고 정확도 향상으로 표현하지 않는다.

## 2. P0 — 모델 확보와 비동기 추론

### 모델·전처리 계약

- DA-V2 Small의 정확한 가중치와 ONNX export 도구/리비전, opset, 라이선스를 확인한다.
  커뮤니티 export를 쓰더라도 원본 가중치와 export 산출물의 출처·사용 조건을 별도로 기록한다.
- 다운로드 파일의 SHA-256을 검증하고 실패 시 불완전 파일을 모델로 사용하지 않는다.
- 기본 배치는 gitignored `Assets/` 하위 ONNX를 ModelAsset으로 import하고 직렬화 참조로 연결한다.
  다운로드 위치와 .meta GUID를 안정적으로 유지하고 클린 클론의 다운로드→import→실행을 검증한다.
- 모델 누락·import 실패는 명확한 오류와 비활성 상태로 표시한다. 가짜 결과나 무음 폴백은 금지한다.
- 입력 RGB 순서, 값 범위/평균/표준편차, 텐서 레이아웃, 출력 크기·방향·부호를 확정한다.
- 입력 크기는 선택한 모델의 고정/동적 shape와 패치 배수 조건으로 결정한다.
  384/256 정사각 입력을 지원한다고 미리 가정하지 않는다.
- 모델이 요구하는 리사이즈·종횡비·패딩 방식을 적용하고, 원본 영상→입력 텐서→출력 맵의
  좌표 변환과 역변환을 함께 보존한다. MediaPipe 입력 반전(`LatestFlipVertically`)도 반영한다.
- CPU 백엔드는 별도 성능을 측정한 경우에만 명시적으로 선택한다. GPU 실패 시 자동 전환해
  실시간 목표를 충족한 것처럼 표시하지 않는다.

### P0 실측 기록 (2026-09-07)

- 모델: `onnx-community/depth-anything-v2-small` `onnx/model.onnx` (fp32, 99,060,839B).
  리비전 `4472b736`, SHA-256 `afb6a5c2…0a1df10c`, Apache-2.0, opset 14, weights `Depth-Anything-V2-Small`.
- 그래프: 입력 `(N,3,H,W)` 동적, 출력 `predicted_depth (N,14*floor(H/14),14*floor(W/14))`.
  연산자는 Add/MatMul/Conv/Softmax/Erf/Resize 등 표준 집합. 384/256 정사각 고정을 가정하지 않는다.
- 전처리: 1/255 rescale, ImageNet mean/std, 종횡비 유지 후 14 배수 패딩, RGB·NCHW.
  onnxruntime 실측에서 640x480→518x392 입력의 출력이 패딩 크기와 일치함을 확인.
- Sentis 경로: Unity 6000.6.0f1 + InferenceEngine 2.6.1에서 ModelAsset import·CPU 스케줄·
  비동기 리드백 완료를 `DepthSpikeTests`로 검증 (140px 그래디언트, 140x140 유한·분산 출력).
- 실사진 출력: 전 범위 유한, 분위수 0.30~3.43, 이웃 기울기/전체 표준편차 1%로 공간 정합.
  합성 평탄 영역은 분포 밖 입력으로 49%가 0이 되므로 방향 판정에 쓰지 않는다.
- 방향 규약: 큰 값=가까움 (HF transformers 문서 등 복수 출처).
  웹캠 실기에서 손 전후 이동으로 대표값 증감을 확인한 뒤 P2 블렌딩에 진입한다.
- 배치 위치: `Assets/MediaPipeUnityDots/Models/*.onnx` + ModelAsset import.
  `Load(path)`는 Sentis 직렬 형식이므로 raw ONNX에는 쓰지 않는다.
  `.onnx`는 git 제외, `.onnx.meta` GUID는 커밋해 씬 참조를 유지한다.
- 미측정 (인터랙티브 세션에서 수행): GPU 15Hz, MediaPipe 30fps 동시 유지,
  캡처→리드백 지연 p50/p95, 프레임 시간 영향. 측정 로깅은 `DepthFrameProvider`에 내장한다.

### 작업 상태와 자원 수명

1. Idle일 때 새로운 캡처 프레임만 제출한다. 같은 LatestPixels를 LateUpdate마다 재제출하지 않는다.
2. 제출한 입력은 텐서 업로드/사용 완료까지 불변으로 보관한다. 다음 웹캠 캡처의 배열 덮어쓰기를 허용하지 않는다.
3. Worker.Schedule 후 출력에 ReadbackRequest를 걸고 Pending 상태로 전환한다.
4. IsReadbackRequestDone을 폴링한다. 미완료면 메인 스레드에서 기다리지 않는다.
5. 완료 후에만 CPU 데이터를 소비한다. 완료 전 출력 덮어쓰기나 Worker 재스케줄을 하지 않는다.
6. 비활성화·파괴·World 교체 시 실행 세대를 변경하고 공개 샘플을 즉시 무효화한다.
   늦게 끝난 결과는 게시하지 않는다. API가 작업 취소를 지원하지 않으면 완료를 확인한 뒤 자원을 해제한다.
7. 입력·출력의 소유권을 구분한다. PeekOutput의 Worker 소유 텐서를 임의 해제하지 않으며,
   CPU 복사 텐서 등 직접 소유한 자원은 한 번만 해제한다. 재활성화가 이전 작업의 자원을 재사용하지 않게 한다.

더블버퍼는 1프레임 지연이나 무스톨을 보장하지 않는다. 한 작업 구조가 성능 목표를 못 맞춘 경우에만
추가하며, 동기 ReadbackAndClone으로 완료를 강제하는 경로는 프레임 루프에 두지 않는다.

## 3. P1 — 시간 정합과 대상 식별

```text
웹캠 새 캡처: CaptureId + CaptureTimestampUs + CaptureEpoch + 좌표 변환
    ├─ MediaPipe 제출 → 해당 캡처 스탬프가 붙은 Hand/Pose 결과
    └─ Depth 제출 → 동일 캡처 스탬프의 깊이맵 비동기 완료
                         ↓
동일 캡처의 랜드마크 스냅샷으로 샘플링 → 대상별 Depth 샘플 게시
                         ↓
렌더: 시간·세대·대상 일치 검사 → 대상별 Z 오프셋 또는 기존 경로
```

- 캡처 메타데이터는 픽셀을 실제 갱신할 때 한 번 생성한다. 타임스탬프와 현재 시각은 같은
  단조 증가 시계의 마이크로초 단위를 사용한다. ECS에는 unmanaged 시각 스냅샷을 전달한다.
- 기존 Service/Snapshot의 TimestampUs가 실제 어느 입력을 나타내는지 추적한 뒤 캡처 스탬프를
  연결한다. 비동기 poll 완료 시 최신 웹캠 번호를 붙이는 구현은 금지한다.
- 깊이 추론 중인 입력 한 건에 대응하는 랜드마크 스냅샷을 보관한다. 동일 캡처 결과를 얻지 못하면
  시간 제한 후 버린다. 이를 위해 필요한 제출/결과 매핑은 전담 및 Holistic 경로 모두에 적용한다.
- 완료된 깊이맵을 샘플링할 때 최신 XY로 바꾸지 않는다. 캡처 당시의 XY·대상 식별자를 사용한다.
- 게시된 샘플을 이후 렌더 프레임에서 재사용할 수 있으나 아래 조건을 모두 충족해야 한다.
  - 현재 시각 - Depth 캡처 시각이 MaxSampleAgeUs 이내
  - 렌더에 쓰는 랜드마크 캡처 시각과 Depth 캡처 시각의 차이가 MaxAlignmentDeltaUs 이내
  - 캡처 세대·트래커 세대·대상 식별자가 일치하고 양쪽 결과가 유효
- FrameCount 차이는 신선도 기준으로 사용하지 않는다. 추적 정지 중에도 실제 시간으로 만료한다.
- 배열 슬롯은 영속 대상 ID가 아니다. Hand는 handedness 변화·재검출·모호한 중복 분류에서
  이전 보정을 폐기한다. 안정적인 매칭이 없는 Pose 슬롯 재배치도 보수적으로 무효화한다.
  P1에서 기존 모델의 식별 가능 범위를 확인하고, 입증하지 못한 연속성은 가정하지 않는다.
- Writer 변경, 트래커 Reset, 캡처 재시작, World 교체, 대상 소실 시 샘플과 보정 기준선을 초기화한다.
  인스턴스 식별자만으로 같은 프로바이더의 재시작을 구분하지 말고 실행 세대를 사용한다.
- 범위 밖 랜드마크·패딩 영역·NaN/Infinity는 무효 샘플이다. 유효 영역의 마지막 픽셀 보간만
  경계를 처리하며, 잘못된 좌표를 영상 가장자리 깊이로 대체하지 않는다.

## 4. P2 — 대상별 Z 오프셋 실험

### 보정 의미

기존 MediaPipe 관절별 상대 깊이와 OneEuroFilter 경로는 그대로 유지한다.
깊이 모델은 대상의 대표 깊이 변화에 따른 공통 표시용 오프셋만 제공한다.

```text
baseDepth[i] = 기존 ResolvePoint의 필터링된 표시용 깊이
correction[target] = 대상 대표 깊이의 기준선 대비 변화 → 표시 단위 변환 → 별도 시간 필터
finalDepth[i] = baseDepth[i] + weight * correction[target]
```

- 대상 대표값은 동일 캡처의 유효한 랜드마크 위치에서 양선형 샘플링한 값의 중앙값을 시작점으로 한다.
  표면 깊이와 관절 중심은 동일하지 않으므로 관절별 overwrite의 근거로 사용하지 않는다.
- 대표값의 안정성과 전후 이동 응답을 먼저 검증한다. 배경 변화만으로 대표값이 흔들리면 통과시키지 않는다.
- 매 프레임 전체 min/max 정규화나 대상별 독립 min/max 정규화로 시간적 척도를 만든다고 가정하지 않는다.
  모델 출력의 프레임 간 척도 안정성을 P0/P2에서 측정한다. 불안정하면 렌더 보정을 중단한다.
- 최초 유효 대상의 대표값을 기준선으로 잡고, P0에서 확인한 출력 방향에 따라 가까워지는 변화가
  음수 표시 오프셋이 되게 한다. 이것은 대상별 기준선 대비 변화이며 대상 간 절대 거리 비교가 아니다.
- DepthGain과 MaxOffset은 명시적인 시각화 조절값으로 둔다. MediaPipe의 world Z span으로
  자동 환산하지 않는다. 값·단위·실험 근거를 기록하며 미터 보정이라고 부르지 않는다.
- weight=1도 관절별 상대 깊이를 교체하지 않는다. `_nearestZ` 추가와 zSpan 블렌드 수식은 제거한다.
- 기존 원근 투영 정합 MapWithDepth 경로를 유지한다. 단, near-plane 안전 제한이 적용된 영역에서는
  형태가 잘릴 수 있으므로 보정 범위와 클리핑을 검증한다. 픽셀 정합 때문에 2D 위치 변화만으로 효과를 평가하지 않는다.

### 필터와 원복 계약

- 보정 필터는 대상별 별도 상태이며 새 Depth 캡처 타임스탬프에서만 전진한다.
  기존 XYZ 필터에 보정값을 넣지 않아 같은 MediaPipe 타임스탬프에 Depth만 갱신되어도 반영 가능하게 한다.
- OFF, weight=0, 만료, 무효 샘플, RenderMode!=1이면 최종 보정은 즉시 0이다.
  기존 필터 상태를 오염시키지 않았으므로 동일 입력의 Depth 미사용 출력으로 즉시 복귀한다.
- OFF/만료/대상·소스 세대 변경 후 재진입 시 보정 필터와 기준선을 초기화한다. XY 필터는 초기화하지 않는다.
- 유효한 WorldLandmarks가 없어 기존 2D 폴백 중인 대상에는 오프셋을 적용하지 않는다.

## 5. 파일·인터페이스 범위

P0 실험 결과로 확정한 계약만 영구 코드에 반영한다. 아래는 소유 영역이며 빈 파일을 미리 만들지 않는다.

### Runtime/Ecs/Depth — unmanaged 데이터

- DepthSettings: Enabled, Weight, DepthGain, MaxOffset, MaxSampleAgeUs, MaxAlignmentDeltaUs.
  싱글턴 미존재 시 Enabled=0. 수치 범위·유한성은 설정 입력 경계에서 검증한다.
- DepthSampleStatus 및 대상별 샘플 버퍼: 캡처 ID/시각/세대, 원본 트래커 세대,
  대상 식별·유효성, 대표 깊이. Hand/Pose의 유효성은 독립적으로 기록한다.
- 대상별 보정 상태: 기준선, 마지막 Depth 시각, 필터 상태, 세대. 관리 객체를 넣지 않는다.

- DepthSamplingSingletonUtil: Depth 전용 싱글턴 단일 작성자 획득·해제.
  Hand/Pose 싱글턴 소유권을 획득하거나 원본을 초기화하지 않는다.

### 신규 — `Runtime/Tracking/Depth/`

- CaptureStamp: 캡처 ID·시각·세대 식별자. 제출→결과 매핑과 시간 정합에 쓴다.
- SubmitStampMap: 제출 시각→캡처 스탬프 매핑. 미적중 시 무효 스탬프로 떨어진다.
- DepthSampler: 좌표 역변환·보간·대상 대표값 계산. DPT 전처리 규격을 포함한다.
- DepthInferenceService: 모델·Worker·텐서 수명, 단일 진행 작업, 리드백 완료 확인.
- CaptureSnapshotRing: 동일 캡처의 Hand/Pose 스냅샷 보관. 깊이 완료 시 조회한다.
- DepthFrameProvider: 명시적으로 연결된 웹캠, 캡처별 스냅샷 정합, 유효 샘플 게시.

### 신규 — UI (기존 직접 바인딩 패턴)

- UI/Source/DepthSettings.uxml / .uss — Foldout "Depth Z 보정 (실험)": 활성화 토글,
  가중치·게인·최대 오프셋 슬라이더, 초기화 버튼, 상태 라벨.
- Sample/HandTracking/Scripts/DepthSettingsPanel.cs — OneEuroFilterSettingsPanel 답습.
  컨트롤 바인딩 + ECS 푸시, R3/MVVM 도입 없음.

## 6. P3 — UI와 수명주기

- `OneEuroFilterSettingsPanel` 직접 바인딩 패턴을 답습한다. 코드베이스에 MVVM+R3 선례가 없고
  R3는 매니페스트 의존성으로만 존재하므로, 단일 규약 규칙에 따라 새 규약을 도입하지 않는다.
  UXML/USS 레이아웃 + C# 바인딩/ECS 푸시 구조를 그대로 따른다.
- UXML/USS로 Depth Foldout(기본 닫힘), 활성화 토글, 가중치·게인·최대 오프셋 슬라이더,
  초기화 버튼, 상태 라벨을 정의한다. 기본값은 `DepthSettings.Default`(Enabled=0, Weight=0)다.
- View는 활성화/바인딩 주기의 콜백 등록·해제를 소유한다. `BindToRoot` 시작 시 `UnbindEvents`로
  이전 바인딩을 해제하고, `OnDisable`과 UI 재바인딩에서 해제한다. `RemoveFromHierarchy`도 답습한다.
- 설정값 검증을 입력 경계에서 수행한다. 가중치는 0~1 클램프, 게인·최대 오프셋은 음수와
  비유한값을 거부한다. 슬라이더 범위를 벗어난 값은 들어오지 않게 UXML 범위를 고정한다.
- UI→ECS는 설정 푸시, ECS→UI는 `DepthSampleStatus` 스냅샷(유효성·캡처 ID)이다.
  상태 라벨은 푸시·바인딩 시점에 갱신하며 별도 폴링 루프를 두지 않는다.
  단순히 Enabled를 켰다는 이유로 추론 정상 상태를 표시하지 않는다.
- 설정 패널을 숨기는 것과 Depth 기능을 끄는 것은 구분한다. 기능 OFF는 결과 무효화와 진행 작업 폐기 계약을 따른다.
### 기존 파일 수정

- WebcamFrameProvider 및 Hand/Pose/Holistic Service/Snapshot/Status: 캡처 스탬프의 제출→결과 전달,
  실행 세대와 대상 연속성 확인에 필요한 메타데이터. 기존 TimestampUs 의미는 무음으로 변경하지 않는다.
- LandmarkRender / LandmarkRenderSystem: 기존 필터 이후 표시용 깊이에 대상별 오프셋 추가.
  LandmarkOverlayMapping과 LandmarkDepthBounds는 원칙적으로 변경하지 않는다.
- Runtime asmdef: Unity.InferenceEngine 참조. 테스트·Sample asmdef는 실제 사용하는 어셈블리만 추가한다.
- EditorTool 모델 다운로드 스크립트와 .gitignore: 고정 출처·해시 검증과 누락 안내.
- SampleScene: P3에서 DepthFrameProvider와 설정 UI만 명시적 직렬화 참조로 배선한다.

## 7. 검증 항목

### 좁은 회귀 테스트

- 캡처 A 깊이맵과 B 랜드마크 오결합 거부, 캡처 정지 중 시간 만료, 미래/다른 세대 스탬프 거부.
- 트래커 Reset·소유자 변경·손 교차/재검출에서 이전 대상 보정 재사용 방지.
- 비정사각 영상·수직 반전·패딩을 포함한 좌표 변환, 양선형 보간 경계, NaN·무효 좌표 거부.
- 대표값 방향, 평탄 출력, 기준선 초기화, 최대 오프셋 제한.
- OFF/weight=0/만료 시 기존 출력 일치, 동일 MediaPipe 시각에서 Depth만 갱신,
  ON→OFF→ON에서 XY 필터 연속성 및 보정 필터 초기화.
- 중복 Depth 작성자 차단, 비소유자의 종료가 기존 결과를 초기화하지 않음.
- UI 바인딩·해제·재바인딩 수명주기, 기본값 동기화, 상태 라벨 표시.

테스트는 위 동작·전이를 방어한다. 값 전달이나 필드 복사 자체를 위한 테스트는 만들지 않는다.

### 실제 Unity 검증

- P0 모델 import·GPU 실행·비동기 리드백과 동시 MediaPipe 처리, 결과 방향을 실제 영상으로 확인한다.
- P1 캡처 스탬프·시간 차이·폐기 사유를 기록해 시간 정합을 검증한다.
- P2 동일 녹화 입력 ON/OFF 비교와 3D 측면 관찰 또는 수치 측정으로 오프셋·형태 보존을 확인한다.
- P3 Play Mode에서 기능 토글, 컴포넌트 비활성화/재활성화, 추론 중 파괴, World 교체,
  모델 누락, UI 재바인딩을 실행한다. 늦은 결과 게시와 자원 누수가 없어야 한다.
- EditMode는 좁은 필터부터 실행한다. 컴파일 성공을 GPU 실행·화면 품질 증거로 대신하지 않는다.

## 8. 경계와 중단 기준

- IComponentData/job struct는 unmanaged 순수 데이터. ReactiveProperty·DI·UniTask 참조를 넣지 않는다.
- UI/App 계층 외부에 R3 구독을 전파하지 않는다. 새 UniTask PlayerLoop 변경은 하지 않는다.
- 새 MonoBehaviour 참조는 SerializeField 또는 기존 DI 배선 사용. Find/GetComponent/AddComponent 자동 배선 금지.
- 모델 출력의 시간적 척도나 대상 연속성이 입증되지 않으면 보정을 적용하지 않는다.
- 성능·품질 게이트 실패 시 UI 확장으로 넘어가지 않고 해당 실험 결과를 보고한다.
- Hand 슬롯 영속 추적, metric 캘리브레이션, 얼굴 확장, 다중 작업 파이프라인은 별도 요구와 증거가 있을 때만 추가한다.

## 참고 근거

- `Docs/monocular-depth-rnd.md` §5, §6.2.3: 후보 실험과 글로벌 깊이/관절 형태 구분.
  기존 FPS 표는 현재 장비의 보장값이 아니며 이 계획의 P0에서 실측한다.
- 설치된 Inference 2.6.1 `Documentation~/read-output-async.md`: 비동기 완료 확인 및 출력 텐서 소유권.
- `Runtime/Ecs/Common/OneEuroFilter.cs`: 동일 타임스탬프에서 이전 값 반환.
- `Runtime/Ecs/Common/LandmarkRender.cs`, `LandmarkOverlayMapping.cs`: 기존 필터와 픽셀 정합 깊이 경로.
