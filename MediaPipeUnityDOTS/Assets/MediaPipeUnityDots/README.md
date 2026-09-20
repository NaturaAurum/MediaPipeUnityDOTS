# MediaPipe Unity DOTS

UPM 패키지 `com.natura-aurum.mediapipe-unity-dots`, 버전 `0.1.0`. Unity `6000.6.2f1` Editor에서 검증했습니다. 현재 네이티브 번들은 macOS **26.0 이상 / Apple Silicon arm64 / CPU 추론**용입니다. Windows·모바일·WebGL·Intel Mac·IL2CPP 지원을 표방하지 않습니다.

소비자는 네이티브 빌드 없이 설치합니다. Git URL의 `?path=/MediaPipeUnityDOTS/Assets/MediaPipeUnityDots`와 검증된 `#리비전`을 함께 지정하세요. Git LFS는 사용하지 않습니다. 패키지 의존성은 자동 설치되며 Samples는 선택 사항입니다. 공개 릴리스 태그는 아직 발행하지 않았습니다.

## 모델 준비

| 모델 | Editor 메뉴 | 소비자 프로젝트의 저장 위치 |
| --- | --- | --- |
| Hand | MediaPipe → Models → Download Hand Task Model | `Assets/StreamingAssets/MediaPipe/Models/hand_landmarker.task` |
| Face | MediaPipe → Models → Download Face Task Model | 같은 폴더의 `face_landmarker.task` |
| Pose | MediaPipe → Models → Download Pose Task Model | 같은 폴더의 `pose_landmarker_full.task` |
| Holistic | MediaPipe → Models → Download Holistic Task Model | 같은 폴더의 `holistic_landmarker.task` |
| Depth, 선택 사항 | MediaPipe → Download Depth Model (DA-V2 Small) | `Assets/MediaPipeUnityDots/Models/depth_anything_v2_small.onnx` |

- [ModelManifest.txt](EditorTool/ModelManifest.txt)가 출처·리비전·SHA-256의 기준입니다. 첫 다운로드는 명시적으로 실행합니다. 패키지 캐시에는 쓰지 않습니다.
- 임시 다운로드의 해시를 확인한 뒤 교체합니다. 실패하면 기존 정상 파일을 보존하고 오류를 보고합니다. 준비된 모델을 사용하는 추론은 네트워크가 필요 없습니다.
- Task 모델은 StreamingAssets에 들어가므로 macOS Player에서도 `ModelPaths.GetPath(TrackingModel.Hand)` 등으로 접근합니다.
- 서비스 생성자의 `modelPath` 또는 Provider의 Inspector `Model Path`로 별도 모델을 지정할 수 있습니다. 사용자 모델의 호환성·라이선스·배포와 Player에서 유효한 경로는 소비자가 책임집니다. 외부 절대 경로의 파일을 자동으로 Player에 복사하지 않습니다.
- Depth는 `.onnx`를 Inference Engine의 `ModelAsset`으로 Import합니다. 기존 `.meta`와 소비자 importer 설정은 덮어쓰지 않으며, 처음 설치할 때만 패키지의 GUID 템플릿을 만듭니다. Provider를 선택하고 **MediaPipe → Models → Assign Depth Model To Selected Providers**로 연결합니다. Player에는 직렬화된 `ModelAsset` 참조로 포함하며 ONNX 파일 경로로 로드하지 않습니다.
- 씬 빌드 검사는 활성 Provider가 사용하는 모델을 검사합니다. 기본 Task 모델의 손상·누락, Depth의 미연결 상태는 빌드 오류입니다. 직접 서비스만 사용하는 코드의 모델 준비는 소비자 검증으로 확인해야 합니다.

배치 준비 진입점은 `MediaPipeUnityDots.EditorTool.DownloadDepthModel.PrepareBatch`이며 `-mpudModels Hand,Face,Pose,Holistic`처럼 선택합니다. 모델 조건은 [Third Party Notices](Third%20Party%20Notices.md)를 확인하세요.

## 렌더 없는 최소 Hand 소비 예제

소비자 asmdef에는 `MediaPipeUnityDots.Runtime`을 참조합니다. 아래 클래스는 카메라·씬·ECS 렌더 엔티티 없이 사용할 수 있습니다. 입력은 `Texture2D.GetPixels32()`에서 얻은 하단 행 우선 RGBA 배열입니다. 배열과 reader를 매 프레임 새로 만들지 마세요.

