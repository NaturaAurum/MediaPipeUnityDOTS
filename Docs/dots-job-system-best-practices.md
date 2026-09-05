# Unity DOTS (ECS, Job System, Burst) 아키텍처 및 Best Practice R&D 보고서

본 문서는 **Unity 6 (6000.6.0f1)** 및 **Entities 6.6.0** 환경에서 MediaPipe 실시간 트래킹 파이프라인(Hand, Face, Pose, Holistic)을 최고 성능과 무결성으로 구동하기 위한 Unity DOTS(Data-Oriented Technology Stack) 최적 설계 패턴, 핵심 지침, 그리고 권장 시스템 아키텍처를 정의한다.

---

## 1. 개요 및 R&D 배경

### 1.1 프로젝트 기술적 도전 과제
- **대규모 실시간 데이터 스트림**:
  - Hand(최대 2손 $\times$ 21 = 42개 점), Face(478개 점), Pose(33개 점), Holistic(총 574개 점)의 3D 좌표($x, y, z$)와 신뢰도 데이터를 60~120fps로 연속 처리해야 함.
  - 다중 인물 또는 대량의 랜드마크 포인트 시각화 시 매 프레임 수천 개의 트랜스폼 및 노이즈 필터 연산이 요구됨.
- **실시간 반응성과 0 GC 제약**:
  - WebRTC/웹캠 입력부터 Native 추론, 노이즈 필터링(1 Euro Filter), 월드 변환, 렌더링까지 엔드투엔드 지연시간(End-to-End Latency)을 30ms 미만으로 억제해야 함.
  - 가비지 컬렉션(GC Alloc)으로 인한 프레임 드롭(Spike)을 완전히 제거(0 Bytes / Frame)해야 함.
- **이종 아키텍처 경계 격리**:
  - 관리형(Managed) UI/App 계층 (`UI Toolkit + MVVM + R3 + UniTask + VContainer`)과 비관리형(Unmanaged) 순수 데이터 중심의 ECS 코어 간 명확한 인터페이스 분리가 필수적임.

### 1.2 목표 아키텍처 핵심 지표
1. **메인 스레드 렌더/필터링 연산 시간**: 프레임당 $< 0.5\text{ ms}$ (워커 스레드 병렬화).
2. **Structural Changes (구조적 변경) 최소화**: 런타임 프레임 중 동적 컴포넌트 추가/제거 및 엔티티 생성/삭제 비용 $0\text{ ms}$ (Sync Point 완전 제거).
3. **CPU 캐시 히트율 극대화**: 16KB L1/L2 캐시 라인 친화적인 SoA(Structure of Arrays) 아키타입 배치.
4. **Burst 컴파일 및 SIMD 벡터화 100% 달성**: `Unity.Mathematics` 기반 1 Euro Filter 및 변환 파이프라인.

---

## 2. Unity DOTS & ECS 핵심 아키텍처 원칙 (Unity 6 기준)

### 2.1 Data-Oriented Design (DOD)과 메모리 구조
Unity ECS는 객체 지향(OOP)의 메모리 파편화(Pointer Chasing, Cache Miss)를 극복하기 위해 메모리를 **Archetype**과 **16KB Chunk** 단위로 관리한다.

```
[ Traditional OOP (AoS) ]
Entity 0: [ Transform | FilterState | LandmarkData | MeshInfo ] -> Heap (파편화)
Entity 1: [ Transform | FilterState | LandmarkData | MeshInfo ] -> Heap (캐시 미스 다발)

[ Unity DOTS (Chunk & SoA) - 16KB Chunk Memory Layout ]
+-----------------------------------------------------------------------------------+
| Chunk Header | LocalTransform[] (연속 메모리) | LandmarkFilterState[] | ...       |
|              | [T0, T1, T2, T3, T4, ...]      | [F0, F1, F2, F3, ...]  |          |
+-----------------------------------------------------------------------------------+
  ==> CPU L1/L2 캐시 라인(64 Bytes)에 한 번에 적재되어 SIMD 자동 벡터화 및 초고속 처리 가능
```

