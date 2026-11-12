#!/usr/bin/env bash
# Builds the old-country desktop app in release mode and installs it for the current user:
#   ~/.local/bin/old-country                         the app (the UI is embedded in it)
#   ~/.local/share/applications/old-country.desktop  launcher entry ("old-country" in the app menu)
#   ~/.local/share/icons/hicolor/*/apps/             its icon
# Run again after pulling changes to update. Settings (the server address) are kept.
# Remove with: packaging/install-linux.sh --uninstall
#
# The app needs the system's WebKitGTK 4.1, libopus and ALSA at runtime (Arch: webkit2gtk-4.1 opus alsa-lib).
# For other distributions, `cargo tauri build` in desktop/ makes a .deb and an AppImage instead.
set -euo pipefail

repo="$(cd "$(dirname "$0")/.." && pwd)"
bin="$HOME/.local/bin/old-country"
desktop_file="$HOME/.local/share/applications/old-country.desktop"
icons="$HOME/.local/share/icons/hicolor"

if [ "${1:-}" = "--uninstall" ]; then
  rm -f "$bin" "$desktop_file" "$icons"/{32x32,64x64,128x128,256x256}/apps/old-country.png
  update-desktop-database "$HOME/.local/share/applications" 2>/dev/null || true
  echo "Removed. Settings and the audio cache stay in ~/.local/share/dev.wiormiw.oldcountry and ~/.cache/dev.wiormiw.oldcountry."
  exit 0
fi

echo "== Building (release)"
(cd "$repo/desktop" && cargo tauri build --no-bundle)

echo "== Installing"
install -Dm755 "$repo/desktop/target/release/old-country" "$bin"
src="$repo/desktop/src-tauri/icons"
install -Dm644 "$src/32x32.png" "$icons/32x32/apps/old-country.png"
install -Dm644 "$src/64x64.png" "$icons/64x64/apps/old-country.png"
install -Dm644 "$src/128x128.png" "$icons/128x128/apps/old-country.png"
install -Dm644 "$src/128x128@2x.png" "$icons/256x256/apps/old-country.png"
mkdir -p "$(dirname "$desktop_file")"
cat >"$desktop_file" <<EOF
[Desktop Entry]
Type=Application
Name=old-country
GenericName=Music Player
Comment=Your music library and YouTube Music
Exec=$bin
Icon=old-country
Terminal=false
Categories=AudioVideo;Audio;Player;
StartupWMClass=old-country
EOF
update-desktop-database "$HOME/.local/share/applications" 2>/dev/null || true
echo "Installed: $bin ($(du -h "$bin" | cut -f1)). Find \"old-country\" in the app launcher."
