// All synthesis, decoding, and file IO happens away from the audio threads.
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;

namespace OpenYap
{
    sealed class SoundProblem : Exception { public SoundProblem(string message) : base(message) { } }

    static class SoundAudio
    {
        public const int Rate = 48000, MaxSeconds = 15;

        public static float[] Samples(SoundClip clip)
        {
            if (clip.Preset >= 0) return Synthesize(clip.Preset);
            if (clip.Path == null || !File.Exists(clip.Path)) throw new SoundProblem("This sound file is missing.");
            return Decode(clip.Path);
        }

        // Returns interleaved stereo 48 kHz float samples, at most 15 seconds.
        public static float[] Decode(string path)
        {
            float[] samples;
            if (TryReadOwnWave(path, out samples)) return samples;
            int rate, channels;
            float[] native = MediaFoundation.DecodeFloat(path, MaxSeconds, out rate, out channels);
            if (channels < 1 || channels > 2 || rate <= 0) throw new SoundProblem("Choose a mono or stereo sound up to 15 seconds long.");
            int frames = native.Length / channels;
            if (frames == 0) throw new SoundProblem("This sound is empty.");
            if ((double)frames / rate > MaxSeconds + 0.001) throw new SoundProblem("Choose a sound up to 15 seconds long.");
            var stereo = new float[frames * 2];
            for (int i = 0; i < frames; i++)
            {
                stereo[i * 2] = native[i * channels];
                stereo[i * 2 + 1] = native[i * channels + channels - 1];
            }
            float[] result = rate == Rate ? stereo : Resample(stereo, rate, Rate);
            for (int i = 0; i < result.Length; i++)
            {
                float x = result[i];
                result[i] = float.IsNaN(x) || float.IsInfinity(x) ? 0 : Math.Max(-1, Math.Min(1, x));
            }
            if (result.Length / 2 > Soundboard.MaxFrames) throw new SoundProblem("Choose a sound up to 15 seconds long.");
            return result;
        }

        // Offline windowed-sinc conversion, the same 32-tap / 256-phase design the mixer uses.
        public static float[] Resample(float[] stereo, int from, int to)
        {
            var filter = new float[Mixer.Phases * Mixer.Taps];
            Mixer.PrepareFilter(filter, (double)from / to);
            int frames = stereo.Length / 2;
            int outFrames = (int)Math.Ceiling((double)frames * to / from);
            var result = new float[outFrames * 2];
            double step = (double)from / to;
            for (int o = 0; o < outFrames; o++)
            {
                double position = o * step;
                int frame = (int)position, phase = (int)((position - frame) * Mixer.Phases);
                float l = 0, r = 0;
                for (int t = 0; t < Mixer.Taps; t++)
                {
                    int index = frame + t - 15;
                    if (index < 0 || index >= frames) continue;
                    float k = filter[phase * Mixer.Taps + t];
                    l += stereo[index * 2] * k;
                    r += stereo[index * 2 + 1] * k;
                }
                result[o * 2] = l;
                result[o * 2 + 1] = r;
            }
            return result;
        }

        // 32-bit float stereo 48 kHz WAV: the library's storage format.
        public static void Save(float[] samples, string path)
        {
            using (var w = new BinaryWriter(File.Create(path)))
            {
                int bytes = samples.Length * 4;
                w.Write(new[] { (byte)'R', (byte)'I', (byte)'F', (byte)'F' });
                w.Write(4 + 26 + 12 + 8 + bytes);
                w.Write(new[] { (byte)'W', (byte)'A', (byte)'V', (byte)'E', (byte)'f', (byte)'m', (byte)'t', (byte)' ' });
                w.Write(18);
                w.Write((short)3); w.Write((short)2); w.Write(Rate); w.Write(Rate * 8); w.Write((short)8); w.Write((short)32); w.Write((short)0);
                w.Write(new[] { (byte)'f', (byte)'a', (byte)'c', (byte)'t' });
                w.Write(4); w.Write(samples.Length / 2);
                w.Write(new[] { (byte)'d', (byte)'a', (byte)'t', (byte)'a' });
                w.Write(bytes);
                var buffer = new byte[bytes];
                Buffer.BlockCopy(samples, 0, buffer, 0, bytes);
                w.Write(buffer);
            }
        }

