#!/bin/bash
set -euo pipefail
SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
NATIVE_DIR="$(dirname "$SCRIPT_DIR")"
REPO_ROOT="$(dirname "$NATIVE_DIR")"
ARTIFACT="$NATIVE_DIR/Artifacts/MacosEditor/libmpud_bridge.dylib"
DESTINATION="$REPO_ROOT/MediaPipeUnityDOTS/Assets/MediaPipeUnityDots/Runtime/Plugins/macOS"
UPSTREAM="$NATIVE_DIR/Upstream/mediapipe"

exec python3 "$SCRIPT_DIR/PackageMacosNative.py" \
    --artifact "$ARTIFACT" \
    --destination "$DESTINATION" \
    --upstream "$UPSTREAM"
