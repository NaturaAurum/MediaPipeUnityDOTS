# Landmark → Unity World 좌표 변환

## 원본 데이터와 표시 좌표

`HandWorldLandmarkElement`와 `PoseWorldLandmarkElement`는 **MediaPipe 월드 랜드마크 원본(미터)**이다.
Unity 씬 좌표가 아니다. 브리지와 프로바이더는 XYZ를 그대로 복사하며, 외부 소비자가 가공할 수 있도록 원본 버퍼를 유지한다.
정규화 버퍼와 같은 대상·포인트 인덱스를 사용한다. Hand/Pose 및 Holistic의 해당 부위에 적용되며 Face에는 월드 출력이 없다.
Face는 대신 52개 blendshape score를 `FaceBlendshapeElement` 버퍼(얼굴당 52개)로 전달한다.

렌더링은 **영상 픽셀 정합**을 우선한다. XY는 정규화 영상 좌표, 깊이는 월드 Z에서 얻는다.
이는 원래 미터 단위 3D 형상을 보존하는 변환도, 카메라에서 대상까지의 절대 거리를 복원하는 방법도 아니다.

## 1. 배경 매핑과 카메라 투영

`WebcamBackgroundRenderer`가 Quad와 UV 크롭·반전을 소유하고 `LandmarkOverlayMapping`에 다음 값을 발행한다.

- Quad 중심 `Origin`, 전체 너비·높이 벡터 `AxisX`, `AxisY`, 카메라 전방 `Forward`
- UV scale/offset, 제출 이미지의 `Flipped`
- `CameraPosition`, `NearClipPlane`, `IsPerspective`

정규화 좌표를 배경 평면의 위치로 바꾸는 식은 다음과 같다.

```text
u = (x - UvOffsetX) / UvScaleX
v = ((Flipped ? 1 - y : y) - UvOffsetY) / UvScaleY
plane = Origin + (u - 0.5) * AxisX + (v - 0.5) * AxisY
```

기존 제출 이미지/UV 반전 규약을 유지한다. `videoVerticallyMirrored`일 때 제출 배열을 뒤집는다.
`videoRotationAngle`을 이용한 90°/270° 보정은 아직 지원하지 않는다.

`MapWithDepth(x, y, depth, mapping)`은 평면 위치를 **같은 화면 픽셀에 투영되는 위치**로 이동한다.

```text
planeDepth = dot(plane - CameraPosition, Forward)
offset = depth - 0.05
// 근접 클리핑을 넘지 않도록 offset 하한을 적용한다.
원근: position = CameraPosition + (plane - CameraPosition) * (1 + offset / planeDepth)
직교: position = plane + Forward * offset
```

단순히 전방 벡터로만 이동하면 원근 카메라에서 화면 XY가 밀린다. 원근 모드에서는 카메라 광선 위에서 이동하여 이를 막는다.
`Map(x, y, mapping)`은 깊이 0인 같은 함수를 사용한다. 따라서 2D와 3D 모두 동일한 픽셀 정합과 한 번의 전방 여유 거리(0.05)를 적용한다.

## 2. 표시용 깊이

Hand/Pose 렌더 시스템은 각 대상의 유효한 정규화·월드 쌍을 한 번 순회해 범위를 집계한다.
관리 배열이나 임시 NativeArray는 만들지 않으며, 대상별 최종 값은 스택의 `FixedList128Bytes<float2>`에 보관한다.

```text
imageSize = (정규화 X 범위 * |AxisX| / UvScaleX,
             정규화 Y 범위 * |AxisY| / UvScaleY)
scale = length(imageSize) / length(월드 XY 범위)
relativeZ = worldZ - 대상의 최대 worldZ
표시 깊이 = filteredRelativeZ * scale
```

- 고정 배율(손 ×4, 포즈 ×1)을 제거했다. 영상상의 대상 크기·Quad 크기·크롭에 따라 깊이 배율이 달라진다.
- 월드 XY 범위가 퇴화하면 깊이 배율은 0이다. 단안 추론 결과로 배율을 정할 수 없을 때 깊이를 임의로 증폭하지 않는다.
- 최대 Z를 기준으로 빼므로 모든 점이 배경 앞에 배치된다. 작은 Z가 더 카메라 쪽이며, 불투명 배경에 점이 가려지는 것을 방지한다.
- 월드 원점을 일괄 이동해도 표시 결과는 변하지 않는다. 손목/고관절 원점 가정은 필요 없다.
- 월드 데이터가 없는 포인트는 2D로 폴백한다. Face도 2D를 유지한다.

## 3. 필터와 시간

필터 입력은 `(정규화 X, 정규화 Y, 상대 월드 Z)`이다. 필터링한 월드 좌표에서 원시 손목을 빼던 혼합 경로는 제거했다.
입력 `TimestampUs`가 바뀔 때만 필터를 전진시키며, 2D/3D 전환 및 월드 데이터 유실·복귀 시 상태를 리셋한다.
필터를 켜면 XY는 평활화된 영상 좌표에 정합되므로 움직이는 원본 영상에 대한 필터 지연은 남는다.

매핑 발행은 `LateUpdate`, 렌더 시스템은 `SimulationSystemGroup`이다. 현재 구조에서 매핑 갱신에는 최대 한 프레임 지연이 있다.
원본 픽셀과 추론 결과의 시간 차이, 추론 오차, 깊이 배율의 프레임별 변화까지 제거하는 시간 동기화·카메라 보정 기능은 아니다.

## 4. 구현과 검증

- `Runtime/Ecs/Common/LandmarkOverlayMapping.cs`: 매핑 데이터, `Map`, `MapWithDepth`, 깊이 배율과 범위 집계.
- `Runtime/Ecs/Hand/HandLandmarkRenderSystem.cs`, `Runtime/Ecs/Pose/PoseLandmarkRenderSystem.cs`: 영상 XY + 상대 월드 Z 표시.
- `Sample/HandTracking/Scripts/WebcamBackgroundRenderer.cs`: Quad 배치와 카메라·UV 매핑 발행. 직교 카메라는 `orthographicSize`를 사용한다.
- `Tests/EditMode/LandmarkOverlayMappingTests.cs`: 원근/직교, 카메라 이동·회전, UV 크롭·반전, 깊이·근접 클리핑, 배율·퇴화 범위 회귀 검사.

시각적 결과는 미터 형상이 아닌 영상 정합용 3D 표현이다. 물리 연산이나 별도 3D 아바타에는 표시 좌표 대신 원본 월드 버퍼를 사용한다.
