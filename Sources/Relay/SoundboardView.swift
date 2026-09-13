import SwiftUI

struct SoundboardView: View {
  @Bindable var model: SessionModel
  @State private var expanded = true
  @State private var search = ""
  private var clips: [SoundClip] {
    model.soundLibrary.clips.filter { search.isEmpty || $0.name.localizedCaseInsensitiveContains(search) }
  }
  var body: some View {
    DisclosureGroup(isExpanded: $expanded) {
      VStack(alignment: .leading, spacing: 12) {
        HStack {
          TextField("Find a sound…", text: $search).textFieldStyle(.roundedBorder)
          Button("Import sounds…", systemImage: "plus", action: model.soundLibrary.importSounds)
            .disabled(model.soundLibrary.importing)
        }
        LazyVGrid(columns: Array(repeating: GridItem(.flexible(), spacing: 8), count: 3), spacing: 8) {
          ForEach(clips) { clip in
            Button { model.playSound(clip) } label: {
              HStack(spacing: 8) {
                Image(systemName: clip.symbol).frame(width: 20)
                Text(clip.name).lineLimit(2).frame(maxWidth: .infinity, alignment: .leading)
              }.font(.callout.weight(.medium)).padding(10).frame(minHeight: 42)
                .background(model.playingSoundID == clip.id ? Color.orange.opacity(0.25) : Color.secondary.opacity(0.1), in: RoundedRectangle(cornerRadius: 9))
            }.buttonStyle(.plain).disabled(!model.active || model.interrupted || model.busy)
              .accessibilityLabel("Play \(clip.name) to me and others")
              .contextMenu {
                if clip.url != nil {
                  Button("Remove sound", role: .destructive) { model.soundLibrary.remove(clip) }
                }
              }
          }
        }
        HStack(spacing: 18) {
          VStack(alignment: .leading, spacing: 4) {
            Text("Effects I hear · \(Int(model.soundHear * 100))%").font(.caption)
            Slider(value: $model.soundHear, in: 0...1).tint(.mint).accessibilityLabel("Effects I hear")
            LevelMeter(level: model.soundMeters[0], tint: .mint)
          }
          VStack(alignment: .leading, spacing: 4) {
            Text("Effects others hear · \(Int(model.soundSend * 100))%").font(.caption)
            Slider(value: $model.soundSend, in: 0...1).tint(.orange).accessibilityLabel("Effects others hear")
            LevelMeter(level: model.soundMeters[1], tint: .orange)
          }
        }
        HStack {
          Text(model.active ? model.soundStatus : "Start sharing to play sounds to your selected device and BlackHole.")
            .font(.caption).foregroundStyle(.secondary)
          Spacer()
          Button("Stop sounds", systemImage: "stop.fill", action: model.stopSounds)
            .tint(.orange)
        }
        Text(model.soundLibrary.message).font(.caption).foregroundStyle(.secondary)
          .fixedSize(horizontal: false, vertical: true)
      }.padding(.top, 10)
    } label: {
      Label("Soundboard", systemImage: "square.grid.3x3.fill").font(.headline)
    }
    .padding(14).background(.quaternary.opacity(0.5), in: RoundedRectangle(cornerRadius: 12))
    .onChange(of: model.soundHear) { model.applySoundGains() }
    .onChange(of: model.soundSend) { model.applySoundGains() }
  }
}
