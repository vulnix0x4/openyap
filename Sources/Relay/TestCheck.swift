import SwiftUI

struct TestCheck: View {
  let number: String
  let title: String
  let detail: String
  let passed: Bool
  var body: some View {
    HStack(alignment: .top, spacing: 12) {
      ZStack {
        Circle().fill(passed ? Color.mint.opacity(0.15) : Color.gray.opacity(0.15))
        if passed {
          Image(systemName: "checkmark").foregroundStyle(.mint)
        } else {
          Text(number).foregroundStyle(.secondary)
        }
      }.frame(width: 26, height: 26)
      VStack(alignment: .leading, spacing: 4) {
        Text(title).font(.headline)
        Text(detail).font(.callout).foregroundStyle(.secondary).fixedSize(
          horizontal: false, vertical: true)
      }
      Spacer()
      Text(passed ? "Verified" : "Pending").font(.caption).foregroundStyle(
        passed ? .mint : .secondary)
    }
  }
}
