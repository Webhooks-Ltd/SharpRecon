#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PUBLISH_DIR="${1:-$SCRIPT_DIR/src/SharpRecon/bin/publish}"
REPO="Webhooks-Ltd/SharpRecon"

get_rid() {
    local os arch
    case "$(uname -s)" in
        Linux*)  os="linux" ;;
        Darwin*) os="osx" ;;
        MINGW*|MSYS*|CYGWIN*) os="win" ;;
        *)       echo "Unsupported OS: $(uname -s)" >&2; exit 1 ;;
    esac
    case "$(uname -m)" in
        x86_64|amd64)  arch="x64" ;;
        aarch64|arm64) arch="arm64" ;;
        *)             echo "Unsupported arch: $(uname -m)" >&2; exit 1 ;;
    esac
    echo "${os}-${arch}"
}

get_latest_tag() {
    curl -sf --connect-timeout 5 \
        -H "Accept: application/vnd.github+json" \
        "https://api.github.com/repos/$REPO/releases/latest" 2>/dev/null \
        | grep -m1 '"tag_name"' | cut -d'"' -f4 || true
}

install_release() {
    local target_dir="$1" tag="$2"
    local rid ext url tmp_file

    rid="$(get_rid)"
    ext="tar.gz"
    [[ "$rid" == win-* ]] && ext="zip"

    local url_base="releases/latest/download"
    [[ -n "$tag" ]] && url_base="releases/download/$tag"
    url="https://github.com/$REPO/$url_base/sharp-recon-${rid}.${ext}"

    echo "Downloading SharpRecon ($rid) from GitHub releases..." >&2
    tmp_file="$(mktemp)"

    if ! curl -fSL --progress-bar -o "$tmp_file" "$url"; then
        rm -f "$tmp_file"
        echo "Failed to download $url — have you created a release?" >&2
        exit 1
    fi

    rm -rf "$target_dir"
    mkdir -p "$target_dir"

    if [[ "$ext" == "zip" ]]; then
        unzip -qo "$tmp_file" -d "$target_dir"
    else
        tar -xzf "$tmp_file" -C "$target_dir"
    fi

    rm -f "$tmp_file"

    [[ -n "$tag" ]] && printf '%s' "$tag" > "$target_dir/.release-tag"
    echo "Installed SharpRecon $tag to $target_dir" >&2
}

has_files=false
[[ -d "$PUBLISH_DIR" ]] && compgen -G "$PUBLISH_DIR/SharpRecon*" > /dev/null 2>&1 && has_files=true

local_tag=""
[[ -f "$PUBLISH_DIR/.release-tag" ]] && local_tag="$(cat "$PUBLISH_DIR/.release-tag")"

if [[ "$has_files" == false ]]; then
    latest_tag="$(get_latest_tag)"
    install_release "$PUBLISH_DIR" "$latest_tag"
elif [[ -n "$local_tag" ]]; then
    latest_tag="$(get_latest_tag)"
    if [[ -n "$latest_tag" && "$latest_tag" != "$local_tag" ]]; then
        echo "Update available: $local_tag -> $latest_tag" >&2
        install_release "$PUBLISH_DIR" "$latest_tag"
    fi
fi

BASE_TEMP="${TMPDIR:-/tmp}/SharpRecon"
mkdir -p "$BASE_TEMP"

find "$BASE_TEMP" -mindepth 1 -maxdepth 1 -type d -mmin +60 -exec rm -rf {} + 2>/dev/null || true

SHADOW_DIR="$BASE_TEMP/$(head -c 16 /dev/urandom | xxd -p)"
mkdir -p "$SHADOW_DIR"

cleanup() { rm -rf "$SHADOW_DIR"; }
trap cleanup EXIT

cp -r "$PUBLISH_DIR"/. "$SHADOW_DIR/"

rid="$(get_rid)"
if [[ "$rid" == win-* ]]; then
    exe="$SHADOW_DIR/SharpRecon.exe"
else
    exe="$SHADOW_DIR/SharpRecon"
    chmod +x "$exe"
fi

exec "$exe"
