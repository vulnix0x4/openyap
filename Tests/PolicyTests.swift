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
    assert(AppRoutingPolicy.isSeparate(music: "com.spotify.client", destination: "com.hnc.Discord"))
    assert(AppRoutingPolicy.isSeparate(music: "com.spotify.client", destination: "us.zoom.xos"))
    assert(
      AppRoutingPolicy.isSeparate(
        music: "com.spotify.client", destination: "com.roblox.RobloxPlayer"))
    assert(
      !AppRoutingPolicy.isSeparate(music: "com.brave.Browser", destination: "com.brave.Browser"))
    assert(
      !AppRoutingPolicy.isSeparate(
        music: "com.brave.Browser.helper", destination: "com.brave.Browser"))
    assert(
      !AppRoutingPolicy.isSeparate(
        music: "com.brave.Browser", destination: "com.brave.Browser.helper"))
    assert(
      !AppRoutingPolicy.isSeparate(music: "local.relay.MusicShare", destination: "com.hnc.Discord"))
    assert(
      !AppRoutingPolicy.isSeparate(
        music: "com.spotify.client", destination: "local.relay.MusicShare"))
    assert(!AppRoutingPolicy.isSeparate(music: "", destination: "com.hnc.Discord"))
    print("PASS: app isolation policy (9 cases, including shared browser/helper and self-capture)")
  }
}
