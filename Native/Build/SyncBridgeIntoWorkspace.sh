#!/bin/bash
set -euo pipefail
SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
NATIVE_DIR="$(dirname "$SCRIPT_DIR")"
UPSTREAM_MP="$NATIVE_DIR/Upstream/mediapipe"
BRIDGE_DEST="$UPSTREAM_MP/mediapipe/mpud_bridge"

echo "[Sync] Bridge → $BRIDGE_DEST"

mkdir -p "$BRIDGE_DEST"
cp "$NATIVE_DIR/Bridge/Include/"*.h  "$BRIDGE_DEST/"
cp "$NATIVE_DIR/Bridge/Src/"*.cc     "$BRIDGE_DEST/"
cp "$NATIVE_DIR/Bridge/BazelOverlay/BUILD" "$BRIDGE_DEST/BUILD"

PATCH_DIR="$NATIVE_DIR/Patches/mediapipe"
if [ -d "$PATCH_DIR" ] && ls "$PATCH_DIR"/*.diff 1>/dev/null 2>&1; then
    cd "$UPSTREAM_MP"
    for patch in "$PATCH_DIR"/*.diff; do
        PATCH_NAME="$(basename "$patch")"
        if git apply --check "$patch" 2>/dev/null; then
            echo "[Patch] Applying $PATCH_NAME"
            git apply "$patch"
        elif git apply --check --reverse "$patch" 2>/dev/null; then
            echo "[Patch] Already applied: $PATCH_NAME"
        else
            echo "[Patch] CONFLICT: $PATCH_NAME — manual resolution required"
            exit 1
        fi
    done
fi

echo "[Sync] Done"
