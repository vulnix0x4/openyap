import AVFoundation

/// Renders to a file, never to a speaker or the default audio device.
@MainActor final class SpeechRenderer {
  private var synthesizer: AVSpeechSynthesizer?
  private var job: SpeechFileJob?
  private var timeout: Task<Void, Never>?

  func cancel() {
    timeout?.cancel()
    timeout = nil
    job?.finish(.failure(CancellationError()))
    job = nil
    synthesizer?.stopSpeaking(at: .immediate)
    synthesizer = nil
  }

  func render(text: String, voiceID: String, pitch: Float) async throws -> [Float] {
    cancel()
    let text = text.trimmingCharacters(in: .whitespacesAndNewlines)
    guard !text.isEmpty, text.count <= 220 else {
      throw SoundAudio.problem("Enter 1–220 characters to speak.")
    }
    guard let voice = AVSpeechSynthesisVoice(identifier: voiceID) else {
      throw SoundAudio.problem("That voice is unavailable. Choose another Mac voice.")
    }
    let url = FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString + ".caf")
    defer { try? FileManager.default.removeItem(at: url) }
    let synth = AVSpeechSynthesizer()
    synthesizer = synth
    defer { if synthesizer === synth { cancel() } }
    let utterance = AVSpeechUtterance(string: text)
    utterance.voice = voice
    utterance.rate = AVSpeechUtteranceDefaultSpeechRate
    utterance.pitchMultiplier = min(2, max(0.5, pitch))
    try await withCheckedThrowingContinuation { (continuation: CheckedContinuation<Void, Error>) in
      let current = SpeechFileJob(url: url, continuation: continuation)
      job = current
      timeout = Task { @MainActor in
        do { try await Task.sleep(for: .seconds(20)) } catch { return }
        current.finish(.failure(SoundAudio.problem("This voice did not finish. Try a different voice or shorter message.")))
        synth.stopSpeaking(at: .immediate)
      }
      synth.write(utterance) { buffer in current.receive(buffer) }
    }
    timeout?.cancel()
    timeout = nil
    return try SoundAudio.decode(url)
  }
}

/// Callback delivery is not guaranteed to be on the main thread. Own the file and
/// continuation under one lock so Stop and late buffers cannot race or resume twice.
private final class SpeechFileJob: @unchecked Sendable {
  private let lock = NSLock()
  private let url: URL
  private var file: AVAudioFile?
  private var duration = 0.0
  private var continuation: CheckedContinuation<Void, Error>?

  init(url: URL, continuation: CheckedContinuation<Void, Error>) {
    self.url = url
    self.continuation = continuation
  }

  func receive(_ buffer: AVAudioBuffer) {
    lock.lock()
    defer { lock.unlock() }
    guard continuation != nil else { return }
    guard let pcm = buffer as? AVAudioPCMBuffer else {
      complete(.failure(SoundAudio.problem("This voice could not produce audio.")))
      return
    }
    if pcm.frameLength == 0 {
      complete(duration > 0 ? .success(()) : .failure(SoundAudio.problem("This voice produced no audio. Choose another voice.")))
      return
    }
    duration += Double(pcm.frameLength) / pcm.format.sampleRate
    guard duration <= 15 else {
      complete(.failure(SoundAudio.problem("That message is longer than 15 seconds. Shorten it and try again.")))
      return
    }
    do {
      if file == nil {
        file = try AVAudioFile(forWriting: url, settings: pcm.format.settings,
                              commonFormat: pcm.format.commonFormat, interleaved: pcm.format.isInterleaved)
      }
      try file?.write(from: pcm)
    } catch { complete(.failure(error)) }
  }

  func finish(_ result: Result<Void, Error>) {
    lock.lock()
    defer { lock.unlock() }
    complete(result)
  }

  private func complete(_ result: Result<Void, Error>) {
    file?.close()
    file = nil
    let pending = continuation
    continuation = nil
    pending?.resume(with: result)
  }
}
