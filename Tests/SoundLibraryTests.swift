import Foundation
@main struct SoundLibraryTests {
  @MainActor static func main() async throws {
    let root=FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString)
    try FileManager.default.createDirectory(at:root,withIntermediateDirectories:true)
    defer { try? FileManager.default.removeItem(at:root) }
    let source=root.appendingPathComponent("My custom boom.caf")
    try SoundAudio.save(SoundAudio.synthesize(0),to:source)
    let directory=root.appendingPathComponent("Library")
    let library=SoundboardLibrary(directory:directory)
    precondition(library.clips.isEmpty && !library.showSynthesized)
    await library.importURLs([source])
    precondition(library.clips.count==1)
    let imported=library.clips.last!
    precondition(imported.name=="My custom boom" && imported.url != source)
    let reopened=SoundboardLibrary(directory:directory)
    precondition(reopened.clips.last!.id==imported.id)
    reopened.showSynthesized = true
    precondition(reopened.clips.count==13)
    reopened.showSynthesized = false
    precondition(reopened.clips.count==1)
    reopened.remove(imported)
    precondition(reopened.clips.isEmpty && FileManager.default.fileExists(atPath:source.path))
    print("PASS: import copies and persists clips; removal preserves original file. Only temporary test folders used.")
  }
}