- **AoS(Array of Structures) vs SoA(Structure of Arrays)**:
  - C# 배열이나 객체 배열은 AoS 형태이므로 특정 컴포넌트(`LocalTransform`)만 순회할 때 불필요한 필드까지 캐시 라인을 낭비함.
  - ECS 청크는 내부적으로 각 컴포넌트 타입별로 밀집 배열(Dense Array) 형태(SoA)로 저장되므로, 쿼리된 컴포넌트만 메모리 대역폭 100%로 순회함.

### 2.2 Component 타입 분류 및 올바른 사용 기준

| 컴포넌트 인터페이스 | 메모리 배치 및 특성 | 권장 용도 | 프로젝트 적용처 |
| :--- | :--- | :--- | :--- |
| **`IComponentData` (Unmanaged)** | 청크 내 연속 배열 저장. 128B 이하의 순수 `struct`. | 엔티티의 빈번히 읽고 쓰는 핵심 상태. | `HandLandmarkPoint`, `LandmarkFilterState`, `HandTrackingStatus` (싱글톤) |
| **`IBufferElementData`** | 청크 내부 헤더에 고정 크기 인라인 버퍼, 초과 시 힙 청크 할당. | 엔티티당 가변 길이의 순차 데이터. | `LandmarkElement` (21/478개 랜드마크 배열) |
| **`IEnableableComponent`** | 청크 재배치 없이 비트 플래그로 켜고 끔 (Zero Structural Change). | 조건부 활성화/비활성화되는 시스템 타겟. | 랜드마크 시각화 가시성 제어 (`LandmarkVisibleTag`) |
| **`ISharedComponentData`** | 동일한 값을 공유하는 엔티티들을 별도의 청크로 그룹화. | 렌더링 배칭, 머티리얼/메시 공유. | `RenderMeshArray` (Entities.Graphics) |
| **`ICleanupComponentData`** | 엔티티 파괴 시에도 컴포넌트가 남아 리소스 해제 후 완전 소멸. | 외부 네이티브 버퍼나 리소스 수명주기 정리. | Native 버퍼 핸들 정리 컴포넌트 |

#### ⚠️ `IEnableableComponent` vs `Scale = 0f` vs `DestroyEntity` 비교
현재 프로젝트는 비활성 랜드마크를 숨길 때 `transform.Scale = 0f` 방식을 사용 중이다:
1. **`Scale = 0f` 방식 (현재)**:
   - 장점: Structural Change가 발생하지 않음.
   - 단점: 렌더 파이프라인(Culling)과 연산 시스템(SystemAPI.Query)에서 여전히 엔티티를 순회하므로 CPU/GPU 오버헤드가 잔존함.
2. **`DestroyEntity` / `AddComponent` 방식**:
   - 단점: **절대 금지**. 매 프레임 청크 복사 및 메모리 재배치, Job Sync Point 유발.
3. **`IEnableableComponent` 방식 (최적 권장)**:
   - 장점: **Structural Change가 완전히 0(청크 이동 없음)**이면서, 쿼리 단계에서 비활성 엔티티가 자동으로 제외되므로 CPU 순회 비용이 0이 됨.

### 2.3 System 구조: `ISystem` vs `SystemBase`

- **`ISystem` (권장)**:
  - `unmanaged struct`로 선언되며, 가비지 컬렉터의 간섭을 받지 않음.
  - 구조체 자체가 Burst 컴파일되어 시스템 디스패치 및 수명주기(`OnCreate`, `OnUpdate`, `OnDestroy`) 오버헤드가 나노초 단위로 단축됨.
  - **프로젝트 전역 원칙**: 모든 런타임 시스템(`HandLandmarkRenderSystem`, `FilterSystem` 등)은 예외 없이 `ISystem`으로 작성한다.
- **`SystemBase` (제한적 사용)**:
  - `class` 기반의 관리형 시스템. 내부에서 Managed 객체(UI 이벤트, MonoBehaviour 접근)를 호출할 때만 사용.
  - 프로젝트에서는 `AGENTS.md` 정책에 따라 UI/App 계층의 Adapter나 Presenter에서 관리형 작업을 수행하므로, ECS 월드 내부에는 `SystemBase`를 두지 않는다.

### 2.4 System Groups 및 실행 순서 파이프라인
Unity 기본 루프에서 시스템 그룹의 실행 흐름은 다음과 같이 고정된다:

