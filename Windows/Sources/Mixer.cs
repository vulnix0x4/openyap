// Real-time mixer: the Windows counterpart of Sources/AudioCore/AudioCore.c.
// Capture threads push into preallocated single-producer/single-consumer rings; render threads pull.
// Audio threads never allocate, lock, log, or touch the UI. Shared values use volatile reads/writes.
using System;
using System.Threading;

namespace OpenYap
{
    sealed class Ring
    {
        public const int Cap = 32768;
        public readonly float[] Data = new float[Cap * 2];
        public long Write, Read;
        public double Phase;
        public bool Primed;
        public readonly float[] Filter = new float[Mixer.Phases * Mixer.Taps];
        public double Rate; // producer rate; 0 = unused
    }

    sealed class Mixer
    {
        public const int Taps = 32, Phases = 256;
        // Sources: music captures 0..2 (one per process tree root), microphone 3.
        public const int MusicSlots = 3, Mic = 3, SourceCount = 4;
        // Sinks: listening device 0 (soundboard effects only), virtual cable 1 (the shared mix).
        public const int Listening = 0, Send = 1;
        // Gains, matching the macOS indexes: music hear, music+effects send, mic, game hear, master.
        // Music and game listening levels are applied to those apps' own Windows volumes, not here.
        public const int GainSend = 1, GainMic = 2, GainMaster = 4;
        // Meters: music, mic, game (unused), listening, send, virtual input return.
        public const int MeterMusic = 0, MeterMic = 1, MeterListening = 3, MeterSend = 4, MeterReturn = 5;
        // Never trust more than this compensation (1 / 0.0002): the floor volume OpenYap uses for "silent".
        public const float MaxCompensation = 5000f;

        public readonly Soundboard Board = new Soundboard();
        readonly Ring[] rings = new Ring[SourceCount];
        readonly float[] gain = new float[5];
        readonly float[] meter = new float[6];
        readonly float[][] smooth = { new float[5], new float[5] };
        readonly double[] sinkRate = new double[2];
        int paused, musicBoost;
        long drops;
        // Process loopback is captured after the app's own Windows volume (measured: source × volume).
        // compensationTarget is published by the control thread; the send sink applies decreases at
        // once and increases only after 150 ms, so a volume change can dip but never spike.
        float compensationTarget = 1, compensation = 1, pendingCompensation = 1;
        long compensationClock = 1;
        readonly long[] historyTime = new long[32];
        readonly float[] historyValue = new float[32];
        int historyNext;

        public Mixer()
        {
            for (int i = 0; i < SourceCount; i++) rings[i] = new Ring();
            historyTime[0] = compensationClock;
            historyValue[0] = 1;
            historyNext = 1;
            float[] g = { .25f, .65f, .7f, .8f, .65f };
            for (int i = 0; i < 5; i++) gain[i] = g[i];
            Volatile.Write(ref Board.Gain[0], .35f);
            Volatile.Write(ref Board.Gain[1], .7f);
            Board.Prepare(0, 48000);
            Board.Prepare(1, 48000);
        }

        static float Clamp(float x, float a, float b) { return Math.Max(a, Math.Min(b, x)); }
        static bool Finite(float x) { return !float.IsNaN(x) && !float.IsInfinity(x); }

        public static float Protect(float x)
        {
            if (!Finite(x)) return 0;
            float a = Math.Abs(x);
            return a <= 0.8f ? x : (float)(Math.Sign(x) * (0.8 + 0.18 * Math.Tanh((a - 0.8) / 0.18)));
        }

