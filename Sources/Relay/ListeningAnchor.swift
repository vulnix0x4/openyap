import CoreAudio
import Foundation

// A fixed, single-device aggregate keeps the system default attached to the selected
// destination when Bluetooth disconnects. It has no built-in speaker subdevice.
final class ListeningAnchor {
  private(set) var device: AudioDeviceID = 0
  let uid = "local.relay.listening.\(UUID().uuidString)"
  init(output: AudioDevice) throws {
    let info: [String: Any] = [
      kAudioAggregateDeviceNameKey: "Relay Listening",
      kAudioAggregateDeviceUIDKey: uid,
      kAudioAggregateDeviceIsPrivateKey: false,
      kAudioAggregateDeviceIsStackedKey: false,
      kAudioAggregateDeviceMainSubDeviceKey: output.uid,
      kAudioAggregateDeviceSubDeviceListKey: [[kAudioSubDeviceUIDKey: output.uid]],
    ]
    try Hardware.check(
      AudioHardwareCreateAggregateDevice(info as CFDictionary, &device),
      "Preparing a fixed listening route")
  }
  func dispose() {
    if device != 0 {
      AudioHardwareDestroyAggregateDevice(device)
      device = 0
    }
  }
  // Explicit disposal after restoring the default; never destroy the active default in deinit.
}
