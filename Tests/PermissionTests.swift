import AppKit
import CoreAudio

@main enum PermissionTests {
  @MainActor static func main() async {
    let before = Hardware.uint(
      AudioObjectID(kAudioObjectSystemObject), kAudioHardwarePropertyDefaultOutputDevice)
    let model = SessionModel(requestMicrophone: { false })
    model.includeVoice = true
    await model.start()
    assert(!model.active && !model.busy)
    assert(model.detail.contains("Microphone permission is off"))
    assert(
      Hardware.uint(
        AudioObjectID(kAudioObjectSystemObject), kAudioHardwarePropertyDefaultOutputDevice)
        == before)
    print("PASS: injected microphone denial shows actionable error; no routing changes")
  }
}
