#!/bin/bash
set -euo pipefail
REPO_ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
DEST_DIR="$REPO_ROOT/MediaPipeUnityDOTS/Assets/StreamingAssets/MediaPipe/Models"
download_model() {
    local name="$1"
    local url="$2"

    if [ ! -f "$DEST_DIR/$name" ]; then
        echo "[Download] $name"
        curl -fL -o "$DEST_DIR/$name" "$url"
    else
        echo "[Skip] $name already exists"
    fi
}

mkdir -p "$DEST_DIR"
download_model hand_landmarker.task \
    https://storage.googleapis.com/mediapipe-models/hand_landmarker/hand_landmarker/float16/latest/hand_landmarker.task
download_model face_landmarker.task \
    https://storage.googleapis.com/mediapipe-models/face_landmarker/face_landmarker/float16/latest/face_landmarker.task
download_model pose_landmarker_full.task \
    https://storage.googleapis.com/mediapipe-models/pose_landmarker/pose_landmarker_full/float16/latest/pose_landmarker_full.task
download_model holistic_landmarker.task \
    https://storage.googleapis.com/mediapipe-models/holistic_landmarker/holistic_landmarker/float16/latest/holistic_landmarker.task

echo "[Done] Models at: $DEST_DIR"
