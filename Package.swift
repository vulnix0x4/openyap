// swift-tools-version: 6.2
import PackageDescription
let package = Package(name: "Relay", platforms: [.macOS(.v26)], products: [.executable(name: "Relay", targets: ["Relay"])], targets: [
 .target(name: "AudioCore", publicHeadersPath: "include", linkerSettings: [.linkedFramework("CoreAudio")]),
 .executableTarget(name: "Relay", dependencies: ["AudioCore"], swiftSettings: [.swiftLanguageMode(.v5)], linkerSettings: [.linkedFramework("SwiftUI"), .linkedFramework("AVFoundation")])])
