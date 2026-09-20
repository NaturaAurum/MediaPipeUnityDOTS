#!/usr/bin/env bash
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
MANIFEST="$REPO_ROOT/MediaPipeUnityDOTS/Assets/MediaPipeUnityDots/EditorTool/ModelManifest.txt"
DEFAULT_DEST_DIR="$REPO_ROOT/MediaPipeUnityDOTS/Assets/StreamingAssets/MediaPipe/Models"
DEST_DIR="${MPUD_MODEL_DEST:-${1:-$DEFAULT_DEST_DIR}}"

if [[ ! -f "$MANIFEST" ]]; then
    echo "[MPUD] 모델 manifest를 찾지 못했습니다: $MANIFEST" >&2
    exit 1
fi

download_model() {
    local name="$1"
    local url="$2"
    local expected_sha256="$3"
    local target="$DEST_DIR/$name"
    local temp
    local actual_sha256

    if [[ -f "$target" ]]; then
        actual_sha256="$(shasum -a 256 "$target" | cut -d ' ' -f 1)"
        if [[ "$actual_sha256" == "$expected_sha256" ]]; then
            echo "[Skip] $name (SHA-256 일치)"
            return
        fi
        echo "[Refresh] $name (SHA-256 불일치: expected=$expected_sha256 actual=$actual_sha256)"
    else
        echo "[Download] $name"
    fi

    mkdir -p "$DEST_DIR"
    temp="$(mktemp "$target.XXXXXX")"
    if ! curl --fail --location --show-error --retry 2 --output "$temp" "$url"; then
        rm -f "$temp"
        echo "[MPUD] 다운로드 실패: $name ($url)" >&2
        return 1
    fi

    actual_sha256="$(shasum -a 256 "$temp" | cut -d ' ' -f 1)"
    if [[ "$actual_sha256" != "$expected_sha256" ]]; then
        rm -f "$temp"
        echo "[MPUD] SHA-256 검증 실패: $name (expected=$expected_sha256 actual=$actual_sha256)" >&2
        return 1
    fi

    mv -f "$temp" "$target"
    echo "[Ready] $target"
}

while IFS=$'\t' read -r model kind file_name revision url sha256; do
    case "$model" in
        ""|\#*) continue ;;
    esac

    if [[ "$kind" != "task" ]]; then
        continue
    fi

    download_model "$file_name" "$url" "$sha256"
done < "$MANIFEST"

echo "[Done] Task 모델 위치: $DEST_DIR"