```
[ InitializationSystemGroup ]
  └── BeginInitializationEntityCommandBufferSystem
  └── NativeTrackingIngestionSystem (C++ 버퍼 -> ECS 동기화)
  └── EndInitializationEntityCommandBufferSystem

[ SimulationSystemGroup ]
  └── BeginSimulationEntityCommandBufferSystem
  └── LandmarkNoiseFilterSystem (1 Euro Filter 병렬 연산)
  └── LandmarkCoordinateTransformSystem (정규화 좌표 -> 월드 변환)
  └── EndSimulationEntityCommandBufferSystem

[ PresentationSystemGroup ]
  └── BeginPresentationEntityCommandBufferSystem
  └── Entities Graphics (BatchRendererGroup 기반 GPU 렌더링)
  └── EndPresentationEntityCommandBufferSystem
```

---

## 3. C# Job System & Burst Compiler Best Practices

### 3.1 Job 인터페이스 선택 및 비교 매트릭스

```
                   [ 랜드마크 처리 잡 선택 기준 ]
                                 │
           ┌─────────────────────┴─────────────────────┐
           ▼                                           ▼
   엔티티 쿼리 기반 처리?                       원시 네이티브 버퍼 기반?
           │                                           │
     ┌─────┴─────┐                               ┌─────┴─────┐
     ▼           ▼                               ▼           ▼
[IJobEntity]  [IJobChunk]                 [IJobParallelFor] [IJob]
(기본 권장,    (SIMD/Unsafe 직접 최적화,   (네이티브 버퍼 변환, (단일 대량
 보일러플레이트 0) Archetype 청크 직접 제어)   멀티코어 분할)     스트리밍)
```

| 인터페이스 | 코드 복잡도 | 메모리 접근 제어 | Burst 최적화 수준 | 적합한 작업 |
| :--- | :---: | :---: | :---: | :--- |
| **`IJobEntity`** | **매우 낮음** (자동 소스 생성) | 자동 (`in`, `ref`) | 우수 | 포인트 엔티티 트랜스폼/필터 갱신 (90% 이상) |
| **`IJobChunk`** | 높음 (Chunk 배열 수동 추출) | 청크 메모리 직접 포인터 제어 | 최우수 (SIMD 최적화 극대화) | 고도로 튜닝된 물리/충돌/벡터화 수학 연산 |
| **`IJobParallelFor`** | 낮음 (배열 인덱스 루프) | `NativeArray<T>` 분할 인덱싱 | 최우수 | C++ 수신 원시 버퍼 파싱 및 이미지 전처리 |
| **`IJob`** | 매우 낮음 | 단일 스레드 비동기 | 우수 | 대용량 상태 요약, 리셋, I/O 대기열 처리 |

### 3.2 JobHandle 의존성 체인과 메인 스레드 Stalling 방지

#### ❌ 메인 스레드 Sync Point 유발 패턴 (안티패턴)
```csharp
// 안티패턴: 잡을 스케줄하고 즉시 Complete() 호출
var handle = job.Schedule(state.Dependency);
handle.Complete(); // 메인 스레드가 워커 스레드가 끝날 때까지 멈춤 (Stall!)
```

####  의존성 체이닝 패턴 (Best Practice)
```csharp
// Best Practice: state.Dependency에 연결하여 프레임 파이프라인 형성
state.Dependency = job.ScheduleParallel(state.Dependency);
// Complete()를 절대 직접 호출하지 않고, 후속 시스템이나 엔진 렌더러가 필요 시점에 대기하도록 위임
```

### 3.3 Burst Compiler 극대화 가이드라인

1. **`Unity.Mathematics` 전면 사용**:
   - `System.Math`나 `UnityEngine.Mathf`는 참조 시 managed 브릿지 또는 추가 분기가 발생할 수 있음.
   - 모든 수학 연산은 `Unity.Mathematics.math` 함수(`math.sin`, `math.lerp`, `math.clamp` 등)와 `float3`, `quaternion`, `float4x4`를 사용.
2. **`[MethodImpl(MethodImplOptions.AggressiveInlining)]` 명시**:
   - 필터링 수식이나 매핑 변환 함수 등 빈번히 호출되는 헬퍼 메서드에 인라인 어트리뷰트를 강제하여 함수 호출 오버헤드를 0으로 만듦.
