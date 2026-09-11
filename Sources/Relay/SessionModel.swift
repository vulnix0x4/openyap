import AVFoundation
import AppKit
import AudioCore
import CoreAudio
import Observation

@MainActor @Observable final class SessionModel {
  var devices = [AudioDevice]()
  var apps = [MusicApp]()
  var musicBundle = "com.spotify.client"
  var destinationBundle =
    UserDefaults.standard.string(forKey: "destinationBundle") ?? "com.roblox.RobloxPlayer"
  var monitorDestination = true
  var destinationName: String {
    apps.first(where: { $0.id == destinationBundle })?.name ?? destinationBundle
  }
  var musicApps: [MusicApp] {
    apps.filter { AppRoutingPolicy.isSeparate(music: $0.id, destination: destinationBundle) }
  }
  var destinationApps: [MusicApp] {
    apps.filter { AppRoutingPolicy.isSeparate(music: musicBundle, destination: $0.id) }
  }
  var destinationInstructions: String {
    "In \(destinationName), choose BlackHole 2ch as microphone/input and \(selectedOutput?.name ?? "your headphones") as output."
  }
  var outputUID = ""
  var micUID = ""
  var includeVoice = false
  var gains: [Float] = [0.25, 0.65, 0.7, 0.8, 0.65]
  var muted = [false, false, false, false, false]
  var meters = [Float](repeating: 0, count: 6)
  var active = false
  var busy = false
  var cancelling = false
  var interrupted = false
  var status = "Ready when you are"
  var detail = "Play a song in Spotify, then start sharing."
  var showTest = false
  var returnCheckEnabled = false
  var returnCheckMessage =
    "Enable the input check to measure BlackHole. This requests microphone permission for the virtual input only."
  var heardMusic = false
  var heardByPlayer = false
  var observed = [Bool](repeating: false, count: 6)
  var dropouts: UInt = 0
  var micLockInstalled = false
  var recoveryPending = false
  var advanced = false
  var normalOutputNeedsSetup = false
  @ObservationIgnored private let requestMicrophone: () async -> Bool
  @ObservationIgnored private var engine: OpaquePointer?
  @ObservationIgnored private var captures = [ProcessCapture]()
  @ObservationIgnored private var anchor: ListeningAnchor?
  @ObservationIgnored private let recovery = AudioRecovery()
  @ObservationIgnored private var timer: Timer?
  @ObservationIgnored private var ticks = 0
  @ObservationIgnored private var sessionOutputID: AudioDeviceID = 0
  @ObservationIgnored private var sessionMicID: AudioDeviceID = 0
  @ObservationIgnored private var sessionBlackHoleID: AudioDeviceID = 0
  @ObservationIgnored private var startedAt = Date()
  var outputs: [AudioDevice] { devices.filter { $0.outputs >= 2 && $0.isPhysical } }
  var microphones: [AudioDevice] {
    devices.filter { $0.inputs > 0 && $0.isPhysical && !$0.isBluetooth }
  }
  var blackHole: AudioDevice? {
    devices.first { $0.name == "BlackHole 2ch" && $0.inputs == 2 && $0.outputs == 2 }
  }
  var selectedOutput: AudioDevice? { outputs.first { $0.uid == outputUID } }
  init(
    requestMicrophone: @escaping () async -> Bool = {
      await AVCaptureDevice.requestAccess(for: .audio)
    }
  ) {
    self.requestMicrophone = requestMicrophone
    refresh()
    outputUID = outputs.first(where: { $0.name.contains("WH-1000XM5") })?.uid ?? ""
    micUID =
      microphones.first(where: { $0.name.localizedCaseInsensitiveContains("fifine") })?.uid ?? ""
    recoveryPending = recovery.pending
    if recoveryPending {
      status = "Previous session needs recovery"
      detail = "Click Restore normal audio before starting."
    }
    timer = Timer.scheduledTimer(withTimeInterval: 0.08, repeats: true) { [weak self] _ in
      Task { @MainActor in self?.poll() }
    }
  }
  func refresh() {
    devices = Hardware.devices()
    if !active && !busy && !recovery.pending {
      let defaultID = Hardware.uint(
        AudioObjectID(kAudioObjectSystemObject), kAudioHardwarePropertyDefaultOutputDevice)
      let normal = devices.first(where: { $0.id == defaultID })
      normalOutputNeedsSetup = !(normal?.isPhysical ?? false) || normal?.uid != outputUID
    }
    apps = NSWorkspace.shared.runningApplications.compactMap { app in
      guard app.activationPolicy == .regular, let bundle = app.bundleIdentifier,
        bundle != Bundle.main.bundleIdentifier
      else { return nil }
      return MusicApp(id: bundle, name: app.localizedName ?? bundle)
    }.sorted { $0.name < $1.name }
    let knownApps = [
      MusicApp(id: "com.spotify.client", name: "Spotify"),
      MusicApp(id: "com.roblox.RobloxPlayer", name: "Roblox"),
      MusicApp(id: "com.hnc.Discord", name: "Discord"), MusicApp(id: "us.zoom.xos", name: "Zoom"),
    ]
    for app in knownApps where !apps.contains(where: { $0.id == app.id }) { apps.append(app) }
    for bundle in [musicBundle, destinationBundle] where !apps.contains(where: { $0.id == bundle })
    { apps.append(MusicApp(id: bundle, name: bundle)) }
    apps = Dictionary(apps.map { ($0.id, $0) }, uniquingKeysWith: { first, _ in first }).values
      .sorted { $0.name < $1.name }
    micLockInstalled = FileManager.default.fileExists(atPath: "/Applications/MicLock.app")
  }
  func useSelectedAsNormalOutput() {
    guard !active, !busy, let output = selectedOutput else { return }
    do {
      try Hardware.setOutputPreservingOtherDefaults(output.id)
      refresh()
      status = "Normal output is ready"
      detail =
        "\(output.name) is now your normal output. Your existing multi-output devices remain unchanged. Future sessions restore this normal output."
    } catch { fail(error.localizedDescription) }
  }
  func applyGains() {
    guard let engine else { return }
    for i in 0..<5 {
      router_gain(engine, Int32(i), muted[i] || (i == 2 && !includeVoice) ? 0 : gains[i])
    }
  }
  func hardwareTest() async {
    guard CommandLine.arguments.contains("--hardware-test") else { return }
    if let index = CommandLine.arguments.firstIndex(of: "--destination"),
      index + 1 < CommandLine.arguments.count
    {
      destinationBundle = CommandLine.arguments[index + 1]
      refresh()
    }
    monitorDestination = !CommandLine.arguments.contains("--no-monitor")
    let savedDestination = UserDefaults.standard.string(forKey: "destinationBundle")
    defer {
      if let savedDestination {
        UserDefaults.standard.set(savedDestination, forKey: "destinationBundle")
      } else {
        UserDefaults.standard.removeObject(forKey: "destinationBundle")
      }
    }
    let previous = Hardware.uint(
      AudioObjectID(kAudioObjectSystemObject), kAudioHardwarePropertyDefaultOutputDevice)
    if normalOutputNeedsSetup { useSelectedAsNormalOutput() }
    gains = [0, 0, 0, 0, 0]
    await start()
    if active { await enableReturnCheck() }
    for _ in 0..<20 {
      try? await Task.sleep(for: .seconds(1))
      print(
        "HARDWARE", destinationBundle, "monitor", monitorDestination, "active", active, "paused",
        interrupted, meters, detail, returnCheckMessage)
      fflush(stdout)
    }
    stop()
    try? Hardware.setOutputPreservingOtherDefaults(previous)
    print("RESTORED", recoveryPending, detail)
    fflush(stdout)
    NSApplication.shared.terminate(nil)
  }
  func start() async {
    guard !busy, !active else { return }
    guard !recovery.pending else {
      fail("Click Restore normal audio before starting a new session.")
      return
    }
    refresh()
    busy = true
    cancelling = false
    status = "Connecting audio"
    detail = "macOS may ask for System Audio Recording. Audio stays on this Mac."
    defer {
      busy = false
      cancelling = false
    }
    refresh()
    guard let output = selectedOutput else {
      fail("Choose your headphones or speakers.")
      return
    }
    guard let bh = blackHole else {
      fail(
        "BlackHole 2ch is unavailable. Reconnect or enable the existing driver; Relay will not install anything."
      )
      return
    }
    guard AppRoutingPolicy.isSeparate(music: musicBundle, destination: destinationBundle) else {
      fail(
        "Choose different apps for music and calls. A process tap cannot separate browser tabs or windows; capturing the call app as music would cause feedback."
      )
      return
    }
    let musicProcesses = Set(Hardware.processIDs(bundle: musicBundle))
    let destinationProcesses = Set(Hardware.processIDs(bundle: destinationBundle))
    guard musicProcesses.isDisjoint(with: destinationProcesses) else {
      fail(
        "These apps share an audio process. Use separate applications for music and calls to prevent feedback."
      )
      return
    }
    var mic: AudioDevice?
    if includeVoice {
      mic = microphones.first { $0.uid == micUID }
      guard mic != nil else {
        fail(
          "Choose a separate microphone. Bluetooth microphone input is excluded to preserve headphone quality."
        )
        return
      }
      let granted = await requestMicrophone()
      guard granted else {
        fail(
          "Microphone permission is off. Allow Relay in System Settings → Privacy & Security → Microphone, or exclude your voice."
        )
        return
      }
    }
    guard !normalOutputNeedsSetup else {
      fail(
        "Use your selected listening device for normal audio first. This prevents Stop from returning to a different device or an always-sharing virtual route."
      )
      return
    }
    observed = Array(repeating: false, count: 6)
    heardMusic = false
    heardByPlayer = false
    do {
      // Mute the selected processes before moving the unsafe multi-output default.
      captures.append(try ProcessCapture(bundle: musicBundle, label: "your music app"))
      if monitorDestination {
        captures.append(try ProcessCapture(bundle: destinationBundle, label: destinationName))
      }
      guard let r = router_create() else {
        throw NSError(
          domain: "Relay", code: 3,
          userInfo: [NSLocalizedDescriptionKey: "Could not allocate audio buffers."])
      }
      engine = r
      router_pause(r, 1)
      let musicDevice = captures[0].device
      try Hardware.check(
        await Task.detached { router_input(r, 0, musicDevice) }.value, "Starting music capture")
      if monitorDestination {
        let callDevice = captures[1].device
        try Hardware.check(
          await Task.detached { router_input(r, 2, callDevice) }.value,
          "Starting \(destinationName) capture")
      }
      if let mic {
        try Hardware.check(
          await Task.detached { router_input(r, 1, mic.id) }.value, "Opening \(mic.name)")
      }
      try Hardware.check(
        await Task.detached { router_output(r, 1, bh.id) }.value, "Opening BlackHole output")
      try Hardware.check(
        await Task.detached { router_output(r, 0, output.id) }.value, "Opening \(output.name)")
      if cancelling {
        cleanup()
        status = "Connection cancelled"
        return
      }
      let fixedOutput = try ListeningAnchor(output: output)
      anchor = fixedOutput
      try recovery.begin(outputID: fixedOutput.device, outputUID: fixedOutput.uid)
      sessionOutputID = output.id
      sessionMicID = mic?.id ?? 0
      sessionBlackHoleID = bh.id
      applyGains()
      router_pause(r, 0)
      active = true
      interrupted = false
      startedAt = Date()
      status = "Sharing music"
      detail = destinationInstructions
      UserDefaults.standard.set(destinationBundle, forKey: "destinationBundle")
      recoveryPending = recovery.pending
    } catch {
      cleanup()
      fail(error.localizedDescription)
    }
  }
  func pauseForSleep() {
    guard active, let engine else {
      if busy { cancelling = true }
      return
    }
    router_pause(engine, 1)
    interrupted = true
    status = "Sharing paused for sleep"
    detail = "After waking, stop and start sharing again when your devices are connected."
  }
  func stop() {
    guard !busy else {
      cancelling = true
      status = "Cancelling connection"
      detail = "Waiting for macOS to finish its audio request; no mix will be sent."
      return
    }
    cleanup()
    status = recovery.pending ? "Recovery needs attention" : "Sharing stopped"
    if !recovery.pending {
      detail = "Your normal output has been restored. Your apps now play normally."
    }
  }
  private func cleanup() {
    if let engine { router_destroy(engine) }
    engine = nil
    do { try recovery.restore() } catch { detail = error.localizedDescription }
    captures.removeAll()
    if !recovery.pending {
      if let anchor,
        Hardware.devices().contains(where: { $0.id == anchor.device && $0.uid == anchor.uid })
      {
        anchor.dispose()
      }
      anchor = nil
    }
    returnCheckEnabled = false
    active = false
    interrupted = false
    meters = Array(repeating: 0, count: 6)
    recoveryPending = recovery.pending
  }
  func restore() { stop() }
  private func fail(_ message: String) {
    status = "Needs attention"
    detail = message
  }
  private func poll() {
    guard !busy else { return }
    ticks += 1
    if ticks % 12 == 0 { refresh() }
    guard active, let engine else { return }
    meters = (0..<6).map { router_meter(engine, Int32($0)) }
    for i in 0..<6 where meters[i] > 0.002 { observed[i] = true }
    dropouts = router_dropouts(engine)
    let outputGone = !devices.contains { $0.id == sessionOutputID && $0.uid == outputUID }
    let micGone = includeVoice && !devices.contains { $0.id == sessionMicID && $0.uid == micUID }
    let bhGone = !devices.contains { $0.id == sessionBlackHoleID }
    let changedOutput =
      Hardware.uint(
        AudioObjectID(kAudioObjectSystemObject), kAudioHardwarePropertyDefaultOutputDevice)
      != anchor?.device
    if !interrupted
      && (RoutingPolicy.shouldPause(
        outputPresent: !outputGone, micRequired: includeVoice, micPresent: !micGone,
        virtualPresent: !bhGone, defaultUnchanged: !changedOutput) || router_is_paused(engine) != 0)
    {
      router_pause(engine, 1)
      interrupted = true
      status = "Sharing paused safely"
      detail =
        "An audio device or the system output changed. Relay is silent and keeps captured apps muted. Reconnect your devices, then stop and start again. No automatic speaker fallback."
    }
    if !interrupted && observed[0] && detail.hasPrefix("No music signal yet") {
      detail = destinationInstructions
    }
    if !interrupted && Date().timeIntervalSince(startedAt) > 8 && !observed[0] {
      detail =
        "No music signal yet. Play a song and check System Settings → Privacy & Security → Screen & System Audio Recording. Protected audio may be unavailable."
    }
  }
  func enableReturnCheck() async {
    guard active, let engine, let bh = blackHole else {
      returnCheckMessage = "Start sharing before enabling this check."
      return
    }
    guard !busy, !returnCheckEnabled else { return }
    busy = true
    defer {
      busy = false
      if cancelling {
        cancelling = false
        stop()
      }
    }
    guard await requestMicrophone() else {
      returnCheckMessage =
        "Microphone permission was denied. The virtual input check needs it; music-only sharing does not."
      return
    }
    do {
      try Hardware.check(
        await Task.detached { router_verify(engine, bh.id) }.value, "Reading BlackHole input")
      returnCheckEnabled = true
      returnCheckMessage =
        "Measuring BlackHole’s input. Other apps writing to BlackHole can also appear here."
    } catch { returnCheckMessage = error.localizedDescription }
  }
  func testSetup() {
    observed = Array(repeating: false, count: 6)
    heardMusic = false
    heardByPlayer = false
    showTest = true
  }
  func privacy() {
    NSWorkspace.shared.open(
      URL(string: "x-apple.systempreferences:com.apple.preference.security?Privacy_AudioCapture")!)
  }
}
