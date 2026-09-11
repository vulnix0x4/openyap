import SwiftUI

struct LevelMeter: View {
  let level: Float
  let tint: Color
  private var fraction: CGFloat {
    level > 0 ? CGFloat(max(0, min(1, (20 * log10(level) + 60) / 60))) : 0
  }
  var body: some View {
    GeometryReader { geometry in
      ZStack(alignment: .leading) {
        Capsule().fill(.quaternary)
        Capsule().fill(level > 0.9 ? Color.orange : tint).frame(
          width: geometry.size.width * fraction)
      }
    }.frame(height: 4).accessibilityLabel("Signal level").accessibilityValue(
      level > 0.001 ? "Signal present" : "Silent")
  }
}
