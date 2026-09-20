import AVFoundation

@main struct SpeechTests {
  @MainActor static func main() async throws {
    let renderer = SpeechRenderer()
    guard let voice = AVSpeechSynthesisVoice(language: "en-US") else { fatalError("No English voice installed") }
    for text in ["", String(repeating: "a", count: 221)] {
      do { _ = try await renderer.render(text: text, voiceID: voice.identifier, pitch: 1); fatalError("Invalid text accepted") }
      catch { print("Rejected invalid text") }
    }
    for pitch: Float in [1, 0.65, 1.6] {
      let samples = try await renderer.render(text: "Hello from Open Yap. Testing the voice soundboard.", voiceID: voice.identifier, pitch: pitch)
      precondition(samples.count > 4800 && samples.count <= 48000 * 15 * 2)
      precondition(samples.count % 2 == 0 && samples.allSatisfy { $0.isFinite && abs($0) <= 1 })
      precondition(samples.contains { abs($0) > 0.001 })
      print("Rendered \(voice.name), pitch \(pitch): \(samples.count / 96000) seconds, nonzero stereo PCM")
    }
    do {
      _ = try await renderer.render(text: "Hello", voiceID: "missing.voice", pitch: 1)
      fatalError("Unavailable voice accepted")
    } catch { print("Unavailable voice rejected") }
    do {
      _ = try await renderer.render(text: String(repeating: "Suspense. ", count: 22), voiceID: voice.identifier, pitch: 1)
      fatalError("Overlong speech accepted")
    } catch { print("Overlong speech rejected") }
    let pending = Task { @MainActor in
      try await renderer.render(text: "This message must be cancelled before it can play.", voiceID: voice.identifier, pitch: 1)
    }
    await Task.yield()
    renderer.cancel()
    do { _ = try await pending.value; fatalError("Cancellation ignored") }
    catch is CancellationError { print("Cancellation passed") }
    let next = try await renderer.render(text: "Ready again.", voiceID: voice.identifier, pitch: 1)
    precondition(!next.isEmpty)
    print("Render after cancellation passed. No audio output devices were opened.")
  }
}