        public static void PrepareFilter(float[] filter, double ratio)
        {
            double cutoff = 0.94 / Math.Max(1, ratio);
            for (int p = 0; p < Phases; p++)
            {
                double sum = 0;
                for (int t = 0; t < Taps; t++)
                {
                    double x = t - 15 - (double)p / Phases;
                    double sinc = Math.Abs(x) < 1e-9 ? cutoff : Math.Sin(Math.PI * cutoff * x) / (Math.PI * x);
                    double window = 0.42 - 0.5 * Math.Cos(2 * Math.PI * t / (Taps - 1)) + 0.08 * Math.Cos(4 * Math.PI * t / (Taps - 1));
                    filter[p * Taps + t] = (float)(sinc * window);
                    sum += sinc * window;
                }
                for (int t = 0; t < Taps; t++) filter[p * Taps + t] = (float)(filter[p * Taps + t] / sum);
            }
        }

        // Control thread, before the matching stream starts.
        public void ConfigureSource(int source, double rate)
        {
            Ring q = rings[source];
            q.Rate = rate;
            PrepareFilter(q.Filter, rate > 0 && sinkRate[Send] > 0 ? rate / sinkRate[Send] : 1);
        }

        public void ConfigureSink(int sink, double rate)
        {
            sinkRate[sink] = rate;
            Board.Prepare(sink, rate);
            if (sink == Send)
                for (int s = 0; s < SourceCount; s++)
                    if (rings[s].Rate > 0) PrepareFilter(rings[s].Filter, rings[s].Rate / rate);
        }

        void Push(Ring q, float l, float r)
        {
            long w = Volatile.Read(ref q.Write), rd = Volatile.Read(ref q.Read);
            if (w - rd >= Ring.Cap - 1) return;
            int i = (int)(w % Ring.Cap) * 2;
            q.Data[i] = l;
            q.Data[i + 1] = r;
            Volatile.Write(ref q.Write, w + 1);
        }

        void Pull(Ring q, double ratio, double rate, ref float outL, ref float outR)
        {
            outL = outR = 0;
            long rd = Volatile.Read(ref q.Read), w = Volatile.Read(ref q.Write);
            long n = w - rd;
            double target = rate * 0.045;
            if (!q.Primed)
            {
                if (n < target) return;
                q.Primed = true;
                q.Phase = 0;
            }
            if (n > rate * 0.18)
            {
                rd = w - (long)target;
                q.Phase = 0;
                n = w - rd;
                Volatile.Write(ref q.Read, rd);
                Interlocked.Increment(ref drops);
            }
            if (n < Taps + 2)
            {
                q.Primed = false;
                Interlocked.Increment(ref drops);
                return;
            }
            // Slowly adapt each consumer to its own hardware clock. No shared output clock.
            double correction = Clamp((float)((n - target) / target * 0.002), -0.005f, 0.005f);
            int phase = (int)(q.Phase * Phases), f = phase * Taps;
            for (int t = 0; t < Taps; t++)
            {
                int i = (int)((rd + t) % Ring.Cap) * 2;
                float k = q.Filter[f + t];
                outL += q.Data[i] * k;
                outR += q.Data[i + 1] * k;
            }
            q.Phase += ratio * (1 + correction);
            long step = (long)q.Phase;
            q.Phase -= step;
            Volatile.Write(ref q.Read, rd + (step < n ? step : n));
        }

        // Capture thread. Interleaved stereo float frames.
        public void Capture(int source, float[] data, int frames, bool silent)
        {
            float peak = 0;
            Ring q = rings[source];
            bool music = source < MusicSlots;
            for (int f = 0; f < frames; f++)
            {
                float l = silent ? 0 : data[f * 2], r = silent ? 0 : data[f * 2 + 1];
                if (!Finite(l)) l = 0;
                if (!Finite(r)) r = 0;
                if (!music) l = r = (l + r) * 0.5f;
                peak = Math.Max(peak, Math.Max(Math.Abs(l), Math.Abs(r)));
                Push(q, l, r);
            }
            int m = music ? MeterMusic : MeterMic;
            if (music)
            {
                // Show the app's own (pre-volume) level, like the macOS tap, across all music roots.
                peak = Math.Min(1, peak * Volatile.Read(ref compensationTarget));
                if (source != 0) peak = Math.Max(peak, Volatile.Read(ref meter[m]));
            }
            Volatile.Write(ref meter[m], peak);
        }

