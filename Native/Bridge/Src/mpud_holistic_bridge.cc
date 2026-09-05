#include "mpud_holistic_bridge.h"

#include <cstring>
#include <memory>
#include <string>

#include "mediapipe/tasks/cc/vision/holistic_landmarker/holistic_landmarker.h"
#include "mediapipe/framework/formats/image_frame.h"

namespace mp_ho = mediapipe::tasks::vision::holistic_landmarker;
namespace mp_vision = mediapipe::tasks::vision;

// 에러 버퍼: thread_local이므로 create/detect/destroy를 같은 스레드에서 호출해야 함.
static thread_local char g_holistic_last_error[512] = "no error";

static void set_holistic_error(const char* msg) {
    strncpy(g_holistic_last_error, msg, sizeof(g_holistic_last_error) - 1);
    g_holistic_last_error[sizeof(g_holistic_last_error) - 1] = '\0';
}

static void set_holistic_error_from_status(const absl::Status& status) {
    set_holistic_error(std::string(status.message()).c_str());
}

struct MpudHolisticTracker {
    std::unique_ptr<mp_ho::HolisticLandmarker> landmarker;
    MpudHolisticResult last_result;
    bool has_result;
    long long last_timestamp_us;
    int64_t last_timestamp_ms; // MediaPipe에 전달한 마지막 ms 값 추적 (단조 증가 보장용)
};

static mediapipe::ImageFormat::Format to_mp_format(int pixel_format) {
    switch (pixel_format) {
        case MPUD_PIXEL_FORMAT_SRGB:  return mediapipe::ImageFormat::SRGB;
        case MPUD_PIXEL_FORMAT_SRGBA: return mediapipe::ImageFormat::SRGBA;
        default: return mediapipe::ImageFormat::UNKNOWN;
    }
}