```csharp
using System;
using MediaPipeUnityDots.Runtime.Interop;
using MediaPipeUnityDots.Runtime.Models;
using MediaPipeUnityDots.Runtime.Tracking;
using UnityEngine;

public sealed class HandTipReader : IDisposable
{
    private readonly HandTrackingService _service = new HandTrackingService(
        ModelPaths.GetPath(TrackingModel.Hand), numHands: 1);
    private readonly MpudNormalizedLandmark[] _points = new MpudNormalizedLandmark[HandLandmarks.Count];

    public TrackingResultStatus Status { get; private set; }
    public string Error => _service.LatestError;

    public bool Submit(Color32[] pixels, int width, int height, CaptureStamp stamp)
        => _service.TrySubmit(pixels, width, height, true, stamp);

    // 제출 직후뿐 아니라 이후 프레임에서도 호출한다. 기다리는 동안 메인 스레드를 막지 않는다.
    public bool TryReadIndexTip(out MpudNormalizedLandmark tip)
    {
        tip = default;
        Status = _service.Poll();
        if (Status != TrackingResultStatus.Success) return false;
        var count = _service.CopyLatestHandLandmarksTo(0, _points);
        var index = (int)HandLandmark.IndexFingerTip;
        if (count <= index) return false;
        tip = _points[index];
        return true;
    }

    public void Dispose() => _service.Dispose();
}
```

`CaptureStamp(captureId, captureTimestampUs, captureEpoch)`는 입력 캡처의 상관관계 정보입니다. 타임스탬프는 단조 시계의 마이크로초 단위로 공급하고, 입력 스트림을 다시 시작하면 epoch를 변경하세요. `Submit`이 `false`이면 해당 프레임을 수락하지 않은 것이며 `Poll()`로 완료 또는 오류를 확인합니다. 수락된 입력은 서비스가 소유 버퍼에 복사하므로 호출이 끝난 뒤 입력 배열을 재사용할 수 있습니다.

## 네 서비스의 공통 읽기 규칙

| 서비스 | 검출 대상 수 | 호출자 소유 배열로 복사 |
| --- | --- | --- |
| `HandTrackingService` | `LatestHandCount` | `CopyLatestHandLandmarksTo`, `CopyLatestHandWorldLandmarksTo` |
| `FaceTrackingService` | `LatestFaceCount` | `CopyLatestFaceLandmarksTo`, `CopyLatestFaceBlendshapesTo` |
| `PoseTrackingService` | `LatestPoseCount` | `CopyLatestPoseLandmarksTo`, `CopyLatestPoseWorldLandmarksTo` |
| `HolisticTrackingService` | 부위별 유효 count | `CopyLatestFaceTo`, `CopyLatestPoseTo`, `CopyLatestLeftHandTo`, `CopyLatestRightHandTo` 및 Pose/Hand의 `*WorldTo` |

반환된 count만 유효합니다. 각 대상용 배열의 최대 크기는 Hand 21, Pose 33, Face `MpudFaceResult.LandmarksPerFace`, blendshape 52입니다. 여러 대상은 프레임 내 ordinal로 순회하며 **ordinal·handedness를 영속 인물 ID로 해석하지 않습니다.** Holistic의 왼손/오른손과 Pose도 같은 전역 원점을 공유하는 것으로 가정하지 않습니다.

서비스 내부 배열이나 네이티브 포인터를 외부에 넘기지 않습니다. 복사한 배열의 소유권과 수명은 호출자에게 있으며 다음 추론 이후에도 보존됩니다. 충분한 용량의 배열을 재사용하고 유효 prefix `[0, count)`만 읽으세요. 서비스 생성·제출·폴링·복사·reset·dispose는 하나의 Unity 메인 스레드에서 순서대로 호출합니다. 네이티브 추론만 내부 워커에서 실행합니다.

`HandFrameProvider`, `FaceFrameProvider`, `PoseFrameProvider`, `HolisticFrameProvider`는 모두 `ResetTracker()`를 제공하며 캡처 epoch와 ECS 공개 상태를 함께 초기화합니다.

### 상태와 시간

