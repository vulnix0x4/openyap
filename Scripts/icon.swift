import AppKit

let root = CommandLine.arguments[1]
try FileManager.default.createDirectory(atPath: root, withIntermediateDirectories: true)
for size in [16, 32, 128, 256, 512] {
  for scale in [1, 2] {
    let n = size * scale
    let image = NSImage(size: NSSize(width: n, height: n))
    image.lockFocus()
    let g = CGFloat(n) / 1024
    let bg = NSBezierPath(
      roundedRect: NSRect(x: 32 * g, y: 32 * g, width: 960 * g, height: 960 * g), xRadius: 220 * g,
      yRadius: 220 * g)
    NSColor(calibratedRed: 0.08, green: 0.12, blue: 0.13, alpha: 1).setFill()
    bg.fill()
    for (i, h) in [150, 260, 480, 650, 380, 220, 100].enumerated() {
      let bar = NSBezierPath(
        roundedRect: NSRect(
          x: CGFloat(196 + i * 92) * g, y: CGFloat(512 - h / 2) * g, width: 42 * g,
          height: CGFloat(h) * g), xRadius: 21 * g, yRadius: 21 * g)
      (i < 4 ? NSColor.systemMint : NSColor.systemOrange).setFill()
      bar.fill()
    }
    image.unlockFocus()
    let bitmap = NSBitmapImageRep(data: image.tiffRepresentation!)!
    let name = "icon_\(size)x\(size)\(scale == 2 ? "@2x" : "").png"
    try bitmap.representation(using: .png, properties: [:])!.write(
      to: URL(fileURLWithPath: root).appendingPathComponent(name))
  }
}
