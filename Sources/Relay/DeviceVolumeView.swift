import SwiftUI
import Combine

/// Reads hardware state without applying a saved volume on launch or reconnection.
struct DeviceVolumeView: View {
  let device: AudioDevice
  @State private var volume: Float = 0
  @State private var available = false
  @State private var editing = false
  @State private var error: String?
  private let timer = Timer.publish(every: 1, on: .main, in: .common).autoconnect()

  var body: some View {
    VStack(alignment: .leading, spacing: 6) {
      HStack {
        Label("Speaker / headphone volume", systemImage: "hifispeaker")
          .font(.headline)
        Spacer()
        if available { Text("\(Int((volume * 100).rounded()))%").monospacedDigit() }
      }
      if available {
        // Only direct user edits write hardware state; polling never writes it back.
        Slider(value: Binding(get: { volume }, set: changeVolume), in: 0...1) {
          Text("Speaker / headphone volume")
        } onEditingChanged: { editing = $0 }
          .tint(.mint)
          .accessibilityValue("\(Int((volume * 100).rounded())) percent")
        Text("\(device.name) · Changes all sound you hear on this device, never the sharing mix. Your adjustment stays after sharing stops.")
          .font(.caption).foregroundStyle(.secondary)
      } else {
        Text("Use the volume buttons on \(device.name); a writable device volume is unavailable.")
          .font(.caption).foregroundStyle(.secondary)
      }
      if let error { Text(error).font(.caption).foregroundStyle(.orange) }
    }
    .task(id: device.id) { refresh() }
    .onReceive(timer) { _ in if !editing { refresh() } }
  }

  private func refresh() {
    if let current = Hardware.outputVolume(device) {
      volume = current
      available = true
    } else {
      available = false
    }
  }

  private func changeVolume(_ value: Float) {
    do {
      try Hardware.setOutputVolume(device, value: value)
      volume = Hardware.outputVolume(device) ?? volume
      error = nil
    } catch {
      self.error = "Could not change device volume. Check that your listening device is connected."
      refresh()
    }
  }
}
