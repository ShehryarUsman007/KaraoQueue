#!/usr/bin/env bash
# Builds every release file into dist/:
#   KaraokeShuffler-<version>-Setup.exe        Windows installer (.NET bundled, no admin needed)
#   karaoke-shuffler_<version>_amd64.deb       Debian / Ubuntu / Mint
#   karaoke-shuffler-<version>-1.x86_64.rpm    Fedora / openSUSE / RHEL
#   KaraokeShuffler-<version>-x86_64.AppImage  any Linux, runs without installing
#   KaraokeShuffler-<version>-macOS-AppleSilicon.zip / -macOS-Intel.zip
#                                              unsigned .app bundles (macOS 12+)
#
# Needs: .NET 10 SDK, makensis (Fedora: mingw32-nsis, Debian: nsis), rpmbuild (rpm-build), dpkg-deb (dpkg).
# appimagetool is downloaded into dist/tools the first time.
# Usage: packaging/build.sh   (from anywhere; SKIP_APPIMAGE=1 skips the AppImage)
set -euo pipefail

# use a per-user .NET install if dotnet isn't on PATH
if ! command -v dotnet >/dev/null && [ -x "$HOME/.dotnet/dotnet" ]; then
    export PATH="$HOME/.dotnet:$PATH" DOTNET_ROOT="$HOME/.dotnet"
fi

PKG="$(cd "$(dirname "$0")" && pwd)"          # this packaging folder
ROOT="$(dirname "$PKG")"                       # project folder
DIST="$ROOT/dist"
VERSION="$(grep -oP '(?<=<Version>)[^<]+' "$ROOT/Singer_Selector.csproj")"
APPNAME="karaoke-shuffler"

