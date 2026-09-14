import AppKit
import AudioCore
import SwiftUI

@main struct OpenYapApp: App {
  @State private var model = SessionModel()
  @NSApplicationDelegateAdaptor(AppDelegate.self) private var delegate
  init() {
    if CommandLine.arguments.contains("--restore") {
      do {
        try AudioRecovery().restore()
        print("Audio restored")
      } catch {
        print(error)
        exit(1)
      }
      exit(0)
    }
    if CommandLine.arguments.contains("--self-test") { exit(Int32(router_selftest())) }
    if CommandLine.arguments.contains("--devices") {
      for d in Hardware.devices() {
        print("\(d.id) | \(d.name) | in:\(d.inputs) out:\(d.outputs) | \(d.uid)")
      }
      exit(0)
    }
  }
  var body: some Scene {
    Window("OpenYap — Share your sound", id: "main") {
      ContentView(model: model)
        .onAppear { delegate.model = model }.task { await model.hardwareTest() }
    }
    .windowResizability(.contentSize)
    .defaultPosition(.center)
    .commands {
      CommandGroup(replacing: .newItem) {}
      CommandMenu("Audio") {
        Button("Stop sharing", action: model.stop).keyboardShortcut(".")
        Button("Restore normal audio", action: model.restore)
      }
    }
  }
}