3. **포인터 앨리어싱(Aliasing) 회피**:
   - Burst 컴파일러는 두 메모리 영역이 겹칠 가능성이 없다고 확신할 때만 강력한 루프 벡터화(AVX-512, Neon)를 적용함.
   - 읽기 전용 컴포넌트는 반드시 `in` 키워드나 `RefRO<T>`로 선언하여 Write-Conflict를 방지.

---

## 4. DOTS 성능 킬러 방지 (Anti-Patterns & Pitfalls)

### 4.1 Structural Changes (구조적 변경)의 치명적 비용
Structural Change란 청크의 메모리 레이아웃이 변경되는 모든 행위를 의미한다:
- `CreateEntity()`, `DestroyEntity()`
- `AddComponent()`, `RemoveComponent()`
- `SetSharedComponent()`

```
[ Structural Change 발생 시 일어나는 현상 ]
1. 모든 실행 중인 Job이 즉시 강제 Complete됨 (Main Thread Sync Point / Pipeline Flush).
2. 대상 엔티티가 새로운 Archetype 청크로 복사 이동됨 (Memory Move Overhead).
3. 기존 청크에 빈자리가 생겨 다른 엔티티를 채워넣는 청크 재정렬(Compaction) 발생.
```

####  해결책
1. **사전 생성 및 풀링 (Pre-spawning)**:
   - 씬 시작 시 최대 손/얼굴 개수에 해당하는 포인트 엔티티를 미리 생성.
2. **`IEnableableComponent` 활용**:
   - 엔티티를 삭제하거나 컴포넌트를 떼지 않고, 비활성화 플래그만 전환.
3. **`EntityCommandBuffer` (ECB) 지연 실행**:
   - 부득이한 구조 변경은 프레임 중간에 즉시 실행하지 않고, `BeginSimulationEntityCommandBufferSystem` 등의 버퍼에 기록하여 프레임 경계에서 일괄 1회 처리.

### 4.2 불필요한 Write-Lock 방지 (`RefRO` vs `RefRW`)
- `SystemAPI.Query<RefRW<LocalTransform>>()`로 쓰기 락을 획득하면, 동일 프레임에서 `LocalTransform`을 읽으려는 다른 병렬 잡이 동시에 실행되지 못하고 직렬화됨.
- 읽기만 하는 컴포넌트는 반드시 `RefRO<T>`로 선언하여 잡 간 읽기 병렬성을 극대화해야 함.

### 4.3 Chunk Fragmentation (청크 단편화)
- Archetype마다 최소 1개의 16KB 청크가 할당됨.
- 엔티티가 1개뿐인 희귀한 컴포넌트 조합이 수십 개 존재하면 메모리가 심각하게 낭비되고 캐시 효율이 급락함.
- **규칙**: 세분화된 태그 컴포넌트 남발을 지양하고, 공통된 엔티티는 동일한 컴포넌트 세트를 갖도록 정규화.

---

## 5. MediaPipeUnityDOTS 권장 타겟 아키텍처

### 5.1 전체 파이프라인 아키텍처 다이어그램

```mermaid
flowchart TD
    subgraph Native_Layer ["1. Native C++ Layer (mpud_bridge)"]
        Webcam[Webcam / Video Stream] --> NativeTracker[MediaPipe Landmarker C++]
        NativeTracker --> RawBuffer[Native Output Landmark Buffer]
    end

    subgraph Service_Layer ["2. C# Interop & Service Layer (Unmanaged Memory Bridge)"]
        RawBuffer -->|Poll / Callback| TrackingService[TrackingService]
        TrackingService -->|Single Copy / Zero Alloc| IngestionBuffer[Direct Memory Ingestion]
    end

    subgraph ECS_Core ["3. Pure ECS DOTS Core Layer (Burst / Jobs / Zero GC)"]
        IngestionBuffer -->|Write to Singleton| StatusSingleton[TrackingStatus Singleton & DynamicBuffer]
        
        StatusSingleton --> FilterJob["LandmarkFilterJob (IJobEntity, Parallel)
        - 1 Euro Filter SIMD
        - Native Coordinate -> Unity LocalTransform
        - IEnableableComponent Visibility Toggle"]
        
        PointEntities[("Point Entities (Pre-spawned)
        - HandLandmarkPoint
        - LocalTransform
        - LandmarkFilterState
        - LandmarkVisibleTag")] --> FilterJob
        
        FilterJob --> UpdatedPoints[Updated LocalTransforms]
        UpdatedPoints --> BatchRenderer[Entities Graphics (BatchRendererGroup)]
    end

    subgraph App_UI_Layer ["4. App & UI Layer (UI Toolkit / VContainer / R3)"]
        StatusSingleton -.->|Read-only Snapshot Copy| Adapter[TrackingAdapter (MonoBehaviour)]
        Adapter -->|DTO Push| ViewModel[UI ViewModel (R3 / UniTask)]
        ViewModel --> UIElements[UI Toolkit (.uxml / .uss)]
        UIElements -->|Settings Command| ConfigSingleton[OneEuroFilterSettings Singleton]
    end
```

