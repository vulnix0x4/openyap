import AVFoundation
import Foundation
@main struct SoundAudioTests {
  static func main() throws {
    let folder=FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString)
    try FileManager.default.createDirectory(at:folder,withIntermediateDirectories:true)
    defer { try? FileManager.default.removeItem(at:folder) }
    for clip in SoundClip.builtIns {
      let samples=try SoundAudio.samples(for:clip)
      precondition(!samples.isEmpty && samples.count <= 720000*2)
      precondition(samples.allSatisfy { $0.isFinite && abs($0) <= 0.851 })
      precondition(samples.contains { abs($0) > 0.05 })
      let url=folder.appendingPathComponent(clip.id+".caf")
      try SoundAudio.save(samples,to:url)
      let decoded=try SoundAudio.decode(url)
      precondition(abs(decoded.count-samples.count)<1024)
      print("PASS: generated and decoded \(clip.name)")
    }
    for rate in [24000.0,44100.0] {
      let format=AVAudioFormat(standardFormatWithSampleRate:rate,channels:1)!
      let buffer=AVAudioPCMBuffer(pcmFormat:format,frameCapacity:AVAudioFrameCount(rate))!
      buffer.frameLength=buffer.frameCapacity
      for i in 0..<Int(buffer.frameLength) { buffer.floatChannelData![0][i]=0.2*sin(Float(i)*2*Float.pi*440/Float(rate)) }
      let url=folder.appendingPathComponent("mono-\(rate).caf")
      do { let file=try AVAudioFile(forWriting:url,settings:format.settings);try file.write(from:buffer);file.close() }
      let decoded=try SoundAudio.decode(url)
      precondition(abs(decoded.count-96000)<1024)
      precondition(decoded.contains { abs($0)>0.1 })
      print("PASS: mono \(rate) Hz converts to stereo 48 kHz")
    }
    let longURL=folder.appendingPathComponent("too-long.caf")
    try SoundAudio.save([Float](repeating: 0, count: 48000*16*2),to:longURL)
    do { _=try SoundAudio.decode(longURL);preconditionFailure("Accepted long audio") } catch { print("PASS: overlong file rejected") }
    let invalid=folder.appendingPathComponent("invalid.wav")
    try Data("not audio".utf8).write(to:invalid)
    do { _=try SoundAudio.decode(invalid);preconditionFailure("Accepted invalid audio") } catch { print("PASS: invalid file rejected") }
    print("No sound played and no audio hardware opened.")
  }
}