| `Poll()` 결과 | 의미와 소비 방법 |
| --- | --- |
| `Waiting` | 새 완료가 없음. 이전 snapshot과 `LatestStatus`를 유지하므로 새 프레임으로 다시 발행하지 않음 |
| `Success` | 새 검출 결과. count를 확인하고 복사 |
| `NoDetection` | 새 미검출 결과. count 0, 이전 데이터를 유효 결과로 노출하지 않음 |
| `Error` | 워커/네이티브 오류. 결과를 무효화하며 `LatestError` 확인 |
| `Stale` | 직전 수락 결과보다 시간이 증가하지 않은 완료. 무효화. 벽시계 기반 TTL은 아님 |
| `Reset` | `ResetTracker()` 직후의 공개 상태. 다음 제출·완료를 기다림 |
| `Disposed` | 종료 상태. `Dispose()` 중복 호출은 안전하고 이후 제출·복사·reset은 예외 |

`LatestMetadata`에는 결과 `TimestampUs`, 입력 `CaptureId`·`CaptureTimestampUs`·`CaptureEpoch`, 결과 `FrameCount`, 워커 `Generation`, 마지막 제출 시각 `SubmittedTimestampUs`가 있습니다. 마지막 제출은 현재 읽는 완료보다 앞설 수 있습니다. 결과 시각은 서비스별 타임라인이므로 서비스 사이의 상관관계에는 capture 필드를 사용하세요. 필터에는 해당 서비스의 결과 `TimestampUs`를 사용합니다. `Reset`은 주로 `LatestStatus`로 관찰하며 새 완료가 없는 다음 `Poll()`은 `Waiting`입니다.

### 좌표와 품질

- **Normalized image**: 제출 이미지 기준 x는 오른쪽, y는 아래쪽입니다. x/y는 보통 0–1이지만 모델 출력을 clamp하지 않습니다. z는 모델 상대 깊이이며 일반적으로 작은 값이 카메라에 가깝습니다. x/y/z를 곧바로 Unity world 미터로 사용하지 마세요.
- **Model world**: Hand/Pose에서 제공하는 모델의 미터 단위 3D 좌표입니다. Hand의 손 중심, Pose의 양쪽 엉덩이 중심 등 부위별 원점을 사용합니다. 카메라 외부 파라미터나 Unity transform이 적용된 좌표가 아닙니다. Face에는 이 API의 world 결과가 없습니다.
- `MpudNormalizedLandmark`는 복사 API의 공통 float 전달 구조체입니다. 이름과 무관하게 `*World*` 메서드의 값은 model-world입니다. `visibility`·`presence`는 모델이 제공한 값만 의미가 있으며 미제공 값을 신뢰도 1로 보완하지 않습니다. Hand 대상별 score/handedness는 별도 접근자로 읽습니다. Holistic 결과에는 손 score가 없으므로 `HolisticFrameProvider`가 발행하는 Hand score는 0입니다.
- **Unity 표시 좌표**: `OverlayMapping` 등 표시 계층에서 생성합니다. y 반전, cover-crop, 화면 비율, 미러링, 깊이 배율을 원시 결과와 혼동하지 마세요. `TrySubmit`의 `flipVertically`는 입력 행 순서 변환이지 좌우 미러링이 아닙니다.

### 이름으로 접근

```csharp
// 해당 서비스가 Success인 프레임에서만 읽는다. 배열과 jawOpenIndex는 미리 준비한다.
var poseCount = pose.CopyLatestPoseLandmarksTo(0, posePoints);
if (poseCount > (int)PoseLandmark.LeftWrist)
    Debug.Log(posePoints[(int)PoseLandmark.LeftWrist].x);

var jawOpenIndex = FaceBlendshapeNames.GetIndex("jawOpen");
var blendshapeCount = face.CopyLatestFaceBlendshapesTo(0, blendshapes);
if (blendshapeCount > jawOpenIndex)
    Debug.Log(blendshapes[jawOpenIndex]);
```

`HandLandmarks.Connections`, `PoseLandmarks.Connections`는 기본 연결선의 `Start`/`End` 인덱스를 제공합니다. `FaceBlendshapeNames.GetName(index)`는 역방향 조회입니다. 전체 얼굴 점에 임의의 해부학적 이름을 부여하지 않습니다.

## 렌더 독립 필터

`MediaPipeUnityDots.Runtime.Tracking.Filtering`의 `LandmarkFilterCoordinator`는 호출자가 소유하는 raw/filtered 버퍼를 분리합니다. 한 coordinator는 한 스트림·대상·좌표 종류에 사용하는 것이 기본입니다. 다른 조합을 넣으면 상태를 섞지 않고 reset합니다. 동시에 여러 대상의 이력을 유지하려면 조합마다 인스턴스를 유지하세요.

