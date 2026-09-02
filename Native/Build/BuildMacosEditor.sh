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

# MediaPipe v1.0.0은 MODULE.bazel에서 python 3.11 toolchain을 사용한다.
# 로컬 기본 python3가 3.14여도 hermetic Python은 3.11로 고정한다.
: "${HERMETIC_PYTHON_VERSION:=3.11}"
export HERMETIC_PYTHON_VERSION

DISTDIR_ROOT="${TMPDIR:-/tmp}"
DISTDIR_ROOT="${DISTDIR_ROOT%/}"
DISTDIR="$DISTDIR_ROOT/mediapipe-unity-bazel-distdir"
mkdir -p "$DISTDIR"

download_distdir_archive() {
    local file_name="$1"
    local url="$2"
    local expected_sha="$3"
    local dest="$DISTDIR/$file_name"
    local actual_sha=""

    if [ -f "$dest" ]; then
        actual_sha="$(shasum -a 256 "$dest" | awk '{ print $1 }')"
        if [ "$actual_sha" = "$expected_sha" ]; then
            echo "[Cache] Using distdir archive: $file_name"
            return
        fi

        echo "[Cache] Removing stale distdir archive: $file_name"
        rm -f "$dest"
    fi

    echo "[Fetch] Downloading $file_name into distdir"
    curl -fL --retry 5 --retry-delay 5 --retry-all-errors "$url" -o "$dest.tmp"
    actual_sha="$(shasum -a 256 "$dest.tmp" | awk '{ print $1 }')"
    if [ "$actual_sha" != "$expected_sha" ]; then
        echo "[Error] SHA256 mismatch for $file_name" >&2
        echo "        expected: $expected_sha" >&2
        echo "        actual:   $actual_sha" >&2
        rm -f "$dest.tmp"
        exit 1
    fi

    mv "$dest.tmp" "$dest"
}

download_distdir_archive \
    "rules_cc-0.1.4.tar.gz" \
    "https://github.com/bazelbuild/rules_cc/releases/download/0.1.4/rules_cc-0.1.4.tar.gz" \
    "0d3b4f984c4c2e1acfd1378e0148d35caf2ef1d9eb95b688f8e19ce0c41bdf5b"

download_distdir_archive \
    "rules_foreign_cc-0.12.0.tar.gz" \
    "https://github.com/bazelbuild/rules_foreign_cc/releases/download/0.12.0/rules_foreign_cc-0.12.0.tar.gz" \
    "a2e6fb56e649c1ee79703e99aa0c9d13c6cc53c8d7a0cbb8797ab2888bbc99a3"

download_distdir_archive \
    "rules_proto_grpc-4.2.0.tar.gz" \
    "https://github.com/rules-proto-grpc/rules_proto_grpc/archive/4.2.0.tar.gz" \
    "bbe4db93499f5c9414926e46f9e35016999a4e9f6e3522482d3760dc61011070"

BAZEL_STARTUP_FLAGS=(--bazelrc="$SCRIPT_DIR/.bazelrc")
BAZEL_FETCH_FLAGS=(--distdir="$DISTDIR")

"$SCRIPT_DIR/SyncBridgeIntoWorkspace.sh"

cd "$UPSTREAM_MP"

read -r EXPECTED_BAZEL_VERSION < .bazelversion
ACTUAL_BAZEL_VERSION="$($BAZEL_CMD "${BAZEL_STARTUP_FLAGS[@]}" version 2>/dev/null | awk '/Build label:/ { print $3; exit }')"
if [ -z "$ACTUAL_BAZEL_VERSION" ] || [ "$ACTUAL_BAZEL_VERSION" != "$EXPECTED_BAZEL_VERSION" ]; then
    echo "[Error] bazelisk resolved Bazel '$ACTUAL_BAZEL_VERSION' but expected '$EXPECTED_BAZEL_VERSION'." >&2
    exit 1
fi
echo "[Build] Using bazelisk -> Bazel $ACTUAL_BAZEL_VERSION"

# --- macOS 26+ 호환: Bazel 6 toolchain 바이너리에 LC_UUID 주입 ---
fix_bazel_toolchain_uuid() {
    local BAZEL_OUTPUT
    BAZEL_OUTPUT="$("$BAZEL_CMD" "${BAZEL_STARTUP_FLAGS[@]}" info output_base 2>/dev/null || true)"
    if [ -z "$BAZEL_OUTPUT" ]; then
        return
    fi

    local CC_DIR="$BAZEL_OUTPUT/external/local_config_cc"
    local WRAPPED="$CC_DIR/wrapped_clang"
    local LIBTOOL="$CC_DIR/libtool_check_unique"

    if [ -f "$WRAPPED" ] && ! otool -l "$WRAPPED" 2>/dev/null | grep -q "LC_UUID"; then
        local INSTALL_DIR
        INSTALL_DIR="$("$BAZEL_CMD" "${BAZEL_STARTUP_FLAGS[@]}" info install_base 2>/dev/null || true)"
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
    BAZEL_OUTPUT="$("$BAZEL_CMD" "${BAZEL_STARTUP_FLAGS[@]}" info output_base 2>/dev/null || true)"
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
"$BAZEL_CMD" "${BAZEL_STARTUP_FLAGS[@]}" fetch "${BAZEL_FETCH_FLAGS[@]}" \
    //mediapipe/mpud_bridge:libmpud_bridge.dylib 2>/dev/null || true

fix_bazel_toolchain_uuid
fix_zlib_fdopen

echo "[Build] Building libmpud_bridge.dylib..."
"$BAZEL_CMD" "${BAZEL_STARTUP_FLAGS[@]}" build "${BAZEL_FETCH_FLAGS[@]}" -c opt \
    //mediapipe/mpud_bridge:libmpud_bridge.dylib

BAZEL_BIN="$("$BAZEL_CMD" "${BAZEL_STARTUP_FLAGS[@]}" info -c opt bazel-bin)"
mkdir -p "$NATIVE_DIR/Artifacts/MacosEditor"
if [ -f "$NATIVE_DIR/Artifacts/MacosEditor/libmpud_bridge.dylib" ]; then
    chmod u+w "$NATIVE_DIR/Artifacts/MacosEditor/libmpud_bridge.dylib"
fi
cp "$BAZEL_BIN/mediapipe/mpud_bridge/libmpud_bridge.dylib" \
   "$NATIVE_DIR/Artifacts/MacosEditor/"

echo "[Build] Complete: $NATIVE_DIR/Artifacts/MacosEditor/libmpud_bridge.dylib"
