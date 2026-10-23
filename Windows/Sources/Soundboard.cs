using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security;
using System.Speech.AudioFormat;
using System.Speech.Synthesis;
using System.Threading;
using System.Threading.Tasks;

namespace OpenYap
{
    sealed class SoundClip
    {
        public string Id, Name, Path;
        public int Preset = -1;

        static readonly string[] builtInNames =
        {
            "Deep boom", "Chat ping", "Air horn", "Record scratch", "Sad trombone", "Crickets",
            "Rimshot", "Laser pew", "Level up", "Drama sting", "Spring boing", "Alarm",
        };
        public static readonly SoundClip[] BuiltIns = builtInNames.Select((name, index) =>
            new SoundClip { Id = "preset-" + index, Name = name, Preset = index }).ToArray();

        // Stable reaction groups keep familiar pads together; unknown imports remain discoverable.
        public static readonly string[] Reactions = { "Good / Happy", "Bad / Awkward", "Funny / Hype", "Suspense / Scary", "Calls / Alerts", "Other sounds" };

        public string Reaction
        {
            get
            {
                switch (Name.ToLowerInvariant())
                {
                    case "kids cheering": case "old spice whistle": case "level up": return Reactions[0];
                    case "bruh": case "windows xp error": case "roblox oof": case "wii sports aww": case "bonk":
                    case "cartoon slip": case "sad trombone": case "crickets": case "record scratch": return Reactions[1];
                    case "noot noot": case "fart with reverb": case "fbi open up": case "clash royale heheheha":
                    case "hog rider": case "air horn": case "rimshot": case "laser pew": case "spring boing": return Reactions[2];
                    case "suspense": case "horror movie suspense": case "horror drone": case "doors screech psst":
                    case "vine boom": case "drama sting": case "deep boom": return Reactions[3];
                    case "discord notification": case "discord incoming call": case "discord outgoing call":
                    case "discord user joined": case "discord user left": case "real door knocking": case "door knocking":
                    case "chat ping": case "alarm": return Reactions[4];
                    default: return Reactions[5];
                }
            }
        }
    }

    sealed class SoundboardLibrary
    {
        public List<SoundClip> Clips = new List<SoundClip>();
        bool showSynthesized;
        public bool Importing;
        public string Message = "Your imported recordings stay on this PC. Use Import sounds to add more.";
        public event Action Changed;
        readonly string directory;

        public SoundboardLibrary(string directory = null)
        {
            this.directory = directory ?? System.IO.Path.Combine(Settings.Folder, "Sounds");
            Refresh();
        }

        public string Directory { get { return directory; } }

        public bool ShowSynthesized
        {
            get { return showSynthesized; }
            set { showSynthesized = value; Refresh(); }
        }

        public void Refresh()
        {
            var files = System.IO.Directory.Exists(directory) ? System.IO.Directory.GetFiles(directory, "*.wav") : new string[0];
            var imported = files.Where(f => !f.EndsWith(".partial.wav", StringComparison.OrdinalIgnoreCase))
                .Select(f => new { Path = f, File = System.IO.Path.GetFileNameWithoutExtension(f) })
                .Where(f => f.File.Length > 37)
                .Select(f => new SoundClip { Id = System.IO.Path.GetFileName(f.Path), Name = f.File.Substring(37), Path = f.Path })
                .OrderBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase);
            Clips = (showSynthesized ? SoundClip.BuiltIns.AsEnumerable() : Enumerable.Empty<SoundClip>()).Concat(imported)
                .OrderBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
            if (Changed != null) Changed();
        }

        public async Task Import(string[] paths)
        {
            if (Importing) return;
            Importing = true;
            if (Changed != null) Changed();
            int successes = 0;
            var failures = new List<string>();
            try
            {
                foreach (var path in paths)
                {
                    if (Clips.Count(c => c.Path != null) >= 48) { failures.Add("The library holds up to 48 imported sounds."); break; }
                    try
                    {
                        string folder = directory;
                        System.IO.Directory.CreateDirectory(folder);
                        string name = System.IO.Path.GetFileNameWithoutExtension(path);
                        foreach (char c in System.IO.Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
                        string destination = System.IO.Path.Combine(folder, Guid.NewGuid().ToString().ToUpperInvariant() + "-" + name + ".wav");
                        await Task.Run(() =>
                        {
                            var samples = SoundAudio.Decode(path);
                            string temporary = System.IO.Path.Combine(folder, Guid.NewGuid() + ".partial.wav");
                            try
                            {
                                SoundAudio.Save(samples, temporary);
                                File.Move(temporary, destination);
                            }
                            finally { if (File.Exists(temporary)) File.Delete(temporary); }
                        });
                        successes++;
                        Refresh();
                    }
                    catch (Exception e) { failures.Add(System.IO.Path.GetFileName(path) + ": " + e.Message); }
                }
            }
            finally { Importing = false; }
            Message = failures.Count == 0 ? "Imported " + successes + " sound" + (successes == 1 ? "" : "s") + "."
                : "Imported " + successes + ". " + string.Join(" ", failures);
            if (Changed != null) Changed();
        }

        public void Remove(SoundClip clip)
        {
            if (clip.Path == null) return;
            try { File.Delete(clip.Path); Refresh(); }
            catch (Exception e) { Message = "Could not remove this sound: " + e.Message; if (Changed != null) Changed(); }
        }
    }