extern "C" {
MPUD_EXPORT int mpud_create_holistic_tracker(
    const MpudHolisticTrackerConfig* config,
    MpudHolisticTracker** out_tracker)
{
    if (!config || !out_tracker) {
        set_holistic_error("null argument");
        return MPUD_ERROR;
    }

    if (!config->model_asset_path || config->model_asset_path[0] == '\0') {
        set_holistic_error("model_asset_path is null or empty");
        return MPUD_ERROR;
    }

    auto options = std::make_unique<mp_ho::HolisticLandmarkerOptions>();
    // 주의: XNNPACK SME/SME2 양자화 커널이 Apple Silicon에서 SIGILL을 유발하므로
    // Native/Build/.bazelrc에서 SME/SME2를 비활성화하고 빌드한다.
    options->base_options.model_asset_path = config->model_asset_path;
    options->min_pose_detection_confidence = config->min_detection_confidence;
    options->min_pose_presence_confidence = config->min_presence_confidence;
    options->running_mode = mp_vision::core::RunningMode::VIDEO;

    auto result = mp_ho::HolisticLandmarker::Create(std::move(options));
    if (!result.ok()) {
        set_holistic_error_from_status(result.status());
        return MPUD_ERROR;
    }

    auto* tracker = new MpudHolisticTracker();
    tracker->landmarker = std::move(result.value());
    tracker->has_result = false;
    tracker->last_timestamp_us = -1;
    tracker->last_timestamp_ms = -1;
    memset(&tracker->last_result, 0, sizeof(tracker->last_result));

    *out_tracker = tracker;
    return MPUD_OK;
}

MPUD_EXPORT void mpud_destroy_holistic_tracker(MpudHolisticTracker* tracker) {
    if (!tracker) return;
    if (tracker->landmarker) {
        auto status = tracker->landmarker->Close();
        if (!status.ok()) {
            set_holistic_error_from_status(status);
        }
    }
    delete tracker;
}

static int copy_normalized_list(
    const mediapipe::tasks::components::containers::NormalizedLandmarks& src,
    MpudNormalizedLandmark* dst,
    int capacity)
{
    int count = (int)src.landmarks.size();
    if (count > capacity) count = capacity;
    for (int i = 0; i < count; ++i) {
        const auto& lm = src.landmarks[i];
        dst[i].x = lm.x;
        dst[i].y = lm.y;
        dst[i].z = lm.z;
        dst[i].visibility = lm.visibility.value_or(0.0f);
        dst[i].presence = lm.presence.value_or(0.0f);
    }
    return count;
}

// 월드 좌표(미터) 복사. 정규화와 같은 인덱스를 유지하도록 정규화 카운트로 클램프.
static int copy_world_list(
    const mediapipe::tasks::components::containers::Landmarks& src,
    MpudNormalizedLandmark* dst,
    int capacity,
    int count_cap)
{
    int count = (int)src.landmarks.size();
    if (count > capacity) count = capacity;
    if (count > count_cap) count = count_cap;
    for (int i = 0; i < count; ++i) {
        const auto& lm = src.landmarks[i];
        dst[i].x = lm.x;
        dst[i].y = lm.y;
        dst[i].z = lm.z;
        dst[i].visibility = lm.visibility.value_or(0.0f);
        dst[i].presence = 0.0f;
    }
    return count;
}

MPUD_EXPORT int mpud_submit_holistic_frame(
    MpudHolisticTracker* tracker,
    const MpudImageFrame* frame)
{
    if (!tracker || !frame || !frame->data) {
        set_holistic_error("null argument");
        return MPUD_ERROR;
    }

    if (frame->width <= 0 || frame->height <= 0 || frame->stride_bytes <= 0) {
        set_holistic_error("invalid image dimensions: width, height, stride_bytes must be > 0");
        return MPUD_ERROR;
    }

    if (frame->timestamp_us <= tracker->last_timestamp_us) {
        set_holistic_error("timestamp_us must be strictly increasing");
        return MPUD_ERROR;
    }

    auto mp_format = to_mp_format(frame->pixel_format);
    if (mp_format == mediapipe::ImageFormat::UNKNOWN) {
        set_holistic_error("unsupported pixel format");
        return MPUD_ERROR;
    }

    auto image_frame = std::make_shared<mediapipe::ImageFrame>(
        mp_format, frame->width, frame->height, frame->stride_bytes,
        const_cast<unsigned char*>(frame->data),
        // no-op deleter: 호출자가 버퍼 소유. DetectForVideo 반환 전까지 유효해야 함.
        [](uint8_t*) {}
    );
    mediapipe::Image mp_image(image_frame);

    // us→ms 변환 + 단조 증가 보장:
    // 연속 프레임이 1ms 미만 간격이면 같은 ms 값이 되어 MediaPipe가 거부함.
    int64_t timestamp_ms = frame->timestamp_us / 1000;
    if (timestamp_ms <= tracker->last_timestamp_ms) {
        timestamp_ms = tracker->last_timestamp_ms + 1;
    }

    auto result = tracker->landmarker->DetectForVideo(mp_image, timestamp_ms);
    if (!result.ok()) {
        set_holistic_error_from_status(result.status());
        return MPUD_ERROR;
    }

    tracker->last_timestamp_us = frame->timestamp_us;
    tracker->last_timestamp_ms = timestamp_ms;

    const auto& holistic = result.value();
    MpudHolisticResult* out = &tracker->last_result;
    memset(out, 0, sizeof(*out));

    // 감지 여부와 무관하게 timestamp_us를 기록 (프레임 기반 추적용)
    out->timestamp_us = frame->timestamp_us;

    out->face_landmark_count = copy_normalized_list(
        holistic.face_landmarks, out->face_landmarks, MPUD_HOLISTIC_FACE_LANDMARKS);
    out->pose_landmark_count = copy_normalized_list(
        holistic.pose_landmarks, out->pose_landmarks, MPUD_HOLISTIC_POSE_LANDMARKS);
    out->left_hand_landmark_count = copy_normalized_list(
        holistic.left_hand_landmarks, out->left_hand_landmarks, MPUD_HOLISTIC_HAND_LANDMARKS);
    out->right_hand_landmark_count = copy_normalized_list(
        holistic.right_hand_landmarks, out->right_hand_landmarks, MPUD_HOLISTIC_HAND_LANDMARKS);
    copy_world_list(holistic.pose_world_landmarks, out->pose_world_landmarks,
        MPUD_HOLISTIC_POSE_LANDMARKS, out->pose_landmark_count);
    copy_world_list(holistic.left_hand_world_landmarks, out->left_hand_world_landmarks,
        MPUD_HOLISTIC_HAND_LANDMARKS, out->left_hand_landmark_count);
    copy_world_list(holistic.right_hand_world_landmarks, out->right_hand_world_landmarks,
        MPUD_HOLISTIC_HAND_LANDMARKS, out->right_hand_landmark_count);

    tracker->has_result = true;
    return MPUD_OK;
}

MPUD_EXPORT int mpud_try_get_latest_holistic_result(
    MpudHolisticTracker* tracker,
    MpudHolisticResult* out_result)
{
    if (!tracker || !out_result) {
        set_holistic_error("null argument");
        return MPUD_ERROR;
    }
    if (!tracker->has_result) {
        return MPUD_NO_RESULT;
    }
    memcpy(out_result, &tracker->last_result, sizeof(MpudHolisticResult));
    return MPUD_OK;
}

MPUD_EXPORT const char* mpud_get_last_holistic_error(void) {
    return g_holistic_last_error;
}

} // extern "C"