아래 예제의 namespace는 `System`, `UnityEngine`, `MediaPipeUnityDots.Runtime.Interop`, `MediaPipeUnityDots.Runtime.Ecs`, `MediaPipeUnityDots.Runtime.Tracking`, `MediaPipeUnityDots.Runtime.Tracking.Filtering`입니다.

```csharp
// 초기화: 이 객체와 배열은 프레임 사이에 재사용한다.
var settings = OneEuroFilterSettings.Default;
using var filter = LandmarkFilterCoordinator.CreateOneEuro(in settings, LandmarkFilterTracker.Hand);
var raw = new MpudNormalizedLandmark[HandLandmarks.Count];
var filtered = new MpudNormalizedLandmark[HandLandmarks.Count];
ulong confirmedTrackToken = 0; // 0은 대상 연속성을 아직 증명하지 못했다는 뜻이다.

// Hand의 Success 완료에서 실행한다.
var count = hand.CopyLatestHandLandmarksTo(0, raw);
var metadata = hand.LatestMetadata;
var context = new LandmarkFilterContext(
    streamId: 1, tracker: LandmarkFilterTracker.Hand, target: 0,
    coordinate: LandmarkFilterCoordinate.NormalizedImage,
    timestampUs: metadata.TimestampUs, captureEpoch: metadata.CaptureEpoch,
    continuityToken: confirmedTrackToken, captureId: metadata.CaptureId,
    frameCount: metadata.FrameCount, captureTimestampUs: metadata.CaptureTimestampUs);
if (!filter.TryProcess(raw.AsSpan(0, count), filtered, in context, out var written))
    Debug.LogError(filter.LastError);
```

- 동일 timestamp·epoch·대상·토큰 재조회는 캐시한 출력을 반환하고 필터 상태를 다시 전진시키지 않습니다.
- 시간 역행, epoch·스트림·대상·좌표 변경, 토큰 변경, `Reset()` 및 `ReplaceFilter()`는 상태를 초기화합니다.
- 토큰 0은 매 새 프레임 reset합니다. 따라서 **연속성 미증명 상태의 기본 One Euro 출력은 raw와 같습니다.** 임의의 고정 토큰이나 프레임 ordinal로 사람 ID를 만들어 이 보호를 우회하지 마세요.
- 추적 손실·오류·stale·서비스 reset을 받으면 관리 계층에서도 `filter.Reset()`을 호출합니다. 빈 입력은 이력을 지우고 `NoData`, count 0을 반환합니다.
- 입력/출력 overlap, 부족한 용량, 잘못된 count, NaN/Infinity, 사용자 필터 예외는 정상 출력으로 발행하지 않습니다. `TryProcess`의 `false`와 `LastResult`/`LastError`를 확인하세요. 명시적 `Reset`/`ReplaceFilter`는 사용자 `Reset` 예외를 호출자에게 전달하지만 이전 캐시를 정상 결과로 재사용하지 않습니다. 소유한 사용자 필터의 `Dispose`가 실패해도 coordinator는 즉시 종료 상태가 되며 캐시를 다시 노출하지 않습니다.
- 기본 One Euro는 기존 `LandmarkFilterState` 계산 코어를 사용합니다. 일정한 입력 용량으로 워밍업한 뒤 **필터 자체**의 프레임별 managed 할당이 없음을 회귀 검사합니다. 입력 캡처·호출자 배열 생성·사용자 구현의 비용까지 0이라고 보장하지 않습니다.

### 소비자 구현 연결

다음은 출력 차이를 확인하기 위한 작은 예제입니다. 실제 프로젝트에서는 같은 topology·좌표 의미를 유지하는 필터를 구현하세요.

```csharp
public sealed class OffsetFilter : ILandmarkFilter
{
    private readonly float _offset;
    public OffsetFilter(float offset) => _offset = offset;

    public int Filter(ReadOnlySpan<MpudNormalizedLandmark> input,
        Span<MpudNormalizedLandmark> output, in LandmarkFilterContext context)
    {
        input.CopyTo(output);
        for (var i = 0; i < input.Length; i++) output[i].x += _offset;
        return input.Length;
    }

    public void Reset() { }
    public void Dispose() { }
}

// CreateOneEuro 대신 소비자 구현을 명시적으로 선택한다.
using var custom = new LandmarkFilterCoordinator(new OffsetFilter(0.001f), ownsFilter: true);
```

