# OpenYap for Windows

The Windows version of OpenYap: share music, your voice, a soundboard, and typed speech into games, calls, and recorders through a virtual microphone, at levels independent of what you hear. Requires **Windows 10 version 2004 (build 20348) or later, 64-bit**, and a virtual audio cable such as the free **VB-CABLE**. One small `.exe`, no installer, no runtime to download (it uses the .NET Framework 4.8 built into Windows), no accounts, no network access.

## Set up once

1. [Download OpenYap-Windows.zip](https://github.com/vulnix0x4/openyap/releases/latest/download/OpenYap-Windows.zip), extract it, and keep `OpenYap.exe` anywhere you like. Or build it from source (below).
2. Install **VB-CABLE** from [vb-audio.com/Cable](https://vb-audio.com/Cable/) (free, donationware) and restart if asked. It adds two devices: **CABLE Input** (OpenYap sends here) and **CABLE Output** (other apps use it as a microphone). OpenYap never installs drivers itself.
3. Recommended: in **Sound settings → More sound settings → Communications**, choose **Do nothing**. Otherwise Windows turns your music app down whenever a call starts, which also lowers what others hear. OpenYap shows a reminder until this is set.
4. If your Windows default *call* microphone is a Bluetooth headset (such as WH-1000XM5 Headset), apps that use it force your headphones into low-quality headset mode. Make your separate USB microphone the default communications device. OpenYap warns about this; it does not change it for you.

## Use

1. Open `OpenYap.exe`. Play a song in your music app (Spotify, a second browser, anything with sound).
2. Choose your **Music app**, the app under **Share with** (Roblox, Discord, Zoom have presets; other running apps appear automatically), **Listen through** (your headphones), and **Virtual microphone** (CABLE Input). Enable **Include my voice** and pick a microphone if you want to talk. Bluetooth microphones are excluded.
3. Click **Start sharing music**.
4. In the receiving app, choose **CABLE Output (VB-Audio Virtual Cable)** as the microphone/input. If an app or game has no microphone picker, set CABLE Output as the Windows default recording device while you play, and set it back afterwards. Never choose CABLE Input as anything's *output*.
5. Adjust **Music I hear** and **Music + effects others hear** independently, **Call / game audio I hear**, **My voice to others**, and **Master listening volume**. **Max sharing** locks the shared level at 100% with +6 dB.
6. Click **Stop sharing** (or Ctrl+.) before quitting. Closing the window also stops and restores.

## How it differs from the Mac version

macOS can capture an app and silence its normal playback at the same time, so the Mac version re-plays your music to you. Windows cannot: [application loopback capture](https://learn.microsoft.com/windows/win32/coreaudio/loopback-recording) records an app *after* its own volume in the Windows Volume Mixer. So on Windows:

- **Music I hear** and **Call / game audio I hear** set those apps' own Windows volumes while you share. Your music and game keep playing through their normal output.
- **Music others hear** divides the music app's volume back out of the captured audio, so lowering what you hear never lowers what others hear. At 0% the music app sits at −74 dB rather than true silence, because Windows would otherwise send OpenYap nothing.
- Volume changes are guarded: when the music app gets quieter, the shared level can dip for up to 150 ms, but never jumps louder.
- Before changing any app's volume, OpenYap saves the original to `%APPDATA%\OpenYap\volume-journal.txt`. **Stop**, closing the window, **Advanced & recovery → Restore normal audio**, a later launch, and `OpenYap.exe --restore` all put those volumes back. An app that was closed is restored automatically the next time it plays audio while OpenYap is open.
- OpenYap never changes your default output, default microphone, or another app's device choices, except when you click **Use headphones for normal audio**, which only appears if your default output is a virtual cable (which would otherwise share everything you hear).
- If you change the music app's volume in the Windows Volume Mixer during a session, OpenYap puts it back to your "Music I hear" level on its next update.

Captures select whole apps (each top-level process and all its child processes), not browser tabs. For browser calls, pick the browser as the receiver and play music in a different app.

## Soundboard and typed speech

Same as the Mac version: open **Soundboard**, start sharing, then click a pad. **Import sounds…** accepts mono/stereo clips up to 15 seconds in any format Windows can decode (WAV, MP3, M4A/AAC, WMA, FLAC). Copies are stored as 48 kHz WAV in `%APPDATA%\OpenYap\Sounds` and never uploaded. Mac library clips (`.caf`) are not read; import the originals instead. **Show synthesized demo sounds** reveals the twelve built-in effects. Right-click an imported pad to remove it.

**Say something** uses the voices installed in Windows (Settings → Time & language → Speech). Deep and Tiny shift pitch by about −23% and +24%. Speech is rendered in memory, never to a speaker, and is limited to 220 characters and 15 seconds.

## Troubleshooting

- **No music signal:** play audio in the selected app. If the app was restarted after sharing began, stop and start again. DRM-protected playback may be silent to capture.
- **Others hear nothing:** the receiving app must use CABLE Output as its microphone. Use **Test my setup → Enable virtual input check** to read back the cable. Windows must allow desktop apps to use the microphone for that readback (Settings → Privacy & security → Microphone).
- **Silent microphone:** check its mute switch and Windows microphone privacy settings.
- **Music too quiet for others:** turn off communications ducking (above), raise **Music + effects others hear**, or use **Max sharing**. Receiving apps may still compress or gate music in their voice processing.
- **Paused safely:** a device was disconnected, or the default output became a virtual cable. Reconnect, then stop and start again. OpenYap never falls back to other speakers automatically.
- **App volumes look wrong after a crash:** run `OpenYap.exe --restore`, or open OpenYap and click **Restore normal audio**.
- **SmartScreen warning:** the build is not code-signed. Choose **More info → Run anyway**, or build it yourself (below).

## Build and test

No Visual Studio or SDK is needed; the build uses the C# compiler included with Windows.

```powershell
powershell -ExecutionPolicy Bypass -File Windows\Scripts\build.ps1     # -> Windows\build\OpenYap.exe
powershell -ExecutionPolicy Bypass -File Windows\Scripts\test.ps1      # DSP self-test, decoding, speech
# Real devices, inaudible -80 dBFS test tone, touches only a test helper's volume:
powershell -ExecutionPolicy Bypass -File Windows\Scripts\hardware-test.ps1 [-Virtual <render device id>]
powershell -ExecutionPolicy Bypass -File Windows\Scripts\recovery-test.ps1 [-Virtual <render device id>]
```

`OpenYap.exe --devices` lists audio endpoint IDs, `--apps` lists capturable apps with their current volumes, and `--restore` restores journaled app volumes. Without VB-CABLE, pass `-Virtual` with an output nobody listens to; the hardware test then reads the send back with endpoint loopback instead of the cable's input.

Sources are in `Windows\Sources`: `Mixer.cs` is the real-time engine (a port of `AudioCore.c`), `Streams.cs` the WASAPI threads, `SessionModel.cs` the session logic, and `MainForm.cs` the interface.
