# Verification report — September 10, 2026

Host: MacBook Pro, macOS 26.6.2, Xcode 26.6, Apple Silicon. BlackHole 2ch, Sony WH-1000XM5 output at 44.1 kHz, Fifine input at 48 kHz. Existing default output: Roblox Music; default input: BlackHole 2ch; sound-effects output: Sony.

## Real hardware and interface checks

- Built, ad-hoc signed, launched and inspected the SwiftUI app. Selectors, sliders, mute buttons, guided setup sheet, advanced diagnostics, and Stop were exercised through native accessibility controls.
- Created/read the Spotify and Roblox process taps and opened the actual Sony and BlackHole output IOProcs.
- Started Spotify playback with listening and sending muted: real music samples were present, outgoing and BlackHole return meters stayed zero.
- Enabled BlackHole input readback. At approximately 5% send gain, observed send peak 0.026 and BlackHole input peak 0.025 in one UI sample (callbacks are asynchronous). Listening output also reported a signal at a conservative level.
- Muted master listening: local output became silent while the real BlackHole signal remained present (observed send/return 0.034/0.034).
- Muted music sending: send and real BlackHole input both became zero while the source music meter remained active.
- Roblox’s capture meter remained zero during the isolated Spotify check. This verifies that Spotify was not being mistaken for Roblox in that check; it does not constitute a real game-play feedback test.
- Opened Fifine input with voice sending muted. Input produced nonzero samples (one peak 0.004); sending stayed zero. This establishes capture, not speech intelligibility.
- A final 20-second muted hardware smoke run opened both capture taps, both outputs, and optional BlackHole input readback, then restored the original routing. Spotify was paused for this run.
- Repeated Start/Stop and restored Roblox Music. A forced termination during a slow startup was recovered using the persisted journal and `--restore`.
- Real default-route integration tests verified aggregate creation/removal, normal restore, preserving an external output selection, retaining recovery when the saved output is deliberately made unavailable in the journal, and successful retry. Input and sound-effects selections remained unchanged after the final corrected tests.
- A recovery test caught macOS automatically selecting the Sony microphone when changing directly to Sony output. Output changes now preserve other defaults; the corrected test passed. Original BlackHole input was restored after that exploratory test.

## Automated tests

`Scripts/test.sh`: C audio tests under AddressSanitizer and UndefinedBehaviorSanitizer, plus five routing interruption-policy cases. All passed:

- Independent listening/sharing equations, every mute path, and master independence.
- Game-only input produces exactly zero sharing output; microphone-only input produces zero local output.
- Full-scale music plus voice remains below the output ceiling; invalid gains are sanitized.
- 106 seconds of simulated 44.1→48 kHz conversion with 120 ppm clock drift and ring wraparound, without buffer recovery.
- Empty-ring silence, bounded overrun recovery, and planar channel reading.
- 1 kHz sine amplitude through 48→44.1 kHz conversion and rejection of above-Nyquist content through 48→16 kHz conversion.
- Missing output, missing required mic, missing virtual device, and external output changes trigger pause; an excluded missing mic does not.

`Scripts/permission-test.sh`: injected microphone permission denial executes the production startup error branch, displays an actionable message, and makes no output change. Passed. This did not revoke real macOS permission.

`Scripts/recovery-test.sh`: actual Core Audio output changes and journal recovery as described above. Passed with connected Sony headphones.

## Still needs hands-on verification

- Listening through the Sony headphones: perceived loudness, distortion, latency, stereo quality, and long-session stability.
- Speaking into the Fifine and having another player judge the resulting voice/music balance.
- Actual Roblox game/other-player audio at independent listening levels, and a live voice session confirming no audible feedback.
- Physically disconnecting/reconnecting Sony and Fifine, sleep/wake, switching Bluetooth profiles, and prolonged clock drift under real Bluetooth load. Policy and pause logic are tested; physical events were not simulated as though they had occurred.
- Actual OS-denied or revoked System Audio Recording/Microphone permissions, and an actively blocking MicLock configuration. The denial branch was injected; no privacy permissions or MicLock settings were changed for that test.
- App/process restarts during active audio, protected Spotify content, other output formats, and another Mac.
- Confirmation that another Roblox player actually receives the virtual microphone. Local output/readback meters cannot prove this.

No drivers were installed. Existing multi-output devices were not edited, audio services were not restarted, and other apps were not closed. Spotify was briefly resumed for testing and left paused. Relay is delivered in standby with the original system routing restored. The first-use button explicitly establishes the Sony as normal output before a user starts sharing, so subsequent Stop operations cannot restore the old always-sharing multi-output route. Temporary test preference changes were restored; that first-use preference is left for the user to choose.

## Version 1.1 update — September 11, 2026

The repository is public and the app now selects a receiver rather than requiring Roblox. This section supplements the historical v1.0 hardware results above; it does not claim those tests were repeated for Discord or Zoom.

Validation for v1.1:

- Release build and native UI inspection of the Share with selector, destination-specific microphone instructions, and optional call/game monitoring.
- Nine app-isolation policy cases: distinct Spotify/Discord, Spotify/Zoom, and Spotify/Roblox selections; rejection of same-browser, parent/helper in both directions, Relay self-capture, and empty source.
- Production startup rejects identical source/receiver selections with monitoring both enabled and disabled, without changing the default output.
- Injected microphone denial still produces an actionable error without opening hardware or changing the default output.
- C DSP tests include no-call-tap mode: local music monitoring and the full music/voice sending mix remain correct with source 2 absent. Existing mute, clipping, resampling, drift, and feedback-isolation tests remain applicable.

