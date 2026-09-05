# Landmark Noise Filter 알고리즘 분석 및 설계

MediaPipe 랜드마크(Hand, Face, Pose, Holistic)의 미세 떨림(Jitter)을 억제하고, 실시간 반응성(Low Latency)을 보장하기 위한 노이즈 필터링 알고리즘 조사 및 설계 문서다.

---

## 1. 문제 정의 및 요구사항

### 1.1 랜드마크 지터의 원인
1. **센서/조명 노이즈**: 웹캠 센서의 고주파 노이즈와 프레임별 셔터/노출 변동.
2. **신경망 양자화/추론 편차**: 프레임마다 바운딩 박스 ROI 및 키포인트 히트맵 회귀 시 수 픽셀 단위의 미세 변동 발생.
3. **깊이(Z축) 불안정성**: 단안(Monocular) 2D 이미지에서 추정한 Z축은 X, Y축 대비 분산이 2~3배 이상 큼.

### 1.2 핵심 상충 관계 (Trade-off)
- **Smoothness (떨림 억제)**: 손이나 얼굴이 멈춰 있을 때 랜드마크가 고정되어야 함 (UI 인터랙션, 버추얼 아바타 떨림 방지).
- **Latency / Responsiveness (반응성)**: 손을 빠르게 휘두르거나 표정을 바꿀 때 필터로 인한 지연(Lag)이나 끌림 현상이 없어야 함.

### 1.3 Unity DOTS / ECS 기술 제약
- **실시간 대량 포인트 연산**: Holistic 기준 프레임당 574개 랜드마크 ($1,722$ float 채널)를 60~120fps 환경에서 지연 없이 필터링해야 함.
- **Burst 호환성**: GC Alloc이 없어야 하며, `Unity.Mathematics` 기반 SIMD 연산 및 Burst 컴파일이 가능해야 함.
- **순수 비관리 데이터**: ECS 컴포넌트(`IComponentData`, `IBufferElementData`)로 직렬화 가능한 단순 상태 구조여야 함.

---

## 2. 후보 필터 알고리즘 비교

| 알고리즘 | 지터 억제 (정지) | 반응성 (고속 이동) | 프레임 지연 (Lag) | 상태 메모리 (점당) | 계산 복잡도 | Burst 적합성 |
| :--- | :---: | :---: | :---: | :---: | :---: | :---: |
| **이동평균 (Moving Average)** | 보통 | 불량 | 고정 지연 ($N/2$) | $N \times 12$ B | 낮음 | 보통 (버퍼 필요) |
| **지수이동평균 (EMA / IIR)** | 보통 | 불량 (지연 심함) | 가변 (계속 누적) | $12$ B | 매우 낮음 | 우수 |
| **칼만 필터 (Kalman Filter)** | 우수 | 보통 | 보통 | $80 \sim 150$ B | 높음 (행렬 연산) | 보통 (연산 과다) |
| **이중 지수 평활 (Holt-Winters)**| 양호 | 보통 | 보통 | $24$ B | 낮음 | 우수 |
| **1 Euro Filter** | **최우수** | **최우수 (무지연)** | **속도 적응형** | **$24$ B** | **낮음 (스칼라/벡터)** | **최우수** |

### 2.1 이동평균 / Savitzky-Golay (FIR Window 계열)
- **원리**: 최근 $N$개 프레임의 위치를 큐에 보관하고 가중합.
- **단점**: 실시간 인터랙션에서 $N/2$ 프레임만큼의 고정 시간 지연 발생 (30fps 기준 $N=5$일 때 66ms 지연). 실시간 상호작용에 부적합.

### 2.2 지수이동평균 (EMA / Simple Low-pass)
- **원리**: $\hat{x}_t = \alpha x_t + (1 - \alpha) \hat{x}_{t-1}$
- **단점**: 고정된 $\alpha$ 계수를 사용하므로, 정지 시 떨림을 잡으려 $\alpha$를 낮추면 빠른 동작에서 지연(Lag)이 발생하고, $\alpha$를 높이면 떨림이 그대로 통과함.

