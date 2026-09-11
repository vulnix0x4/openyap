import Foundation

@main enum PolicyTests {
  static func main() {
    assert(
      !RoutingPolicy.shouldPause(
        outputPresent: true, micRequired: false, micPresent: false, virtualPresent: true,
        defaultUnchanged: true))
    assert(
      RoutingPolicy.shouldPause(
        outputPresent: false, micRequired: false, micPresent: true, virtualPresent: true,
        defaultUnchanged: true))
    assert(
      RoutingPolicy.shouldPause(
        outputPresent: true, micRequired: true, micPresent: false, virtualPresent: true,
        defaultUnchanged: true))
    assert(
      RoutingPolicy.shouldPause(
        outputPresent: true, micRequired: false, micPresent: true, virtualPresent: false,
        defaultUnchanged: true))
    assert(
      RoutingPolicy.shouldPause(
        outputPresent: true, micRequired: false, micPresent: true, virtualPresent: true,
        defaultUnchanged: false))
    print("PASS: routing interruption policy (5 cases)")
  }
}
