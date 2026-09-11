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