        public void ReturnMeter(float[] data, int frames, bool silent)
        {
            float peak = 0;
            if (!silent)
                for (int i = 0; i < frames * 2; i++) peak = Math.Max(peak, Math.Abs(data[i]));
            Volatile.Write(ref meter[MeterReturn], Finite(peak) ? peak : 0);
        }

        // Render thread. Fills interleaved stereo float frames.
        public void Render(int sink, float[] output, int frames)
        {
            double rate = sinkRate[sink];
            bool isPaused = Volatile.Read(ref paused) != 0;
            float peak = 0, boardPeak = 0;
            float[] sm = smooth[sink];
            if (sink == Send) UpdateCompensation(frames, rate);
            float comp = compensation;
            for (int f = 0; f < frames; f++)
            {
                float el, er;
                Board.Sample(sink, rate, isPaused, out el, out er);
                float musicL = 0, musicR = 0, micL = 0, micR = 0;
                if (sink == Send)
                {
                    for (int s = 0; s < SourceCount; s++)
                    {
                        Ring q = rings[s];
                        if (q.Rate <= 0) continue;
                        float l = 0, r = 0;
                        Pull(q, q.Rate / rate, q.Rate, ref l, ref r);
                        if (s == Mic) { micL = l; micR = r; }
                        else { musicL += l * comp; musicR += r * comp; }
                    }
                }
                for (int g = 0; g < 5; g++)
                {
                    float target = Volatile.Read(ref gain[g]); // exact mute, ramp unmute/gain changes
                    if (sink == Send && g == GainSend && Volatile.Read(ref musicBoost) != 0)
                        target *= 2.0f; // +6 dB on shared music and effects, before output protection
                    sm[g] = target == 0 ? 0 : sm[g] + (target - sm[g]) * 0.002f;
                }
                float xl, xr;
                if (sink == Listening)
                {
                    xl = el * sm[GainMaster];
                    xr = er * sm[GainMaster];
                    boardPeak = Math.Max(boardPeak, Math.Max(Math.Abs(xl), Math.Abs(xr)));
                }
                else
                {
                    xl = (musicL + el) * sm[GainSend] + micL * sm[GainMic];
                    xr = (musicR + er) * sm[GainSend] + micR * sm[GainMic];
                    boardPeak = Math.Max(boardPeak, Math.Max(Math.Abs(el), Math.Abs(er)) * sm[GainSend]);
                }
                xl = isPaused ? 0 : Protect(xl);
                xr = isPaused ? 0 : Protect(xr);
                output[f * 2] = xl;
                output[f * 2 + 1] = xr;
                peak = Math.Max(peak, Math.Max(Math.Abs(xl), Math.Abs(xr)));
            }
            Volatile.Write(ref meter[MeterListening + sink], peak);
            Volatile.Write(ref Board.Meter[sink], boardPeak);
        }

        // compensation = the smallest target published during the last 150 ms. Audio captured before a
        // volume change is still buffered for roughly that long, so it is never multiplied by a larger
        // compensation meant for a quieter app volume.
        void UpdateCompensation(int frames, double rate)
        {
            compensationClock += frames;
            float target = Volatile.Read(ref compensationTarget);
            if (target != pendingCompensation)
            {
                pendingCompensation = target;
                historyTime[historyNext] = compensationClock;
                historyValue[historyNext] = target;
                historyNext = (historyNext + 1) % historyTime.Length;
            }
            long start = compensationClock - (long)(rate * 0.15);
            float result = target, atStart = float.MaxValue;
            long atStartTime = long.MinValue;
            for (int i = 0; i < historyTime.Length; i++)
            {
                if (historyTime[i] == 0) continue;
                if (historyTime[i] > start) result = Math.Min(result, historyValue[i]);
                else if (historyTime[i] > atStartTime) { atStartTime = historyTime[i]; atStart = historyValue[i]; }
            }
            compensation = Math.Min(result, atStart);
        }

