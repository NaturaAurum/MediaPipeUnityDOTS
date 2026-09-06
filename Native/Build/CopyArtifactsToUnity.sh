#!/bin/bash
set -euo pipefail
SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
NATIVE_DIR="$(dirname "$SCRIPT_DIR")"
REPO_ROOT="$(dirname "$NATIVE_DIR")"
SRC="$NATIVE_DIR/Artifacts/MacosEditor/libmpud_bridge.dylib"
DEST_DIR="$REPO_ROOT/MediaPipeUnityDOTS/Assets/MediaPipeUnityDots/Runtime/Plugins/macOS"
DEST="$DEST_DIR/libmpud_bridge.dylib"

if [ ! -f "$SRC" ]; then
    echo "[Error] Artifact not found: $SRC"
    echo "        Run BuildMacosEditor.sh first."
    exit 1
fi

mkdir -p "$DEST_DIR"
if [ -f "$DEST" ]; then
    chmod u+w "$DEST"
fi
cp "$SRC" "$DEST"

install_name_tool -id "@loader_path/libmpud_bridge.dylib" "$DEST"

codesign --force -s - "$DEST"

echo "[Copy] Done: $DEST"
echo "[Post] install_name fixed, ad-hoc signed"

echo "--- Verify ---"
echo "install_name:"
otool -D "$DEST"
echo "codesign:"
codesign -dv "$DEST" 2>&1 | head -3