rm -rf "$DIST"/app-* "$DIST/stage"
mkdir -p "$DIST/stage" "$DIST/tools"
rm -f "$DIST"/*.exe "$DIST"/*.deb "$DIST"/*.rpm "$DIST"/*.AppImage "$DIST"/*.zip

# ---------- 1. self-contained builds (.NET included, debug symbols removed) ----------
for rid in win-x64 linux-x64 osx-arm64 osx-x64; do
    dotnet publish "$ROOT/Singer_Selector.csproj" -c Release -r "$rid" --self-contained \
        -p:DebugType=none -o "$DIST/app-$rid"
    find "$DIST/app-$rid" -name '*.pdb' -delete
done
# tracing plugin that needs liblttng (not on most systems, not needed)
rm -f "$DIST/app-linux-x64/libcoreclrtraceptprovider.so"

# ---------- 2. Windows installer ----------
(cd "$PKG" && makensis -V2 -DVERSION="$VERSION" -DSRCDIR="$DIST/app-win-x64" \
    -DOUTFILE="$DIST/KaraokeShuffler-$VERSION-Setup.exe" installer.nsi)

# ---------- 3. shared Linux file layout: /opt app, /usr/bin link, menu entry, icons ----------
lay_out() {     # $1 = root folder to fill
    mkdir -p "$1/opt/$APPNAME" "$1/usr/bin"
    cp -a "$DIST/app-linux-x64/." "$1/opt/$APPNAME/"
    ln -sf "/opt/$APPNAME/Singer_Selector" "$1/usr/bin/$APPNAME"
    install -Dm644 "$PKG/$APPNAME.desktop" "$1/usr/share/applications/$APPNAME.desktop"
    install -Dm644 "$PKG/icon.png" "$1/usr/share/icons/hicolor/256x256/apps/$APPNAME.png"
    install -Dm644 "$PKG/icon.svg" "$1/usr/share/icons/hicolor/scalable/apps/$APPNAME.svg"
}

# ---------- 4. .deb ----------
DEB="$DIST/stage/deb"
lay_out "$DEB"
mkdir -p "$DEB/DEBIAN"
cat > "$DEB/DEBIAN/control" <<CONTROL
Package: $APPNAME
Version: $VERSION
Architecture: amd64
Maintainer: Shehryar Usman <94995564+ShehryarUsman007@users.noreply.github.com>
Installed-Size: $(du -sk "$DEB" | cut -f1)
Depends: libx11-6, libxext6, libxfixes3, libxi6, libxrandr2, libfontconfig1, libfreetype6, libicu70 | libicu72 | libicu74 | libicu76 | libicu77 | libicu78, xdg-utils
Section: sound
Priority: optional
Homepage: https://github.com/ShehryarUsman007/Kareoke-Shuffler
Description: Shuffle and track the karaoke singer rotation
 A very basic karaoke singer rotation app, made for a karaoke group in
 Windsor, Ontario. Shuffles the singers, highlights whose turn it is and
 handles late arrivals.
CONTROL
dpkg-deb --root-owner-group -Zxz --build "$DEB" "$DIST/${APPNAME}_${VERSION}_amd64.deb"

# ---------- 5. .rpm ----------
rpmbuild -bb --quiet \
    --define "_topdir $DIST/stage/rpm" \
    --define "_rpmdir $DIST" \
    --define "_build_name_fmt %%{NAME}-%%{VERSION}-%%{RELEASE}.%%{ARCH}.rpm" \
    --define "_version $VERSION" \
    --define "_appdir $DIST/app-linux-x64" \
    --define "_pkgdir $PKG" \
    "$PKG/$APPNAME.spec"

# ---------- 6. AppImage ----------
if [ "${SKIP_APPIMAGE:-}" != 1 ]; then
    APPDIR="$DIST/stage/AppDir"
    mkdir -p "$APPDIR/usr/lib/$APPNAME"
    cp -a "$DIST/app-linux-x64/." "$APPDIR/usr/lib/$APPNAME/"
    cp "$PKG/$APPNAME.desktop" "$APPDIR/"
    cp "$PKG/icon.png" "$APPDIR/$APPNAME.png"
    ln -sf "$APPNAME.png" "$APPDIR/.DirIcon"
    # AppRun: starts the bundled app from wherever the AppImage is mounted
    printf '%s\n' '#!/bin/sh' \
        'HERE="$(dirname "$(readlink -f "$0")")"' \
        'exec "$HERE/usr/lib/karaoke-shuffler/Singer_Selector" "$@"' > "$APPDIR/AppRun"
    chmod +x "$APPDIR/AppRun"

    TOOL="$DIST/tools/appimagetool-x86_64.AppImage"
    if [ ! -x "$TOOL" ]; then
        curl -fL -o "$TOOL" https://github.com/AppImage/appimagetool/releases/download/continuous/appimagetool-x86_64.AppImage
        chmod +x "$TOOL"
    fi
    ARCH=x86_64 "$TOOL" --appimage-extract-and-run "$APPDIR" "$DIST/KaraokeShuffler-$VERSION-x86_64.AppImage"
fi

# ---------- 7. macOS .app bundles (zipped) ----------
# Uno only builds .app bundles when run on a Mac, so the bundle is put together here by hand:
#   Karaoke Shuffler.app/Contents/{Info.plist, MacOS/<app files>, Resources/icon.icns}
# Not signed by Apple: first launch needs System Settings > Privacy & Security > "Open Anyway".

# .icns icon = "icns" header followed by PNG entries (ic07=128, ic08=256, ic09=512, ic10=1024 px)
ICNS="$DIST/stage/icon.icns"
for size in 128 256 512 1024; do rsvg-convert -w $size -h $size "$PKG/icon.svg" -o "$DIST/stage/icon$size.png"; done
python3 - "$DIST/stage" <<'PY'
import struct, sys
folder = sys.argv[1]
entries = b""
for kind, size in [(b"ic07", 128), (b"ic08", 256), (b"ic09", 512), (b"ic10", 1024)]:
    png = open(f"{folder}/icon{size}.png", "rb").read()
    entries += kind + struct.pack(">I", len(png) + 8) + png
open(f"{folder}/icon.icns", "wb").write(b"icns" + struct.pack(">I", len(entries) + 8) + entries)
PY

make_app() {    # $1 = runtime id, $2 = label used in the zip name
    local out="$DIST/stage/$1"
    local app="$out/Karaoke Shuffler.app/Contents"
    mkdir -p "$app/MacOS" "$app/Resources"
    cp -a "$DIST/app-$1/." "$app/MacOS/"
    cp "$ICNS" "$app/Resources/icon.icns"
    cat > "$app/Info.plist" <<PLIST
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
    <key>CFBundleName</key>               <string>Karaoke Shuffler</string>
    <key>CFBundleDisplayName</key>        <string>Karaoke Shuffler</string>
    <key>CFBundleIdentifier</key>         <string>io.github.shehryarusman007.karaokeshuffler</string>
    <key>CFBundleExecutable</key>         <string>Singer_Selector</string>
    <key>CFBundleIconFile</key>           <string>icon.icns</string>
    <key>CFBundlePackageType</key>        <string>APPL</string>
    <key>CFBundleVersion</key>            <string>$VERSION</string>
    <key>CFBundleShortVersionString</key> <string>$VERSION</string>
    <key>LSMinimumSystemVersion</key>     <string>12.0</string>
    <key>NSHighResolutionCapable</key>    <true/>
</dict>
</plist>
PLIST
    # -y keeps symlinks, zip keeps the executable permissions macOS needs
    (cd "$out" && zip -qry "$DIST/KaraokeShuffler-$VERSION-macOS-$2.zip" "Karaoke Shuffler.app")
}
rm -f "$DIST"/*-macOS-*.zip
make_app osx-arm64 AppleSilicon
make_app osx-x64 Intel

rm -rf "$DIST/stage"
echo
echo "Done:"
ls -lh "$DIST"/KaraokeShuffler-* "$DIST"/karaoke-shuffler*
