import SwiftUI

struct SetupTestView: View {
  @Bindable var model: SessionModel
  @Environment(\.dismiss) private var dismiss
  var body: some View {
    VStack(alignment: .leading, spacing: 18) {
      Text("Test your setup").font(.title.bold())
      Text(
        "Start sharing first. These checks use real audio; OpenYap never sends a test tone into your receiving app."
      ).foregroundStyle(.secondary)
      TestCheck(
        number: "1", title: "Music capture", detail: "Play a song in the selected music app.",
        passed: model.observed[0])
      TestCheck(
        number: "2", title: "Headphone playback",
        detail:
          "Listening output has \(model.observed[3] ? "signal" : "no signal yet"). Only your ears can confirm the correct device.",
        passed: model.heardMusic)
      Toggle("I can hear the music through my chosen device", isOn: $model.heardMusic).padding(
        .leading, 38)
      TestCheck(
        number: "3", title: model.includeVoice ? "Microphone capture" : "Microphone excluded",
        detail: model.includeVoice
          ? "Speak into the selected microphone. If silent, check mute, privacy permission, and MicLock."
          : "To test a mic, stop sharing and enable Include my voice.",
        passed: model.includeVoice && model.observed[1])
      TestCheck(
        number: "4", title: "BlackHole input receives signal",
        detail:
          "\(model.observed[4] ? "Mix is being written." : "No mix written yet.") \(model.observed[5] ? "Signal was read back from BlackHole’s input." : "Waiting for input loopback; silence may also mean microphone access is blocked.")",
        passed: model.observed[4] && model.observed[5])
      Text(model.returnCheckMessage).font(.caption).foregroundStyle(.secondary)
      if !model.returnCheckEnabled {
        Button("Enable BlackHole input check") { Task { await model.enableReturnCheck() } }
          .disabled(!model.active)
      }
      Divider()
      Text("Final check in \(model.destinationName)").font(.headline)
      Text(
        "Select BlackHole 2ch as input and your headphones as output. Ask another person to confirm music and voice, or check a recording in the receiving app. Local meters cannot verify delivery to a call or recording."
      ).font(.callout)
      Toggle("The receiver or recording confirmed the audio", isOn: $model.heardByPlayer)
      HStack {
        if !model.active {
          Button("Start sharing for test") { Task { await model.start() } }.disabled(model.busy)
        }
        Button("Reset checks", action: model.testSetup)
        Spacer()
        Button("Done") { dismiss() }.keyboardShortcut(.defaultAction)
      }
    }.padding(26).frame(width: 540)
  }
}