        // volume = the music app's current Windows session volume (the loudest of its sessions).
        public void SetMusicVolume(float volume)
        {
            float v = Finite(volume) ? Clamp(volume, 0, 1) : 0;
            float c = v <= 0 ? 0 : Math.Min(MaxCompensation, 1 / v);
            Volatile.Write(ref compensationTarget, c);
        }

        public float MusicCompensation { get { return Volatile.Read(ref compensationTarget); } }

        public void SetGain(int control, float g)
        {
            if (control >= 0 && control < 5) Volatile.Write(ref gain[control], Finite(g) ? Clamp(g, 0, 1) : 0);
        }

        public void SetMusicBoost(bool enabled) { Volatile.Write(ref musicBoost, enabled ? 1 : 0); }
        public void Pause(bool p) { Volatile.Write(ref paused, p ? 1 : 0); }
        public bool IsPaused { get { return Volatile.Read(ref paused) != 0; } }
        public float Meter(int m) { return Volatile.Read(ref meter[m]); }
        public long Dropouts { get { return Interlocked.Read(ref drops); } }

        public int SoundLoad(float[] samples) { return Board.Load(samples); }
        public void SoundPlay(int slot) { Board.Trigger(slot); }
        public void SoundGain(int sink, float g)
        {
            if (sink >= 0 && sink < 2) Volatile.Write(ref Board.Gain[sink], Finite(g) ? Clamp(g, 0, 1) : 0);
        }
        public bool SoundPlaying
        {
            get
            {
                if (IsPaused) return false;
                long command = Interlocked.Read(ref Board.Command);
                return (command & 255) != 0 && (Interlocked.Read(ref Board.Playing[0]) == command || Interlocked.Read(ref Board.Playing[1]) == command);
            }
        }
        public float SoundMeter(int sink) { return sink >= 0 && sink < 2 ? Volatile.Read(ref Board.Meter[sink]) : 0; }

