import AppKit
import Observation
import UniformTypeIdentifiers

@MainActor @Observable final class SoundboardLibrary {
  var clips = [SoundClip]()
  var showSynthesized = false { didSet { refresh() } }
  var importing = false
  var message = "Your imported recordings stay on this Mac. Use Import sounds to add more."
  private let directoryOverride: URL?
  private var directory: URL {
    if let directoryOverride { return directoryOverride }
    return FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask)[0]
      .appendingPathComponent("Relay/Sounds", isDirectory: true)
  }
  init(directory: URL? = nil) { directoryOverride = directory; refresh() }
  func refresh() {
    let urls = (try? FileManager.default.contentsOfDirectory(at: directory, includingPropertiesForKeys: nil)) ?? []
    clips = (showSynthesized ? SoundClip.builtIns : []) + urls.filter { $0.pathExtension == "caf" && !$0.lastPathComponent.hasSuffix(".partial.caf") }.sorted { $0.lastPathComponent < $1.lastPathComponent }.map {
      SoundClip(id: $0.lastPathComponent, name: String($0.deletingPathExtension().lastPathComponent.dropFirst(37)), symbol: "waveform", preset: nil, url: $0)
    }.sorted { $0.name.localizedStandardCompare($1.name) == .orderedAscending }
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
      guard clips.filter({ $0.url != nil }).count < 48 else { failures.append("The library holds up to 48 imported sounds."); break }
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
        refresh()
      } catch { failures.append("\(url.lastPathComponent): \(error.localizedDescription)") }
    }
    message = failures.isEmpty ? "Imported \(successes) sound\(successes == 1 ? "" : "s")." : "Imported \(successes). " + failures.joined(separator: " ")
  }
  func remove(_ clip: SoundClip) {
    guard let url = clip.url else { return }
    do { try FileManager.default.removeItem(at: url); refresh() }
    catch { message = "Could not remove this sound: \(error.localizedDescription)" }
  }
}
