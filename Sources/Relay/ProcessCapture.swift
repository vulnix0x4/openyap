import CoreAudio
import Foundation

final class ProcessCapture {
  private(set) var tap: AudioObjectID = 0
  private(set) var device: AudioDeviceID = 0
  init(bundle: String, label: String) throws {
    let processes = Hardware.processIDs(bundle: bundle)
    guard !processes.isEmpty else {
      throw NSError(
        domain: "Relay", code: 4,
        userInfo: [
          NSLocalizedDescriptionKey:
            "Open \(label) before starting. Play some audio in it first so Relay can find its audio process. For a call app, you can also turn off Control call/game audio in Relay."
        ])
    }
    let description = CATapDescription(stereoMixdownOfProcesses: processes)
    description.name = "Relay \(label)"
    description.isPrivate = true
    description.isExclusive = false
    description.bundleIDs = [bundle]
    description.isProcessRestoreEnabled = true
    description.muteBehavior = .muted
    try Hardware.check(
      AudioHardwareCreateProcessTap(description, &tap),
      "Capturing \(label). Allow System Audio Recording for Relay")
    let dictionary: [String: Any] = [
      kAudioAggregateDeviceNameKey: "Relay capture \(label)",
      kAudioAggregateDeviceUIDKey: "local.relay.capture.\(UUID().uuidString)",
      kAudioAggregateDeviceIsPrivateKey: true,
      kAudioAggregateDeviceIsStackedKey: false,
      kAudioAggregateDeviceTapAutoStartKey: true,
      kAudioAggregateDeviceTapListKey: [
        [kAudioSubTapUIDKey: description.uuid.uuidString, kAudioSubTapDriftCompensationKey: true]
      ],
    ]
    do {
      try Hardware.check(
        AudioHardwareCreateAggregateDevice(dictionary as CFDictionary, &device),
        "Opening \(label) capture")
    } catch {
      AudioHardwareDestroyProcessTap(tap)
      tap = 0
      throw error
    }
  }
  deinit {
    if device != 0 { AudioHardwareDestroyAggregateDevice(device) }
    if tap != 0 { AudioHardwareDestroyProcessTap(tap) }
  }
}
