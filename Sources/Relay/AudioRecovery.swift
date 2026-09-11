import CoreAudio
import Foundation

// Persist BEFORE changing a setting. Recoverable after a crash or forced quit.
final class AudioRecovery {
  private let defaults = UserDefaults.standard
  var pending: Bool { defaults.string(forKey: "savedOutputUID") != nil }
  func begin(outputID: AudioDeviceID, outputUID: String) throws {
    if pending {
      throw NSError(
        domain: "Relay", code: 1,
        userInfo: [
          NSLocalizedDescriptionKey: "Restore the previous audio session before starting a new one."
        ])
    }
    let previous = Hardware.uint(
      AudioObjectID(kAudioObjectSystemObject), kAudioHardwarePropertyDefaultOutputDevice)
    defaults.set(Hardware.string(previous, kAudioDevicePropertyDeviceUID), forKey: "savedOutputUID")
    defaults.set(outputUID, forKey: "relayOutputUID")
    defaults.synchronize()
    try Hardware.setOutputPreservingOtherDefaults(outputID)
  }
  func restore() throws {
    guard let old = defaults.string(forKey: "savedOutputUID") else { return }
    let current = Hardware.uint(
      AudioObjectID(kAudioObjectSystemObject), kAudioHardwarePropertyDefaultOutputDevice)
    let currentUID = Hardware.string(current, kAudioDevicePropertyDeviceUID)
    let expected = defaults.string(forKey: "relayOutputUID")
    // Respect a deliberate output change made outside Relay during a session.
    if currentUID == expected {
      guard let device = Hardware.devices().first(where: { $0.uid == old && $0.outputs > 0 }) else {
        throw NSError(
          domain: "Relay", code: 2,
          userInfo: [
            NSLocalizedDescriptionKey:
              "The previous output is disconnected. Reconnect it, then click Restore normal audio. No speaker fallback was selected by Relay."
          ])
      }
      try Hardware.setOutputPreservingOtherDefaults(device.id)
    }
    if let expected,
      let anchor = Hardware.devices().first(where: {
        $0.uid == expected && $0.uid.hasPrefix("local.relay.listening.")
      })
    {
      try Hardware.check(
        AudioHardwareDestroyAggregateDevice(anchor.id), "Removing the temporary listening route")
    }
    defaults.removeObject(forKey: "savedOutputUID")
    defaults.removeObject(forKey: "relayOutputUID")
  }
}