### 5.2 계층별 상세 설계 명세

#### 계층 1: Native Ingestion (C++ -> ECS)
- **방식**: C++ 브릿지 포인터를 C# `NativeArray<LandmarkElement>`로 래핑 (`NativeArrayUnsafeUtility.ConvertExistingDataToNativeArray`).
- **동기화**: `InitializationSystemGroup`에서 싱글톤 `DynamicBuffer<LandmarkElement>`에 단 1회의 네이티브 메모리 복사 수행 (`buffer.CopyFrom(...)`).
- **GC 발생량**: 0 Bytes.

#### 계층 2: 병렬 필터링 및 변환 시스템 (`LandmarkTransformSystem`)
- **방식**: `IJobEntity`를 구현하고 `.ScheduleParallel(state.Dependency)`로 워커 스레드 분할.
- **책임**:
  1. 각 포인트 엔티티의 `HandIndex`/`FaceIndex` 및 `Index`를 기반으로 입력 버퍼 매핑.
  2. 트래킹 유효성 검사 및 `LandmarkVisibleTag` 켜기/끄기 (`IEnableableComponent`).
  3. `OneEuroFilter.Filter`를 통한 3축 지터 억제 (Burst SIMD, `Enabled` 내장).
  4. 배경 Quad 화면 비율에 맞춘 `LandmarkOverlayMapping` 변환 후 `LocalTransform` 갱신.

#### 계층 3: 렌더링 계층 (`Entities.Graphics`)
- **방식**: 기존 GameObject 렌더러 대신 `RenderMeshArray` 기반의 GPU 인스턴싱 드로우콜 유지.
- **최적화**: 가시성이 꺼진(`LandmarkVisibleTag` = false) 엔티티는 BatchRendererGroup 인스턴스 렌더링 목록에서 즉각 배제되어 GPU 드로우콜 비용 절감.

#### 계층 4: UI/App 연동 계층 (`AGENTS.md` 완전 준수)
- **ECS -> UI**: `TrackingAdapter`가 렌더 프레임당 단 1회 싱글톤 상태와 버퍼를 `TrackingDto`로 스냅샷 복사. UI 계층은 ECS 내부 엔티티에 직접 쿼리하거나 개입하지 않음.
- **UI -> ECS**: UI Toolkit 설정 패널(필터 계수, 토글 등)에서 발생한 변경값은 `SystemAPI.GetSingletonRW<OneEuroFilterSettings>()`로 단방향 푸시.

---

## 6. 코드 구현 가이드 (Before vs After)

### 6.1 [핵심 개선] 메인 스레드 쿼리 루프 -> `IJobEntity` 병렬 잡 전환

#### ❌ Before: 메인 스레드 순차 순회 (`FaceLandmarkRenderSystem.cs`)
```csharp
// 문제점: 메인 스레드에서 478개 이상의 엔티티를 직렬로 순회하여 코어 활용 불가
[BurstCompile]
public void OnUpdate(ref SystemState state)
{
    var status = SystemAPI.GetSingleton<FaceTrackingStatus>();
    var landmarks = SystemAPI.GetSingletonBuffer<FaceLandmarkElement>();
    var mapping = SystemAPI.GetSingleton<LandmarkOverlayMapping>();
    var filterSettings = SystemAPI.GetSingleton<OneEuroFilterSettings>();

    // 메인 스레드에서 병목 유발
    foreach ((var transform, var point, var filter)
        in SystemAPI.Query<RefRW<LocalTransform>, RefRO<FaceLandmarkPoint>, RefRW<LandmarkFilterState>>())
    {
        // 1 Euro Filter 및 Transform 매핑 계산 (직렬 처리)
    }
}
```

