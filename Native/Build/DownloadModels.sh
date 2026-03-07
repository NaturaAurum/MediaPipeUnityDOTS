#!/bin/bash
set -euo pipefail
REPO_ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
DEST_DIR="$REPO_ROOT/MediaPipeUnityDOTS/Assets/StreamingAssets/MediaPipe/Models"
MODEL_URL="https://storage.googleapis.com/mediapipe-models/hand_landmarker/hand_landmarker/float16/latest/hand_landmarker.task"

mkdir -p "$DEST_DIR"
if [ ! -f "$DEST_DIR/hand_landmarker.task" ]; then
    echo "[Download] hand_landmarker.task"
    curl -L -o "$DEST_DIR/hand_landmarker.task" "$MODEL_URL"
else
    echo "[Skip] hand_landmarker.task already exists"
fi
echo "[Done] Model at: $DEST_DIR/hand_landmarker.task"
