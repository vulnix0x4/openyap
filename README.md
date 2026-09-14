# OpenYap

A native, local Mac mixer for sharing music and an optional microphone into voice-chat, meeting, game, or recording apps that accept **BlackHole 2ch** as an input. Requires **macOS 26 or later**, Apple Silicon for the included build, and the existing **BlackHole 2ch** driver. No subscriptions, downloads, or paid libraries.

## Open and use

1. [Download OpenYap](https://github.com/vulnix0x4/openyap/releases/latest), extract the ZIP, and open OpenYap.app. Or build `build/OpenYap.app` from source. Open your music and receiving apps, then play a song.
2. Choose your **Music app**, the receiving app under **Share with**, and **Listen through**. Roblox, Discord, and Zoom have presets; other running desktop apps and browsers appear automatically. Choose **WH-1000XM5** for your headphones. Enable **Include my voice** and choose **fifine Microphone** if you want to talk. Bluetooth microphone inputs are intentionally excluded.
3. For your current “Roblox Music” default, first click **Use headphones for normal audio**. This is an explicit, one-time change to your normal output; the multi-output device itself stays intact. It prevents Stop from returning to a route that keeps sharing. Then click **Start sharing music**. Allow **System Audio Recording** when macOS asks. Microphone permission is requested only for voice capture or the optional BlackHole input check.
4. In the receiving app, choose **BlackHole 2ch** as the microphone/input, and your selected headphones as output. OpenYap does not change the other app’s selections. Do not select “Roblox Music” or BlackHole as its output.
5. Adjust **Music I hear** quietly, **Music others hear** independently, and **Call / game audio I hear** for the receiving app and other participants. **Master listening volume** affects only the music/game mix you hear.
6. Click **Stop sharing** before quitting. Closing OpenYap also stops sharing and restores that normal listening output.

Keep Spotify’s own volume at a consistent level; it affects the source feeding both music controls. OpenYap’s sliders range from silence to unity gain. Start low and have another player help set the sending level. The receiving app may suppress, compress, or gate music through its voice processing.

## Other apps and browser calls

- **Discord:** choose BlackHole 2ch under Voice & Video → Input Device. [Discord’s input-device guide](https://support.discord.com/hc/en-us/articles/360045138471-Discord-Voice-and-Video-Troubleshooting-Guide).
- **Zoom:** select BlackHole 2ch in its microphone dropdown and your headphones as speaker. [Zoom’s audio-device guide](https://support.zoom.com/hc/en/article?id=zm_kb&sysparm_article=KB0060836).
- **Other games, calls, and recorders:** select their running app in OpenYap, then choose BlackHole in that app if it supports choosing an audio input. Presets are convenient selections, not certifications that calls were tested.
- **No capturable output / recording only:** turn off **Advanced & recovery → Control call/game audio in OpenYap**. No receiver tap is created, so Roblox is never required. Music and microphone mixing still reach BlackHole. Call audio plays normally; its volume is controlled in that app, and OpenYap’s master controls only local music.
- **Browser calls:** select the browser as receiver and use a separate music app or browser. Taps capture whole applications, not separate tabs. OpenYap rejects the same app, parent/helper bundle pairs, and overlapping discovered audio processes as source and receiver.

“Share with” selects which app OpenYap monitors and displays instructions for; it does not make BlackHole private to that app. Any application already listening to BlackHole can receive the mix. Do not use the music-source app for a second call while sharing.

Apps using shared audio helper processes or protected content may not expose an isolated capture. Disable call monitoring if the receiver has no capturable audio; do not use a shared browser process as the music source. Actual call/recording reception must be confirmed in the receiving app.

## Test my setup

The guided flow separately checks music capture, local output signal plus your listening confirmation, microphone capture, and BlackHole input readback. **Enable BlackHole input check** requests microphone permission to measure the virtual input; it does not add the physical mic to your mix. It never generates a test tone.

A local signal does not prove the receiving app is transmitting or recording. The last checkbox is a manual confirmation from another person or a recording. A BlackHole readback can include another app that writes directly to BlackHole; keep other routing software from writing to it during the test.

## Disconnection and recovery

OpenYap temporarily makes a single-device aggregate named **OpenYap Listening** the default output. Its only destination is the device you chose. Existing “Roblox Music” and “Multi-Output Device” configurations are not edited. Starting requires the chosen listening device to be your normal default, so restoring it cannot unexpectedly choose a different speaker. The existing default microphone, sound-effects device, sample rates, and hardware volumes are preserved around output changes.

Device loss, sample-rate changes, or an external default-output change pause both mixes. Reconnect the same device, then **Stop sharing** and start again. OpenYap never automatically resumes or chooses the MacBook speakers. If you explicitly selected speakers, they are your intended output.

**Advanced & recovery → Restore normal audio** restores the saved output and removes OpenYap’s temporary route. A saved journal survives a forced quit. If the old output is unavailable, reconnect it and retry. A deliberate output change outside OpenYap is respected. Stopping releases process taps, so your music/call apps return to their ordinary app volumes on the chosen device; pause music first if you do not want normal-volume playback to resume.

For recovery without the window:

```sh
"build/OpenYap.app/Contents/MacOS/Relay" --restore
```

Do not run recovery while another OpenYap session is active. In a complete OS/driver failure, use macOS Sound settings to choose your desired output and microphone. OpenYap cannot guarantee other applications’ behavior or recover settings while it is not running. It never restarts audio services or reboots the Mac.

## Troubleshooting

- **No music signal:** play music in the selected app; check System Settings → Privacy & Security → Screen & System Audio Recording → System Audio Recording Only → OpenYap. Some protected content cannot be captured. Keep the selected receiving app and music app open before starting; if the receiver has no audio process, disable call/game monitoring.
- **Silent microphone:** check the Fifine hardware mute, Microphone permission, and MicLock. OpenYap detects that MicLock is installed, but cannot inspect its private blocking state or bypass it. A zero meter alone cannot establish which cause applies.
- **Headset-quality audio:** OpenYap never opens the Sony microphone. Other applications or macOS’s input selection can still activate it. Use the Fifine or BlackHole input in those applications.
- **Game too quiet or duplicated:** The receiving app’s output should be the selected physical device or the system default, not BlackHole or the old multi-output. Restart sharing after changing routing. The call/game slider covers all captured audio from the receiving app, including other participants.
- **First connection is slow:** macOS can take time to negotiate audio-recording permission. The interface provides Cancel connection; cleanup occurs after macOS returns its request. No mix is sent before startup completes.
- **After rebuilding:** this is an ad-hoc signed local development build, not a notarized distribution. macOS may request permissions again after binary changes. No paid signing account was used.

## Build and tests

Open `Package.swift` in Xcode to browse and edit. Use the build script to produce the runnable app bundle with its permission descriptions (do not launch the bare Swift package executable for normal use):

```sh
./Scripts/build.sh
./Scripts/test.sh
./Scripts/permission-test.sh
# Actual device/default-output integration test; close OpenYap first:
./Scripts/recovery-test.sh
```

The recovery test targets this Mac’s connected stereo Sony output. The permission test injects a denial and opens no microphone. See `TEST_REPORT.md` for what was actually checked and `ARCHITECTURE.md` for the routing implementation.

### Speaker / headphone volume (1.1.1)

The slider below Master listening controls the selected physical device’s own volume. This can explain quiet playback even when OpenYap’s mix sliders are at 100%. It affects all local sound on that device, not the BlackHole sharing mix. It reads the current setting on launch/reconnection; only moving the slider changes it. Your deliberate volume adjustment remains after sharing stops. Devices without a writable main volume control show instructions to use their physical volume buttons.

For an update staged in `build/OpenYap-next/OpenYap.app`, finish your current call, quit the old OpenYap normally, then open the new copy. Do not run both copies together.

Version 1.1.2 starts “Music others hear” at 100%. Listening and microphone defaults are unchanged; all live sliders remain adjustable.

### Louder sharing (1.1.4)

Click **Louder sharing (+6 dB)** below “Music others hear” to boost only shared music. Click again to turn it off. It starts off each launch, keeps the music mute effective, and leaves listening and microphone gains unchanged. The output ceiling stays active; loud material may sound more compressed, and Roblox’s own processing may reduce the boost.

### Max sharing (1.1.5)

**Max sharing** replaces Louder sharing. Turning it on sets shared music to 100%, locks that slider, and keeps the +6 dB boost active. Mute remains available. Turning it off unlocks the slider and removes the boost. Headphone, microphone, game, and master listening levels stay separate. The output ceiling and Roblox processing still apply; this does not guarantee maximum loudness at another player’s headphones.

### Soundboard (1.2.0)

Open **Soundboard**, start sharing, then click a pad. Twelve original synthesized effects are included: Deep boom, Chat ping, Air horn, Record scratch, Sad trombone, Crickets, Rimshot, Laser pew, Level up, Drama sting, Spring boing, and Alarm. These are original effects, not the exact Vine Boom or Discord recordings.

- **Import sounds…** adds your own mono/stereo audio clips up to 15 seconds (up to 48 custom pads). Files macOS can decode, such as WAV, AIFF, MP3, M4A, and CAF, can be selected; unsupported files show an error. Imported copies remain on your Mac in Application Support/Relay/Sounds and are never uploaded.
- **Effects I hear** controls local effects. **Music + effects others hear** controls the shared send level for both music and soundboard clips; both slider locations mirror the same setting. Shared mute and Max sharing apply to both. The listening master also controls local effects. Stop sounds stops just the effects.
- Clicking a new pad replaces the previous sound. **Stop sounds** stops just the effects, leaving music sharing running. An interruption cancels effects rather than replaying them after reconnection.
- Search filters pads. Right-click an imported pad to remove its local copy; your original file stays intact.

Effects use the existing selected output and BlackHole routes and the same output protection. There is no new driver or permission request. Pads are disabled until sharing starts. Bluetooth listening can arrive later than the wired/virtual output. The in-session immutable clip cache holds up to three minutes of unique decoded audio; a full cache shows a message, never automatically restarts sharing. Stop/start later clears it.

### Recordings-first soundboard (1.2.1)

The soundboard starts collapsed. Synthesized demo pads are now hidden by default; use **Show synthesized demo sounds** to reveal them. Imported recordings appear alphabetically, and their copies persist across app updates.

The local installation for this task includes 21 sourced recordings, including the original Vine Boom from [Bluezone Corporation](https://www.bluezone-corporation.com/blog/original-vine-boom-sound-effect) and Discord Notification, Bruh, Bonk, Roblox Oof, Emotional Damage, Taco Bell Bong, and other short clips from the [3kh0 soundboard collection](https://github.com/3kh0/soundboard/blob/main/sounds.json). Source URLs and SHA-256 hashes are saved locally in Application Support/Relay/Sources. These third-party recordings are not included in the Git repository or downloadable app bundle. Bluezone permits creative use of its original boom but excludes redistribution as a sound library; the other recordings are not represented as freely redistributable assets. Public installs can import their own files.

### More recordings and refresh (1.2.2)

Expanding the soundboard refreshes the local library, so clips added while OpenYap is open appear without another restart once this version is running. It still starts collapsed.

The local library now has 42 sourced recordings. New additions include Discord incoming/outgoing calls and voice-channel join/leave sounds from [lefuturiste/discord-sounds](https://github.com/lefuturiste/discord-sounds), plus Metal Pipe, Talking Ben Yes/No, Cartoon Slip, Minecraft Anvil, Doors Screech Psst, Clash Royale Heheheha, A Few Moments Later, and more from the previously credited 3kh0 collection. These files remain local, not part of the public app bundle.

**Door Knocking credit:** “Door Knocking Sound Effect” by Alexander / Orange Free Sounds, [source](https://orangefreesounds.com/door-knocking-sound-effect/), [CC BY 4.0](https://creativecommons.org/licenses/by/4.0/). The local playback copy is converted from MP3 to stereo 48 kHz CAF. No endorsement implied.


### Shared music/effects level (1.3.0)

Music and soundboard effects now share a single send gain and mute, shown in both places. **Max sharing** sets and locks that shared level at 100% and adds +6 dB to both before the output protector. Microphone gain remains separate, as do local music/effect listening levels. The shared control does not bypass Roblox processing. The local collection was trimmed to 28 clips based on feedback; removed files were moved out of the active library into a local Removed Sounds backup.

Local “Mafia Voting Suspense” cue: a 12-second excerpt of [Suspense Drum Sound Effect](https://orangefreesounds.com/suspense-drum-sound-effect/) by Alexander / Orange Free Sounds, [CC BY-NC 4.0](https://creativecommons.org/licenses/by-nc/4.0/). Converted to stereo 48 kHz CAF with a 100 ms fade-out. This personal, noncommercial-use recording stays in the local library and is not bundled in releases. Decode and persistence were verified without live playback.

### Local horror/knocking replacements

The local collection replaces the drum-based voting cue with **Horror Movie Suspense**, a 14-second excerpt (40–54 s) of [Slow-Rising Mysterious Tension](https://orangefreesounds.com/slow-rising-mysterious-tension-sound-effect/) by Alexander / Orange Free Sounds, [CC BY-NC 4.0](https://creativecommons.org/licenses/by-nc/4.0/). **Horror Drone** is a 14-second excerpt (5–19 s) of the same artist’s [Drone Sound](https://orangefreesounds.com/drone-sound/), [CC BY 4.0](https://creativecommons.org/licenses/by/4.0/). **Real Door Knocking** replaces the earlier knock with [Door Knocking by altfuture](https://freesound.org/people/altfuture/sounds/174640/), CC0, using the publicly provided high-quality MP3 preview. These playback copies use stereo 48 kHz CAF with 10 ms fade-ins and 100 ms fade-outs. No endorsement implied.

Metal Pipe, Minecraft Anvil, MLG Airhorn, Nope, Rimshot, Taco Bell Bong, Wilhelm Scream, the previous knock, and the drum suspense cue were moved to a local Removed Sounds backup. The active local collection has 23 clips. All third-party recordings stay local and are not bundled in the repository or app releases. New files passed decode/persistence checks without live playback; subjective sound quality and Roblox reception still need listening verification. Collapse and expand Soundboard to refresh its pads.

### Reaction groups (1.3.1)

Soundboard groups pads into Good / Happy, Bad / Awkward, Funny / Hype, Suspense / Scary, and Calls / Alerts. Unrecognized imports appear under Other sounds. Search filters across all groups, empty groups are hidden, and the board still starts collapsed. Existing send, mute, and Max sharing controls are unchanged.

The local pad **Suspense** replaces both earlier horror cues with a 14-second musical excerpt (24–38 s) from [Short Tension 1 by David Fesliyan](https://www.fesliyanstudios.com/royalty-free-music/download/short-tension-1/140). Credit to [Fesliyan Studios](https://www.fesliyanstudios.com) for the background music. It is used locally under the site's free noncommercial policy; commercial use requires a license. This audio is not distributed with OpenYap. Stereo 48 kHz conversion and short edge fades were applied. Decode and persistence were checked without playback.

Validation: release build and strict code-signature verification passed; all current local clips map to a reaction group and unknown imports fall back to Other sounds. UI interaction and subjective listening need hands-on verification. The app on disk was updated without restarting the running process; new grouping appears on the next normal launch.

### OpenYap (1.4.0)

Formerly Relay. OpenYap shares music and soundboard effects into games and voice apps that support selecting BlackHole 2ch as their microphone. Compatibility depends on the receiving app; its voice processing can alter music.

The bundle identifier, internal executable name, settings keys, and Application Support/Relay library remain unchanged so existing permissions, recovery data, and imported sounds carry forward. The visible app is now OpenYap.app.
