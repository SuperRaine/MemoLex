#!/bin/bash
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
OUTPUT="${MEMOLEX_OUTPUT:-$ROOT/macos/output}"
APP="$OUTPUT/MemoLex.app"
VERSION="$(/usr/bin/plutil -extract CFBundleShortVersionString raw "$ROOT/macos/Info.plist")"
WORK="$(mktemp -d -t memolex-build)"
trap 'rm -rf "$WORK"' EXIT
mkdir -p "$OUTPUT"
if [ -e "$APP" ]; then
  echo 'Output already exists. Choose an empty MEMOLEX_OUTPUT directory.' >&2
  exit 1
fi

# The release archive is a pinned data source; the Windows program is not included.
DATA_ROOT="${MEMOLEX_DATA_ROOT:-}"
if [ -z "$DATA_ROOT" ]; then
  curl --fail --location --retry 3 --output "$WORK/windows.zip" 'https://github.com/SuperRaine/MemoLex/releases/download/v1.9.0/MemoLex-1.9.0-Windows-x64.zip'
  printf '%s  %s\n' '0d11962fcdfc1ca12e4e0e26e34350161461c17505edfec83513b7d78345222e' "$WORK/windows.zip" | shasum -a 256 -c -
  unzip -q "$WORK/windows.zip" 'MemoLex/app/data/*' 'MemoLex/app/ThirdParty/*' -d "$WORK/data-source"
  DATA_ROOT="$WORK/data-source/MemoLex/app"
fi
test -f "$DATA_ROOT/data/dictionary.index.tsv"

export MACOSX_DEPLOYMENT_TARGET=13.0
swift build --package-path "$ROOT/macos" -c release --arch arm64 --scratch-path "$WORK/swift-arm64"
swift build --package-path "$ROOT/macos" -c release --arch x86_64 --scratch-path "$WORK/swift-x64"
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"
cp "$ROOT/macos/Info.plist" "$APP/Contents/Info.plist"
lipo -create "$WORK/swift-arm64/arm64-apple-macosx/release/MemoLex" "$WORK/swift-x64/x86_64-apple-macosx/release/MemoLex" -output "$APP/Contents/MacOS/MemoLex"
for ARCH in arm64 x64; do
  dotnet publish "$ROOT/macos/Core/MemoLex.Core.csproj" -c Release -r "osx-$ARCH" --self-contained true \
    -p:RuntimeFrameworkVersion=8.0.34 -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true \
    -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -p:DebugSymbols=false \
    -o "$WORK/core-$ARCH" --nologo
  mkdir -p "$APP/Contents/Resources/Core-$ARCH"
  cp "$WORK/core-$ARCH/MemoLex.Core" "$APP/Contents/Resources/Core-$ARCH/"
  codesign --force --sign - --identifier "com.superraine.MemoLex.Core.$ARCH" --entitlements "$ROOT/macos/Core.entitlements" "$APP/Contents/Resources/Core-$ARCH/MemoLex.Core"
done
ditto "$DATA_ROOT/data" "$APP/Contents/Resources/data"
ditto "$DATA_ROOT/ThirdParty" "$APP/Contents/Resources/ThirdParty"
cp "$ROOT/src/Assets/owl-logo.png" "$ROOT/src/Assets/sponsor-wechat.jpg" "$ROOT/src/Assets/sponsor-alipay.jpg" "$APP/Contents/Resources/"
cp "$ROOT/LICENSE" "$APP/Contents/Resources/LICENSE.txt"
cp "$ROOT/macos/使用说明.md" "$APP/Contents/Resources/使用说明.md"
ICONSET="$WORK/MemoLex.iconset"
mkdir -p "$ICONSET"
for SIZE in 16 32 128 256 512; do
  sips -z "$SIZE" "$SIZE" "$ROOT/src/Assets/owl-logo.png" --out "$ICONSET/icon_${SIZE}x${SIZE}.png" >/dev/null
  DOUBLE=$((SIZE * 2))
  sips -z "$DOUBLE" "$DOUBLE" "$ROOT/src/Assets/owl-logo.png" --out "$ICONSET/icon_${SIZE}x${SIZE}@2x.png" >/dev/null
done
iconutil -c icns "$ICONSET" -o "$APP/Contents/Resources/MemoLex.icns"
chmod 755 "$APP/Contents/MacOS/MemoLex" "$APP/Contents/Resources/Core-arm64/MemoLex.Core" "$APP/Contents/Resources/Core-x64/MemoLex.Core"
codesign --force --sign - --identifier com.superraine.MemoLex "$APP"
codesign --verify --deep --strict --verbose=2 "$APP"
plutil -lint "$APP/Contents/Info.plist"
ARCH="$(uname -m)"; [ "$ARCH" != x86_64 ] || ARCH=x64
MEMOLEX_RESOURCES="$APP/Contents/Resources" MEMOLEX_PROFILE="$WORK/profile" "$APP/Contents/Resources/Core-$ARCH/MemoLex.Core" --smoke-test

# This archive transfers the app to the native verification job.
ditto -c -k --sequesterRsrc --keepParent "$APP" "$OUTPUT/MemoLex-$VERSION-macOS-universal.zip"
if [ "${MEMOLEX_VERIFY_ONLY:-0}" = "1" ]; then
  exit 0
fi
mkdir "$WORK/dmg"
ditto "$APP" "$WORK/dmg/MemoLex.app"
ln -s /Applications "$WORK/dmg/Applications"
cp "$ROOT/macos/使用说明.md" "$WORK/dmg/使用说明.md"
hdiutil create -volname "墨墨搜词 $VERSION" -srcfolder "$WORK/dmg" -format UDZO -imagekey zlib-level=9 "$OUTPUT/MemoLex-$VERSION-macOS-universal.dmg"
echo "Built MemoLex $VERSION for Apple Silicon and Intel."
