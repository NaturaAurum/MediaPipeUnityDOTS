cc_library(
    name = "mpud_bridge_lib",
    srcs = ["mpud_bridge.cc", "mpud_face_bridge.cc", "mpud_pose_bridge.cc", "mpud_holistic_bridge.cc"],
    hdrs = ["mpud_bridge.h", "mpud_face_bridge.h", "mpud_pose_bridge.h", "mpud_holistic_bridge.h"],
    includes = ["."],
    alwayslink = True,
    copts = ["-fvisibility=hidden"],
    deps = [
        "//mediapipe/tasks/cc/vision/hand_landmarker:hand_landmarker",
        "//mediapipe/tasks/cc/vision/face_landmarker:face_landmarker",
        "//mediapipe/tasks/cc/vision/pose_landmarker:pose_landmarker",
        "//mediapipe/tasks/cc/vision/holistic_landmarker:holistic_landmarker",
        "//mediapipe/framework/formats:image_frame",
    ],
    visibility = ["//visibility:public"],
)

cc_binary(
    name = "libmpud_bridge.dylib",
    deps = [":mpud_bridge_lib"],
    linkshared = True,
    linkstatic = True,
    visibility = ["//visibility:public"],
)

cc_binary(
    name = "mpud_smoke_test",
    srcs = ["mpud_smoke_test.cc"],
    deps = [":mpud_bridge_lib"],
    visibility = ["//visibility:public"],
)
