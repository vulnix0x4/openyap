import Foundation

struct SoundClip: Identifiable, Sendable {
  let id: String
  let name: String
  let symbol: String
  let preset: Int?
  let url: URL?

  static let builtIns: [SoundClip] = [
    ("Deep boom", "burst.fill"), ("Chat ping", "message.badge.filled.fill"),
    ("Air horn", "megaphone.fill"), ("Record scratch", "opticaldisc"),
    ("Sad trombone", "cloud.rain.fill"), ("Crickets", "moon.stars.fill"),
    ("Rimshot", "music.note"), ("Laser pew", "bolt.fill"),
    ("Level up", "star.fill"), ("Drama sting", "theatermasks.fill"),
    ("Spring boing", "arrow.up.and.down"), ("Alarm", "alarm.fill"),
  ].enumerated().map { index, item in
    SoundClip(id: "preset-\(index)", name: item.0, symbol: item.1, preset: index, url: nil)
  }
}

// Stable reaction groups keep familiar pads together; unknown imports remain discoverable.
enum SoundReaction: String, CaseIterable, Identifiable {
  case good = "Good / Happy"
  case bad = "Bad / Awkward"
  case funny = "Funny / Hype"
  case suspense = "Suspense / Scary"
  case alerts = "Calls / Alerts"
  case other = "Other sounds"
  var id: String { rawValue }
}

extension SoundClip {
  var reaction: SoundReaction {
    switch name.lowercased() {
    case "kids cheering", "old spice whistle", "level up": return .good
    case "bruh", "windows xp error", "roblox oof", "wii sports aww", "bonk",
         "cartoon slip", "sad trombone", "crickets", "record scratch": return .bad
    case "suspense", "horror movie suspense", "horror drone", "doors screech psst",
         "vine boom", "drama sting", "deep boom": return .suspense
    case "discord notification", "discord incoming call", "discord outgoing call",
         "discord user joined", "discord user left", "real door knocking", "door knocking",
         "chat ping", "alarm": return .alerts
    case "noot noot", "fart with reverb", "fbi open up", "clash royale heheheha",
         "hog rider", "air horn", "rimshot", "laser pew", "spring boing": return .funny
    default: return .other
    }
  }
}
