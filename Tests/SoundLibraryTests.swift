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
    await library.importURLs([source])
    precondition(library.clips.count==13)
    let imported=library.clips.last!
    precondition(imported.name=="My custom boom" && imported.url != source)
    let reopened=SoundboardLibrary(directory:directory)
    precondition(reopened.clips.last!.id==imported.id)
    reopened.remove(imported)
    precondition(reopened.clips.count==12 && FileManager.default.fileExists(atPath:source.path))
    print("PASS: import copies and persists clips; removal preserves original file. Only temporary test folders used.")
  }
}
