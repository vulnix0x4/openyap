import Foundation
@main struct MaxSharingTests {
  @MainActor static func main() {
    let model = SessionModel(requestMicrophone: { false })
    model.gains = [0.2, 0.15, 0.3, 0.4, 0.5]
    model.includeVoice = true
    model.maxSharing = true
    precondition(model.gains[1] == 1 && model.effectiveGain(1) == 1)
    model.gains[1] = 0.01
    precondition(model.effectiveGain(1) == 1)
    for i in [0, 2, 3, 4] { precondition(model.effectiveGain(i) == model.gains[i]) }
    model.muted[1] = true
    precondition(model.effectiveGain(1) == 0)
    model.muted[1] = false
    precondition(model.effectiveGain(1) == 1)
    model.maxSharing = false
    precondition(model.effectiveGain(1) == 0.01)
    print("PASS: max sets and enforces full sharing gain, isolates other controls, preserves mute, and unlocks when off. No audio session started.")
  }
}
