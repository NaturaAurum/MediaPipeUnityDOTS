#ifndef MPUD_FACE_BRIDGE_H
#define MPUD_FACE_BRIDGE_H

#include "mpud_bridge.h"

#ifdef __cplusplus
extern "C" {
#endif

// 결과 계약:
//   face_count>0: 얼굴 감지됨. faces[0..face_count) 유효.
//   face_count=0: 얼굴 미감지. timestamp_us만 유효 (프레임 처리 완료 확인용).
#define MPUD_MAX_FACES 2
#define MPUD_FACE_LANDMARKS 478
#define MPUD_FACE_BLENDSHAPES 52

typedef struct MpudFace {
    int landmark_count;
    MpudNormalizedLandmark landmarks[MPUD_FACE_LANDMARKS];
    int blendshape_count;
    float blendshapes[MPUD_FACE_BLENDSHAPES];
} MpudFace;

typedef struct MpudFaceResult {
    int face_count;
    long long timestamp_us;
    MpudFace faces[MPUD_MAX_FACES];
} MpudFaceResult;

// ABI 고정: C# MpudFaceResult와 바이트 일치해야 함. 깨지면 양쪽 static assert가 잡는다.
_Static_assert(sizeof(MpudFace) == 9776, "MpudFace layout changed");
_Static_assert(sizeof(MpudFaceResult) == 19568, "MpudFaceResult layout changed");

// --- Config ---
typedef struct MpudFaceTrackerConfig {
    const char* model_asset_path;   // .task 파일의 절대 경로 (null/empty 시 MPUD_ERROR)
    int num_faces;                  // 1..MPUD_MAX_FACES, 범위 밖은 클램프됨
    float min_detection_confidence;
    float min_tracking_confidence;
} MpudFaceTrackerConfig;

// --- Lifecycle ---
// 주의: create는 모델 로딩을 포함하며 수십~수백 ms 블로킹.
//       Unity 메인 스레드 외부에서 호출하는 것을 권장.
typedef struct MpudFaceTracker MpudFaceTracker;

MPUD_EXPORT int mpud_create_face_tracker(const MpudFaceTrackerConfig* config, MpudFaceTracker** out_tracker);
MPUD_EXPORT void mpud_destroy_face_tracker(MpudFaceTracker* tracker);

// --- Frame processing ---
// timestamp_us는 이전 호출보다 반드시 커야 함 (엄격한 단조 증가)
MPUD_EXPORT int mpud_submit_face_frame(MpudFaceTracker* tracker, const MpudImageFrame* frame);
MPUD_EXPORT int mpud_try_get_latest_face_result(MpudFaceTracker* tracker, MpudFaceResult* out_result);

// --- Error ---
// thread_local 저장소 사용(hand bridge와 별개). 반드시 에러 발생과 같은 스레드에서 호출할 것.
MPUD_EXPORT const char* mpud_get_last_face_error(void);

#ifdef __cplusplus
}
#endif

#endif // MPUD_FACE_BRIDGE_H
