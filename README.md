# Relay

A native, local Mac mixer for sharing music and an optional microphone into voice-chat, meeting, game, or recording apps that accept **BlackHole 2ch** as an input. Requires **macOS 26 or later**, Apple Silicon for the included build, and the existing **BlackHole 2ch** driver. No subscriptions, downloads, or paid libraries.

## Open and use

1. [Download Relay 1.1.0](https://github.com/vulnix0x4/relay/releases/tag/v1.1.0), extract the ZIP, and open Relay.app. Or build `build/Relay.app` from source. Open your music and receiving apps, then play a song.
2. Choose your **Music app**, the receiving app under **Share with**, and **Listen through**. Roblox, Discord, and Zoom have presets; other running desktop apps and browsers appear automatically. Choose **WH-1000XM5** for your headphones. Enable **Include my voice** and choose **fifine Microphone** if you want to talk. Bluetooth microphone inputs are intentionally excluded.
3. For your current “Roblox Music” default, first click **Use headphones for normal audio**. This is an explicit, one-time change to your normal output; the multi-output device itself stays intact. It prevents Stop from returning to a route that keeps sharing. Then click **Start sharing music**. Allow **System Audio Recording** when macOS asks. Microphone permission is requested only for voice capture or the optional BlackHole input check.
4. In the receiving app, choose **BlackHole 2ch** as the microphone/input, and your selected headphones as output. Relay does not change the other app’s selections. Do not select “Roblox Music” or BlackHole as its output.
5. Adjust **Music I hear** quietly, **Music others hear** independently, and **Call / game audio I hear** for the receiving app and other participants. **Master listening volume** affects only the music/game mix you hear.
6. Click **Stop sharing** before quitting. Closing Relay also stops sharing and restores that normal listening output.

Keep Spotify’s own volume at a consistent level; it affects the source feeding both music controls. Relay’s sliders range from silence to unity gain. Start low and have another player help set the sending level. The receiving app may suppress, compress, or gate music through its voice processing.

## Other apps and browser calls

- **Discord:** choose BlackHole 2ch under Voice & Video → Input Device. [Discord’s input-device guide](https://support.discord.com/hc/en-us/articles/360045138471-Discord-Voice-and-Video-Troubleshooting-Guide).
- **Zoom:** select BlackHole 2ch in its microphone dropdown and your headphones as speaker. [Zoom’s audio-device guide](https://support.zoom.com/hc/en/article?id=zm_kb&sysparm_article=KB0060836).
- **Other games, calls, and recorders:** select their running app in Relay, then choose BlackHole in that app if it supports choosing an audio input. Presets are convenient selections, not certifications that calls were tested.
- **No capturable output / recording only:** turn off **Advanced & recovery → Control call/game audio in Relay**. No receiver tap is created, so Roblox is never required. Music and microphone mixing still reach BlackHole. Call audio plays normally; its volume is controlled in that app, and Relay’s master controls only local music.
- **Browser calls:** select the browser as receiver and use a separate music app or browser. Taps capture whole applications, not separate tabs. Relay rejects the same app, parent/helper bundle pairs, and overlapping discovered audio processes as source and receiver.

“Share with” selects which app Relay monitors and displays instructions for; it does not make BlackHole private to that app. Any application already listening to BlackHole can receive the mix. Do not use the music-source app for a second call while sharing.

Apps using shared audio helper processes or protected content may not expose an isolated capture. Disable call monitoring if the receiver has no capturable audio; do not use a shared browser process as the music source. Actual call/recording reception must be confirmed in the receiving app.

## Test my setup

The guided flow separately checks music capture, local output signal plus your listening confirmation, microphone capture, and BlackHole input readback. **Enable BlackHole input check** requests microphone permission to measure the virtual input; it does not add the physical mic to your mix. It never generates a test tone.

A local signal does not prove the receiving app is transmitting or recording. The last checkbox is a manual confirmation from another person or a recording. A BlackHole readback can include another app that writes directly to BlackHole; keep other routing software from writing to it during the test.

## Disconnection and recovery

Relay temporarily makes a single-device aggregate named **Relay Listening** the default output. Its only destination is the device you chose. Existing “Roblox Music” and “Multi-Output Device” configurations are not edited. Starting requires the chosen listening device to be your normal default, so restoring it cannot unexpectedly choose a different speaker. The existing default microphone, sound-effects device, sample rates, and hardware volumes are preserved around output changes.

Device loss, sample-rate changes, or an external default-output change pause both mixes. Reconnect the same device, then **Stop sharing** and start again. Relay never automatically resumes or chooses the MacBook speakers. If you explicitly selected speakers, they are your intended output.

**Advanced & recovery → Restore normal audio** restores the saved output and removes Relay’s temporary route. A saved journal survives a forced quit. If the old output is unavailable, reconnect it and retry. A deliberate output change outside Relay is respected. Stopping releases process taps, so your music/call apps return to their ordinary app volumes on the chosen device; pause music first if you do not want normal-volume playback to resume.

For recovery without the window:

```sh
"build/Relay.app/Contents/MacOS/Relay" --restore
```

Do not run recovery while another Relay session is active. In a complete OS/driver failure, use macOS Sound settings to choose your desired output and microphone. Relay cannot guarantee other applications’ behavior or recover settings while it is not running. It never restarts audio services or reboots the Mac.

## Troubleshooting

- **No music signal:** play music in the selected app; check System Settings → Privacy & Security → Screen & System Audio Recording → System Audio Recording Only → Relay. Some protected content cannot be captured. Keep the selected receiving app and music app open before starting; if the receiver has no audio process, disable call/game monitoring.
- **Silent microphone:** check the Fifine hardware mute, Microphone permission, and MicLock. Relay detects that MicLock is installed, but cannot inspect its private blocking state or bypass it. A zero meter alone cannot establish which cause applies.
- **Headset-quality audio:** Relay never opens the Sony microphone. Other applications or macOS’s input selection can still activate it. Use the Fifine or BlackHole input in those applications.
- **Game too quiet or duplicated:** The receiving app’s output should be the selected physical device or the system default, not BlackHole or the old multi-output. Restart sharing after changing routing. The call/game slider covers all captured audio from the receiving app, including other participants.
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

The recovery test targets this Mac’s connected stereo Sony output. The permission test injects a denial and opens no microphone. See `TEST_REPORT.md` for what was actually checked and `ARCHITECTURE.md` for the routing implementation.

### Speaker / headphone volume (1.1.1)

The slider below Master listening controls the selected physical device’s own volume. This can explain quiet playback even when Relay’s mix sliders are at 100%. It affects all local sound on that device, not the BlackHole sharing mix. It reads the current setting on launch/reconnection; only moving the slider changes it. Your deliberate volume adjustment remains after sharing stops. Devices without a writable main volume control show instructions to use their physical volume buttons.

For an update staged in `build/Relay-next/Relay.app`, finish your current call, quit the old Relay normally, then open the new copy. Do not run both copies together.
