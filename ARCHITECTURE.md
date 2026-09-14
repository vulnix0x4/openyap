# Audio architecture

## Choice and platform basis

Apple’s Core Audio process taps capture named processes without ScreenCaptureKit’s screen/display capture machinery. OpenYap uses CATapDescription stereo mixdown, an explicit nonempty process allowlist, private taps, and tap-only private aggregate inputs. `muteBehavior = .muted` suppresses original playback while OpenYap controls monitoring. macOS 26 bundle IDs and process restoration keep the selected app associated with its tap across restarts. There is no global/system-mix tap.

References verified against the installed Xcode 26.6 SDK and Apple’s documentation:

- https://developer.apple.com/documentation/CoreAudio/capturing-system-audio-with-core-audio-taps
- https://developer.apple.com/documentation/coreaudio/catapdescription
- https://developer.apple.com/documentation/coreaudio/catapmutebehavior
- Installed `CATapDescription.h`, `AudioHardware.h`, and `AudioHardwareBase.h` specify bundle restoration, aggregate tap lists, stream usage, and ownership of returned CF objects.

BlackHole is reused as the public virtual input seen by the receiving app. OpenYap installs no driver and does not rename it. The “app’s virtual microphone” is therefore labeled **BlackHole 2ch**, not an invented OpenYap microphone device.

## Signal paths

```
Spotify allowlist → process tap → music rings ┬→ local music gain ─┐
                                            │                   ├→ master → protection → Sony output
Receiver allowlist → process tap → game ring ───┼→ game gain ────────┘
                                            │
                                            └→ music send gain ─┐
Fifine hardware input → mono voice ring → voice gain ────────────┴→ protection → BlackHole output

BlackHole input → optional readback peak meter only (never connected to either mixer)
```

The game source is never enqueued into the sharing rings. The microphone is never enqueued into monitoring rings. A virtual or aggregate device cannot be selected as a physical microphone or listening destination. Bluetooth inputs cannot be selected. The send bus has no master-listening control.

Only these two allowlisted app streams are included in OpenYap’s listening volume. Notification sounds and other apps remain ordinary system audio and are not controlled by OpenYap’s master.

## Real-time engine

`AudioCore.c` owns HAL device IO callbacks and preallocated single-producer/single-consumer rings. Swift never executes in an audio callback. Callbacks do no allocation, logging, file IO, locks, or UI work. C11 atomics exchange gains, meter values, and ring positions.

Each output consumes independent source rings at its device’s native clock. A normalized 32-tap, 256-phase windowed-sinc resampler supports 8–192 kHz Float32 hardware formats, interleaved or planar. Occupancy feedback trims the conversion ratio by at most 0.5% for clock drift. About 45 ms is buffered per source/consumer; serious underruns re-prime to silence and excess backlog skips to recent audio. Bluetooth adds its own unavoidable transport latency; no promise of zero latency or tight audio/video synchronization is made.

Gains are limited to [0,1], with smoothed changes and immediate exact-zero mute. A soft saturation knee starts at 0.8 and asymptotically bounds each output channel below 0.98 full scale (about −0.18 dBFS). This prevents numeric clipping but can audibly compress very hot music-plus-voice mixes. Nonfinite input/gains are sanitized. There is no automatic gain boost or microphone voice processing.

HAL stream usage disables unused streams, preventing an output-only endpoint from activating input. A separate BlackHole input IOProc exists only after the user enables the optional check. Its samples go exclusively to a meter.

## Lifecycle and settings

The interface is SwiftUI with an observable main-actor session model. Process tap ownership is scoped; teardown stops/destroys IOProcs before destroying aggregates and taps. Potentially slow IOProc startup runs off the main actor. Cancellation waits for macOS to complete an outstanding HAL request and never enables the mix afterward.

A one-time explicit setup action establishes the selected physical device as the user’s normal output. Sessions refuse an unsafe baseline (virtual/aggregate output or a different physical destination): restoring the old Roblox Music multi-output would otherwise keep sending system sound into BlackHole after Stop. This explicit normal-output preference is distinct from temporary session changes; the existing multi-output device remains unchanged.

Before a session default-output mutation, the prior UID and temporary-route UID are durably journaled in UserDefaults. Output changes preserve the contemporaneous default input and sound-effects device, including macOS’s automatic input-selection side effects. Recovery restores a still-owned default route; an external deliberate change is retained. Missing previous outputs keep the journal and safe route for a later retry.

The temporary public “OpenYap Listening” aggregate contains exactly one selected output device. It prevents OpenYap from intentionally substituting built-in speakers. Device-alive and sample-rate HAL listeners immediately pause the C mixer; UI polling also checks device identity and default-output changes. Reconnection requires explicit stop/start and UID re-resolution. Physical reconnection remains a hands-on test item.

Permissions: NSAudioCaptureUsageDescription for system audio; NSMicrophoneUsageDescription only when voice or the optional virtual-input measurement is requested. No screen frames, Accessibility permission, Apple Events automation, network service, telemetry, or audio recordings are used by the app. MicLock installation is observable; its proprietary block state is not.

## Scope and limitations

The included executable targets this Apple Silicon Mac and macOS 26+. It is ad-hoc signed, not notarized. The package builds with Xcode’s Swift 6 toolchain in Swift 5 language compatibility mode for the C callback/pointer boundary. No third-party dependencies are linked.

The receiving app’s microphone/output selection and voice processing are external to OpenYap. Protected music, OS privacy denial, other apps writing to BlackHole, driver failures, and Bluetooth transport quality can affect results. Software isolation prevents OpenYap from routing game audio back into the mic; using loud speakers can still cause physical acoustic pickup through a real microphone.

## Version 1.1: selectable receivers

The destination bundle is selected by the user rather than hardcoded to Roblox. The same C source slot 2 remains a listening-only bus: it can carry Discord, Zoom, another game, or any other individually capturable selected receiver. That slot is structurally excluded from the send bus.

With call monitoring disabled, source slot 2 has no IOProc and a zero sample rate; the music and optional microphone still mix into BlackHole. The receiver plays directly to its selected output, so OpenYap disables the call/game slider and explains that master listening no longer controls that app. No receiver process is required to start in this mode.

A shared AppRoutingPolicy rejects identical, ancestor/helper, empty, and OpenYap-owned bundle selections. Startup also compares discovered process IDs for overlap. Music and receiver pickers filter conflicting entries. Browser tabs cannot be isolated by these process taps; music and calls must use different apps. Receiver selection does not restrict which external applications can read BlackHole.

## Soundboard

The control thread publishes bounded, immutable stereo 48 kHz clips into a 64-slot C bank (15 seconds/clip, three minutes total decoded audio). Only output callbacks read that bank; no recording source can write to it. Decoding and synthesis run off the main/audio threads. Imported files are copied as CAF, and an explicit read loop handles partial AVAudioFile reads before AVAudioConverter performs format conversion. Publication uses atomics; clip memory is freed only after IOProcs stop. Each physical/virtual output owns its playback cursor and a 32-tap, 256-phase windowed-sinc converter for its hardware rate. A command generation restarts/replaces playback; a stop command silences it, and device interruption consumes the clip rather than replaying on resume. Effects retain a separate local gain. On the send path, music and effects are summed and multiplied by the same smoothed gain (including Max sharing); microphone is added with its separate gain, then the output protector applies. Shared mute immediately zeros both music and effects. The listening master applies only to the local path. Pads require an active session. New sound effects do not capture system audio, change defaults, or activate a Bluetooth microphone.
