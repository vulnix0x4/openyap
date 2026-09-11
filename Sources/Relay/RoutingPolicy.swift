import Foundation

// IDs identify the current device incarnation; UIDs identify the user's choice.
// A reconnected device gets a new ID, so resuming always requires a new session.
enum RoutingPolicy {
  static func shouldPause(
    outputPresent: Bool, micRequired: Bool, micPresent: Bool, virtualPresent: Bool,
    defaultUnchanged: Bool
  ) -> Bool {
    !outputPresent || (micRequired && !micPresent) || !virtualPresent || !defaultUnchanged
  }
}
