#ifndef MPUD_BRIDGE_H
#define MPUD_BRIDGE_H

#ifdef __cplusplus
extern "C" {
#endif

#ifdef _WIN32
  #define MPUD_EXPORT __declspec(dllexport)
#else
  #define MPUD_EXPORT __attribute__((visibility("default")))
#endif

// --- 상태 코드 ---
#define MPUD_OK          0
#define MPUD_ERROR      -1
#define MPUD_NO_RESULT  -2

// --- Opaque handle ---
typedef struct MpudHandTracker MpudHandTracker;

// --- 픽셀 포맷 ---
// MediaPipe ImageFormat과의 매핑:
//   MPUD_PIXEL_FORMAT_SRGB  → ImageFormat::SRGB  (24-bit, R8G8B8)
//   MPUD_PIXEL_FORMAT_SRGBA → ImageFormat::SRGBA (32-bit, R8G8B8A8)
// 주의: Unity의 BGRA32 포맷은 지원하지 않음 → 호출 측에서 RGB/RGBA로 변환 필요
#define MPUD_PIXEL_FORMAT_SRGB   0
#define MPUD_PIXEL_FORMAT_SRGBA  1

// --- POD structs ---
typedef struct MpudImageFrame {
    const unsigned char* data;
    int width;
    int height;
    int stride_bytes;
    int pixel_format;       // MPUD_PIXEL_FORMAT_SRGB 또는 MPUD_PIXEL_FORMAT_SRGBA
    long long timestamp_us; // 단조 증가 필수. 이전 프레임보다 작거나 같으면 MPUD_ERROR 반환
} MpudImageFrame;

typedef struct MpudNormalizedLandmark {
    float x;
    float y;
    float z;
    float visibility;
    float presence;
} MpudNormalizedLandmark;

// 결과 계약:
//   hand_count>0: 손 감지됨. hands[0..hand_count) 유효.
//   hand_count=0: 손 미감지. timestamp_us만 유효 (프레임 처리 완료 확인용).
//   hand_landmarks/handedness는 MediaPipe 반환 순서대로 같은 인덱스에 정렬됨.
#define MPUD_MAX_HANDS 4
#define MPUD_LANDMARKS_PER_HAND 21
typedef struct MpudHand {
    int landmark_count;
    int handedness;     // 0 = Left, 1 = Right
    float score;
    MpudNormalizedLandmark landmarks[MPUD_LANDMARKS_PER_HAND];
} MpudHand;
typedef struct MpudHandResult {
    int hand_count;
    long long timestamp_us;
    MpudHand hands[MPUD_MAX_HANDS];
} MpudHandResult;

// ABI 고정: C# MpudHandResult와 바이트 일치해야 함. 깨지면 양쪽 static assert가 잡는다.
_Static_assert(sizeof(MpudHand) == 432, "MpudHand layout changed");
_Static_assert(sizeof(MpudHandResult) == 1744, "MpudHandResult layout changed");

// --- Config ---
typedef struct MpudHandTrackerConfig {
    const char* model_asset_path;   // .task 파일의 절대 경로 (null/empty 시 MPUD_ERROR)
    int num_hands;                  // 1..MPUD_MAX_HANDS, 범위 밖은 클램프됨
    float min_detection_confidence;
    float min_tracking_confidence;
    int running_mode;               // PoC: 무시됨. 내부적으로 VIDEO(1) 고정. 향후 확장 예약.
} MpudHandTrackerConfig;

// --- Lifecycle ---
// 주의: mpud_create_hand_tracker는 모델 로딩을 포함하며 수십~수백 ms 블로킹.
//       Unity 메인 스레드 외부에서 호출하는 것을 권장.
MPUD_EXPORT int mpud_create_hand_tracker(const MpudHandTrackerConfig* config, MpudHandTracker** out_tracker);
MPUD_EXPORT int mpud_start_hand_tracker(MpudHandTracker* tracker);
// 주의: 내부적으로 Close() 후 리소스 해제. Detect 진행 중 호출 금지.
MPUD_EXPORT void mpud_destroy_hand_tracker(MpudHandTracker* tracker);

// --- Frame processing ---
// timestamp_us는 이전 호출보다 반드시 커야 함 (엄격한 단조 증가)
MPUD_EXPORT int mpud_submit_frame(MpudHandTracker* tracker, const MpudImageFrame* frame);
MPUD_EXPORT int mpud_try_get_latest_result(MpudHandTracker* tracker, MpudHandResult* out_result);

// --- Error ---
// thread_local 저장소 사용. 반드시 에러 발생과 같은 스레드에서 호출할 것.
MPUD_EXPORT const char* mpud_get_last_error(void);

#ifdef __cplusplus
}
#endif

#endif // MPUD_BRIDGE_H