        static bool TryReadOwnWave(string path, out float[] samples)
        {
            samples = null;
            if (!path.EndsWith(".wav", StringComparison.OrdinalIgnoreCase)) return false;
            using (var r = new BinaryReader(File.OpenRead(path)))
            {
                if (r.BaseStream.Length < 44 || new string(r.ReadChars(4)) != "RIFF") return false;
                r.ReadInt32();
                if (new string(r.ReadChars(4)) != "WAVE") return false;
                int format = 0, channels = 0, rate = 0, bits = 0;
                while (r.BaseStream.Position + 8 <= r.BaseStream.Length)
                {
                    string id = new string(r.ReadChars(4));
                    int size = r.ReadInt32();
                    long next = r.BaseStream.Position + size + (size & 1);
                    if (id == "fmt ")
                    {
                        format = r.ReadInt16(); channels = r.ReadInt16(); rate = r.ReadInt32();
                        r.ReadInt32(); r.ReadInt16(); bits = r.ReadInt16();
                    }
                    else if (id == "data")
                    {
                        if (format != 3 || bits != 32 || channels != 2 || rate != Rate) return false;
                        if (size / 8 > Soundboard.MaxFrames || size > r.BaseStream.Length - r.BaseStream.Position) return false;
                        var bytes = r.ReadBytes(size);
                        samples = new float[size / 4];
                        Buffer.BlockCopy(bytes, 0, samples, 0, samples.Length * 4);
                        for (int i = 0; i < samples.Length; i++)
                            if (float.IsNaN(samples[i]) || float.IsInfinity(samples[i])) samples[i] = 0;
                        return samples.Length > 0;
                    }
                    r.BaseStream.Position = next;
                }
            }
            return false;
        }

        public static float[] Synthesize(int preset)
        {
            double duration = new[] { 1.8, 0.8, 1.3, 0.65, 2.1, 2.0, 0.7, 0.55, 0.8, 1.5, 1.2, 1.5 }[preset];
            int count = (int)(duration * Rate);
            var result = new float[count * 2];
            uint random = 12345;
            for (int i = 0; i < count; i++)
            {
                double t = (double)i / Rate;
                random = unchecked(random * 1664525 + 1013904223);
                double noise = (double)random / uint.MaxValue * 2 - 1;
                double attack = Math.Min(1, t / 0.005), release = Math.Min(1, (duration - t) / 0.03);
                Func<double, double> tone = frequency => Math.Sin(2 * Math.PI * frequency * t);
                double value;
                switch (preset)
                {
                    case 0: value = Math.Sin(2 * Math.PI * (48 * t + 45 * (1 - Math.Exp(-10 * t)) / 10)) * Math.Exp(-3 * t) + noise * Math.Exp(-45 * t) * 0.25; break;
                    case 1: value = (tone(880) + 0.5 * tone(t < 0.12 ? 1320 : 1760)) * Math.Exp(-6 * t) * 0.5; break;
                    case 2: value = (tone(220) + tone(277.18) + 0.5 * tone(440)) * 0.3 * Math.Min(1, (duration - t) * 8); break;
                    case 3: value = (noise * 0.4 + Math.Sin(2 * Math.PI * (1400 * t - 850 * t * t)) * 0.6) * Math.Pow(Math.Sin(Math.PI * t / duration), 2); break;
                    case 4: value = (Math.Sin(2 * Math.PI * (310 * t - 40 * t * t)) + 0.25 * Math.Sin(4 * Math.PI * (310 * t - 40 * t * t))) * 0.6 * (0.8 + 0.2 * Math.Sin(2 * Math.PI * 5 * t)); break;
                    case 5: value = tone(4300) * Math.Pow(Math.Max(0, Math.Sin(2 * Math.PI * 3 * t)), 12) * 0.45; break;
                    case 6: value = noise * Math.Exp(-25 * t) * 0.65 + tone(180) * Math.Exp(-45 * t) * 0.4 + noise * Math.Exp(-10 * Math.Max(0, t - 0.13)) * (t > 0.13 ? 0.2 : 0); break;
                    case 7: value = Math.Sin(2 * Math.PI * (1300 * t - 1000 * t * t)) * Math.Exp(-7 * t) * 0.8; break;
                    case 8:
                        double[] notes = { 523.25, 659.25, 783.99, 1046.5 };
                        value = tone(notes[Math.Min(3, (int)(t / 0.16))]) * 0.6 * Math.Exp(-2 * t); break;
                    case 9: value = (tone(146.83) + tone(155.56) + tone(220)) * 0.25 * Math.Exp(-1.8 * t); break;
                    case 10: value = Math.Sin(2 * Math.PI * 170 * t + 15 * Math.Sin(2 * Math.PI * 7 * t) * Math.Exp(-3 * t)) * Math.Exp(-3 * t) * 0.8; break;
                    default: value = tone((int)(t * 6) % 2 == 0 ? 740 : 988) * 0.55; break;
                }
                float sample = (float)Math.Max(-0.85, Math.Min(0.85, value * attack * release));
                result[i * 2] = sample;
                result[i * 2 + 1] = sample;
            }
            return result;
        }
    }

