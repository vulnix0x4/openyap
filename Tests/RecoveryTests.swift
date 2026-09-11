import CoreAudio
import Foundation

@main enum RecoveryTests {
  static func main() throws {
    let recovery = AudioRecovery()
    guard !recovery.pending else { fatalError("Restore the existing session before this test") }
    let system = AudioObjectID(kAudioObjectSystemObject)
    let initial = Hardware.uint(system, kAudioHardwarePropertyDefaultOutputDevice)
    let initialInput = Hardware.uint(system, kAudioHardwarePropertyDefaultInputDevice)
    let initialEffects = Hardware.uint(system, kAudioHardwarePropertyDefaultSystemOutputDevice)
    let initialUID = Hardware.string(initial, kAudioDevicePropertyDeviceUID)
    guard
      let headphones = Hardware.devices().first(where: {
        $0.name == "WH-1000XM5" && $0.outputs == 2
      })
    else { fatalError("Connect Sony headphones for this host integration test") }
    defer { try? Hardware.setOutputPreservingOtherDefaults(initial) }
    let anchor = try ListeningAnchor(output: headphones)
    try recovery.begin(outputID: anchor.device, outputUID: anchor.uid)
    assert(
      recovery.pending
        && Hardware.uint(system, kAudioHardwarePropertyDefaultOutputDevice) == anchor.device)
    try recovery.restore()
    assert(
      !recovery.pending
        && Hardware.uint(system, kAudioHardwarePropertyDefaultOutputDevice) == initial)
    // HAL device-list notifications converge asynchronously after destroy returns.
    let deadline = Date().addingTimeInterval(2)
    while Hardware.devices().contains(where: { $0.uid == anchor.uid }) && Date() < deadline {
      RunLoop.current.run(until: Date().addingTimeInterval(0.02))
    }
    assert(!Hardware.devices().contains(where: { $0.uid == anchor.uid }))
    print("PASS: real route change, persistent journal, restore, aggregate removal")
    let second = try ListeningAnchor(output: headphones)
    try recovery.begin(outputID: second.device, outputUID: second.uid)
    try Hardware.setOutputPreservingOtherDefaults(headphones.id)
    try recovery.restore()
    assert(Hardware.uint(system, kAudioHardwarePropertyDefaultOutputDevice) == headphones.id)
    print("PASS: deliberate external output change is preserved")
    try Hardware.setOutputPreservingOtherDefaults(initial)
    let third = try ListeningAnchor(output: headphones)
    try recovery.begin(outputID: third.device, outputUID: third.uid)
    UserDefaults.standard.set("missing-test-device", forKey: "savedOutputUID")
    do {
      try recovery.restore()
      fatalError("Must retain recovery for missing prior output")
    } catch {}
    assert(
      recovery.pending
        && Hardware.uint(system, kAudioHardwarePropertyDefaultOutputDevice) == third.device)
    UserDefaults.standard.set(initialUID, forKey: "savedOutputUID")
    try recovery.restore()
    assert(!recovery.pending)
    assert(Hardware.uint(system, kAudioHardwarePropertyDefaultInputDevice) == initialInput)
    assert(Hardware.uint(system, kAudioHardwarePropertyDefaultSystemOutputDevice) == initialEffects)
    print(
      "PASS: unavailable prior output retains safe route; retry restores; input/effects unchanged")
  }
}
