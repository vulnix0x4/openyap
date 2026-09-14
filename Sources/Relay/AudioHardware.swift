import AppKit
import CoreAudio
import AudioToolbox

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
    }.filter { !$0.name.hasPrefix("OpenYap capture") && !$0.name.hasPrefix("Relay capture") }
  }
  // Address the chosen physical device only, never the default aggregate or BlackHole.
  private static func volumeProperty(_ device: AudioDevice) -> AudioObjectPropertyAddress? {
    guard device.isPhysical,
      string(device.id, kAudioDevicePropertyDeviceUID) == device.uid,
      uint(device.id, kAudioDevicePropertyDeviceIsAlive) != 0 else { return nil }
    // Bluetooth devices can expose channel volumes and a virtual main control,
    // while built-in speakers expose the ordinary main scalar.
    for selector in [kAudioDevicePropertyVolumeScalar,
      kAudioHardwareServiceDeviceProperty_VirtualMainVolume] {
      var a = address(selector, kAudioObjectPropertyScopeOutput)
      var writable: DarwinBoolean = false
      var value: Float = 0
      var size: UInt32 = 4
      if AudioObjectIsPropertySettable(device.id, &a, &writable) == noErr,
        writable.boolValue,
        AudioObjectGetPropertyData(device.id, &a, 0, nil, &size, &value) == noErr,
        value.isFinite, (0...1).contains(value) { return a }
    }
    return nil
  }

  static func outputVolume(_ device: AudioDevice) -> Float? {
    guard var a = volumeProperty(device) else { return nil }
    var value: Float = 0
    var size: UInt32 = 4
    guard AudioObjectGetPropertyData(device.id, &a, 0, nil, &size, &value) == noErr,
      value.isFinite, (0...1).contains(value) else { return nil }
    return value
  }

  static func setOutputVolume(_ device: AudioDevice, value: Float) throws {
    guard value.isFinite, (0...1).contains(value), var a = volumeProperty(device) else {
      throw NSError(domain: "OpenYap.Audio", code: -1,
        userInfo: [NSLocalizedDescriptionKey: "Device volume is unavailable."])
    }
    var value = value
    try check(AudioObjectSetPropertyData(device.id, &a, 0, nil, 4, &value),
      "Changing listening device volume")
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
        domain: "OpenYap.Audio", code: Int(status),
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
