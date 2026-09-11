import AppKit

@MainActor final class AppDelegate: NSObject, NSApplicationDelegate {
  var model: SessionModel?
  func applicationDidFinishLaunching(_ notification: Notification) {
    NSWorkspace.shared.notificationCenter.addObserver(
      self, selector: #selector(willSleep), name: NSWorkspace.willSleepNotification, object: nil)
  }
  @objc private func willSleep() { model?.pauseForSleep() }
  func applicationShouldTerminate(_ sender: NSApplication) -> NSApplication.TerminateReply {
    if model?.busy == true {
      model?.stop()
      Task { @MainActor in
        while model?.busy == true { try? await Task.sleep(for: .milliseconds(100)) }
        model?.stop()
        sender.reply(toApplicationShouldTerminate: true)
      }
      return .terminateLater
    }
    model?.stop()
    return .terminateNow
  }
  func applicationShouldTerminateAfterLastWindowClosed(_ sender: NSApplication) -> Bool { true }
}
