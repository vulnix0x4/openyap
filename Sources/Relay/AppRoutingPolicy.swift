import Foundation

/// Core Audio taps select processes, not tabs or individual windows.
enum AppRoutingPolicy {
  static func isSeparate(
    music: String, destination: String, relay: String = "local.relay.MusicShare"
  ) -> Bool {
    guard !music.isEmpty, !destination.isEmpty else { return false }
    func overlaps(_ a: String, _ b: String) -> Bool {
      a == b || a.hasPrefix(b + ".") || b.hasPrefix(a + ".")
    }
    return !overlaps(music, destination) && !overlaps(music, relay) && !overlaps(destination, relay)
  }
}
