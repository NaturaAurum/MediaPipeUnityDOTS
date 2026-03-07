#!/bin/bash
set -euo pipefail
SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
NATIVE_DIR="$(dirname "$SCRIPT_DIR")"
UPSTREAM_MP="$NATIVE_DIR/Upstream/mediapipe"

"$SCRIPT_DIR/SyncBridgeIntoWorkspace.sh"

cd "$UPSTREAM_MP"

# --- macOS 26+ 호환: Bazel 6 toolchain 바이너리에 LC_UUID 주입 ---
fix_bazel_toolchain_uuid() {
    local BAZEL_OUTPUT
    BAZEL_OUTPUT="$(bazel --bazelrc="$SCRIPT_DIR/.bazelrc" info output_base 2>/dev/null || true)"
    if [ -z "$BAZEL_OUTPUT" ]; then
        return
    fi

    local CC_DIR="$BAZEL_OUTPUT/external/local_config_cc"
    local WRAPPED="$CC_DIR/wrapped_clang"
    local LIBTOOL="$CC_DIR/libtool_check_unique"

    if [ -f "$WRAPPED" ] && ! otool -l "$WRAPPED" 2>/dev/null | grep -q "LC_UUID"; then
        local INSTALL_DIR
        INSTALL_DIR="$(bazel --bazelrc="$SCRIPT_DIR/.bazelrc" info install_base 2>/dev/null || true)"
        local WRAPPED_SRC="$INSTALL_DIR/embedded_tools/tools/osx/crosstool/wrapped_clang.cc"
        local LIBTOOL_SRC="$INSTALL_DIR/embedded_tools/tools/objc/libtool_check_unique.cc"

        if [ -f "$WRAPPED_SRC" ]; then
            echo "[Fix] wrapped_clang: LC_UUID 주입"
            clang++ -std=c++17 -stdlib=libc++ -lc++ -O2 -Wl,-random_uuid \
                -o "$WRAPPED" "$WRAPPED_SRC" 2>/dev/null
        fi
        if [ -f "$LIBTOOL_SRC" ]; then
            echo "[Fix] libtool_check_unique: LC_UUID 주입"
            clang++ -std=c++17 -stdlib=libc++ -lc++ -O2 -Wl,-random_uuid \
                -o "$LIBTOOL" "$LIBTOOL_SRC" 2>/dev/null
        fi
    fi
}

# --- macOS 26+ 호환: vendored zlib fdopen 매크로 충돌 수정 ---
fix_zlib_fdopen() {
    local BAZEL_OUTPUT
    BAZEL_OUTPUT="$(bazel --bazelrc="$SCRIPT_DIR/.bazelrc" info output_base 2>/dev/null || true)"
    if [ -z "$BAZEL_OUTPUT" ]; then
        return
    fi

    local ZUTIL="$BAZEL_OUTPUT/external/zlib/zutil.h"
    if [ -f "$ZUTIL" ] && grep -q 'define fdopen(fd,mode) NULL' "$ZUTIL"; then
        echo "[Fix] zlib zutil.h: fdopen 매크로 제거 (SDK 26+ 호환)"
        sed -i '' '/#if defined(MACOS) || defined(TARGET_OS_MAC)/,/#endif/{
            /^#      ifndef fdopen$/d
            /^#        define fdopen(fd,mode) NULL/d
            /^#    else$/d
        }' "$ZUTIL"
    fi
}

# fetch → toolchain 바이너리 생성 → 패치
echo "[Build] Fetching dependencies..."
bazel --bazelrc="$SCRIPT_DIR/.bazelrc" fetch \
    //mediapipe/mpud_bridge:libmpud_bridge.dylib 2>/dev/null || true

fix_bazel_toolchain_uuid
fix_zlib_fdopen

echo "[Build] Building libmpud_bridge.dylib..."
bazel --bazelrc="$SCRIPT_DIR/.bazelrc" build -c opt \
    //mediapipe/mpud_bridge:libmpud_bridge.dylib

BAZEL_BIN="$(bazel --bazelrc="$SCRIPT_DIR/.bazelrc" info -c opt bazel-bin)"
mkdir -p "$NATIVE_DIR/Artifacts/MacosEditor"
cp "$BAZEL_BIN/mediapipe/mpud_bridge/libmpud_bridge.dylib" \
   "$NATIVE_DIR/Artifacts/MacosEditor/"

echo "[Build] Complete: $NATIVE_DIR/Artifacts/MacosEditor/libmpud_bridge.dylib"
