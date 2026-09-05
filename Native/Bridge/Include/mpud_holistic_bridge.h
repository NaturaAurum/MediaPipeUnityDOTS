#ifndef MPUD_HOLISTIC_BRIDGE_H
#define MPUD_HOLISTIC_BRIDGE_H

#include "mpud_bridge.h"

#ifdef __cplusplus
extern "C" {
#endif

// 결과 계약:
//   각 landmark_count>0이면 해당 부위 감지됨. 전부 0이면 미감지.
//   timestamp_us는 감지 여부와 무관하게 유효 (프레임 처리 완료 확인용).
#define MPUD_HOLISTIC_FACE_LANDMARKS 478
#define MPUD_HOLISTIC_POSE_LANDMARKS 33
#define MPUD_HOLISTIC_HAND_LANDMARKS 21

typedef struct MpudHolisticResult {
    int face_landmark_count;
    int pose_landmark_count;
    int left_hand_landmark_count;
    int right_hand_landmark_count;
    long long timestamp_us;
    MpudNormalizedLandmark face_landmarks[MPUD_HOLISTIC_FACE_LANDMARKS];
    MpudNormalizedLandmark pose_landmarks[MPUD_HOLISTIC_POSE_LANDMARKS];
    MpudNormalizedLandmark left_hand_landmarks[MPUD_HOLISTIC_HAND_LANDMARKS];
    MpudNormalizedLandmark right_hand_landmarks[MPUD_HOLISTIC_HAND_LANDMARKS];
} MpudHolisticResult;

// ABI 고정: C# MpudHolisticResult와 바이트 일치해야 함. 깨지면 양쪽 static assert가 잡는다.
_Static_assert(sizeof(MpudHolisticResult) == 11088, "MpudHolisticResult layout changed");

// --- Config ---
typedef struct MpudHolisticTrackerConfig {
    const char* model_asset_path;   // .task 파일의 절대 경로 (null/empty 시 MPUD_ERROR)
    float min_detection_confidence; // face/hand/pose detection에 공통 적용
    float min_presence_confidence;  // face/hand/pose presence에 공통 적용
} MpudHolisticTrackerConfig;

// --- Lifecycle ---
// 주의: create는 모델 로딩을 포함하며 수십~수백 ms 블로킹.
//       Unity 메인 스레드 외부에서 호출하는 것을 권장.
typedef struct MpudHolisticTracker MpudHolisticTracker;

MPUD_EXPORT int mpud_create_holistic_tracker(const MpudHolisticTrackerConfig* config, MpudHolisticTracker** out_tracker);
MPUD_EXPORT void mpud_destroy_holistic_tracker(MpudHolisticTracker* tracker);

// --- Frame processing ---
// timestamp_us는 이전 호출보다 반드시 커야 함 (엄격한 단조 증가)
MPUD_EXPORT int mpud_submit_holistic_frame(MpudHolisticTracker* tracker, const MpudImageFrame* frame);
MPUD_EXPORT int mpud_try_get_latest_holistic_result(MpudHolisticTracker* tracker, MpudHolisticResult* out_result);

// --- Error ---
// thread_local 저장소 사용(다른 bridge와 별개). 반드시 에러 발생과 같은 스레드에서 호출할 것.
MPUD_EXPORT const char* mpud_get_last_holistic_error(void);

#ifdef __cplusplus
}
#endif

#endif // MPUD_HOLISTIC_BRIDGE_H
