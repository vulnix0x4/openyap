import SwiftUI

struct ContentView: View {
  @Bindable var model: SessionModel
  var body: some View {
    ScrollView {
      VStack(alignment: .leading, spacing: 18) {
        HStack(spacing: 12) {
          Image(systemName: "waveform.path").font(.largeTitle).foregroundStyle(.mint)
            .accessibilityHidden(true)
          VStack(alignment: .leading, spacing: 2) {
            Text("Relay").font(.largeTitle.bold())
            Text("Your music. Their soundtrack.").foregroundStyle(.secondary)
          }
          Spacer()
          Label(
            model.interrupted ? "Paused" : model.active ? "Live" : "Standby",
            systemImage: model.active && !model.interrupted
              ? "dot.radiowaves.left.and.right" : "circle.dotted"
          )
          .font(.callout.weight(.semibold)).foregroundStyle(model.active ? Color.mint : .secondary)
          .padding(10).background(.quaternary, in: Capsule())
        }
        VStack(spacing: 12) {
          Picker("Music app", selection: $model.musicBundle) {
            ForEach(model.musicApps) { Text($0.name).tag($0.id) }
          }
          Picker("Share with", selection: $model.destinationBundle) {
            ForEach(model.destinationApps) { Text($0.name).tag($0.id) }
          }
          Picker("Listen through", selection: $model.outputUID) {
            Text("Choose a device…").tag("")
            ForEach(model.outputs) { Text($0.name).tag($0.uid) }
          }
          Toggle("Include my voice", isOn: $model.includeVoice)
          if model.includeVoice {
            Picker("Microphone", selection: $model.micUID) {
              Text("Choose a microphone…").tag("")
              ForEach(model.microphones) { Text($0.name).tag($0.uid) }
            }
          }
        }.frame(maxWidth: .infinity).disabled(model.active || model.busy).padding(16).background(
          .quaternary.opacity(0.5), in: RoundedRectangle(cornerRadius: 16))
        if model.devices.contains(where: { $0.isBluetooth && $0.outputs == 1 }) {
          Text(
            "Bluetooth headset mode detected. Another app may be using the headphone microphone. Select a separate microphone in that app to restore stereo playback; Relay will not switch it for you."
          ).font(.caption).foregroundStyle(.orange).fixedSize(horizontal: false, vertical: true)
        }
        Text(
          "Choose BlackHole 2ch in the receiving app. For web calls, choose the browser and play music in a different app; taps capture whole apps, not individual tabs."
        ).font(.caption).foregroundStyle(.secondary).fixedSize(horizontal: false, vertical: true)
        if !model.monitorDestination {
          Text(
            "Call audio plays normally through your selected device. Adjust it in the call app; Relay’s master controls only your music."
          ).font(.caption).foregroundStyle(.secondary).fixedSize(horizontal: false, vertical: true)
        }
        if model.normalOutputNeedsSetup && !model.active && !model.recoveryPending {
          VStack(alignment: .leading, spacing: 7) {
            Label("One-time setup", systemImage: "arrow.triangle.branch").font(.headline)
            Text(
              "Make your chosen listening device the normal output first. This prevents Stop from returning to a different device or an always-sharing multi-output route. Existing devices stay intact."
            ).font(.caption).foregroundStyle(.secondary).fixedSize(
              horizontal: false, vertical: true)
            Button("Use headphones for normal audio", action: model.useSelectedAsNormalOutput)
              .disabled(model.selectedOutput == nil || model.busy)
          }.padding(12).frame(maxWidth: .infinity, alignment: .leading).background(
            Color.orange.opacity(0.1), in: RoundedRectangle(cornerRadius: 12))
        }
        HStack(spacing: 16) {
          VStack(alignment: .leading, spacing: 16) {
            Label("JUST FOR YOU", systemImage: "headphones").font(.caption.weight(.bold))
              .foregroundStyle(.secondary)
            MixerStrip(
              title: "Music I hear", symbol: "music.note", gain: $model.gains[0],
              muted: $model.muted[0],
              level: model.meters[0] * model.gains[0] * (model.muted[0] ? 0 : 1), tint: .mint)
            MixerStrip(
              title: "Call / game audio I hear", symbol: "gamecontroller", gain: $model.gains[3],
              muted: $model.muted[3],
              level: model.meters[2] * model.gains[3] * (model.muted[3] ? 0 : 1), tint: .mint
            ).disabled(!model.monitorDestination)
          }
          Divider()
          VStack(alignment: .leading, spacing: 16) {
            Label("TO OTHERS", systemImage: "person.wave.2").font(.caption.weight(.bold))
              .foregroundStyle(.secondary)
            MixerStrip(
              title: "Music + effects others hear", symbol: "music.note", gain: $model.gains[1],
              muted: $model.muted[1],
              level: model.meters[0] * model.gains[1] * (model.muted[1] ? 0 : 1), tint: .orange,
              sliderLocked: model.maxSharing)
            Toggle(isOn: $model.maxSharing) {
              Label(model.maxSharing ? "Max sharing · ON" : "Max sharing",
                systemImage: "speaker.wave.3.fill")
            }
            .toggleStyle(.button).tint(.orange)
            .help("Locks shared music and effects at 100% with +6 dB boost. Mute still works. Roblox may limit loudness.")
            .accessibilityLabel("Max sharing, locks music and effects at full volume with boost")
            MixerStrip(
              title: "My voice to others", symbol: "mic", gain: $model.gains[2],
              muted: $model.muted[2],
              level: model.includeVoice
                ? model.meters[1] * model.gains[2] * (model.muted[2] ? 0 : 1) : 0, tint: .orange
            ).disabled(!model.includeVoice)
          }
        }.fixedSize(horizontal: false, vertical: true)
        SoundboardView(model: model)
        VStack(alignment: .leading, spacing: 6) {
          MixerStrip(
            title: "Master listening volume", symbol: "speaker.wave.2", gain: $model.gains[4],
            muted: $model.muted[4], level: model.meters[3], tint: .mint)
          Text(
            model.monitorDestination
              ? "Controls your music + call/game mix. Never changes what others hear."
              : "Controls your music only. Call audio stays controlled by the call app."
          ).font(.caption)
            .foregroundStyle(.secondary)
        }.padding(14).background(.quaternary.opacity(0.5), in: RoundedRectangle(cornerRadius: 12))
        if let output = model.selectedOutput {
          DeviceVolumeView(device: output).id(output.uid)
            .padding(14).background(.quaternary.opacity(0.5), in: RoundedRectangle(cornerRadius: 12))
        }
        HStack {
          Image(systemName: "arrow.turn.down.right").foregroundStyle(.orange)
          VStack(alignment: .leading, spacing: 3) {
            Text("Microphone for \(model.destinationName): BlackHole 2ch").font(.headline)
            Text(
              "Select this in the receiving app’s input settings. Its voice processing may alter music."
            ).font(
              .caption
            ).foregroundStyle(.secondary)
          }
          Spacer()
          LevelMeter(level: model.meters[4], tint: .orange).frame(width: 60)
        }
        VStack(alignment: .leading, spacing: 5) {
          Text(model.status).font(.headline)
          Text(model.detail).font(.callout).foregroundStyle(.secondary).fixedSize(
            horizontal: false, vertical: true)
        }.frame(maxWidth: .infinity, alignment: .leading)
        HStack {
          Button("Test my setup", systemImage: "checkmark.circle", action: model.testSetup)
          Spacer()
          if model.active || model.busy {
            Button(
              model.busy ? "Cancel connection" : "Stop sharing", systemImage: "stop.fill",
              role: .destructive, action: model.stop
            ).buttonStyle(.borderedProminent).tint(.red).controlSize(.large)
          } else {
            Button {
              Task { await model.start() }
            } label: {
              Label(model.busy ? "Connecting…" : "Start sharing music", systemImage: "play.fill")
            }.buttonStyle(.plain).font(.headline).padding(.horizontal, 18).padding(.vertical, 12)
              .background(Color.mint, in: RoundedRectangle(cornerRadius: 10)).foregroundStyle(
                .black
              )
              .disabled(model.busy || model.recoveryPending || model.normalOutputNeedsSetup)
              .opacity(
                model.busy || model.recoveryPending || model.normalOutputNeedsSetup ? 0.45 : 1)
          }
        }
        DisclosureGroup("Advanced & recovery", isExpanded: $model.advanced) {
          VStack(alignment: .leading, spacing: 8) {
            Toggle("Control call/game audio in Relay", isOn: $model.monitorDestination).disabled(
              model.active || model.busy)
            Text(
              "Turn this off for recorders or apps without a capturable output. Music and voice sharing still work; set call playback volume inside that app."
            ).font(.caption).fixedSize(horizontal: false, vertical: true)
            Text(
              "Music → listening + BlackHole\nSelected microphone → BlackHole only\n\(model.destinationName) → \(model.monitorDestination ? "listening only; never the sharing bus" : "normal app playback; not captured")"
            ).font(.caption.monospaced())
            Text(
              "Relay temporarily uses Relay Listening, a fixed route to your chosen device, as the default output. Stop restores the previous output. Existing multi-output devices, and input selection are untouched. Device volume changes only when you move the speaker / headphone slider; that intentional adjustment is kept."
            ).font(.caption)
            Text(
              "Live peaks: music \(model.meters[0], specifier: "%.3f") · mic \(model.meters[1], specifier: "%.3f") · call/game \(model.meters[2], specifier: "%.3f") · send \(model.meters[4], specifier: "%.3f") · return \(model.meters[5], specifier: "%.3f")"
            ).font(.caption.monospacedDigit())
            Text(
              "Separate clock buffers • adaptive sample-rate conversion • −0.18 dBFS ceiling • buffer recoveries: \(model.dropouts)"
            ).font(.caption).foregroundStyle(.secondary)
            if model.micLockInstalled {
              Text(
                "MicLock is installed. Silent mic input can mean a hardware mute, privacy denial, or MicLock blocking. Check it yourself; Relay cannot identify or bypass its internal block state."
              ).font(.caption).foregroundStyle(.orange)
            }
            HStack {
              Button("Restore normal audio", action: model.restore)
              Button("Audio permissions", action: model.privacy)
              Button("Refresh devices", action: model.refresh)
            }
          }.padding(.top, 8)
        }.font(.caption).foregroundStyle(.secondary)
      }
      .padding(24).frame(width: 650).background(Color(nsColor: .windowBackgroundColor))
    }
    .frame(width: 650, height: min(850, (NSScreen.main?.visibleFrame.height ?? 950) - 90))
    .onChange(of: model.outputUID) { model.refresh() }
    .onChange(of: model.gains) { model.applyGains() }.onChange(of: model.muted) {
      model.applyGains()
    }
    .sheet(isPresented: $model.showTest) { SetupTestView(model: model) }
  }
}
