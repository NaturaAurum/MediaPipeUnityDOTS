#include "mpud_bridge.h"

#include <cstring>
#include <memory>
#include <string>

#include "mediapipe/tasks/cc/vision/hand_landmarker/hand_landmarker.h"
#include "mediapipe/framework/formats/image_frame.h"

namespace mp_hl = mediapipe::tasks::vision::hand_landmarker;
namespace mp_vision = mediapipe::tasks::vision;

// 에러 버퍼: thread_local이므로 create/detect/destroy를 같은 스레드에서 호출해야 함.
// 크로스 스레드 사용 시 에러 메시지 유실 가능 — 헤더 및 C# 래퍼에서 경고 문서화됨.
static thread_local char g_last_error[512] = "no error";

static void set_error(const char* msg) {
    strncpy(g_last_error, msg, sizeof(g_last_error) - 1);
    g_last_error[sizeof(g_last_error) - 1] = '\0';
}

static void set_error_from_status(const absl::Status& status) {
    set_error(std::string(status.message()).c_str());
}

struct MpudHandTracker {
    std::unique_ptr<mp_hl::HandLandmarker> landmarker;
    MpudHandResult last_result;
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

MPUD_EXPORT int mpud_create_hand_tracker(
    const MpudHandTrackerConfig* config,
    MpudHandTracker** out_tracker)
{
    if (!config || !out_tracker) {
        set_error("null argument");
        return MPUD_ERROR;
    }

    if (!config->model_asset_path || config->model_asset_path[0] == '\0') {
        set_error("model_asset_path is null or empty");
        return MPUD_ERROR;
    }

    auto options = std::make_unique<mp_hl::HandLandmarkerOptions>();
    options->base_options.model_asset_path = config->model_asset_path;
    int num_hands = config->num_hands < 1 ? 1 : config->num_hands;
    if (num_hands > MPUD_MAX_HANDS) num_hands = MPUD_MAX_HANDS;
    options->num_hands = num_hands;
    options->min_hand_detection_confidence = config->min_detection_confidence;
    options->min_tracking_confidence = config->min_tracking_confidence;

    // PoC: VIDEO 모드 고정. config.running_mode는 무시됨 (향후 확장 예정).
    options->running_mode = mp_vision::core::RunningMode::VIDEO;

    auto result = mp_hl::HandLandmarker::Create(std::move(options));
    if (!result.ok()) {
        set_error_from_status(result.status());
        return MPUD_ERROR;
    }

    auto* tracker = new MpudHandTracker();
    tracker->landmarker = std::move(result.value());
    tracker->has_result = false;
    tracker->last_timestamp_us = -1;
    tracker->last_timestamp_ms = -1;
    memset(&tracker->last_result, 0, sizeof(tracker->last_result));

    *out_tracker = tracker;
    return MPUD_OK;
}

MPUD_EXPORT int mpud_start_hand_tracker(MpudHandTracker* tracker) {
    if (!tracker) { set_error("null tracker"); return MPUD_ERROR; }
    // VIDEO 모드에서는 별도 start 불필요. 향후 LIVE_STREAM 모드 지원 시 사용.
    return MPUD_OK;
}

MPUD_EXPORT void mpud_destroy_hand_tracker(MpudHandTracker* tracker) {
    if (!tracker) return;
    if (tracker->landmarker) {
        auto status = tracker->landmarker->Close();
        if (!status.ok()) {
            set_error_from_status(status);
        }
    }
    delete tracker;
}

MPUD_EXPORT int mpud_submit_frame(
    MpudHandTracker* tracker,
    const MpudImageFrame* frame)
{
    if (!tracker || !frame || !frame->data) {
        set_error("null argument");
        return MPUD_ERROR;
    }

    if (frame->width <= 0 || frame->height <= 0 || frame->stride_bytes <= 0) {
        set_error("invalid image dimensions: width, height, stride_bytes must be > 0");
        return MPUD_ERROR;
    }

    if (frame->timestamp_us <= tracker->last_timestamp_us) {
        set_error("timestamp_us must be strictly increasing");
        return MPUD_ERROR;
    }

    auto mp_format = to_mp_format(frame->pixel_format);
    if (mp_format == mediapipe::ImageFormat::UNKNOWN) {
        set_error("unsupported pixel format");
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
    // last_timestamp_ms보다 최소 1 큰 값을 보장.
    int64_t timestamp_ms = frame->timestamp_us / 1000;
    if (timestamp_ms <= tracker->last_timestamp_ms) {
        timestamp_ms = tracker->last_timestamp_ms + 1;
    }

    auto result = tracker->landmarker->DetectForVideo(mp_image, timestamp_ms);
    if (!result.ok()) {
        set_error_from_status(result.status());
        return MPUD_ERROR;
    }

    tracker->last_timestamp_us = frame->timestamp_us;
    tracker->last_timestamp_ms = timestamp_ms;

    const auto& hand_result = result.value();
    MpudHandResult* out = &tracker->last_result;
    memset(out, 0, sizeof(*out));

    // 손 감지 여부와 무관하게 timestamp_us를 기록 (프레임 기반 추적용)
    out->timestamp_us = frame->timestamp_us;

    int hand_total = (int)hand_result.hand_landmarks.size();
    if (hand_total > MPUD_MAX_HANDS) hand_total = MPUD_MAX_HANDS;
    out->hand_count = hand_total;
    for (int h = 0; h < hand_total; ++h) {
        const auto& lm_list = hand_result.hand_landmarks[h];
        MpudHand* hand = &out->hands[h];
        hand->landmark_count = (int)lm_list.landmarks.size();
        if (hand->landmark_count > MPUD_LANDMARKS_PER_HAND) {
            hand->landmark_count = MPUD_LANDMARKS_PER_HAND;
        }

        for (int i = 0; i < hand->landmark_count; ++i) {
            const auto& lm = lm_list.landmarks[i];
            hand->landmarks[i].x = lm.x;
            hand->landmarks[i].y = lm.y;
            hand->landmarks[i].z = lm.z;
            hand->landmarks[i].visibility = lm.visibility.value_or(0.0f);
            hand->landmarks[i].presence = lm.presence.value_or(0.0f);
        }

        hand->handedness = 1;
        hand->score = 0.0f;
        if (h < (int)hand_result.handedness.size() &&
            !hand_result.handedness[h].categories.empty()) {
            const auto& cat = hand_result.handedness[h].categories[0];
            hand->handedness = (cat.category_name == "Left") ? 0 : 1;
            hand->score = cat.score;
        }
    }
    // hand_count=0이면 손 미감지. timestamp_us는 유효 → C#에서 "최신 프레임이지만 손 없음" 판별 가능.

    tracker->has_result = true;
    return MPUD_OK;
}

MPUD_EXPORT int mpud_try_get_latest_result(
    MpudHandTracker* tracker,
    MpudHandResult* out_result)
{
    if (!tracker || !out_result) {
        set_error("null argument");
        return MPUD_ERROR;
    }
    if (!tracker->has_result) {
        return MPUD_NO_RESULT;
    }
    memcpy(out_result, &tracker->last_result, sizeof(MpudHandResult));
    return MPUD_OK;
}

MPUD_EXPORT const char* mpud_get_last_error(void) {
    return g_last_error;
}

} // extern "C"