    // Media Foundation source reader: decodes WAV, MP3, AAC/M4A, WMA, FLAC and other formats Windows supports.
    static class MediaFoundation
    {
        const uint FirstAudioStream = 0xFFFFFFFD, AllStreams = 0xFFFFFFFE;
        static Guid MajorType = new Guid("48eba18e-f8c9-4687-bf11-0a74c9f96a8f"), Subtype = new Guid("f7e34c9a-42e8-4714-b74b-cb29d72c35e5"),
            AudioMajor = new Guid("73647561-0000-0010-8000-00AA00389B71"), FloatSubtype = new Guid("00000003-0000-0010-8000-00AA00389B71"),
            Channels = new Guid("37e48bf5-645e-4c5b-89de-ada9e29b696a"), SampleRate = new Guid("5faeeae7-0290-4c31-9e8a-c534f68d9dba");

        public static float[] DecodeFloat(string path, int maxSeconds, out int rate, out int channels)
        {
            rate = channels = 0;
            Check(MFStartup(0x20070, 0));
            IMFSourceReader reader = null;
            IMFMediaType type = null, current = null;
            try
            {
                if (MFCreateSourceReaderFromURL(path, IntPtr.Zero, out reader) < 0) throw new SoundProblem("This audio format could not be decoded.");
                reader.SetStreamSelection(AllStreams, 0);
                if (reader.SetStreamSelection(FirstAudioStream, 1) < 0) throw new SoundProblem("This file has no audio.");
                Check(MFCreateMediaType(out type));
                type.SetGUID(ref MajorType, ref AudioMajor);
                type.SetGUID(ref Subtype, ref FloatSubtype);
                if (reader.SetCurrentMediaType(FirstAudioStream, IntPtr.Zero, type) < 0) throw new SoundProblem("This audio format could not be decoded.");
                Check(reader.GetCurrentMediaType(FirstAudioStream, out current));
                uint c, r;
                current.GetUINT32(ref Channels, out c);
                current.GetUINT32(ref SampleRate, out r);
                channels = (int)c;
                rate = (int)r;
                if (channels < 1 || channels > 2 || rate <= 0) throw new SoundProblem("Choose a mono or stereo sound up to 15 seconds long.");
                long limit = (long)(maxSeconds + 0.05) * rate * channels + rate * channels / 20;
                var result = new List<float>();
                while (true)
                {
                    uint actual, flags;
                    long timestamp;
                    IMFSample sample;
                    if (reader.ReadSample(FirstAudioStream, 0, out actual, out flags, out timestamp, out sample) < 0) throw new SoundProblem("Could not read this sound.");
                    if (sample != null)
                    {
                        IMFMediaBuffer buffer;
                        if (sample.ConvertToContiguousBuffer(out buffer) >= 0)
                        {
                            IntPtr data;
                            int max, length;
                            if (buffer.Lock(out data, out max, out length) >= 0)
                            {
                                var chunk = new float[length / 4];
                                Marshal.Copy(data, chunk, 0, chunk.Length);
                                buffer.Unlock();
                                result.AddRange(chunk);
                            }
                            Marshal.ReleaseComObject(buffer);
                        }
                        Marshal.ReleaseComObject(sample);
                    }
                    if (result.Count > limit) throw new SoundProblem("Choose a sound up to 15 seconds long.");
                    if ((flags & 0x1) != 0) throw new SoundProblem("The decoder stopped before the sound finished.");
                    if ((flags & 0x2) != 0) break;
                }
                return result.ToArray();
            }
            finally
            {
                Native.Release(current);
                Native.Release(type);
                Native.Release(reader);
                MFShutdown();
            }
        }

