#ifndef MPUD_POSE_BRIDGE_H
#define MPUD_POSE_BRIDGE_H

#include "mpud_bridge.h"

#ifdef __cplusplus
extern "C" {
#endif

// 결과 계약:
//   pose_count>0: 사람 감지됨. poses[0..pose_count) 유효.
//   pose_count=0: 사람 미감지. timestamp_us만 유효 (프레임 처리 완료 확인용).
#define MPUD_MAX_POSES 2
#define MPUD_POSE_LANDMARKS 33

typedef struct MpudPose {
    int landmark_count;
    MpudNormalizedLandmark landmarks[MPUD_POSE_LANDMARKS];
    // 월드 좌표(미터). landmarks와 같은 인덱스, 같은 landmark_count 공유.
    MpudNormalizedLandmark world_landmarks[MPUD_POSE_LANDMARKS];
} MpudPose;

typedef struct MpudPoseResult {
    int pose_count;
    long long timestamp_us;
    MpudPose poses[MPUD_MAX_POSES];
} MpudPoseResult;

// ABI 고정: C# MpudPoseResult와 바이트 일치해야 함. 깨지면 양쪽 static assert가 잡는다.
_Static_assert(sizeof(MpudPose) == 1324, "MpudPose layout changed");
_Static_assert(sizeof(MpudPoseResult) == 2664, "MpudPoseResult layout changed");

// --- Config ---
typedef struct MpudPoseTrackerConfig {
    const char* model_asset_path;   // .task 파일의 절대 경로 (null/empty 시 MPUD_ERROR)
    int num_poses;                  // 1..MPUD_MAX_POSES, 범위 밖은 클램프됨
    float min_detection_confidence;
    float min_tracking_confidence;
} MpudPoseTrackerConfig;

// --- Lifecycle ---
// 주의: create는 모델 로딩을 포함하며 수십~수백 ms 블로킹.
//       Unity 메인 스레드 외부에서 호출하는 것을 권장.
typedef struct MpudPoseTracker MpudPoseTracker;

MPUD_EXPORT int mpud_create_pose_tracker(const MpudPoseTrackerConfig* config, MpudPoseTracker** out_tracker);
MPUD_EXPORT void mpud_destroy_pose_tracker(MpudPoseTracker* tracker);

// --- Frame processing ---
// timestamp_us는 이전 호출보다 반드시 커야 함 (엄격한 단조 증가)
MPUD_EXPORT int mpud_submit_pose_frame(MpudPoseTracker* tracker, const MpudImageFrame* frame);
MPUD_EXPORT int mpud_try_get_latest_pose_result(MpudPoseTracker* tracker, MpudPoseResult* out_result);

// --- Error ---
// thread_local 저장소 사용(hand/face bridge와 별개). 반드시 에러 발생과 같은 스레드에서 호출할 것.
MPUD_EXPORT const char* mpud_get_last_pose_error(void);

#ifdef __cplusplus
}
#endif

#endif // MPUD_POSE_BRIDGE_H
