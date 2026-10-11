#!/usr/bin/env bash
# Converts every image in this folder to a JPEG under the Steam Workshop size limit,
# picking the highest quality that fits (binary search), downscaling only if needed.
# Originals are kept; output goes to ./jpeg/.
set -euo pipefail

LIMIT=${LIMIT:-2000000}   # bytes; stays safely under 2 MB
MIN_Q=40
MAX_Q=95

cd "$(dirname "$0")"
mkdir -p jpeg
tmp=$(mktemp --suffix=.jpg)
trap 'rm -f "$tmp"' EXIT

encode() { # src quality scale% dst
    magick "$1" -flatten -resize "$3%" -strip -sampling-factor 4:2:0 \
        -interlace JPEG -colorspace sRGB -quality "$2" "$4"
}

shopt -s nullglob nocaseglob
for src in *.png *.jpg *.jpeg *.webp *.bmp *.tga *.tif *.tiff *.psd; do
    input=$src
    [[ ${src,,} == *.psd ]] && input="$src[0]"   # PSD layer 0 is the flattened composite
    dst="jpeg/${src%.*}.jpg"

    scale=100
    while :; do
        lo=$MIN_Q hi=$MAX_Q best=
        while (( lo <= hi )); do
            q=$(( (lo + hi) / 2 ))
            encode "$input" "$q" "$scale" "$tmp"
            if (( $(stat -c%s "$tmp") <= LIMIT )); then
                best=$q; cp "$tmp" "$dst"; lo=$(( q + 1 ))
            else
                hi=$(( q - 1 ))
            fi
        done
        [[ -n $best ]] && break
        scale=$(( scale * 85 / 100 ))   # even MIN_Q is too big: shrink and retry
    done

    printf '%-40s q=%-3s scale=%3s%%  %s KB\n' "$src" "$best" "$scale" "$(( $(stat -c%s "$dst") / 1024 ))"
done
