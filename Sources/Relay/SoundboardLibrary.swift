import AppKit
import Observation
import UniformTypeIdentifiers

@MainActor @Observable final class SoundboardLibrary {
  var clips = SoundClip.builtIns
  var importing = false
  var message = "Original effects included. Import your own Vine Boom, Discord ping, or other clips."
  private let directoryOverride: URL?
  private var directory: URL {
    if let directoryOverride { return directoryOverride }
    return     FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask)[0]
      .appendingPathComponent("Relay/Sounds", isDirectory: true)
  }
  init(directory: URL? = nil) { directoryOverride = directory; reload() }
  private func reload() {
    let urls = (try? FileManager.default.contentsOfDirectory(at: directory, includingPropertiesForKeys: nil)) ?? []
    clips = SoundClip.builtIns + urls.filter { $0.pathExtension == "caf" && !$0.lastPathComponent.hasSuffix(".partial.caf") }.sorted { $0.lastPathComponent < $1.lastPathComponent }.map {
      SoundClip(id: $0.lastPathComponent, name: String($0.deletingPathExtension().lastPathComponent.dropFirst(37)), symbol: "waveform", preset: nil, url: $0)
    }
  }
  func importSounds() {
    guard !importing else { return }
    let panel = NSOpenPanel()
    panel.allowedContentTypes = [.audio]
    panel.allowsMultipleSelection = true
    panel.message = "Choose audio clips up to 15 seconds each. Files are copied into Relay."
    panel.begin { [weak self] response in
      guard response == .OK, let self else { return }
      Task { @MainActor in await self.importURLs(panel.urls) }
    }
  }
  func importURLs(_ urls: [URL]) async {
    importing = true
    defer { importing = false }
    var successes = 0, failures = [String]()
    for url in urls {
      guard clips.count < 60 else { failures.append("The library holds up to 48 imported sounds."); break }
      do {
        let folder = directory
        try FileManager.default.createDirectory(at: folder, withIntermediateDirectories: true)
        let destination = folder.appendingPathComponent(UUID().uuidString + "-" + url.deletingPathExtension().lastPathComponent + ".caf")
        try await Task.detached {
          let samples = try SoundAudio.decode(url)
          let temporary = folder.appendingPathComponent(UUID().uuidString + ".partial.caf")
          defer { try? FileManager.default.removeItem(at: temporary) }
          try SoundAudio.save(samples, to: temporary)
          try FileManager.default.moveItem(at: temporary, to: destination)
        }.value
        successes += 1
        reload()
      } catch { failures.append("\(url.lastPathComponent): \(error.localizedDescription)") }
    }
    message = failures.isEmpty ? "Imported \(successes) sound\(successes == 1 ? "" : "s")." : "Imported \(successes). " + failures.joined(separator: " ")
  }
  func remove(_ clip: SoundClip) {
    guard let url = clip.url else { return }
    do { try FileManager.default.removeItem(at: url); reload() }
    catch { message = "Could not remove this sound: \(error.localizedDescription)" }
  }
}
