import AVFoundation
import SwiftUI

struct SpeechView: View {
  @Bindable var model: SessionModel
  @State private var message = ""
  @State private var voiceID = AVSpeechSynthesisVoice(language: "en-US")?.identifier ?? ""
  @State private var pitch: Float = 1
  private let voices = AVSpeechSynthesisVoice.speechVoices().sorted {
    if $0.language.hasPrefix("en") != $1.language.hasPrefix("en") { return $0.language.hasPrefix("en") }
    return $0.name < $1.name
  }

  var body: some View {
    VStack(alignment: .leading, spacing: 10) {
      Label("Say something", systemImage: "text.bubble.fill").font(.headline)
      TextField("Type a message to play…", text: $message, axis: .vertical)
        .lineLimit(2...4).textFieldStyle(.roundedBorder)
        .accessibilityLabel("Message to speak")
      HStack {
        Picker("Voice", selection: $voiceID) {
          ForEach(voices, id: \.identifier) { voice in
            Text("\(voice.name) · \(voice.language)").tag(voice.identifier)
          }
        }
        Picker("Style", selection: $pitch) {
          Text("Natural").tag(Float(1))
          Text("Deep").tag(Float(0.65))
          Text("Tiny").tag(Float(1.6))
        }.frame(maxWidth: 160)
        Button("Play voice", systemImage: "play.fill") {
          model.playSpeech(text: message, voiceID: voiceID, pitch: pitch)
        }
        .disabled(!model.active || model.busy || model.interrupted || voiceID.isEmpty || message.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty || message.count > 220)
      }
      Text("\(message.count)/220 characters · Up to 15 seconds. Uses the effects listening level and shared music + effects volume, mute, and Max. Stop sounds stops speech too.")
        .font(.caption).foregroundStyle(.secondary).fixedSize(horizontal: false, vertical: true)
    }
    .padding(12).background(.quaternary.opacity(0.5), in: RoundedRectangle(cornerRadius: 10))
  }
}
