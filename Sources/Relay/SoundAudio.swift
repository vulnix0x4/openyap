import AVFoundation
import Foundation

/// All synthesis and file decoding happens away from the audio callbacks.
enum SoundAudio {
  static func samples(for clip: SoundClip) throws -> [Float] {
    if let preset = clip.preset { return synthesize(preset) }
    guard let url = clip.url else { throw problem("This sound file is missing.") }
    return try decode(url)
  }
  static func problem(_ message: String) -> NSError {
    NSError(domain: "Relay.Soundboard", code: 1, userInfo: [NSLocalizedDescriptionKey: message])
  }
  static func decode(_ url: URL) throws -> [Float] {
    let file = try AVAudioFile(forReading: url, commonFormat: .pcmFormatFloat32, interleaved: false)
    let inputFormat = file.processingFormat
    guard inputFormat.sampleRate > 0, file.length > 0,
      Double(file.length) / inputFormat.sampleRate <= 15,
      inputFormat.channelCount <= 2 else {
      throw problem("Choose a mono or stereo sound up to 15 seconds long.")
    }
    guard let input = AVAudioPCMBuffer(pcmFormat: inputFormat, frameCapacity: AVAudioFrameCount(file.length)),
      let format = AVAudioFormat(commonFormat: .pcmFormatFloat32, sampleRate: 48000, channels: 2, interleaved: false),
      let converter = AVAudioConverter(from: inputFormat, to: format),
      let output = AVAudioPCMBuffer(pcmFormat: format, frameCapacity: AVAudioFrameCount(ceil(Double(file.length) * 48000 / inputFormat.sampleRate)) + 512)
    else { throw problem("This audio format could not be decoded.") }
    var readFrames: AVAudioFrameCount = 0
    while readFrames < input.frameCapacity {
      guard let chunk = AVAudioPCMBuffer(pcmFormat: inputFormat, frameCapacity: input.frameCapacity - readFrames),
        let target = input.floatChannelData else { throw problem("Could not read this sound.") }
      try file.read(into: chunk)
      guard chunk.frameLength > 0, let source = chunk.floatChannelData else { break }
      for channel in 0..<Int(inputFormat.channelCount) {
        target[channel].advanced(by: Int(readFrames)).update(from: source[channel], count: Int(chunk.frameLength))
      }
      readFrames += chunk.frameLength
    }
    input.frameLength = readFrames
    converter.primeMethod = .normal
    var cursor: AVAudioFrameCount = 0
    var result = [Float]()
    while true {
      var error: NSError?
      let status = converter.convert(to: output, error: &error) { requested, state in
        guard cursor < input.frameLength else { state.pointee = .endOfStream; return nil }
        let count = min(requested, input.frameLength - cursor)
        guard let chunk = AVAudioPCMBuffer(pcmFormat: inputFormat, frameCapacity: count),
          let source = input.floatChannelData, let target = chunk.floatChannelData else {
          state.pointee = .endOfStream; return nil
        }
        chunk.frameLength = count
        for channel in 0..<Int(inputFormat.channelCount) {
          target[channel].update(from: source[channel].advanced(by: Int(cursor)), count: Int(count))
        }
        cursor += count
        state.pointee = .haveData
        return chunk
      }
      if let error { throw error }
      guard status != .error, let channels = output.floatChannelData else {
        throw problem("The sound could not be converted.")
      }
      for i in 0..<Int(output.frameLength) {
        for c in 0..<2 {
          let value = channels[c][i]
          result.append(value.isFinite ? min(1, max(-1, value)) : 0)
        }
      }
      guard result.count <= 720000 * 2 else { throw problem("Choose a sound up to 15 seconds long.") }
      if status == .endOfStream { break }
      if output.frameLength == 0 { throw problem("The decoder stopped before the sound finished.") }
    }
    guard !result.isEmpty else { throw problem("This sound is empty.") }
    return result
  }
  static func save(_ samples: [Float], to url: URL) throws {
    let format = AVAudioFormat(commonFormat: .pcmFormatFloat32, sampleRate: 48000, channels: 2, interleaved: false)!
    guard let buffer = AVAudioPCMBuffer(pcmFormat: format, frameCapacity: AVAudioFrameCount(samples.count / 2)),
      let channels = buffer.floatChannelData else { throw problem("Could not prepare the sound.") }
    buffer.frameLength = buffer.frameCapacity
    for i in 0..<Int(buffer.frameLength) { for c in 0..<2 { channels[c][i] = samples[i * 2 + c] } }
    var settings = format.settings
    settings[AVLinearPCMIsNonInterleaved] = false
    let file = try AVAudioFile(forWriting: url, settings: settings)
    try file.write(from: buffer)
    file.close()
  }
  static func synthesize(_ preset: Int) -> [Float] {
    let duration = [1.8, 0.8, 1.3, 0.65, 2.1, 2.0, 0.7, 0.55, 0.8, 1.5, 1.2, 1.5][preset]
    let count = Int(duration * 48000)
    var result = [Float](repeating: 0, count: count * 2)
    var random: UInt32 = 12345
    for i in 0..<count {
      let t = Double(i) / 48000
      random = random &* 1664525 &+ 1013904223
      let noise = Double(random) / Double(UInt32.max) * 2 - 1
      let attack = min(1, t / 0.005), release = min(1, (duration - t) / 0.03)
      func tone(_ frequency: Double) -> Double { sin(2 * .pi * frequency * t) }
      var value: Double
      switch preset {
      case 0: value = (sin(2 * .pi * (48 * t + 45 * (1 - exp(-10 * t)) / 10)) * exp(-3 * t) + noise * exp(-45 * t) * 0.25)
      case 1: value = (tone(880) + 0.5 * tone(t < 0.12 ? 1320 : 1760)) * exp(-6 * t) * 0.5
      case 2: value = (tone(220) + tone(277.18) + 0.5 * tone(440)) * 0.3 * min(1, (duration - t) * 8)
      case 3: value = (noise * 0.4 + sin(2 * .pi * (1400 * t - 850 * t * t)) * 0.6) * pow(sin(.pi * t / duration), 2)
      case 4: value = (sin(2 * .pi * (310 * t - 40 * t * t)) + 0.25 * sin(4 * .pi * (310 * t - 40 * t * t))) * 0.6 * (0.8 + 0.2 * sin(2 * .pi * 5 * t))
      case 5: value = tone(4300) * pow(max(0, sin(2 * .pi * 3 * t)), 12) * 0.45
      case 6: value = noise * exp(-25 * t) * 0.65 + tone(180) * exp(-45 * t) * 0.4 + noise * exp(-10 * max(0, t - 0.13)) * (t > 0.13 ? 0.2 : 0)
      case 7: value = sin(2 * .pi * (1300 * t - 1000 * t * t)) * exp(-7 * t) * 0.8
      case 8:
        let notes = [523.25, 659.25, 783.99, 1046.5]
        value = tone(notes[min(3, Int(t / 0.16))]) * 0.6 * exp(-2 * t)
      case 9: value = (tone(146.83) + tone(155.56) + tone(220)) * 0.25 * exp(-1.8 * t)
      case 10: value = sin(2 * .pi * 170 * t + 15 * sin(2 * .pi * 7 * t) * exp(-3 * t)) * exp(-3 * t) * 0.8
      default: value = tone(Int(t * 6) % 2 == 0 ? 740 : 988) * 0.55
      }
      let sample = Float(max(-0.85, min(0.85, value * attack * release)))
      result[i * 2] = sample
      result[i * 2 + 1] = sample
    }
    return result
  }
}
