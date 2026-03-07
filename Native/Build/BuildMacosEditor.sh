#!/bin/bash
set -euo pipefail
SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
NATIVE_DIR="$(dirname "$SCRIPT_DIR")"
UPSTREAM_MP="$NATIVE_DIR/Upstream/mediapipe"

"$SCRIPT_DIR/SyncBridgeIntoWorkspace.sh"

cd "$UPSTREAM_MP"
bazel --bazelrc="$SCRIPT_DIR/.bazelrc" build -c opt \
    //mediapipe/mpud_bridge:libmpud_bridge.dylib

BAZEL_BIN="$(bazel --bazelrc="$SCRIPT_DIR/.bazelrc" info -c opt bazel-bin)"
mkdir -p "$NATIVE_DIR/Artifacts/MacosEditor"
cp "$BAZEL_BIN/mediapipe/mpud_bridge/libmpud_bridge.dylib" \
   "$NATIVE_DIR/Artifacts/MacosEditor/"

echo "[Build] Complete: $NATIVE_DIR/Artifacts/MacosEditor/libmpud_bridge.dylib"
