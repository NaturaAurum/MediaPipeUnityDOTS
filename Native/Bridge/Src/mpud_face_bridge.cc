#include "mpud_face_bridge.h"

#include <cstring>
#include <memory>
#include <string>

#include "mediapipe/tasks/cc/vision/face_landmarker/face_landmarker.h"
#include "mediapipe/framework/formats/image_frame.h"

namespace mp_fl = mediapipe::tasks::vision::face_landmarker;
namespace mp_vision = mediapipe::tasks::vision;

// 에러 버퍼: thread_local이므로 create/detect/destroy를 같은 스레드에서 호출해야 함.
static thread_local char g_face_last_error[512] = "no error";

static void set_face_error(const char* msg) {
    strncpy(g_face_last_error, msg, sizeof(g_face_last_error) - 1);
    g_face_last_error[sizeof(g_face_last_error) - 1] = '\0';
}

static void set_face_error_from_status(const absl::Status& status) {
    set_face_error(std::string(status.message()).c_str());
}

struct MpudFaceTracker {
    std::unique_ptr<mp_fl::FaceLandmarker> landmarker;
    MpudFaceResult last_result;
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

MPUD_EXPORT int mpud_create_face_tracker(
    const MpudFaceTrackerConfig* config,
    MpudFaceTracker** out_tracker)
{
    if (!config || !out_tracker) {
        set_face_error("null argument");
        return MPUD_ERROR;
    }

    if (!config->model_asset_path || config->model_asset_path[0] == '\0') {
        set_face_error("model_asset_path is null or empty");
        return MPUD_ERROR;
    }

    int num_faces = config->num_faces < 1 ? 1 : config->num_faces;
    if (num_faces > MPUD_MAX_FACES) num_faces = MPUD_MAX_FACES;

    auto options = std::make_unique<mp_fl::FaceLandmarkerOptions>();
    options->base_options.model_asset_path = config->model_asset_path;
    options->num_faces = num_faces;
    options->output_face_blendshapes = true;
    options->min_face_detection_confidence = config->min_detection_confidence;
    options->min_face_presence_confidence = config->min_detection_confidence;
    options->min_tracking_confidence = config->min_tracking_confidence;
    options->running_mode = mp_vision::core::RunningMode::VIDEO;

    auto result = mp_fl::FaceLandmarker::Create(std::move(options));
    if (!result.ok()) {
        set_face_error_from_status(result.status());
        return MPUD_ERROR;
    }

    auto* tracker = new MpudFaceTracker();
    tracker->landmarker = std::move(result.value());
    tracker->has_result = false;
    tracker->last_timestamp_us = -1;
    tracker->last_timestamp_ms = -1;
    memset(&tracker->last_result, 0, sizeof(tracker->last_result));

    *out_tracker = tracker;
    return MPUD_OK;
}

MPUD_EXPORT void mpud_destroy_face_tracker(MpudFaceTracker* tracker) {
    if (!tracker) return;
    if (tracker->landmarker) {
        auto status = tracker->landmarker->Close();
        if (!status.ok()) {
            set_face_error_from_status(status);
        }
    }
    delete tracker;
}

MPUD_EXPORT int mpud_submit_face_frame(
    MpudFaceTracker* tracker,
    const MpudImageFrame* frame)
{
    if (!tracker || !frame || !frame->data) {
        set_face_error("null argument");
        return MPUD_ERROR;
    }

    if (frame->width <= 0 || frame->height <= 0 || frame->stride_bytes <= 0) {
        set_face_error("invalid image dimensions: width, height, stride_bytes must be > 0");
        return MPUD_ERROR;
    }

    if (frame->timestamp_us <= tracker->last_timestamp_us) {
        set_face_error("timestamp_us must be strictly increasing");
        return MPUD_ERROR;
    }

    auto mp_format = to_mp_format(frame->pixel_format);
    if (mp_format == mediapipe::ImageFormat::UNKNOWN) {
        set_face_error("unsupported pixel format");
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
        set_face_error_from_status(result.status());
        return MPUD_ERROR;
    }

    tracker->last_timestamp_us = frame->timestamp_us;
    tracker->last_timestamp_ms = timestamp_ms;

    const auto& face_result = result.value();
    MpudFaceResult* out = &tracker->last_result;
    memset(out, 0, sizeof(*out));

    // 얼굴 감지 여부와 무관하게 timestamp_us를 기록 (프레임 기반 추적용)
    out->timestamp_us = frame->timestamp_us;

    int face_total = (int)face_result.face_landmarks.size();
    if (face_total > MPUD_MAX_FACES) face_total = MPUD_MAX_FACES;
    out->face_count = face_total;
    for (int f = 0; f < face_total; ++f) {
        const auto& lm_list = face_result.face_landmarks[f];
        MpudFace* face = &out->faces[f];
        face->landmark_count = (int)lm_list.landmarks.size();
        if (face->landmark_count > MPUD_FACE_LANDMARKS) {
            face->landmark_count = MPUD_FACE_LANDMARKS;
        }
        for (int i = 0; i < face->landmark_count; ++i) {
            const auto& lm = lm_list.landmarks[i];
            face->landmarks[i].x = lm.x;
            face->landmarks[i].y = lm.y;
            face->landmarks[i].z = lm.z;
            face->landmarks[i].visibility = lm.visibility.value_or(0.0f);
            face->landmarks[i].presence = lm.presence.value_or(0.0f);
        }

        // blendshapes는 optional이다. 요청해도 얼굴별로 비어 있을 수 있다.
        face->blendshape_count = 0;
        if (face_result.face_blendshapes.has_value() &&
            f < (int)face_result.face_blendshapes->size()) {
            const auto& categories = (*face_result.face_blendshapes)[f].categories;
            int blendshape_count = (int)categories.size();
            if (blendshape_count > MPUD_FACE_BLENDSHAPES) blendshape_count = MPUD_FACE_BLENDSHAPES;
            face->blendshape_count = blendshape_count;
            for (int i = 0; i < blendshape_count; ++i) {
                face->blendshapes[i] = categories[i].score;
            }
        }
    }
    // face_count=0이면 얼굴 미감지. timestamp_us는 유효 → C#에서 "최신 프레임이지만 얼굴 없음" 판별 가능.

    tracker->has_result = true;
    return MPUD_OK;
}

MPUD_EXPORT int mpud_try_get_latest_face_result(
    MpudFaceTracker* tracker,
    MpudFaceResult* out_result)
{
    if (!tracker || !out_result) {
        set_face_error("null argument");
        return MPUD_ERROR;
    }
    if (!tracker->has_result) {
        return MPUD_NO_RESULT;
    }
    memcpy(out_result, &tracker->last_result, sizeof(MpudFaceResult));
    return MPUD_OK;
}

MPUD_EXPORT const char* mpud_get_last_face_error(void) {
    return g_face_last_error;
}

} // extern "C"