### 2.3 칼만 필터 (Kalman Filter / Constant Velocity Model)
- **원리**: 위치-속도 상태 벡터와 공분산 행렬($Q, R$)을 갱신하며 최적 추정.
- **장점**: 측정 오차와 시스템 노이즈가 가우시안일 때 수학적 최적해.
- **단점**:
  - 점당 공분산 행렬 역연산/곱셈으로 연산량 과다 (574개 포인트 $\times$ 6차원 행렬 = 수천 회 연산).
  - 사람의 불규칙한 손/얼굴 움직임은 등속도 모델을 벗어나므로 오버슈트(Overshoot) 현상 발생.

### 2.4 1 Euro Filter (Casiez et al., 2012)
- **원리**: 신호의 **변화율(속도)** 에 따라 차단 주파수(Cutoff Frequency)를 동적으로 조절하는 1차 적응형 저주파 통과 필터.
  - **정지/저속 이동**: 차단 주파수를 낮춰 노이즈를 강력하게 억제 (Jitter 제거).
  - **고속 이동**: 차단 주파수를 높여 원본 신호를 거의 그대로 통과 (Lag 제거).
- **장점**:
  - 인간-컴퓨터 상호작용(HCI), VR 트래킹, MediaPipe 공식 레퍼런스(OneEuroFilterCalculator)에서 표준으로 사용됨.
  - 점당 상태 저장 공간이 매우 작음 (이전 필터값, 이전 미분값 = float 2개/축).
  - 행렬 연산 없이 완전한 SIMD 벡터화 가능 (`float3` 단위 연산).

---

## 3. 최적 알고리즘: 1 Euro Filter 수식 정의

1 Euro Filter는 속도($\dot{x}$) 추정과 신호($x$) 필터링의 2단계 1차 지수 평활로 구성된다.

### 3.1 수식 유도

시간 간격 $\Delta t = t - t_{prev}$ 일 때:

1. **신호 변화율(속도) 추정**:
   $$dx_t = \frac{x_t - \hat{x}_{t-1}}{\Delta t}$$

2. **변화율 평활화 (미분 노이즈 제거)**:
   $$\alpha_d = \frac{1}{1 + \frac{\tau_d}{\Delta t}}, \quad \tau_d = \frac{1}{2\pi f_{c_{d}}}$$
   $$\hat{dx}_t = \alpha_d \cdot dx_t + (1 - \alpha_d) \cdot \hat{dx}_{t-1}$$
   *(일반적으로 $f_{c_{d}} = 1.0\text{ Hz}$ 고정)*

3. **적응형 차단 주파수 계산**:
   $$f_c = f_{c_{min}} + \beta \cdot |\hat{dx}_t|$$
   - $f_{c_{min}}$: 정지 상태 차단 주파수 (떨림 억제 강도 결정)
   - $\beta$: 속도 반응 계수 (값이 클수록 움직일 때 래그 제거)

4. **최종 신호 필터링**:
   $$\alpha = \frac{1}{1 + \frac{\tau}{\Delta t}}, \quad \tau = \frac{1}{2\pi f_c}$$
   $$\hat{x}_t = \alpha \cdot x_t + (1 - \alpha) \cdot \hat{x}_{t-1}$$

---

## 4. 부위별 권장 튜닝 파라미터

MediaPipe 랜드마크 데이터 특성에 맞춘 시작 파라미터는 다음과 같다:

| 트래커 | $f_{c_{min}}$ (정지 안정성) | $\beta$ (속도 반응성) | $f_{c_{d}}$ | 비고 |
| :--- | :---: | :---: | :---: | :--- |
| **Hand (손)** | `1.0 Hz` | `0.007` | `1.0 Hz` | 미세 손가락 떨림을 잡으면서 빠른 제스처 스윙 추종 |
| **Face (얼굴)** | `0.6 Hz` | `0.004` | `1.0 Hz` | 얼굴은 급격한 순간이동이 적으므로 안정성에 무게 |
| **Pose (신체)** | `0.5 Hz` | `0.010` | `1.0 Hz` | 몸통 안정화 우선, 팔/다리 스윙 시 빠른 반응 |
| **Z축 (공통)** | `0.3 Hz` | `0.002` | `1.0 Hz` | 단안 깊이 노이즈가 크므로 X/Y 대비 강력 필터링 |