        static void Check(int hr) { if (hr < 0) throw new SoundProblem("Windows could not decode audio (0x" + hr.ToString("X8") + ")."); }

        [DllImport("mfplat.dll")] static extern int MFStartup(uint version, uint flags);
        [DllImport("mfplat.dll")] static extern int MFShutdown();
        [DllImport("mfplat.dll")] static extern int MFCreateMediaType(out IMFMediaType type);
        [DllImport("mfreadwrite.dll", CharSet = CharSet.Unicode)] static extern int MFCreateSourceReaderFromURL(string url, IntPtr attributes, out IMFSourceReader reader);

        [ComImport, Guid("70ae66f2-c809-4e4f-8915-bdcb406b7993"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IMFSourceReader
        {
            [PreserveSig] int GetStreamSelection(uint stream, out int selected);
            [PreserveSig] int SetStreamSelection(uint stream, int selected);
            [PreserveSig] int GetNativeMediaType(uint stream, uint index, out IMFMediaType type);
            [PreserveSig] int GetCurrentMediaType(uint stream, out IMFMediaType type);
            [PreserveSig] int SetCurrentMediaType(uint stream, IntPtr reserved, IMFMediaType type);
            [PreserveSig] int SetCurrentPosition(IntPtr format, IntPtr position);
            [PreserveSig] int ReadSample(uint stream, uint control, out uint actualStream, out uint flags, out long timestamp, out IMFSample sample);
        }

        // IMFAttributes occupies the first 30 slots of both IMFMediaType and IMFSample.
        [ComImport, Guid("44ae0fa8-ea31-4109-8d2e-4cae4997c555"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IMFMediaType
        {
            [PreserveSig] int GetItem(IntPtr a, IntPtr b);
            [PreserveSig] int GetItemType(IntPtr a, IntPtr b);
            [PreserveSig] int CompareItem(IntPtr a, IntPtr b, IntPtr c);
            [PreserveSig] int Compare(IntPtr a, int b, IntPtr c);
            [PreserveSig] int GetUINT32(ref Guid key, out uint value);
            [PreserveSig] int GetUINT64(IntPtr a, IntPtr b);
            [PreserveSig] int GetDouble(IntPtr a, IntPtr b);
            [PreserveSig] int GetGUID(IntPtr a, IntPtr b);
            [PreserveSig] int GetStringLength(IntPtr a, IntPtr b);
            [PreserveSig] int GetString(IntPtr a, IntPtr b, int c, IntPtr d);
            [PreserveSig] int GetAllocatedString(IntPtr a, IntPtr b, IntPtr c);
            [PreserveSig] int GetBlobSize(IntPtr a, IntPtr b);
            [PreserveSig] int GetBlob(IntPtr a, IntPtr b, int c, IntPtr d);
            [PreserveSig] int GetAllocatedBlob(IntPtr a, IntPtr b, IntPtr c);
            [PreserveSig] int GetUnknown(IntPtr a, IntPtr b, IntPtr c);
            [PreserveSig] int SetItem(IntPtr a, IntPtr b);
            [PreserveSig] int DeleteItem(IntPtr a);
            [PreserveSig] int DeleteAllItems();
            [PreserveSig] int SetUINT32(ref Guid key, uint value);
            [PreserveSig] int SetUINT64(IntPtr a, ulong b);
            [PreserveSig] int SetDouble(IntPtr a, double b);
            [PreserveSig] int SetGUID(ref Guid key, ref Guid value);
        }

        [ComImport, Guid("c40a00f2-b93a-4d80-ae8c-5a1c634f58e4"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IMFSample
        {
            [PreserveSig] int GetItem(IntPtr a, IntPtr b);
            [PreserveSig] int GetItemType(IntPtr a, IntPtr b);
            [PreserveSig] int CompareItem(IntPtr a, IntPtr b, IntPtr c);
            [PreserveSig] int Compare(IntPtr a, int b, IntPtr c);
            [PreserveSig] int GetUINT32(IntPtr a, IntPtr b);
            [PreserveSig] int GetUINT64(IntPtr a, IntPtr b);
            [PreserveSig] int GetDouble(IntPtr a, IntPtr b);
            [PreserveSig] int GetGUID(IntPtr a, IntPtr b);
            [PreserveSig] int GetStringLength(IntPtr a, IntPtr b);
            [PreserveSig] int GetString(IntPtr a, IntPtr b, int c, IntPtr d);
            [PreserveSig] int GetAllocatedString(IntPtr a, IntPtr b, IntPtr c);
            [PreserveSig] int GetBlobSize(IntPtr a, IntPtr b);
            [PreserveSig] int GetBlob(IntPtr a, IntPtr b, int c, IntPtr d);
            [PreserveSig] int GetAllocatedBlob(IntPtr a, IntPtr b, IntPtr c);
            [PreserveSig] int GetUnknown(IntPtr a, IntPtr b, IntPtr c);
            [PreserveSig] int SetItem(IntPtr a, IntPtr b);
            [PreserveSig] int DeleteItem(IntPtr a);
            [PreserveSig] int DeleteAllItems();
            [PreserveSig] int SetUINT32(IntPtr a, uint b);
            [PreserveSig] int SetUINT64(IntPtr a, ulong b);
            [PreserveSig] int SetDouble(IntPtr a, double b);
            [PreserveSig] int SetGUID(IntPtr a, IntPtr b);
            [PreserveSig] int SetString(IntPtr a, IntPtr b);
            [PreserveSig] int SetBlob(IntPtr a, IntPtr b, int c);
            [PreserveSig] int SetUnknown(IntPtr a, IntPtr b);
            [PreserveSig] int LockStore();
            [PreserveSig] int UnlockStore();
            [PreserveSig] int GetCount(IntPtr a);
            [PreserveSig] int GetItemByIndex(int a, IntPtr b, IntPtr c);
            [PreserveSig] int CopyAllItems(IntPtr a);
            [PreserveSig] int GetSampleFlags(IntPtr a);
            [PreserveSig] int SetSampleFlags(int a);
            [PreserveSig] int GetSampleTime(IntPtr a);
            [PreserveSig] int SetSampleTime(long a);
            [PreserveSig] int GetSampleDuration(IntPtr a);
            [PreserveSig] int SetSampleDuration(long a);
            [PreserveSig] int GetBufferCount(IntPtr a);
            [PreserveSig] int GetBufferByIndex(int a, IntPtr b);
            [PreserveSig] int ConvertToContiguousBuffer(out IMFMediaBuffer buffer);
        }

        [ComImport, Guid("045FA593-8799-42b8-BC8D-8968C6453507"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IMFMediaBuffer
        {
            [PreserveSig] int Lock(out IntPtr data, out int maxLength, out int currentLength);
            [PreserveSig] int Unlock();
        }
    }
}
