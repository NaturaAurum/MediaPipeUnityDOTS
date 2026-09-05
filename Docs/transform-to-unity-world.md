# Landmark → Unity World 좌표 변환

MediaPipe 정규화 랜드마크를 Unity world 좌표로 옮기는 모듈의 설계 문서다.
배경 WebcamTexture 픽셀과의 정합을 우선 목표로 한다.

## 1. 목표와 범위

- 목표: Hand/Face/Pose/Holistic이 공유하는 정규화→world 변환 함수 1개. 배경 Quad 픽셀과 일치시킨다.
- 범위 내: 정규화 랜드마크(`x`, `y` ∈ [0, 1], `z`는 이미지 너비 기준 정규화 깊이)의 Unity 매핑.
- 월드 랜드마크 (구현됨): Hand/Pose/Holistic이 월드 좌표(미터, pose는 고관절 중심 원점)를 같은 결과 구조체에 함께 싣는다.
  `WorldLandmarkElement`/`PoseWorldLandmarkElement` 버퍼로 ECS까지 전달되며 정규화와 같은 인덱스를 공유한다.
  Face는 Tasks 출력에 월드 좌표가 없어 제외. 배경 Quad 매핑(정규화 전용)과는 별개이며 렌더 경로는 미정.

## 2. 입력 보장

- `WebcamFrameProvider`는 `videoVerticallyMirrored`일 때만 `FlipVertical` 후 submit한다. MediaPipe는 항상 정립(upright) 이미지를 본다고 가정한다.
- `videoRotationAngle`은 사용하지 않는다. 세로(portrait) 웹캠은 미지원이다 (`WebcamBackgroundRenderer`의 ponytail 주석과 동일 가정).
- 좌우 미러 보정은 없다. 전면 카메라 프리뷰 미러링은 UV에서만 처리된다.

## 3. 배경 정합 매핑 (구현됨)

`WebcamBackgroundRenderer`가 UV 크롭식을 단일 소유하고, 매 `LateUpdate`마다 `LandmarkOverlayMapping` singleton에 기록한다. Hand/Face/Pose 3개 RenderSystem은 `LandmarkOverlayMapping.Map`을 공유 호출한다. 이전 고정 rect 매직 상수(`WorldWidth = 2`, `WorldHeight = 1.5`)는 삭제됐다.

```text
u = (x - UvOffsetX) / UvScaleX
v = ((Flipped ? 1 - y : y) - UvOffsetY) / UvScaleY
world = Origin + (u - 0.5) * AxisX + (v - 0.5) * AxisY - Forward * 0.05
```

- 리더는 반전 없이 직접 인덱싱한다. flip=false면 y가 배열 분율 그대로, flip=true면 뒤집힌 배열에서 읽으므로 위와 같이 복원한다.
- 비디오가 없으면 `IsValid = 0`을 발행하고 포인트를 숨긴다.
- ECS 시스템은 `SimulationSystemGroup`, 발행은 `LateUpdate`이므로 최대 1프레임 지연이 있다.

## 4. 참조: MediaPipeUnityPlugin

`homuler/MediaPipeUnityPlugin`, `Runtime/Scripts/Unity/CoordinateSystem/` 기준이다.

- 정규화→로컬 핵심식 (`ImageCoordinate.ImageNormalizedToLocalPoint`):
  - 회전 90°/270°이면 `(nx, ny)`를 맞바꾼다.
  - x는 `Lerp(xMin, xMax, nx)`, y는 `Lerp(yMax, yMin, ny)`가 기본이다. 즉 y 뒤집기는 기본 동작이다.
  - z는 `zScale * nz`이며, `zScale` 미지정 시 rect 너비를 쓴다 ("Z usually uses roughly the same scale as X").
- 회전/미러 반전 테이블 (`ImageCoordinate`/`RealWorldCoordinate` 공통):

| 조건 | X 반전 | Y 반전 | 축 교환 |
|------|--------|--------|---------|
| Rotation0, 미러 없음 | 없음 | 있음 | 없음 |
| Rotation90, 미러 없음 | 있음 | 있음 | 있음 |
| Rotation180, 미러 없음 | 있음 | 없음 | 없음 |
| Rotation270, 미러 없음 | 없음 | 없음 | 있음 |
| Rotation0, 미러 있음 | 있음 | 있음 | 없음 |

- 실월드 랜드마크 (`RealWorldCoordinate.RealWorldToLocalPoint`): 미터값을 `scale`로 곱하고, pose는 `_hipHeightMeter`(기본 0.9m)만큼 원점을 들어 올린다 (`PoseWorldLandmarkListAnnotationController`, `_scale` 기본 100).

우리 식과의 차이 2가지:

1. y 뒤집기는 일치한다. `Flipped ? y : 1 - y`는 반전 테이블의 Rotation0 행과 같다.
2. z 부호가 다르다. 참조는 `+zScale * nz` 유지, 우리는 Quad 평면 앞에 고정 배치한다. 참조의 포인트는 화면 공간 Rect 위에 직접 그리므로 z는 깊이 힌트에 가깝고, 우리는 엔티티를 배경 앞 3D 공간에 두므로 평면으로 고정했다.

## 5. 구현 위치

- `Runtime/Ecs/LandmarkOverlayMapping.cs`: singleton 컴포넌트 + Burst 호환 `Map`.
- `Sample/HandTracking/Scripts/WebcamBackgroundRenderer.cs`: `PublishOverlayMapping`/`TryGetMappingEntity`. Sample→ECS push이며 씬 배선 추가는 없다.
- `Runtime/Ecs/Hand|Face|PoseLandmarkRenderSystem.cs`: `Map` 호출로 교체. Holistic 렌더 시스템은 아직 없어 적용 대상이 아니다.

## 6. 미결정 사항

- z 스케일: 평면 고정 유지 vs landmark z 반영.
- Holistic 렌더 포인트 수: Face 478 + Pose 33 + 양손 21×2 = 574개를 전부 스폰할 것인가.

## 7. 검증 기준

- 수식 단위 검증: 원시 배열 분율 마커(jx=0.3, j=0.2)를 flip 양쪽·크롭 3종에 제출→Map→배경 샘플링 round-trip으로 복원 확인 (6/6 통과).
- Editor 확인 필요: 실프레임에서 포인트가 배경 영상 특징 위에 올라오는지. Unity 컴파일은 이 환경에서 불가하므로 Editor 실행 체크가 남았다.