---

## 5. Unity DOTS / ECS 아키텍처 연계 방안

### 5.1 필터링 적용 계층 비교
- **계층 후보 1: Native C++ Bridge**
  - 장점: Unity 도달 전 원본 데이터 정제.
  - 단점: 씬/부위별 실시간 파라미터 튜닝 불가, C++ 재빌드 오버헤드.
- **계층 후보 2: ECS System (추천)**
  - 장점:
    - Burst 컴파일 기반 일괄 SIMD 처리.
    - 정규화 랜드마크 버퍼(`LandmarkElement`) 단계 또는 World 트랜스폼 반영 직전 단계에서 선택적 적용.
    - 부위별 파라미터를 ECS 싱글턴 컴포넌트에서 실시간 튜닝 가능.

### 5.2 ECS 데이터 구조 설계 (Burst-ready)

```csharp
// 점당 이전 상태 저장 버퍼 (비관리 순수 데이터)
public struct OneEuroFilterState : IBufferElementData
{
    public float3 PrevFiltered;
    public float3 PrevDerivative;
    public int Initialized;
}

// 필터 파라미터 설정 컴포넌트
public struct OneEuroFilterConfig : IComponentData
{
    public float MinCutoff; // f_c_min (Hz)
    public float Beta;      // 속도 가중치
    public float DCutoff;   // f_c_d (기본 1.0 Hz)
}
```

### 5.3 1 Euro Filter 순수 함수 (Burst 인라인 가능)

```csharp
[BurstCompile]
public static class OneEuroFilterMath
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float3 Filter(
        float3 current,
        ref OneEuroFilterState state,
        in OneEuroFilterConfig config,
        float dt)
    {
        if (state.Initialized == 0 || dt <= 0f)
        {
            state.PrevFiltered = current;
            state.PrevDerivative = float3.zero;
            state.Initialized = 1;
            return current;
        }

        // 1. 변화율 계산 및 필터링
        float3 dx = (current - state.PrevFiltered) / dt;
        float dAlpha = ComputeAlpha(config.DCutoff, dt);
        float3 hatDx = math.lerp(state.PrevDerivative, dx, dAlpha);

        // 2. 속도에 따른 적응형 차단 주파수 산출
        float3 speed = math.abs(hatDx);
        float3 cutoff = config.MinCutoff + config.Beta * speed;

        // 3. 최종 값 필터링
        float3 alpha = ComputeAlpha(cutoff, dt);
        float3 filtered = math.lerp(state.PrevFiltered, current, alpha);

        // 4. 상태 갱신
        state.PrevFiltered = filtered;
        state.PrevDerivative = hatDx;

        return filtered;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float ComputeAlpha(float cutoff, float dt)
    {
        float tau = 1f / (2f * math.PI * cutoff);
        return 1f / (1f + tau / dt);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float3 ComputeAlpha(float3 cutoff, float dt)
    {
        float3 tau = 1f / (2f * math.PI * cutoff);
        return 1f / (1f + tau / dt);
    }
}
```

---

## 6. 결론 및 권장 구현 단계

1. **최적 알고리즘**: **1 Euro Filter**가 지터 억제, 지연 최소화, 연산 효율, Burst/ECS 적합성 면에서 압도적으로 최적이다.
2. **구현 1단계 (Core Math)**: `OneEuroFilterMath` 및 상태 컴포넌트 정의.
3. **구현 2단계 (ECS Integration)**:
   - `HandLandmarkRenderSystem`, `FaceLandmarkRenderSystem`, `PoseLandmarkRenderSystem` 직전 또는 내부에서 `Map()` 전 정규화 좌표를 필터링하도록 연계.
4. **구현 3단계 (Z축 독립 튜닝)**: X/Y 평면 대비 Z축의 $f_{c_{min}}$을 낮추어 깊이 튀는 현상 집중 완화.
