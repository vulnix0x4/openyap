import SwiftUI

struct MixerStrip: View {
  let title: String
  let symbol: String
  @Binding var gain: Float
  @Binding var muted: Bool
  let level: Float
  let tint: Color
  var sliderLocked = false
  var body: some View {
    VStack(alignment: .leading, spacing: 7) {
      HStack {
        Label(title, systemImage: symbol).font(.callout.weight(.medium))
        Spacer(minLength: 4)
        Text(muted ? "Muted" : sliderLocked ? "100% · Locked" : "\(Int((gain * 100).rounded()))%").font(.caption.monospacedDigit())
          .foregroundStyle(.secondary)
      }
      HStack(spacing: 8) {
        Slider(value: $gain, in: 0...1).tint(tint).accessibilityLabel(title).accessibilityValue(
          "\(Int((gain * 100).rounded())) percent")
          .disabled(sliderLocked)
        Button {
          muted.toggle()
        } label: {
          Image(systemName: muted ? "speaker.slash.fill" : "speaker.wave.2").frame(width: 20)
        }.buttonStyle(.borderless).foregroundStyle(muted ? Color.red : .secondary)
          .accessibilityLabel("\(muted ? "Unmute" : "Mute") \(title)").help(
            "\(muted ? "Unmute" : "Mute") \(title)")
      }
      LevelMeter(level: level, tint: tint)
    }
  }
}
