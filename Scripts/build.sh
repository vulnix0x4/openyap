#!/bin/zsh
set -euo pipefail
cd "${0:A:h:h}"
swift build -c release
app="${RELAY_APP_OUTPUT:-$PWD/build/Relay.app}"
mkdir -p "$app/Contents/MacOS" "$app/Contents/Resources"
swift Scripts/icon.swift build/Relay.iconset
iconutil -c icns build/Relay.iconset -o "$app/Contents/Resources/Relay.icns"
cp .build/release/Relay "$app/Contents/MacOS/Relay"
cat > "$app/Contents/Info.plist" <<'PLIST'
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0"><dict>
<key>CFBundleExecutable</key><string>Relay</string>
<key>CFBundleIdentifier</key><string>local.relay.MusicShare</string>
<key>CFBundleIconFile</key><string>Relay</string>
<key>CFBundleName</key><string>Relay</string>
<key>CFBundleDisplayName</key><string>Relay</string>
<key>CFBundlePackageType</key><string>APPL</string>
<key>CFBundleShortVersionString</key><string>1.1.3</string>
<key>CFBundleVersion</key><string>5</string>
<key>LSMinimumSystemVersion</key><string>26.0</string>
<key>NSAudioCaptureUsageDescription</key><string>Relay separates music and call audio so you can listen and share at independent volumes. Audio stays on this Mac.</string>
<key>NSMicrophoneUsageDescription</key><string>Relay uses your chosen microphone only when you include your voice. The setup test can also measure BlackHole’s virtual input.</string>
<key>NSHighResolutionCapable</key><true/>
</dict></plist>
PLIST
codesign --force --sign - "$app"
echo "Built $app"
