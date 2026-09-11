import AppKit
import CoreAudio

struct AudioDevice: Identifiable, Hashable {
  let id: AudioDeviceID
  let uid: String
  let name: String
  let inputs: Int
  let outputs: Int
  let transport: UInt32
  var isPhysical: Bool {
    transport != kAudioDeviceTransportTypeVirtual && transport != kAudioDeviceTransportTypeAggregate
      && !name.contains("BlackHole")
  }
  var isBluetooth: Bool {
    transport == kAudioDeviceTransportTypeBluetooth
      || transport == kAudioDeviceTransportTypeBluetoothLE
  }
}

enum Hardware {
  static func address(
    _ selector: AudioObjectPropertySelector,
    _ scope: AudioObjectPropertyScope = kAudioObjectPropertyScopeGlobal
  ) -> AudioObjectPropertyAddress {
    .init(mSelector: selector, mScope: scope, mElement: kAudioObjectPropertyElementMain)
  }
  static func uint(_ id: AudioObjectID, _ key: AudioObjectPropertySelector) -> UInt32 {
    var a = address(key)
    var value: UInt32 = 0
    var size: UInt32 = 4
    _ = AudioObjectGetPropertyData(id, &a, 0, nil, &size, &value)
    return value
  }
  static func string(_ id: AudioObjectID, _ key: AudioObjectPropertySelector) -> String {
    var a = address(key)
    var value: Unmanaged<CFString>?
    var size = UInt32(MemoryLayout<Unmanaged<CFString>?>.size)
    guard AudioObjectGetPropertyData(id, &a, 0, nil, &size, &value) == 0, let value else {
      return ""
    }
    return value.takeRetainedValue() as String
  }
  static func ids(_ id: AudioObjectID, _ key: AudioObjectPropertySelector) -> [UInt32] {
    var a = address(key)
    var size: UInt32 = 0
    guard AudioObjectGetPropertyDataSize(id, &a, 0, nil, &size) == 0 else { return [] }
    var values = [UInt32](repeating: 0, count: Int(size) / 4)
    guard size > 0 else { return [] }
    _ = values.withUnsafeMutableBytes {
      AudioObjectGetPropertyData(id, &a, 0, nil, &size, $0.baseAddress!)
    }
    return values
  }
  static func channels(_ id: AudioDeviceID, _ scope: AudioObjectPropertyScope) -> Int {
    var a = address(kAudioDevicePropertyStreamConfiguration, scope)
    var size: UInt32 = 0
    guard AudioObjectGetPropertyDataSize(id, &a, 0, nil, &size) == 0, size > 0 else { return 0 }
    let p = UnsafeMutableRawPointer.allocate(
      byteCount: Int(size), alignment: MemoryLayout<AudioBufferList>.alignment)
    defer { p.deallocate() }
    guard AudioObjectGetPropertyData(id, &a, 0, nil, &size, p) == 0 else { return 0 }
    return UnsafeMutableAudioBufferListPointer(p.assumingMemoryBound(to: AudioBufferList.self))
      .reduce(0) { $0 + Int($1.mNumberChannels) }
  }
  static func devices() -> [AudioDevice] {
    ids(AudioObjectID(kAudioObjectSystemObject), kAudioHardwarePropertyDevices).map { id in
      AudioDevice(
        id: id, uid: string(id, kAudioDevicePropertyDeviceUID),
        name: string(id, kAudioObjectPropertyName),
        inputs: channels(id, kAudioObjectPropertyScopeInput),
        outputs: channels(id, kAudioObjectPropertyScopeOutput),
        transport: uint(id, kAudioDevicePropertyTransportType))
    }.filter { !$0.name.hasPrefix("Relay capture") }
  }
  static func setDefault(
    _ id: AudioDeviceID,
    key: AudioObjectPropertySelector = kAudioHardwarePropertyDefaultOutputDevice
  ) throws {
    var a = address(key)
    var value = id
    try check(
      AudioObjectSetPropertyData(AudioObjectID(kAudioObjectSystemObject), &a, 0, nil, 4, &value),
      "Changing audio output")
  }
  static func setOutputPreservingOtherDefaults(_ id: AudioDeviceID) throws {
    let system = AudioObjectID(kAudioObjectSystemObject)
    let input = uint(system, kAudioHardwarePropertyDefaultInputDevice)
    let effects = uint(system, kAudioHardwarePropertyDefaultSystemOutputDevice)
    try setDefault(id)
    if input != 0 && uint(system, kAudioHardwarePropertyDefaultInputDevice) != input {
      try setDefault(input, key: kAudioHardwarePropertyDefaultInputDevice)
    }
    if effects != 0 && uint(system, kAudioHardwarePropertyDefaultSystemOutputDevice) != effects {
      try setDefault(effects, key: kAudioHardwarePropertyDefaultSystemOutputDevice)
    }
  }
  static func check(_ status: OSStatus, _ action: String) throws {
    guard status == noErr else {
      throw NSError(
        domain: "Relay.Audio", code: Int(status),
        userInfo: [
          NSLocalizedDescriptionKey:
            "\(action) failed (Core Audio \(status)). Check audio permissions and device availability."
        ])
    }
  }
  static func processIDs(bundle: String) -> [AudioObjectID] {
    ids(AudioObjectID(kAudioObjectSystemObject), kAudioHardwarePropertyProcessObjectList).filter {
      let b = string($0, kAudioProcessPropertyBundleID)
      return b == bundle || b.hasPrefix(bundle + ".")
    }
  }
}
