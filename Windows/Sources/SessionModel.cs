using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace OpenYap
{
    // The Windows counterpart of SessionModel.swift. Lives on the UI thread; the UI redraws on Changed.
    sealed class SessionModel
    {
        // Windows' volume for "silent": process loopback is captured after app volume, so the music app
        // is never fully muted. 0.0002 is −74 dB for local listening; the send divides it back out.
        public const float SilentFloor = 0.0002f;

        public readonly SoundboardLibrary SoundLibrary = new SoundboardLibrary();
        readonly SpeechRenderer speechRenderer = new SpeechRenderer();
        public float SoundHear = 0.35f;
        public float[] SoundMeters = { 0, 0 };
        public string PlayingSoundId;
        public string SoundStatus = "Tap a pad. A new sound replaces the previous one.";
        readonly Dictionary<string, int> soundSlots = new Dictionary<string, int>();
        int soundRequest;
        DateTime soundStartedAt = DateTime.MinValue;

        public List<AudioDevice> Devices = new List<AudioDevice>();
        public List<MusicApp> Apps = new List<MusicApp>();
        public string MusicApp = "spotify";
        public string DestinationApp = Settings.Get("destinationApp") ?? "robloxplayerbeta";
        public bool MonitorDestination = true;
        public string OutputId = "", MicId = "", VirtualId = Settings.Get("virtualId") ?? "";
        public bool IncludeVoice;
        public float[] Gains = { 0.25f, 1.0f, 0.7f, 0.8f, 0.65f };
        bool maxSharing;
        public bool[] Muted = new bool[5];
        public float[] Meters = new float[6];
        public bool Active, Busy, Cancelling, Interrupted;
        public string Status = "Ready when you are";
        public string Detail = "Play a song in Spotify, then start sharing.";
        public bool ReturnCheckEnabled;
        public string ReturnCheckMessage = "Enable the input check to measure the virtual cable's input. Windows may need microphone access for desktop apps.";
        public bool HeardMusic, HeardByPlayer;
        public bool[] Observed = new bool[6];
        public long Dropouts;
        public bool RecoveryPending, NormalOutputNeedsSetup, DuckingActive, BluetoothDefaultMic;
        public string DefaultOutputName = "";
        public event Action Changed;

        Mixer engine;
        readonly List<AudioStream> streams = new List<AudioStream>();
        CaptureStream returnStream;
        readonly VolumeJournal journal = new VolumeJournal(Path.Combine(Settings.Folder, "volume-journal.txt"));
        List<AppSession> sessions = new List<AppSession>();
        List<ProcessInfo> processes = new List<ProcessInfo>();
        HashSet<int> musicTree = new HashSet<int>(), destinationTree = new HashSet<int>();
        List<int> musicRoots = new List<int>();
        int ticks;
        string sessionOutputId = "", sessionMicId = "", sessionVirtualId = "";
        DateTime startedAt = DateTime.Now;
        readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer { Interval = 80 };

        public SessionModel()
        {
            Refresh();
            var output = Outputs.FirstOrDefault(d => d.Name.Contains("WH-1000XM5")) ?? Outputs.FirstOrDefault(d => d.Id == Hardware.DefaultId(EDataFlow.Render, ERole.Console));
            OutputId = output != null ? output.Id : "";
            var mic = Microphones.FirstOrDefault(d => d.Name.IndexOf("fifine", StringComparison.OrdinalIgnoreCase) >= 0);
            MicId = mic != null ? mic.Id : "";
            if (VirtualOutput == null && VirtualOutputs.Count > 0) VirtualId = VirtualOutputs[0].Id;
            RecoveryPending = journal.Pending;
            if (RecoveryPending)
            {
                Status = "Restoring app volumes from the last session";
                Detail = "OpenYap restores each app's own Windows volume as soon as that app plays audio again.";
            }
            timer.Tick += (s, e) => Poll();
            timer.Start();
        }

        void Notify() { if (Changed != null) Changed(); }

        public bool MaxSharing
        {
            get { return maxSharing; }
            set
            {
                maxSharing = value;
                if (value) Gains[1] = 1;
                ApplyGains();
            }
        }

        public List<AudioDevice> Outputs { get { return Devices.Where(d => d.IsRender && d.IsPhysical && d.Channels != 1).ToList(); } }
        public List<AudioDevice> Microphones { get { return Devices.Where(d => !d.IsRender && d.IsPhysical && !d.IsBluetooth).ToList(); } }
        public List<AudioDevice> VirtualOutputs { get { return Devices.Where(d => d.IsRender && d.IsVirtual).OrderBy(d => d.Name.StartsWith("CABLE Input") ? 0 : 1).ToList(); } }
        public AudioDevice SelectedOutput { get { return Outputs.FirstOrDefault(d => d.Id == OutputId); } }
        public AudioDevice VirtualOutput
        {
            get
            {
                return VirtualOutputs.FirstOrDefault(d => d.Id == VirtualId) ??
                    (TestAnyVirtual ? Devices.FirstOrDefault(d => d.IsRender && d.Id == VirtualId) : null);
            }
        }
        // --hardware-test only: lets a physical output stand in for the cable on a PC without one.
        public bool TestAnyVirtual;

        // The recording side of the selected cable, which the receiving app chooses as its microphone.
        public AudioDevice VirtualInput
        {
            get
            {
                var output = VirtualOutput;
                if (output == null) return null;
                return Devices.Where(d => !d.IsRender && d.IsVirtual)
                    .OrderBy(d => d.Interface == output.Interface ? 0 : 1)
                    .FirstOrDefault(d => d.Interface == output.Interface || d.Name.StartsWith("CABLE Output"));
            }
        }

        public string VirtualInputName
        {
            get
            {
                var input = VirtualInput;
                return input != null ? input.Name : VirtualOutput != null ? "the matching virtual cable input" : "CABLE Output (VB-Audio Virtual Cable)";
            }
        }

        public string AppName(string id)
        {
            var app = Apps.FirstOrDefault(a => a.Id == id);
            return app != null ? app.Name : id;
        }
        public string DestinationName { get { return AppName(DestinationApp); } }
        public List<MusicApp> MusicApps { get { return Apps.Where(a => AppRoutingPolicy.IsSeparate(a.Id, DestinationApp)).ToList(); } }
        public List<MusicApp> DestinationApps { get { return Apps.Where(a => AppRoutingPolicy.IsSeparate(MusicApp, a.Id)).ToList(); } }
        public string DestinationInstructions
        {
            get
            {
                var output = SelectedOutput;
                return "In " + DestinationName + ", choose " + VirtualInputName + " as microphone/input and " +
                    (output != null ? output.Name : "your headphones") + " as output.";
            }
        }

        public void Refresh()
        {
            Devices = Hardware.Devices();
            string defaultId = Hardware.DefaultId(EDataFlow.Render, ERole.Console);
            var normal = Devices.FirstOrDefault(d => d.Id == defaultId);
            DefaultOutputName = normal != null ? normal.Name : "";
            if (!Active && !Busy) NormalOutputNeedsSetup = normal != null && normal.IsVirtual;
            string defaultMic = Hardware.DefaultId(EDataFlow.Capture, ERole.Communications);
            BluetoothDefaultMic = Devices.Any(d => d.Id == defaultMic && d.IsBluetooth);
            processes = Processes.Snapshot();
            if (!Active) RefreshSessions();
            var running = Processes.RunningApps(processes, sessions.Select(s => s.ProcessId));
            foreach (var known in Processes.Known) if (!running.Any(a => a.Id == known.Id)) running.Add(known);
            foreach (var id in new[] { MusicApp, DestinationApp }) if (!running.Any(a => a.Id == id)) running.Add(new MusicApp(id, id));
            Apps = running.OrderBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
            DuckingActive = DuckingEnabled();
            if (!Active && journal.Pending)
            {
                journal.Restore(sessions);
                RecoveryPending = journal.Pending;
                if (!RecoveryPending && Status.StartsWith("Restoring")) { Status = "App volumes restored"; Detail = "Ready when you are."; }
            }
        }

        void RefreshSessions()
        {
            Hardware.Release(sessions);
            sessions = Hardware.Sessions();
        }

        // Windows lowers other apps' volume during calls unless Sound → Communications is set to "Do nothing".
        // That would lower the music captured for sharing too, so OpenYap warns about it.
        static bool DuckingEnabled()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Multimedia\Audio"))
                {
                    object value = key == null ? null : key.GetValue("UserDuckingPreference");
                    return !(value is int) || (int)value != 3;
                }
            }
            catch (Exception) { return true; }
        }

        public void UseSelectedAsNormalOutput()
        {
            var output = SelectedOutput;
            if (Active || Busy || output == null) return;
            try
            {
                Hardware.SetDefaultOutput(output.Id);
                Refresh();
                Status = "Normal output is ready";
                Detail = output.Name + " is now your Windows default output. Your virtual cable is unchanged, and the other app's microphone choice is untouched.";
            }
            catch (Exception e) { Fail(e.Message); }
            Notify();
        }

        public void ApplySoundGains()
        {
            if (engine == null) return;
            engine.SoundGain(0, SoundHear);
            engine.SoundGain(1, 1); // music + effects share gain[1] in the send mixer
        }

        public void PlaySound(SoundClip clip)
        {
            if (!Active || Busy || Interrupted || engine == null) return;
            speechRenderer.Cancel();
            int request = ++soundRequest;
            var current = engine;
            current.SoundPlay(-1); // stop the old clip immediately; decoding cannot accidentally resume it
            PlayingSoundId = clip.Id;
            SoundStatus = "Loading " + clip.Name + "…";
            Notify();
            LoadAndPlay(request, current, clip.Id, () => Task.Run(() => SoundAudio.Samples(clip)), "Playing " + clip.Name);
        }

        public void PlaySpeech(string text, string voice, int style)
        {
            if (!Active || Busy || Interrupted || engine == null) return;
            StopSounds();
            int request = soundRequest;
            string key = "speech:" + voice + ":" + style + ":" + text;
            PlayingSoundId = key;
            SoundStatus = "Loading voice…";
            Notify();
            LoadAndPlay(request, engine, key, () => speechRenderer.Render(text, voice, style), "Playing voice");
        }

        async void LoadAndPlay(int request, Mixer current, string key, Func<Task<float[]>> load, string playing)
        {
            try
            {
                int slot;
                if (!soundSlots.TryGetValue(key, out slot))
                {
                    var samples = await load();
                    if (request != soundRequest || !Active || Interrupted || engine != current) return;
                    slot = current.SoundLoad(samples);
                    if (slot < 0) throw new SoundProblem("Sound memory is full for this session. Restart sharing later to clear it.");
                    soundSlots[key] = slot;
                }
                if (request != soundRequest || !Active || Interrupted || engine != current) return;
                ApplySoundGains();
                current.SoundPlay(slot);
                soundStartedAt = DateTime.Now;
                SoundStatus = playing;
            }
            catch (TaskCanceledException) { return; }
            catch (Exception e)
            {
                if (request != soundRequest) return;
                PlayingSoundId = null;
                SoundStatus = e.Message;
            }
            Notify();
        }

        public void StopSounds()
        {
            speechRenderer.Cancel();
            soundRequest++;
            if (engine != null) engine.SoundPlay(-1);
            PlayingSoundId = null;
            SoundStatus = "Sounds stopped. Music sharing is unchanged.";
            SoundMeters = new float[] { 0, 0 };
        }

        public float EffectiveGain(int control)
        {
            if (Muted[control] || (control == 2 && !IncludeVoice)) return 0;
            return control == 1 && maxSharing ? 1 : Gains[control];
        }

        float MusicLevel { get { return Math.Max(SilentFloor, EffectiveGain(0) * EffectiveGain(4)); } }
        float DestinationLevel { get { return EffectiveGain(3) * EffectiveGain(4); } }

        public void ApplyGains()
        {
            if (engine == null) return;
            engine.SetMusicBoost(maxSharing);
            for (int i = 0; i < 5; i++) engine.SetGain(i, EffectiveGain(i));
            ApplyAppVolumes();
        }

        // Music and call/game listening levels are those apps' own Windows volumes while sharing.
        void ApplyAppVolumes()
        {
            if (engine == null || !Active) return;
            // Read first: an outside change (Volume Mixer) must lower compensation before OpenYap resets it.
            float before = VolumeJournal.Loudest(sessions, musicTree);
            engine.SetMusicVolume(before < 0 ? 0 : before);
            bool ok = journal.Apply(sessions, musicTree, MusicLevel);
            if (ok && MonitorDestination) ok = journal.Apply(sessions, destinationTree, DestinationLevel);
            float after = VolumeJournal.Loudest(sessions, musicTree);
            engine.SetMusicVolume(after < 0 ? 0 : after);
            if (!ok && !Interrupted) PauseSafely("OpenYap could not save its volume recovery file, so it will not change app volumes. Check that " + Settings.Folder + " is writable, then stop and start again.");
        }

        public async Task Start()
        {
            if (Busy || Active) return;
            Refresh();
            Busy = true;
            Cancelling = false;
            Status = "Connecting audio";
            Detail = "Windows is starting app audio capture. Audio stays on this PC.";
            Notify();
            try
            {
                var output = SelectedOutput;
                if (output == null) { Fail("Choose your headphones or speakers."); return; }
                var cable = VirtualOutput;
                if (cable == null) { Fail("No virtual cable found. Install VB-CABLE (free, from vb-audio.com), restart OpenYap, and choose CABLE Input under Virtual microphone. OpenYap does not install drivers."); return; }
                if (!AppRoutingPolicy.IsSeparate(MusicApp, DestinationApp))
                {
                    Fail("Choose different apps for music and calls. App capture cannot separate browser tabs or windows; capturing the call app as music would cause feedback.");
                    return;
                }
                musicRoots = Processes.Roots(processes, MusicApp).Take(Mixer.MusicSlots).ToList();
                musicTree = Processes.Tree(processes, MusicApp);
                destinationTree = MonitorDestination ? Processes.Tree(processes, DestinationApp) : new HashSet<int>();
                if (musicRoots.Count == 0)
                {
                    Fail("Open " + AppName(MusicApp) + " before starting. Play some audio in it first so OpenYap can find it.");
                    return;
                }
                if (musicTree.Overlaps(destinationTree))
                {
                    Fail("These apps share a process. Use separate applications for music and calls to prevent feedback.");
                    return;
                }
                AudioDevice mic = null;
                if (IncludeVoice)
                {
                    mic = Microphones.FirstOrDefault(d => d.Id == MicId);
                    if (mic == null) { Fail("Choose a separate microphone. Bluetooth microphone input is excluded to preserve headphone quality."); return; }
                }
                if (NormalOutputNeedsSetup)
                {
                    Fail("Your Windows default output is a virtual cable, so everything you hear would be shared. Use your selected listening device for normal audio first.");
                    return;
                }
                Observed = new bool[6];
                HeardMusic = HeardByPlayer = false;
                var r = new Mixer();
                engine = r;
                r.Pause(true);
                r.SetMusicVolume(0); // silent until the app's real volume is known
                // Sinks first so the music and microphone rings are prepared for the send clock.
                await StartStream(new RenderStream(r, Mixer.Send, cable.Id, "Virtual microphone"));
                await StartStream(new RenderStream(r, Mixer.Listening, output.Id, output.Name));
                for (int i = 0; i < musicRoots.Count; i++)
                    await StartStream(new CaptureStream(r, CaptureStream.Kind.ProcessTree, i, null, musicRoots[i], AppName(MusicApp)));
                if (mic != null) await StartStream(new CaptureStream(r, CaptureStream.Kind.Microphone, Mixer.Mic, mic.Id, 0, mic.Name));
                if (Cancelling)
                {
                    Cleanup();
                    Status = "Connection cancelled";
                    return;
                }
                sessionOutputId = output.Id;
                sessionMicId = mic != null ? mic.Id : "";
                sessionVirtualId = cable.Id;
                Active = true;
                Interrupted = false;
                RefreshSessions();
                ApplyGains();
                ApplySoundGains();
                r.Pause(false);
                startedAt = DateTime.Now;
                Status = "Sharing music";
                Detail = DestinationInstructions;
                Settings.Set("destinationApp", DestinationApp);
                Settings.Set("virtualId", VirtualId);
                RecoveryPending = journal.Pending;
            }
            catch (Exception e)
            {
                Cleanup();
                Fail(e.Message);
            }
            finally
            {
                Busy = false;
                Cancelling = false;
                Notify();
            }
        }

        async Task StartStream(AudioStream stream)
        {
            streams.Add(stream);
            await Task.Run(() => stream.Start());
        }

        public void PauseForSleep()
        {
            if (!Active || engine == null)
            {
                if (Busy) Cancelling = true;
                return;
            }
            PauseSafely("After waking, stop and start sharing again when your devices are connected.");
            Status = "Sharing paused for sleep";
            Notify();
        }

        void PauseSafely(string detail)
        {
            StopSounds();
            if (engine != null) engine.Pause(true);
            Interrupted = true;
            Status = "Sharing paused safely";
            Detail = detail;
        }

        public void Stop()
        {
            if (Busy)
            {
                Cancelling = true;
                Status = "Cancelling connection";
                Detail = "Waiting for Windows to finish its audio request; no mix will be sent.";
                Notify();
                return;
            }
            bool wasActive = Active;
            Cleanup();
            if (wasActive || journal.Pending)
            {
                Status = RecoveryPending ? "Sharing stopped · volume restore pending" : "Sharing stopped";
                Detail = RecoveryPending
                    ? "Some app volumes could not be restored because those apps are closed. OpenYap restores them automatically when they play again."
                    : "App volumes have been restored. Your apps now play normally.";
            }
            Notify();
        }

        void Cleanup()
        {
            StopSounds();
            soundSlots.Clear();
            if (engine != null) engine.Pause(true);
            foreach (var s in streams.AsEnumerable().Reverse()) s.Stop();
            streams.Clear();
            returnStream = null;
            engine = null;
            RefreshSessions();
            journal.Restore(sessions);
            ReturnCheckEnabled = false;
            Active = false;
            Interrupted = false;
            Meters = new float[6];
            RecoveryPending = journal.Pending;
        }

        public void Restore()
        {
            Stop();
            if (Busy) return;
            RefreshSessions();
            journal.Restore(sessions);
            RecoveryPending = journal.Pending;
            if (!RecoveryPending && !Active) { Status = "Normal audio restored"; Detail = "Every app volume OpenYap changed is back to where it was."; }
            Notify();
        }

        void Fail(string message)
        {
            Status = "Needs attention";
            Detail = message;
        }

        void Poll()
        {
            if (Busy) return;
            ticks++;
            if (ticks % 12 == 0) Refresh();
            if (!Active || engine == null)
            {
                if (ticks % 12 == 0) Notify();
                return;
            }
            if (ticks % 6 == 0)
            {
                RefreshSessions();
                musicTree = Processes.Tree(processes, MusicApp);
                if (MonitorDestination) destinationTree = Processes.Tree(processes, DestinationApp);
                destinationTree.ExceptWith(musicTree);
            }
            if (!Interrupted) ApplyAppVolumes();
            for (int i = 0; i < 2; i++) SoundMeters[i] = engine.SoundMeter(i);
            if (PlayingSoundId != null && (DateTime.Now - soundStartedAt).TotalSeconds > 0.3 && !SoundStatus.StartsWith("Loading") && !engine.SoundPlaying)
            {
                PlayingSoundId = null;
                SoundStatus = "Ready for another sound.";
            }
            for (int i = 0; i < 6; i++) Meters[i] = engine.Meter(i);
            for (int i = 0; i < 6; i++) if (Meters[i] > 0.002f) Observed[i] = true;
            Dropouts = engine.Dropouts;
            bool outputGone = !Devices.Any(d => d.Id == sessionOutputId);
            bool micGone = IncludeVoice && !Devices.Any(d => d.Id == sessionMicId);
            bool cableGone = !Devices.Any(d => d.Id == sessionVirtualId);
            string defaultId = Hardware.DefaultId(EDataFlow.Render, ERole.Console);
            bool defaultSafe = !Devices.Any(d => d.Id == defaultId && d.IsVirtual);
            bool streamFailed = streams.Any(s => s.Failed);
            if (!Interrupted && (RoutingPolicy.ShouldPause(!outputGone, IncludeVoice, !micGone, !cableGone, defaultSafe) || streamFailed || engine.IsPaused))
            {
                PauseSafely(!defaultSafe
                    ? "Your Windows default output changed to a virtual cable, which would share everything you hear. OpenYap is silent. Choose your headphones as the default output, then stop and start again."
                    : "An audio device changed or disconnected. OpenYap is silent. Reconnect your devices, then stop and start again. No automatic speaker fallback.");
            }
            if (!Interrupted && Observed[0] && Detail.StartsWith("No music signal yet")) Detail = DestinationInstructions;
            if (!Interrupted && (DateTime.Now - startedAt).TotalSeconds > 8 && !Observed[0])
                Detail = "No music signal yet. Play a song in " + AppName(MusicApp) + ". If it was restarted after sharing began, stop and start again. Protected audio may be unavailable.";
            Notify();
        }

        public async Task EnableReturnCheck(bool useRenderLoopback = false)
        {
            var input = VirtualInput;
            if (!Active || engine == null || (input == null && !useRenderLoopback))
            {
                ReturnCheckMessage = input == null ? "No matching virtual cable input was found to measure." : "Start sharing before enabling this check.";
                Notify();
                return;
            }
            if (Busy || ReturnCheckEnabled) return;
            Busy = true;
            try
            {
                var stream = useRenderLoopback
                    ? new CaptureStream(engine, CaptureStream.Kind.RenderLoopback, 0, sessionVirtualId, 0, "Virtual microphone loopback")
                    : new CaptureStream(engine, CaptureStream.Kind.Return, 0, input.Id, 0, input.Name);
                await StartStream(stream);
                returnStream = stream;
                ReturnCheckEnabled = true;
                ReturnCheckMessage = "Measuring " + (useRenderLoopback ? "the virtual microphone output" : input.Name) + ". Other apps writing to the cable can also appear here.";
            }
            catch (Exception e) { ReturnCheckMessage = e.Message; }
            finally
            {
                Busy = false;
                if (Cancelling) { Cancelling = false; Stop(); }
                Notify();
            }
        }

        public void TestSetup()
        {
            Observed = new bool[6];
            HeardMusic = HeardByPlayer = false;
            Notify();
        }

        public static void OpenSoundSettings(int tab)
        {
            try { Process.Start("control.exe", "mmsys.cpl,," + tab); } catch (Exception) { }
        }

        public static void OpenPrivacy()
        {
            try { Process.Start("ms-settings:privacy-microphone"); } catch (Exception) { }
        }

        public void Shutdown()
        {
            timer.Stop();
            if (Busy) Cancelling = true;
            Cleanup();
        }

        // --hardware-test: a scripted 20-second session with every gain at zero (or the given levels).
        public async Task<int> HardwareTest(string[] args, Action<string> log)
        {
            Func<string, string> arg = name => { int i = Array.IndexOf(args, name); return i >= 0 && i + 1 < args.Length ? args[i + 1] : null; };
            if (arg("--music") != null) MusicApp = arg("--music");
            if (arg("--destination") != null) DestinationApp = arg("--destination");
            if (arg("--virtual") != null) { VirtualId = arg("--virtual"); TestAnyVirtual = true; }
            if (arg("--output") != null) OutputId = arg("--output");
            MonitorDestination = !args.Contains("--no-monitor");
            string savedDestination = Settings.Get("destinationApp"), savedVirtual = Settings.Get("virtualId");
            var levels = (arg("--gains") ?? "0,0,0,0,0").Split(',').Select(s => float.Parse(s, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
            for (int i = 0; i < 5; i++) Gains[i] = levels[i];
            int seconds = int.Parse(arg("--seconds") ?? "20");
            Refresh();
            await Start();
            log("START active=" + Active + " status=" + Status + " detail=" + Detail);
            if (Active) await EnableReturnCheck(args.Contains("--return-loopback"));
            var sweep = arg("--hear-sweep");
            float expectSend = arg("--expect-send") != null ? float.Parse(arg("--expect-send"), System.Globalization.CultureInfo.InvariantCulture) : 0;
            int violations = 0;
            for (int second = 0; second < seconds; second++)
            {
                if (sweep != null && second > 0 && second % 3 == 0)
                {
                    var steps = sweep.Split(',');
                    Gains[0] = float.Parse(steps[(second / 3 - 1) % steps.Length], System.Globalization.CultureInfo.InvariantCulture);
                    ApplyGains();
                }
                for (int i = 0; i < 12; i++) await Task.Delay(80);
                float appLevel = VolumeJournal.Loudest(sessions, musicTree);
                log(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                    "HARDWARE active={0} paused={1} appVolume={2:0.0000} compensation={3:0.0} music={4:E2} mic={5:E2} listen={6:E2} send={7:E2} return={8:E2} drops={9} | {10} | {11}",
                    Active, Interrupted, appLevel, engine != null ? engine.MusicCompensation : 0, Meters[0], Meters[1], Meters[3], Meters[4], Meters[5], Dropouts, Detail, ReturnCheckMessage));
                if (expectSend > 0 && Math.Abs(Meters[4] / expectSend - 1) > 0.2f)
                {
                    violations++;
                    log("FAIL send level " + Meters[4] + " is not within 20% of " + expectSend + " at app volume " + appLevel);
                }
            }
            int result = Active && !Interrupted && violations == 0 ? 0 : 1;
            Stop();
            Settings.Set("destinationApp", savedDestination);
            Settings.Set("virtualId", savedVirtual);
            log("RESTORED pending=" + RecoveryPending + " " + Detail);
            return result;
        }
    }
}
