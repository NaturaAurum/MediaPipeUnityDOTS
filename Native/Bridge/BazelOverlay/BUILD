cc_library(
    name = "mpud_bridge_lib",
    srcs = ["mpud_bridge.cc"],
    hdrs = ["mpud_bridge.h"],
    includes = ["."],
    alwayslink = True,
    copts = ["-fvisibility=hidden"],
    deps = [
        "//mediapipe/tasks/cc/vision/hand_landmarker:hand_landmarker",
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