The Sony currently reports a mono Bluetooth headset output, and the Fifine is not connected. Relay explains the headset-mode state and does not open the Bluetooth microphone or silently fall back to speakers. No new live call, hardware routing, or recovery test was performed on this changed setup. Live Discord/Zoom/other-app reception, browser helper behavior, and physical device reconnection remain hands-on verification items. Their published microphone-selection instructions were checked; that is distinct from a live integration test.

## 1.1.1 — physical listening volume

Built and ad-hoc signature verified at `build/Relay-next/Relay.app` without replacing or launching the active app. The existing DSP sanitizer and routing/app-isolation suites passed (zero failures). A separate read-only helper using the new hardware accessor verified the speakers at 40%, rejected nonfinite/out-of-range writes before any hardware mutation, and confirmed virtual/aggregate devices cannot use this control. The control writes only the selected physical device’s output scalar and never changes defaults or mixer gains. No valid hardware write or new UI interaction was exercised during the active call. Slider interaction and physical disconnect/reconnect still need hands-on verification after the call. Unsupported devices deliberately offer physical-button guidance instead of substituting software gain.

## 1.1.2 — sharing default and local update

Changed the initial music-send gain from 0.65 to 1.0. Release compilation and strict bundle signature verification passed. Installed the new bundle at the original local app path using an atomic directory swap, retaining the previous bundle and verifying the existing Relay PID remained alive. No app restart, audio-device writes, or live mixing changes were performed. The new UI and default take effect on the next launch; that launch was intentionally not tested during the existing session.

## 1.1.3 — Sony headphone volume detection

Live read-only inspection found WH-1000XM5 stereo output has no ordinary main scalar but does expose a writable virtual main volume (about 30%) and per-channel controls. Added virtual-main fallback while retaining physical-device UID/alive checks. The new accessor successfully read Sony volume; rejected invalid values and excluded virtual/aggregate devices without writing hardware. Release build and strict signature verification passed. Updated the usual app bundle without restarting the running process, retaining its old bundle without an .app extension to avoid duplicate app-search entries. Actual slider writes and UI validation remain deferred until the user ends their live sharing session.

## 1.1.4 — Louder sharing button

Added an optional +6 dB music-send boost before the existing output protector, with smoothed transitions and immediate music mute. Release build and strict signature verification passed. DSP tests under AddressSanitizer/UndefinedBehaviorSanitizer passed, including boost initially off, music-only gain increase with unchanged microphone contribution, unchanged listening mix, immediate mute, protected full-scale boosted mix, return to normal gain, and no game-to-send path. Existing resampling and routing/app-isolation tests also passed. Installed at the usual app path via atomic bundle swap without restarting the existing process. Live UI interaction and remote Roblox loudness remain unverified during the active session.

## 1.1.5 — Max sharing locks the music-send level

Release build and signature verification passed. A model-level test (`Tests/MaxSharingTests.swift`) verified enabling Max sharing sets 100%, effective music-send gain stays at unity even if the backing slider value is changed, other gains remain independent, mute overrides the lock, and disabling restores slider control. The test never started an audio session. Existing +6 dB DSP boost/protection code is unchanged from the tested 1.1.4 version. Updated the usual bundle without restarting the active process. Live UI and remote-player loudness remain untested during the current call.

## 1.2.0 — soundboard

Release build succeeded. Existing sanitizer DSP/routing/app-isolation suites passed. `Scripts/soundboard-test.sh` passed with the following coverage:

- C playback through both output callbacks, separate effect gains, local master isolation, music-mute/boost independence, immediate zero/stop, retrigger, natural completion, interruption with no replay, immutable clips, finite sample validation, and bounded registration.
- Independent 44.1/48 kHz cursors and downsampling rejection of a 12 kHz signal at 16 kHz output.
- Combined full-scale music/effects remain protected; stopping effects leaves music active.
- All 12 synthesized presets are finite, bounded, non-silent, and survive save/decode; 24 and 44.1 kHz mono imports convert to stereo 48 kHz. Invalid and overlong audio are rejected. A partial-file-read issue found by testing was fixed by reading until EOF before conversion.
- Library tests in temporary directories verified import/copy, persisted identity after reopening, and deletion without removing the source file.

No sound was played through live devices, no sharing session was started/stopped by tests, and remote Roblox audibility remains unverified. Actual pad/slider interaction, import-panel interaction, and physical reconnection require hands-on verification after the current session. Offscreen rendering does not substitute for live native UI verification.

Strict bundle signature verification passed. The local app was updated by atomic directory exchange; the existing PID remained alive, and its previous bundle was retained without an .app extension so app search gains no duplicate.

## 1.2.1 — collapsed soundboard and sourced local recordings

Changed initial disclosure state to collapsed and hid synthesized demos by default. Library tests passed for default hidden demos, explicitly showing/hiding them, persisted imported clips, and deleting imported copies without touching source files. Release build and strict signature verification passed. Twenty-one downloaded recordings were decoded, checked for non-silent finite audio and duration (0.29–4.39 seconds), converted to local CAF copies, and verified present after a fresh library load. Original Vine Boom came from Bluezone's direct WAV; other clips came from the 3kh0 soundboard collection. Source URLs/checksums remain local. No third-party audio is committed or bundled publicly. No recordings were played through live devices; audible identity and live pad interaction were not tested during the user's session. Current sharing is not restarted by the update.