        // Mirrors router_selftest plus the Windows-specific compensation rules.
        public static int SelfTest(Action<string> log)
        {
            int fail = 0;
            Action<bool, string> expect = (ok, what) => { if (!ok) { fail++; log("FAIL " + what); } };
            var m = new Mixer();
            float[] g = { .2f, .7f, .4f, .8f, .5f };
            for (int i = 0; i < 5; i++) { m.SetGain(i, g[i]); m.smooth[0][i] = g[i]; m.smooth[1][i] = g[i]; }
            m.ConfigureSink(Send, 48000);
            m.ConfigureSink(Listening, 48000);
            m.ConfigureSource(0, 48000);
            m.ConfigureSource(Mic, 48000);
            var input = new float[512];
            var output = new float[512];
            // Music at 0.1, microphone at 0.4.
            for (int b = 0; b < 12; b++)
            {
                for (int i = 0; i < 512; i++) input[i] = .1f;
                m.Capture(0, input, 256, false);
                for (int i = 0; i < 512; i++) input[i] = .4f;
                m.Capture(Mic, input, 256, false);
            }
            m.Render(Send, output, 256);
            expect(Math.Abs(output[100] - (.1f * .7f + .4f * .4f)) < .001f, "send = music·send + mic·mic");
            m.Render(Listening, output, 256);
            expect(output[100] == 0, "listening carries no music or microphone");
            m.SetGain(GainMaster, 0);
            m.Render(Send, output, 256);
            expect(Math.Abs(output[100] - .23f) < .001f, "master listening never changes the send");
            m.SetGain(GainSend, 0);
            m.Render(Send, output, 256);
            expect(Math.Abs(output[100] - .16f) < .001f, "send mute keeps the microphone");
            m.SetGain(GainMic, 0);
            m.Render(Send, output, 256);
            expect(output[100] == 0, "all send mutes are exact");
            for (int i = -1000; i <= 1000; i++) if (Math.Abs(Protect(i * .01f)) > .981f) { fail++; log("FAIL ceiling at " + i); break; }
            expect(Protect(float.NaN) == 0, "NaN sanitized");
            m.SetGain(GainSend, float.PositiveInfinity);
            expect(m.gain[GainSend] == 0, "invalid gains sanitized");

            // Volume compensation: decreases apply at once, increases only after a 250 ms hold.
            m.SetMusicVolume(1);
            m.Render(Send, output, 256);
            expect(m.compensation == 1, "unity compensation at full app volume");
            m.SetMusicVolume(0.5f);
            m.Render(Send, output, 256);
            expect(m.compensation == 1, "increase is held (no spike from audio captured at the old volume)");
            for (int i = 0; i < 50; i++) m.Render(Send, output, 256);
            expect(Math.Abs(m.compensation - 2) < 1e-4, "increase applies after the hold");
            m.SetMusicVolume(1);
            m.Render(Send, output, 256);
            expect(m.compensation == 1, "decrease applies immediately");
            for (float v = 0.9f; v >= 0.45f; v -= 0.1f)
                for (int i = 0; i < 15; i++) { if (i == 0) m.SetMusicVolume(v); m.Render(Send, output, 256); }
            expect(m.compensation > 1.5f && m.compensation < 2, "dragging the listening slider lags by at most 150 ms (" + m.compensation + ")");
            m.SetMusicVolume(0);
            m.Render(Send, output, 256);
            expect(m.compensation == 0, "muted app sends silence");
            m.SetMusicVolume(0.0001f);
            expect(m.MusicCompensation <= MaxCompensation, "compensation is bounded");

            // Pause silences everything.
            m.SetGain(GainSend, 1);
            m.Pause(true);
            m.Render(Send, output, 256);
            expect(output[100] == 0, "paused output is silent");

            // 44.1 kHz source into a 48 kHz sink with 120 ppm drift for 106 s: no recoveries after priming.
            var d = new Mixer();
            d.ConfigureSink(Send, 48000);
            d.ConfigureSource(0, 44100);
            d.SetGain(GainSend, 1);
            double phase = 0, produced = 0, consumed = 0;
            var chunk = new float[2048];
            long startDrops = -1;
            for (int block = 0; block < 106 * 100; block++)
            {
                produced += 441 * 1.00012;
                int n = (int)produced; produced -= n;
                for (int i = 0; i < n; i++) { float s = (float)(0.5 * Math.Sin(phase)); phase += 2 * Math.PI * 1000 / 44100; chunk[i * 2] = s; chunk[i * 2 + 1] = s; }
                d.Capture(0, chunk, n, false);
                consumed += 480;
                int c = (int)consumed; consumed -= c;
                d.Render(Send, chunk, c);
                if (block == 50) startDrops = d.Dropouts;
            }
            expect(d.Dropouts == startDrops, "drift conversion without buffer recovery (" + (d.Dropouts - startDrops) + ")");
            float tone = 0;
            for (int i = 0; i < 960; i++) tone = Math.Max(tone, Math.Abs(chunk[i]));
            expect(Math.Abs(tone - Protect(0.5f)) < 0.02f, "1 kHz amplitude preserved through 44.1→48 kHz (" + tone + ")");

            // Soundboard: effects reach both sinks; the listening master never changes the send.
            var e = new Mixer();
            e.ConfigureSink(Send, 48000);
            e.ConfigureSink(Listening, 48000);
            var clip = new float[48000 * 2];
            for (int i = 0; i < clip.Length; i++) clip[i] = .5f;
            int slot = e.SoundLoad(clip);
            expect(slot == 0, "clip loads");
            e.SoundGain(0, 1); e.SoundGain(1, 1);
            e.SetGain(GainSend, 1); e.SetGain(GainMaster, 0);
            e.SoundPlay(slot);
            for (int i = 0; i < 40; i++) e.Render(Send, output, 256);
            expect(output[200] > .4f, "effect reaches the send");
            for (int i = 0; i < 40; i++) e.Render(Listening, output, 256);
            expect(output[200] == 0, "listening master mutes local effects only");
            e.SoundPlay(-1);
            e.Render(Send, output, 256);
            expect(output[200] == 0 || Math.Abs(output[200]) < 1e-6, "stop sounds is immediate");
            expect(e.SoundLoad(new float[(48000 * 16) * 2]) == -1, "clips over 15 s are rejected");

            log("DSP self-test: " + (fail != 0 ? "FAIL" : "PASS") + " (" + fail + " failures)");
            return fail;
        }
    }

