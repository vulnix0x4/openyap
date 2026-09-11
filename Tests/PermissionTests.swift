import AppKit
import CoreAudio

@main enum PermissionTests {
  @MainActor static func main() async {
    let before = Hardware.uint(
      AudioObjectID(kAudioObjectSystemObject), kAudioHardwarePropertyDefaultOutputDevice)
    let model = SessionModel(requestMicrophone: { false })
    // Only select IDs in the model. Permission denial occurs before any hardware opens.
    model.outputUID = model.outputs.first?.uid ?? ""
    model.micUID = model.microphones.first?.uid ?? ""
    model.includeVoice = true
    await model.start()
    assert(!model.active && !model.busy)
    assert(model.detail.contains("Microphone permission is off"))
    assert(
      Hardware.uint(
        AudioObjectID(kAudioObjectSystemObject), kAudioHardwarePropertyDefaultOutputDevice)
        == before)
    print("PASS: injected microphone denial shows actionable error; no routing changes")
    model.destinationBundle = model.musicBundle
    model.includeVoice = false
    await model.start()
    assert(!model.active && model.detail.contains("Choose different apps"))
    model.monitorDestination = false
    await model.start()
    assert(!model.active && model.detail.contains("Choose different apps"))
    assert(
      Hardware.uint(
        AudioObjectID(kAudioObjectSystemObject), kAudioHardwarePropertyDefaultOutputDevice)
        == before)
    print(
      "PASS: identical source/destination blocked in both monitored and direct-playback modes without routing changes"
    )
  }
}