####  After: `IJobEntity` 기반 워커 스레드 멀티코어 병렬화
```csharp
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace MediaPipeUnityDots.Runtime.Ecs
{
    [BurstCompile]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial struct OptimizedFaceLandmarkRenderSystem : ISystem
    {
        private const float PointScale = 0.02f;
        private const int MaxLandmarksPerFace = 478;

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<FaceTrackingStatus>();
            state.RequireForUpdate<FaceLandmarkPoint>();
            state.RequireForUpdate<LandmarkOverlayMapping>();
            state.RequireForUpdate<OneEuroFilterSettings>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var status = SystemAPI.GetSingleton<FaceTrackingStatus>();
            var landmarks = SystemAPI.GetSingletonBuffer<FaceLandmarkElement>();
            var mapping = SystemAPI.GetSingleton<LandmarkOverlayMapping>();
            var filterSettings = SystemAPI.GetSingleton<OneEuroFilterSettings>();

            var updateJob = new FaceLandmarkUpdateJob
            {
                Status = status,
                Landmarks = landmarks.AsNativeArray(), // 읽기 전용 NativeArray로 전달
                Mapping = mapping,
                FilterSettings = filterSettings,
                PointScale = PointScale,
                InputTimestampUs = status.TimestampUs,
                MinCutoff = new float3(filterSettings.FaceMinCutoff, filterSettings.FaceMinCutoff, filterSettings.ZMinCutoff),
                Beta = new float3(filterSettings.FaceBeta, filterSettings.FaceBeta, filterSettings.ZBeta)
            };

            // 워커 스레드로 병렬 디스패치 및 의존성 연결 (메인 스레드 Stall 0)
            state.Dependency = updateJob.ScheduleParallel(state.Dependency);
        }

        [BurstCompile]
        public partial struct FaceLandmarkUpdateJob : IJobEntity
        {
            [ReadOnly] public FaceTrackingStatus Status;
            [ReadOnly] public NativeArray<FaceLandmarkElement> Landmarks;
            [ReadOnly] public LandmarkOverlayMapping Mapping;
            [ReadOnly] public OneEuroFilterSettings FilterSettings;
            public float PointScale;
            public long InputTimestampUs;
            public float3 MinCutoff;
            public float3 Beta;

            public void Execute(
                ref LocalTransform transform,
                ref LandmarkFilterState filter,
                EnabledRefRW<LandmarkVisibleTag> isVisible, // IEnableableComponent 가시성 제어
                in FaceLandmarkPoint point)
            {
                var face = point.FaceIndex;
                var index = point.Index;
                var bufferIndex = face * MaxLandmarksPerFace + index;

                if (Mapping.IsValid != 0 && Status.IsValid && face >= 0 && face < Status.FaceCount
                    && index >= 0 && index < MaxLandmarksPerFace
                    && bufferIndex >= 0 && bufferIndex < Landmarks.Length
                    && Landmarks[bufferIndex].FaceIndex == face)
                {
                    var element = Landmarks[bufferIndex];
                    float3 targetPos;

                    // Enabled는 Filter 내부 계약. 외부 분기 금지.
                    var filtered = OneEuroFilter.Filter(
                        new float3(element.X, element.Y, element.Z),
                        ref filter,
                        FilterSettings.Enabled,
                        MinCutoff,
                        Beta,
                        FilterSettings.DerivativeCutoffHz,
                        InputTimestampUs);
                    targetPos = LandmarkOverlayMapping.Map(filtered.x, filtered.y, in mapping);

                    transform = LocalTransform.FromPositionRotationScale(targetPos, quaternion.identity, PointScale);
                    isVisible.ValueRW = true; // 렌더 활성화
                }
                else
                {
                    filter.Initialized = 0;
                    isVisible.ValueRW = false; // 청크 이동 없이 렌더 제외 (Zero Cost)
                }
            }
        }
    }
}
```

### 6.2 [가시성 제어] `Scale = 0f` -> `IEnableableComponent` 도입

#### 가시성 컴포넌트 정의
```csharp
using Unity.Entities;

namespace MediaPipeUnityDots.Runtime.Ecs
{
    /// <summary>
    /// 청크 재배치(Structural Change) 없이 렌더/연산 포함 여부를 O(1) 비트로 토글하는 컴포넌트
    /// </summary>
    public struct LandmarkVisibleTag : IComponentData, IEnableableComponent
    {
    }
}
```