필요한 using은 `System`, `MediaPipeUnityDots.Runtime.Interop`, `MediaPipeUnityDots.Runtime.Tracking.Filtering`입니다. 필터는 호출 중에만 span을 사용하고 보관하지 않습니다. 출력 count는 입력 count와 같아야 하며 순서·visibility/presence 의미를 유지합니다. 호출은 coordinator를 사용하는 메인 스레드에서 동기 실행합니다. span을 비동기 작업에 넘기지 말고, Unity API를 사용한다면 이 메인 스레드 호출 안으로 제한하세요.

`CreateOneEuro`는 기본 구현을 소유·해제합니다. 외부 구현은 기본적으로 호출자가 소유하며 `ownsFilter: true`로만 해제 책임을 이전합니다. `ReplaceFilter`는 이전 owned 구현을 해제합니다. 서로 다른 coordinator가 하나의 상태를 가진 구현을 공유하지 마세요.

### ECS 경로

`LandmarkFilterSystem`은 raw Hand/Face/Pose singleton을 읽고 별도 `FilteredLandmarkStatus`, `FilteredLandmarkElement`, `FilteredWorldLandmarkElement`를 게시합니다. Holistic의 부위 결과는 기존 Hand/Face/Pose 경로로 공급됩니다. 렌더 엔티티는 필요 없습니다.

`FilteredLandmarkReader.TryCopy`/`TryCopyWorld`로 caller-owned span에 복사하거나 DynamicBuffer의 `Target`/`Index`를 읽습니다. Face world처럼 제공되지 않는 결과는 성공으로 위장하지 않습니다. ECS의 기존 raw 요소가 보존하지 않는 품질 필드는 서비스 복사 API에서 읽으세요.

대상 연속성을 확인한 소비자는 raw singleton의 선택적 `LandmarkContinuityElement` 버퍼에 target별 토큰을 공급할 수 있습니다. 누락/0은 연속성 미증명입니다. 기본 샘플은 ordinal이나 handedness로 영속 ID를 만들어 주지 않습니다.

managed 인터페이스/coordinator를 컴포넌트·버퍼·Job에 넣지 않습니다. managed 사용자 필터는 관리 계층에서 호출하고 필요한 값만 자신의 ECS 데이터에 전달합니다. 사용자 구현을 자동으로 Burst/Jobs로 실행하는 계약은 없습니다. 기본 ECS 필터의 상태는 unmanaged이고 렌더러는 이미 필터된 값을 표시만 합니다.

## 선택형 Samples

### Tracking Demo

1. URP `com.unity.render-pipelines.universal` `17.6.0`을 설치하고 샘플을 Import합니다. UI 모듈을 제외한 최소 프로젝트에서는 `com.unity.modules.uielements` `1.0.0`도 활성화합니다.
2. Import한 `Settings/DemoRenderPipeline.asset`을 프로젝트 Graphics/Quality의 Render Pipeline Asset으로 지정합니다.
3. 사용할 Task 모델을 준비합니다. Depth를 사용할 경우 선택적 모델도 준비하고 Provider의 ModelAsset을 연결합니다.
4. `Scenes/SampleScene.unity`를 엽니다. 카메라 권한과 Player의 Camera Usage Description은 소비자 프로젝트에서 설정합니다.
5. UI는 UI Toolkit이며 `.uxml`/`.uss`, PanelSettings, 테마, 프로필, 씬 참조가 샘플 안에 있습니다. 편집은 Import한 복사본에서 합니다.

### Landmark API

이 샘플은 원본 SampleScene 없이 서비스·복사·필터를 검증합니다. 이미지 로딩을 위해 `com.unity.modules.imageconversion` `1.0.0`이 필요합니다. 테스트 사진은 재배포하지 않습니다. `Fixtures/NOTICE.txt`에 출처가 있고 저장소의 소비자 검증 스크립트가 해시를 확인하며 준비합니다.

Editor 메뉴는 **MediaPipe → Landmark API → Run Consumer Smoke**입니다. 입력 위치는 `Assets/StreamingAssets/MediaPipe/Fixtures/` 또는 `-mpudFixture`로 지정한 이미지와 같은 폴더의 `portrait.jpg`입니다. `BuildStandalone` 배치 진입점은 별도 검증 프로젝트에서만 실행하며 임시 씬으로 macOS Player를 만듭니다.

## 라이선스

자체 코드: [MIT](LICENSE.md). 네이티브 의존성·Unity 패키지·모델·사진은 [Third Party Notices](Third%20Party%20Notices.md)의 별도 조건을 따릅니다. [변경 내역](CHANGELOG.md)도 확인하세요.