    // Immutable clip bank. One control-thread producer publishes clips; each output owns its cursor.
    sealed class Soundboard
    {
        public const int Slots = 64, MaxFrames = 48000 * 15, TotalFrames = 48000 * 180;
        readonly float[][] clips = new float[Slots][];
        public long Command;
        public readonly long[] Playing = new long[2];
        public readonly float[] Gain = new float[2], Meter = new float[2];
        readonly long[] playerCommand = new long[2];
        readonly double[] position = new double[2];
        readonly float[] playerGain = new float[2];
        readonly float[][] filter = { new float[256 * 32], new float[256 * 32] };
        int totalFrames;

        public void Prepare(int sink, double rate) { Mixer.PrepareFilter(filter[sink], 48000 / rate); }

        public int Load(float[] data)
        {
            int frames = data == null ? 0 : data.Length / 2;
            if (frames == 0 || frames > MaxFrames || frames > TotalFrames - totalFrames) return -1;
            int slot = 0;
            while (slot < Slots && Volatile.Read(ref clips[slot]) != null) slot++;
            if (slot == Slots) return -1;
            var clip = new float[frames * 2];
            for (int i = 0; i < clip.Length; i++)
            {
                float x = data[i];
                clip[i] = float.IsNaN(x) || float.IsInfinity(x) ? 0 : Math.Max(-1, Math.Min(1, x));
            }
            totalFrames += frames;
            Volatile.Write(ref clips[slot], clip);
            return slot;
        }

        public void Trigger(int slot)
        {
            if (slot < -1 || slot >= Slots || (slot >= 0 && Volatile.Read(ref clips[slot]) == null)) return;
            long old = Interlocked.Read(ref Command);
            Interlocked.Exchange(ref Command, (((old >> 8) + 1) << 8) | (long)(uint)(slot + 1));
        }

        public void Sample(int sink, double rate, bool paused, out float left, out float right)
        {
            left = right = 0;
            long command = Interlocked.Read(ref Command);
            if (playerCommand[sink] != command) { playerCommand[sink] = command; position[sink] = 0; }
            int slot = (int)(command & 255) - 1;
            if (paused) { position[sink] = MaxFrames; Interlocked.Exchange(ref Playing[sink], 0); return; }
            float[] clip = slot >= 0 && slot < Slots ? Volatile.Read(ref clips[slot]) : null;
            int frames = clip == null ? 0 : clip.Length / 2;
            if (clip == null || position[sink] >= frames) { Interlocked.Exchange(ref Playing[sink], 0); return; }
            Interlocked.Exchange(ref Playing[sink], command);
            float target = Volatile.Read(ref Gain[sink]);
            playerGain[sink] = target == 0 ? 0 : playerGain[sink] + (target - playerGain[sink]) * .002f;
            int frame = (int)position[sink], phase = (int)((position[sink] - frame) * 256);
            float[] k = filter[sink];
            float pg = playerGain[sink];
            for (int t = 0; t < 32; t++)
            {
                int index = frame + t - 15;
                if (index < 0 || index >= frames) continue;
                float w = k[phase * 32 + t] * pg;
                left += clip[index * 2] * w;
                right += clip[index * 2 + 1] * w;
            }
            position[sink] += 48000 / rate;
        }
    }
}
