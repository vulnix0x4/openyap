# Relay

A native, local Mac mixer for sharing Spotify into Roblox voice chat. Requires **macOS 26 or later**, Apple Silicon for the included build, and the existing **BlackHole 2ch** driver. No subscriptions, downloads, or paid libraries.

## Open and use

1. Open `build/Relay.app`. Keep Spotify and Roblox open. Play a song in Spotify.
2. Choose **Spotify** and **WH-1000XM5**. Enable **Include my voice** and choose **fifine Microphone** if you want to talk. Bluetooth microphone inputs are intentionally excluded.
3. For your current “Roblox Music” default, first click **Use headphones for normal audio**. This is an explicit, one-time change to your normal output; the multi-output device itself stays intact. It prevents Stop from returning to a route that keeps sharing. Then click **Start sharing music**. Allow **System Audio Recording** when macOS asks. Microphone permission is requested only for voice capture or the optional BlackHole input check.
4. In Roblox, choose **BlackHole 2ch** as the microphone/input, and **WH-1000XM5** as output. Relay does not change Roblox’s own selections. Do not select “Roblox Music” or BlackHole as Roblox’s output.
5. Adjust **Music I hear** quietly, **Music others hear** independently, and **Game audio I hear** for the game and other players. **Master listening volume** affects only the music/game mix you hear.
6. Click **Stop sharing** before quitting. Closing Relay also stops sharing and restores that normal listening output.

Keep Spotify’s own volume at a consistent level; it affects the source feeding both music controls. Relay’s sliders range from silence to unity gain. Start low and have another player help set the sending level. Roblox may suppress, compress, or gate music through its voice processing.

## Test my setup

The guided flow separately checks music capture, local output signal plus your listening confirmation, microphone capture, and BlackHole input readback. **Enable BlackHole input check** requests microphone permission to measure the virtual input; it does not add the physical mic to your mix. It never generates a test tone.

A local signal does not prove Roblox is transmitting. The last checkbox is a manual confirmation from another player. A BlackHole readback can include another app that writes directly to BlackHole; keep other routing software from writing to it during the test.

## Disconnection and recovery

Relay temporarily makes a single-device aggregate named **Relay Listening** the default output. Its only destination is the device you chose. Existing “Roblox Music” and “Multi-Output Device” configurations are not edited. Starting requires the chosen listening device to be your normal default, so restoring it cannot unexpectedly choose a different speaker. The existing default microphone, sound-effects device, sample rates, and hardware volumes are preserved around output changes.

Device loss, sample-rate changes, or an external default-output change pause both mixes. Reconnect the same device, then **Stop sharing** and start again. Relay never automatically resumes or chooses the MacBook speakers. If you explicitly selected speakers, they are your intended output.

**Advanced & recovery → Restore normal audio** restores the saved output and removes Relay’s temporary route. A saved journal survives a forced quit. If the old output is unavailable, reconnect it and retry. A deliberate output change outside Relay is respected. Stopping releases process taps, so Spotify/Roblox return to their ordinary app volumes on the chosen device; pause music first if you do not want normal-volume playback to resume.

For recovery without the window:

```sh
"build/Relay.app/Contents/MacOS/Relay" --restore
```

Do not run recovery while another Relay session is active. In a complete OS/driver failure, use macOS Sound settings to choose your desired output and microphone. Relay cannot guarantee other applications’ behavior or recover settings while it is not running. It never restarts audio services or reboots the Mac.

## Troubleshooting

- **No music signal:** play music in the selected app; check System Settings → Privacy & Security → Screen & System Audio Recording → System Audio Recording Only → Relay. Some protected content cannot be captured. Keep Roblox and your music app open before starting.
- **Silent microphone:** check the Fifine hardware mute, Microphone permission, and MicLock. Relay detects that MicLock is installed, but cannot inspect its private blocking state or bypass it. A zero meter alone cannot establish which cause applies.
- **Headset-quality audio:** Relay never opens the Sony microphone. Other applications or macOS’s input selection can still activate it. Use the Fifine or BlackHole input in those applications.
- **Game too quiet or duplicated:** Roblox’s output should be the selected physical device or the system default, not BlackHole or the old multi-output. Restart sharing after changing routing. The game slider covers all audio captured from Roblox, including other players.
- **First connection is slow:** macOS can take time to negotiate audio-recording permission. The interface provides Cancel connection; cleanup occurs after macOS returns its request. No mix is sent before startup completes.
- **After rebuilding:** this is an ad-hoc signed local development build, not a notarized distribution. macOS may request permissions again after binary changes. No paid signing account was used.

## Build and tests

Open `Package.swift` in Xcode to browse and edit. Use the build script to produce the runnable app bundle with its permission descriptions (do not launch the bare Swift package executable for normal use):

```sh
./Scripts/build.sh
./Scripts/test.sh
./Scripts/permission-test.sh
# Actual device/default-output integration test; close Relay first:
./Scripts/recovery-test.sh
```

The last two tests target this Mac’s connected Sony/Fifine/BlackHole setup. See `TEST_REPORT.md` for what was actually checked and `ARCHITECTURE.md` for the routing implementation.
