# 단안 깊이 추정 R&D (웹캠 Z축 보강)

MediaPipe 랜드마크의 Z축(깊이)은 단안 2D 추정의 한계로 X/Y 대비 분산이 크다.
웹캠 1대로 깊이를 보강하기 위한 오픈소스 후보 조사 기록이다.

## 1. 배경

- 현 파이프라인의 Z는 이미지 너비 기준 정규화 깊이이며, 단안이라 절대 척도가 없다.
- Unity 필터(`OneEuroFilter`, Z축 `0.3/0.002` 강필터)는 시간축 평활만 하며 기하학적 근거는 못 만든다.
- 목표: 손/얼굴 ROI의 깊이를 외부 근거로 보정하는 최소 실험 단위 확보.

## 2. 후보 목록

| 모델 | 출력 | 라이선스 | 비고 |
|---|---|---|---|
| Depth Anything V2 (Small, ViT-S) | 상대 깊이 | Apache-2.0 | 표준 선택지. 생태계·ONNX 자료 최다 |
| MiDaS (small) | 상대 깊이 | MIT | 가장 가볍고 검증됨. 정확도는 한 세대 전 |
| ZoeDepth | 미터 깊이 | MIT | MiDaS 기반 + metric head. 실내 근거리 강함 |
| Depth Pro (Apple) | 미터 깊이, 고해상도 | Apple 커스텀 (상용 제한적) | 품질 최상급이나 라이선스·무거움 주의 |
| MoGe (Microsoft) | 깊이+포인트맵+노멀 | MIT (DINOv2 부분 Apache-2.0) | 3D 복원용. 실시간보다 품질 위주 |
| YOLO26-Depth (Ultralytics) | 상대 깊이 | AGPL-3.0 | 빠르다고 주장하나 상용은 유료 라이선스 필요 |
| Marigold (diffusion) | 상대 깊이 | Apache-2.0 | 느려서 실시간 제외 |
| UniDepth / Metric3D | 미터 깊이 | 각자 확인 필요 | metric 필요시 비교군 |

## 3. 선정 기준 (우리 프로젝트)

1. **라이선스**: 상용 전제면 MIT/Apache만. AGPL(YOLO), Apple 커스텀(Depth Pro) 제외.
2. **실시간성**: ViT-S급 소형 + 저해상도 입력(256~384px) + ONNX/CoreML 경로가 있는 것.
3. **목적 적합성**: 풀프레임 metric보다 손/얼굴 ROI 상대 깊기의 시계열 안정화가 우선.
4. **1차 조합**: Depth Anything V2 Small (또는 MiDaS small) + 기존 Z축 강필터.

## 4. 배포 현실 (macOS + Unity)

- ONNX→CoreML 또는 네이티브 브리지 경유 실행이 필요하고 MediaPipe 추론과 GPU를 나눠 쓴다.
- 30fps 유지는 소형 모델 + 저해상도 입력 전제. 전제 깨지면 후순위.

## 5. 다음 단계

1. DA-V2 Small ONNX 확보 후 테스트 이미지 깊이맵 스파이크.
2. 손 ROI 깊이 vs MediaPipe Z 상관 확인.
3. 상관 있으면 브리지 입력 또는 후처리 가중치 설계로 진행.

## 6. 추가 조사: 실시간 성능 비교 및 MediaPipe 결합 방식

### 6.1 모델별 성능 및 추천 환경

| 모델 | 실시간성 (FPS) | 정밀도 / 엣지 | 특징 | 추천 상황 |
| :--- | :--- | :--- | :--- | :--- |
| **Depth Anything V2 (Small)** | 빠름 (GPU 40~60+ FPS, CPU 10~15 FPS) | **최상 (SOTA)** | 사실상 표준. ONNX/TensorRT 변환 용이. Metric 버전 별도 존재 | **품질 최우선 (GPU 추론 가능 시)** |
| **MiDaS (v2.1 Small)** | **매우 빠름** (CPU 30+ FPS) | 보통 | 검증된 경량 모델. TFLite/ONNX 포맷 널리 배포됨 | CPU 전용 환경, 극단적 경량화 |
| **FastDepth** | **초고속** (100+ FPS) | 낮음 | MobileNet-NNConv 구조의 모바일/임베디드 타깃 | 극도로 연산 자원이 부족한 환경 |
| **Metric3D v2 / ZoeDepth** | 느림 (10~25 FPS) | 상 (실제 m 단위) | 카메라 왜곡/화각 반영 절대 거리 추정 | 3D 복원 (실시간 트래킹용엔 무거움) |

### 6.2 MediaPipe 연동 3가지 패턴

1. **Dense Depth Map Keypoint 샘플링 (전체 뎁스 버퍼 활용)**
   - 웹캠 프레임 1개를 입력으로 받아 MediaPipe(랜드마크 검출)와 Depth 모델(Unity Sentis / ONNX Runtime)을 병렬 실행.
   - MediaPipe 2D 키포인트 $(x, y)$ 좌표의 Depth 픽셀값을 샘플링하여 Z축 보정 근거로 사용.

2. **하이브리드 Metric 보정 (Scale Factor 추정)**
   - 단안 Depth 모델의 '상대 깊이(Relative Depth, 절대 거리 모호성)' 한계 극복 방식.
   - MediaPipe 안면(양 눈간 거리 약 6.3cm) 또는 손바닥 크기 기반으로 기준 거리(Scale factor)를 계산.
   - 상대 Depth 맵 전체를 실제 미터 단위(Metric)로 스케일 변환.

3. **손/얼굴 자체 Z축 분산 보완용 한계 인지**
   - 배경과의 상호작용(오클루전, 전신 배치)이 아닌 손가락/얼굴 내부의 3D 형태는 MediaPipe의 `WorldLandmarks`가 단안 깊이 모델보다 해상력(관절 단위 구분)이 높음.
   - 단안 Depth 모델은 전체 오브젝트의 카메라 대비 Z 거리(글로벌 깊이) 기준선 제공에 집중하는 것이 효과적.

## 출처

- https://depth-anything-v2.github.io/
- https://github.com/isl-org/MiDaS
- https://github.com/isl-org/ZoeDepth
- https://github.com/apple/ml-depth-pro/blob/main/LICENSE
- https://github.com/microsoft/moge
- https://docs.ultralytics.com/tasks/depth
- https://github.com/dwofk/fast-depth
- https://github.com/YvanYin/Metric3D
