#!/bin/bash
set -euo pipefail
SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
NATIVE_DIR="$(dirname "$SCRIPT_DIR")"
UPSTREAM_MP="$NATIVE_DIR/Upstream/mediapipe"

BAZEL_CMD="bazelisk"
if ! command -v "$BAZEL_CMD" >/dev/null 2>&1; then
    echo "[Error] '$BAZEL_CMD' not found. Install bazelisk." >&2
    exit 1
fi

"$SCRIPT_DIR/SyncBridgeIntoWorkspace.sh"

cd "$UPSTREAM_MP"

read -r EXPECTED_BAZEL_VERSION < .bazelversion
ACTUAL_BAZEL_VERSION="$($BAZEL_CMD version 2>/dev/null | awk '/Build label:/ { print $3; exit }')"
if [ -z "$ACTUAL_BAZEL_VERSION" ] || [ "$ACTUAL_BAZEL_VERSION" != "$EXPECTED_BAZEL_VERSION" ]; then
    echo "[Error] bazelisk resolved Bazel '$ACTUAL_BAZEL_VERSION' but expected '$EXPECTED_BAZEL_VERSION'." >&2
    exit 1
fi
echo "[Build] Using bazelisk -> Bazel $ACTUAL_BAZEL_VERSION"

# --- macOS 26+ 호환: Bazel 6 toolchain 바이너리에 LC_UUID 주입 ---
fix_bazel_toolchain_uuid() {
    local BAZEL_OUTPUT
    BAZEL_OUTPUT="$("$BAZEL_CMD" --bazelrc="$SCRIPT_DIR/.bazelrc" info output_base 2>/dev/null || true)"
    if [ -z "$BAZEL_OUTPUT" ]; then
        return
    fi

    local CC_DIR="$BAZEL_OUTPUT/external/local_config_cc"
    local WRAPPED="$CC_DIR/wrapped_clang"
    local LIBTOOL="$CC_DIR/libtool_check_unique"

    if [ -f "$WRAPPED" ] && ! otool -l "$WRAPPED" 2>/dev/null | grep -q "LC_UUID"; then
        local INSTALL_DIR
        INSTALL_DIR="$("$BAZEL_CMD" --bazelrc="$SCRIPT_DIR/.bazelrc" info install_base 2>/dev/null || true)"
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
    BAZEL_OUTPUT="$("$BAZEL_CMD" --bazelrc="$SCRIPT_DIR/.bazelrc" info output_base 2>/dev/null || true)"
    if [ -z "$BAZEL_OUTPUT" ]; then
        return
    fi

    local ZUTIL="$BAZEL_OUTPUT/external/zlib/zutil.h"
    if [ ! -f "$ZUTIL" ]; then
        return
    fi

    python3 - "$ZUTIL" <<'PY'
from pathlib import Path
import sys

path = Path(sys.argv[1])
text = path.read_text()

original = """#if defined(MACOS) || defined(TARGET_OS_MAC)
#  define OS_CODE  7
#  ifndef Z_SOLO
#    if defined(__MWERKS__) && __dest_os != __be_os && __dest_os != __win32_os
#      include <unix.h> /* for fdopen */
#    else
#      ifndef fdopen
#        define fdopen(fd,mode) NULL /* No fdopen() */
#      endif
#    endif
#  endif
#endif
"""

broken = """#if defined(MACOS) || defined(TARGET_OS_MAC)
#  define OS_CODE  7
#  ifndef Z_SOLO
#    if defined(__MWERKS__) && __dest_os != __be_os && __dest_os != __win32_os
#      include <unix.h> /* for fdopen */
#      endif
#    endif
#  endif
#endif
"""

replacement = """#if defined(MACOS) || defined(TARGET_OS_MAC)
#  define OS_CODE  7
#  ifndef Z_SOLO
#    if defined(__MWERKS__) && __dest_os != __be_os && __dest_os != __win32_os
#      include <unix.h> /* for fdopen */
#    endif
#  endif
#endif
"""

if original in text:
    path.write_text(text.replace(original, replacement, 1))
    print("[Fix] zlib zutil.h: fdopen 매크로 제거 (SDK 26+ 호환)")
elif broken in text:
    path.write_text(text.replace(broken, replacement, 1))
    print("[Fix] zlib zutil.h: 깨진 fdopen 블록 복구")
else:
    print("[Skip] zlib zutil.h: 대상 블록 없음")
PY
}

# fetch → toolchain 바이너리 생성 → 패치
echo "[Build] Fetching dependencies..."
"$BAZEL_CMD" --bazelrc="$SCRIPT_DIR/.bazelrc" fetch \
    //mediapipe/mpud_bridge:libmpud_bridge.dylib 2>/dev/null || true

fix_bazel_toolchain_uuid
fix_zlib_fdopen

echo "[Build] Building libmpud_bridge.dylib..."
"$BAZEL_CMD" --bazelrc="$SCRIPT_DIR/.bazelrc" build -c opt \
    //mediapipe/mpud_bridge:libmpud_bridge.dylib

BAZEL_BIN="$("$BAZEL_CMD" --bazelrc="$SCRIPT_DIR/.bazelrc" info -c opt bazel-bin)"
mkdir -p "$NATIVE_DIR/Artifacts/MacosEditor"
if [ -f "$NATIVE_DIR/Artifacts/MacosEditor/libmpud_bridge.dylib" ]; then
    chmod u+w "$NATIVE_DIR/Artifacts/MacosEditor/libmpud_bridge.dylib"
fi
cp "$BAZEL_BIN/mediapipe/mpud_bridge/libmpud_bridge.dylib" \
   "$NATIVE_DIR/Artifacts/MacosEditor/"

echo "[Build] Complete: $NATIVE_DIR/Artifacts/MacosEditor/libmpud_bridge.dylib"
