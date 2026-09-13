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