#### 스포너 초기화 시 추가
```csharp
// 포인트 엔티티 생성 시 IEnableableComponent 부착 (초기값 false)
entityManager.AddComponentData(entity, new LandmarkVisibleTag());
entityManager.SetComponentEnabled<LandmarkVisibleTag>(entity, false);
```

> **적용 보류 (실측)**: 설치된 Entities Graphics의 `DisableRendering`은 enableable이
> 아니고, 커스텀 태그는 BRG 쿼리에 포함되지 않아 실제 컬링이 안 된다.
> `Scale = 0f` 유지를 기본값으로 두고, 전이 구조적 변경 제거 이후 p99 재측정에서
> 초과가 확인될 때만 재검토한다.

---

## 7. 성능 프로파일링 및 벤치마크 검증 체계

새로운 구조 도입 시 반드시 통과해야 하는 객관적 검증 기준을 정의한다.

### 7.1 프로파일링 도구별 점검 항목

| 프로파일러 도구 | 핵심 확인 지표 | 합격 판정 기준 |
| :--- | :--- | :--- |
| **Unity Profiler (CPU Usage)** | 메인 스레드 점유율 | `SimulationSystemGroup` 소요 시간 $< 0.4\text{ ms}$ |
| **Unity Profiler (GC Alloc)** | 프레임당 가비지 컬렉션 할당 | **0 B (Zero Allocation)** |
| **Profiler Job System Marker** | 워커 스레드 분산 상태 | 잡들이 여러 워커 코어에 균등 분산되고 `WaitForJob` 스톨이 없을 것 |
| **Burst Inspector** | 어셈블리 SIMD 벡터화 | Green(컴파일 성공), 벡터 레지스터(`ymm` / `zmm` / `v`) 활용 확인 |
| **Profile Analyzer** | 300프레임 기준 Frame Time 편차 | 평균 편차 $< 0.5\text{ ms}$ (지터 프리) |

---

## 8. 단계별 구현 및 마이그레이션 로드맵

```
Phase 1: Job System 병렬화
  ├── HandLandmarkRenderSystem -> IJobEntity.ScheduleParallel 전환
  ├── FaceLandmarkRenderSystem -> IJobEntity.ScheduleParallel 전환
  └── PoseLandmarkRenderSystem -> IJobEntity.ScheduleParallel 전환
  └── [검증]: 메인 스레드 렌더 시스템 소요 시간 70% 이상 단축 확인

Phase 2: Zero Structural Change 안착
  ├── LandmarkVisibleTag (IEnableableComponent) 정의 및 시스템 연동
  ├── Scale = 0f 렌더 숨김 로직을 Enableable Component 토글로 치환
  └── [검증]: 렌더링 Culling 및 불필요한 GPU 드로우콜 제거 확인

Phase 3: Native Ingestion Zero-Copy 파이프라인
  ├── Native C++ 포인터를 NativeArray로 무복사 바인딩
  ├── DynamicBuffer.CopyFrom 기반 일괄 메모리 스트리밍
  └── [검증]: GC Alloc 0 B 유지 및 Ingestion 레이턴시 최소화
```

---

## 9. 결론 및 권장 지침 요약

1. **`ISystem` + `[BurstCompile]` + `IJobEntity.ScheduleParallel` 삼위일체 원칙**:
   - 모든 연산은 워커 스레드로 위임하며, 메인 스레드는 `state.Dependency` 체인 연결만 수행한다.
2. **Sync Point 발생 코드 완전 배제**:
   - 런타임 루프 도중 `Complete()`, `CreateEntity()`, `AddComponent()`를 호출하지 않는다.
3. **가시성 토글은 `IEnableableComponent` 사용**:
   - 불필요한 연산과 드로우콜을 차단하면서 청크 재배치를 완전히 방지한다.
4. **엄격한 UI/App 경계 유지 (`AGENTS.md`)**:
   - ECS 코어는 순수 비관리형 데이터 파이프라인으로 유지하고, UI Toolkit/VContainer/R3 계층과는 `Adapter` 단방향 스냅샷으로만 소통한다.
