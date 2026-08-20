#!/usr/bin/env bash
# baut nanoMIDIPlayer.app als single-file bundle fuer macOS.
# auf einem mac ausfuehren (braucht sips/iconutil/codesign fuer icon + signatur).
#
#   ./build/publish-mac.sh              -> arm64 (apple silicon)
#   ./build/publish-mac.sh osx-x64      -> intel
#
set -euo pipefail

RID="${1:-osx-arm64}"
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
OUT="$ROOT/dist/$RID"
APP="$OUT/nanoMIDIPlayer.app"

echo "==> publish $RID"
rm -rf "$OUT"
dotnet publish "$ROOT/nanoMIDIPlayerCS.csproj" \
    -c Release \
    -r "$RID" \
    --self-contained true \
    -o "$OUT/bin"

echo "==> .app bundle bauen"
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"
cp "$ROOT/build/Info.plist" "$APP/Contents/Info.plist"
cp "$OUT/bin/nanoMIDIPlayer" "$APP/Contents/MacOS/nanoMIDIPlayer"
chmod +x "$APP/Contents/MacOS/nanoMIDIPlayer"

# icon aus dem png erzeugen
if command -v sips >/dev/null && command -v iconutil >/dev/null; then
    ICONSET="$OUT/icon.iconset"
    mkdir -p "$ICONSET"
    for size in 16 32 64 128 256 512; do
        sips -z $size $size "$ROOT/Assets/icon.png" \
            --out "$ICONSET/icon_${size}x${size}.png" >/dev/null 2>&1
        sips -z $((size*2)) $((size*2)) "$ROOT/Assets/icon.png" \
            --out "$ICONSET/icon_${size}x${size}@2x.png" >/dev/null 2>&1
    done
    iconutil -c icns "$ICONSET" -o "$APP/Contents/Resources/nanoMIDIPlayer.icns"
    rm -rf "$ICONSET"
else
    echo "    (sips/iconutil fehlen - kein icon)"
fi

# ad-hoc signieren: macOS bindet das Bedienungshilfen-Recht an die signatur,
# ohne sie muss das recht nach jedem rebuild neu vergeben werden
if command -v codesign >/dev/null; then
    echo "==> ad-hoc signieren"
    codesign --force --deep --sign - "$APP"
fi

rm -rf "$OUT/bin"
echo
echo "fertig: $APP"
echo "beim ersten start: Systemeinstellungen > Datenschutz & Sicherheit >"
echo "Bedienungshilfen > nanoMIDIPlayer aktivieren, dann app neu starten."
