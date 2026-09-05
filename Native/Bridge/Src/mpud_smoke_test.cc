// 네이티브 브릿지 스모크 테스트: 합성 프레임으로 4종 트래커 create/submit/poll/destroy.
// Unity 없이 브릿지+모델+커널 문제를 가른다. 실패 시 nonzero 종료.
// 사용: bazel run //mediapipe/mpud_bridge:mpud_smoke_test -- <models dir>
#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <string>

#include "mpud_bridge.h"
#include "mpud_face_bridge.h"
#include "mpud_holistic_bridge.h"
#include "mpud_pose_bridge.h"

namespace {

int g_width = 256;
int g_height = 256;

// 결정적 합성 영상: 그라디언트 + 격자. 실제 얼굴/손은 없으므로 count 0이 정상.
void FillSynthetic(unsigned char* data, int frame_index) {
    for (int y = 0; y < g_height; ++y) {
        for (int x = 0; x < g_width; ++x) {
            int i = (y * g_width + x) * 4;
            data[i] = (unsigned char)((x + frame_index * 7) & 0xFF);
            data[i + 1] = (unsigned char)((y + frame_index * 13) & 0xFF);
            data[i + 2] = (unsigned char)(((x + y) / 2) & 0xFF);
            data[i + 3] = 255;
        }
    }
}

int failures = 0;

void Check(bool ok, const char* what) {
    printf("[%s] %s\n", ok ? "PASS" : "FAIL", what);
    if (!ok) ++failures;
}

} // namespace

int main(int argc, char** argv) {
    if (argc < 2) {
        printf("usage: mpud_smoke_test <models dir> [width height] [rawfile]\n");
        return 2;
    }
    std::string models = argv[1];
    if (argc >= 4) {
        g_width = atoi(argv[2]);
        g_height = atoi(argv[3]);
    }
    // argv[4]가 있으면 해당 raw RGBA 파일을 모든 프레임에 사용한다.
    bool have_raw = false;
    unsigned char* pixels = (unsigned char*)malloc(g_width * g_height * 4);
    if (argc >= 5) {
        FILE* raw = fopen(argv[4], "rb");
        if (raw) {
            have_raw = fread(pixels, 1, g_width * g_height * 4, raw) == (size_t)(g_width * g_height * 4);
            fclose(raw);
        }
    }
    if (!have_raw) {
        FillSynthetic(pixels, 0);
    }

    // --- Hand ---
    {
        std::string path = models + "/hand_landmarker.task";
        MpudHandTrackerConfig config = {path.c_str(), 1, 0.5f, 0.5f, 1};
        MpudHandTracker* tracker = nullptr;
        int rc = mpud_create_hand_tracker(&config, &tracker);
        Check(rc == MPUD_OK, "hand create");
        if (rc == MPUD_OK) {
            for (int f = 0; f < 3; ++f) {
                if (!have_raw) FillSynthetic(pixels, f);
                MpudImageFrame frame = {pixels, g_width, g_height, g_width * 4,
                                        MPUD_PIXEL_FORMAT_SRGBA, (long long)(f + 1) * 33333};
                rc = mpud_submit_frame(tracker, &frame);
                Check(rc == MPUD_OK, "hand submit");
                MpudHandResult result;
                memset(&result, 0xCD, sizeof(result));
                rc = mpud_try_get_latest_result(tracker, &result);
                Check(rc == MPUD_OK && result.hand_count == 0, "hand poll empty");
            }
            mpud_destroy_hand_tracker(tracker);
            Check(true, "hand destroy");
        }
    }

    // --- Face ---
    {
        std::string path = models + "/face_landmarker.task";
        MpudFaceTrackerConfig config = {path.c_str(), 1, 0.5f, 0.5f};
        MpudFaceTracker* tracker = nullptr;
        int rc = mpud_create_face_tracker(&config, &tracker);
        Check(rc == MPUD_OK, "face create");
        if (rc == MPUD_OK) {
            for (int f = 0; f < 3; ++f) {
                if (!have_raw) FillSynthetic(pixels, f + 10);
                MpudImageFrame frame = {pixels, g_width, g_height, g_width * 4,
                                        MPUD_PIXEL_FORMAT_SRGBA, (long long)(f + 1) * 33333};
                rc = mpud_submit_face_frame(tracker, &frame);
                Check(rc == MPUD_OK, "face submit");
                MpudFaceResult result;
                memset(&result, 0xCD, sizeof(result));
                rc = mpud_try_get_latest_face_result(tracker, &result);
                Check(rc == MPUD_OK && result.face_count == 0, "face poll empty");
            }
            mpud_destroy_face_tracker(tracker);
            Check(true, "face destroy");
        }
    }

    // --- Pose ---
    {
        std::string path = models + "/pose_landmarker_full.task";
        MpudPoseTrackerConfig config = {path.c_str(), 1, 0.5f, 0.5f};
        MpudPoseTracker* tracker = nullptr;
        int rc = mpud_create_pose_tracker(&config, &tracker);
        Check(rc == MPUD_OK, "pose create");
        if (rc == MPUD_OK) {
            for (int f = 0; f < 3; ++f) {
                if (!have_raw) FillSynthetic(pixels, f + 20);
                MpudImageFrame frame = {pixels, g_width, g_height, g_width * 4,
                                        MPUD_PIXEL_FORMAT_SRGBA, (long long)(f + 1) * 33333};
                rc = mpud_submit_pose_frame(tracker, &frame);
                Check(rc == MPUD_OK, "pose submit");
                MpudPoseResult result;
                memset(&result, 0xCD, sizeof(result));
                rc = mpud_try_get_latest_pose_result(tracker, &result);
                Check(rc == MPUD_OK && result.pose_count == 0, "pose poll empty");
            }
            mpud_destroy_pose_tracker(tracker);
            Check(true, "pose destroy");
        }
    }

    // --- Holistic ---
    {
        std::string path = models + "/holistic_landmarker.task";
        MpudHolisticTrackerConfig config = {path.c_str(), 0.5f, 0.5f};
        MpudHolisticTracker* tracker = nullptr;
        int rc = mpud_create_holistic_tracker(&config, &tracker);
        Check(rc == MPUD_OK, "holistic create");
        if (rc == MPUD_OK) {
            for (int f = 0; f < 3; ++f) {
                if (!have_raw) FillSynthetic(pixels, f + 30);
                MpudImageFrame frame = {pixels, g_width, g_height, g_width * 4,
                                        MPUD_PIXEL_FORMAT_SRGBA, (long long)(f + 1) * 33333};
                rc = mpud_submit_holistic_frame(tracker, &frame);
                Check(rc == MPUD_OK, "holistic submit");
                MpudHolisticResult result;
                memset(&result, 0xCD, sizeof(result));
                rc = mpud_try_get_latest_holistic_result(tracker, &result);
                Check(rc == MPUD_OK && result.face_landmark_count == 0, "holistic poll empty");
            }
            mpud_destroy_holistic_tracker(tracker);
            Check(true, "holistic destroy");
        }
    }

    free(pixels);
    printf(failures == 0 ? "SMOKE OK\n" : "SMOKE FAILED (%d)\n", failures);
    return failures == 0 ? 0 : 1;
}