    // Renders speech into memory, never to a speaker or the default audio device.
    sealed class SpeechRenderer
    {
        SpeechSynthesizer synthesizer;
        TaskCompletionSource<bool> pending;
        readonly object gate = new object();

        public static List<string> Voices()
        {
            using (var s = new SpeechSynthesizer())
                return s.GetInstalledVoices().Where(v => v.Enabled)
                    .OrderBy(v => v.VoiceInfo.Culture.Name.StartsWith("en") ? 0 : 1).ThenBy(v => v.VoiceInfo.Name)
                    .Select(v => v.VoiceInfo.Name).ToList();
        }

        public static string Describe(string voice)
        {
            using (var s = new SpeechSynthesizer())
            {
                var info = s.GetInstalledVoices().Select(v => v.VoiceInfo).FirstOrDefault(v => v.Name == voice);
                return info == null ? voice : info.Name.Replace("Microsoft ", "").Replace(" Desktop", "") + " · " + info.Culture.Name;
            }
        }

        public void Cancel()
        {
            lock (gate)
            {
                if (synthesizer != null) { try { synthesizer.SpeakAsyncCancelAll(); } catch (Exception) { } }
                if (pending != null) pending.TrySetCanceled();
                pending = null;
            }
        }

        // style: 0 Natural, 1 Deep, 2 Tiny.
        public async Task<float[]> Render(string text, string voice, int style)
        {
            Cancel();
            text = (text ?? "").Trim();
            if (text.Length == 0 || text.Length > 220) throw new SoundProblem("Enter 1–220 characters to speak.");
            if (!Voices().Contains(voice)) throw new SoundProblem("That voice is unavailable. Choose another Windows voice.");
            var stream = new MemoryStream();
            var synth = new SpeechSynthesizer();
            var done = new TaskCompletionSource<bool>();
            lock (gate) { synthesizer = synth; pending = done; }
            try
            {
                synth.SelectVoice(voice);
                synth.SetOutputToAudioStream(stream, new SpeechAudioFormatInfo(48000, AudioBitsPerSample.Sixteen, AudioChannel.Stereo));
                // Windows desktop voices honor relative pitch more strongly than x-low/x-high (measured ≈ −23% / +24%).
                string pitch = style == 1 ? "-50%" : style == 2 ? "+80%" : "medium";
                string culture = synth.Voice.Culture.Name;
                string ssml = "<speak version=\"1.0\" xmlns=\"http://www.w3.org/2001/10/synthesis\" xml:lang=\"" + culture + "\">" +
                    "<prosody pitch=\"" + pitch + "\">" + SecurityElement.Escape(text) + "</prosody></speak>";
                synth.SpeakCompleted += (sender, e) =>
                {
                    if (e.Cancelled) done.TrySetCanceled();
                    else if (e.Error != null) done.TrySetException(new SoundProblem("This voice could not produce audio. Choose another voice."));
                    else done.TrySetResult(true);
                };
                synth.SpeakSsmlAsync(ssml);
                var finished = await Task.WhenAny(done.Task, Task.Delay(20000));
                if (finished != done.Task)
                {
                    synth.SpeakAsyncCancelAll();
                    throw new SoundProblem("This voice did not finish. Try a different voice or shorter message.");
                }
                await done.Task; // throws when cancelled
                synth.SetOutputToNull();
                var bytes = stream.ToArray();
                int frames = bytes.Length / 4;
                if (frames == 0) throw new SoundProblem("This voice produced no audio. Choose another voice.");
                if (frames > Soundboard.MaxFrames) throw new SoundProblem("That message is longer than 15 seconds. Shorten it and try again.");
                var samples = new float[frames * 2];
                for (int i = 0; i < samples.Length; i++) samples[i] = BitConverter.ToInt16(bytes, i * 2) / 32768f;
                return samples;
            }
            finally
            {
                lock (gate) { if (synthesizer == synth) { synthesizer = null; pending = null; } }
                synth.Dispose();
            }
        }
    }
}
