#!/usr/bin/env bash
# Builds the macOS .app bundle that `vpk pack` then wraps (docs/M10-spec.md §3).
#
# Velopack can build a bundle by itself, but it cannot put CFBundleDocumentTypes in the Info.plist —
# and without those keys a .tboard file on a Mac opens in nothing. So the bundle is assembled here,
# with build/macos/Info.plist, and handed to vpk complete.
#
# Usage: make-app-bundle.sh <publish-dir> <version> <output-dir>
set -euo pipefail

publish_dir=$1
version=$2
output_dir=$3
here=$(cd "$(dirname "$0")" && pwd)
repo=$(cd "$here/../.." && pwd)

app="$output_dir/TrestleBoard.app"
rm -rf "$app"
mkdir -p "$app/Contents/MacOS" "$app/Contents/Resources"

cp -R "$publish_dir/." "$app/Contents/MacOS/"
chmod +x "$app/Contents/MacOS/TrestleBoard.App"

sed "s/__VERSION__/$version/g" "$here/Info.plist" > "$app/Contents/Info.plist"

# The icon is a nicety and the tools only exist on a Mac; a missing icon must not fail a release.
if command -v sips >/dev/null 2>&1 && command -v iconutil >/dev/null 2>&1; then
  iconset=$(mktemp -d)/trestleboard.iconset
  mkdir -p "$iconset"
  for size in 16 32 128 256 512; do
    sips -z "$size" "$size" "$repo/assets-src/icons/trestleboard.png" \
      --out "$iconset/icon_${size}x${size}.png" >/dev/null
    double=$((size * 2))
    sips -z "$double" "$double" "$repo/assets-src/icons/trestleboard.png" \
      --out "$iconset/icon_${size}x${size}@2x.png" >/dev/null
  done
  iconutil -c icns "$iconset" -o "$app/Contents/Resources/trestleboard.icns" || true
  rm -rf "$iconset"
fi

echo "$app"
