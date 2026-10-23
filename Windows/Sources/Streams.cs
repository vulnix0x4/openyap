// One dedicated MTA thread per WASAPI stream. Each thread creates, uses, and releases its own COM
// objects, runs under MMCSS "Pro Audio", and only exchanges samples with the mixer's rings.
using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace OpenYap
{
    abstract class AudioStream
    {
        public const int Rate = 48000;
        protected readonly Mixer Mixer;
        readonly string label;
        Thread thread;
        volatile bool running;
        readonly ManualResetEvent ready = new ManualResetEvent(false);
        Exception startError;
        protected IntPtr Event;
        protected IAudioClient Client;
        public volatile bool Failed;

        protected AudioStream(Mixer mixer, string label) { Mixer = mixer; this.label = label; }

        // Blocks until the stream is running or has failed. Safe to call off the UI thread.
        public void Start()
        {
            running = true;
            thread = new Thread(Run) { IsBackground = true, Priority = ThreadPriority.Highest, Name = "OpenYap " + label };
            thread.SetApartmentState(ApartmentState.MTA);
            thread.Start();
            if (!ready.WaitOne(15000))
            {
                Stop();
                throw new AudioException(label + " did not respond. Check that the device is connected.", -1);
            }
            if (startError != null)
            {
                thread.Join(2000);
                throw startError is AudioException ? startError : new AudioException(label + " failed: " + startError.Message, -1);
            }
        }

        public void Stop()
        {
            running = false;
            if (Event != IntPtr.Zero) Native.SetEvent(Event);
            if (thread != null && thread != Thread.CurrentThread) thread.Join(3000);
        }

        void Run()
        {
            uint task = 0;
            IntPtr mmcss = Native.AvSetMmThreadCharacteristics("Pro Audio", ref task);
            IntPtr format = Native.FloatFormat(Rate, 2);
            bool started = false;
            try
            {
                Event = Native.CreateEvent(IntPtr.Zero, false, false, null);
                Open(format);
                Native.Check(Client.SetEventHandle(Event), label);
                Prepare();
                Native.Check(Client.Start(), label);
                started = true;
                ready.Set();
                while (running)
                {
                    Native.WaitForSingleObject(Event, 200);
                    if (!running) break;
                    int hr = Process();
                    if (hr < 0)
                    {
                        // Device removed, format changed, or the endpoint went exclusive: pause the mix.
                        Failed = true;
                        Mixer.Pause(true);
                        break;
                    }
                }
            }
            catch (Exception e)
            {
                if (!started) startError = e;
                else { Failed = true; Mixer.Pause(true); }
            }
            finally
            {
                try { if (Client != null && started) Client.Stop(); } catch (Exception) { }
                Release();
                Native.Release(Client);
                Client = null;
                Marshal.FreeHGlobal(format);
                if (Event != IntPtr.Zero) { Native.CloseHandle(Event); Event = IntPtr.Zero; }
                if (mmcss != IntPtr.Zero) Native.AvRevertMmThreadCharacteristics(mmcss);
                ready.Set();
            }
        }

        protected abstract void Open(IntPtr format);
        protected abstract void Prepare();
        protected abstract int Process();
        protected abstract void Release();

        protected static IAudioClient ActivateDevice(string deviceId, string label)
        {
            var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();
            IMMDevice device = null;
            try
            {
                Native.Check(enumerator.GetDevice(deviceId, out device), label);
                uint state;
                if (device.GetState(out state) < 0 || state != Native.DeviceStateActive)
                    throw new AudioException(label + " is disconnected.", Native.AudclntDeviceInvalidated);
                object o;
                Native.Check(device.Activate(ref Native.IAudioClientId, Native.ClsCtxAll, IntPtr.Zero, out o), label);
                return (IAudioClient)o;
            }
            finally
            {
                Native.Release(device);
                Native.Release(enumerator);
            }
        }

        // Our own streams must never be ducked when a call starts; that would quietly lower the shared mix.
        protected void OptOutOfDucking()
        {
            object o;
            if (Client.GetService(ref Native.IAudioSessionControlId, out o) < 0) return;
            var control = o as IAudioSessionControl2;
            if (control != null) control.SetDuckingPreference(1);
            Native.Release(o);
        }
    }

    sealed class RenderStream : AudioStream
    {
        readonly string deviceId;
        readonly int sink;
        IAudioRenderClient render;
        uint bufferFrames;
        readonly float[] scratch = new float[Rate * 2];

        public RenderStream(Mixer mixer, int sink, string deviceId, string label) : base(mixer, label)
        {
            this.sink = sink;
            this.deviceId = deviceId;
            mixer.ConfigureSink(sink, Rate);
        }

        protected override void Open(IntPtr format)
        {
            Client = ActivateDevice(deviceId, "Opening " + Label);
            Native.Check(Client.Initialize(Native.ShareModeShared, Native.StreamEventCallback | Native.StreamAutoConvertPcm | Native.StreamSrcDefaultQuality,
                400000, 0, format, IntPtr.Zero), "Opening " + Label);
        }

        string Label { get { return sink == Mixer.Send ? "the virtual microphone" : "your listening device"; } }

        protected override void Prepare()
        {
            Native.Check(Client.GetBufferSize(out bufferFrames), "Opening " + Label);
            object o;
            Native.Check(Client.GetService(ref Native.IAudioRenderClientId, out o), "Opening " + Label);
            render = (IAudioRenderClient)o;
            OptOutOfDucking();
            Process(); // prefill so the first period is silence, not garbage
        }

        protected override int Process()
        {
            uint padding;
            int hr = Client.GetCurrentPadding(out padding);
            if (hr < 0) return hr;
            int frames = (int)Math.Min(bufferFrames - padding, (uint)(scratch.Length / 2));
            if (frames <= 0) return 0;
            IntPtr data;
            hr = render.GetBuffer((uint)frames, out data);
            if (hr < 0) return hr;
            Mixer.Render(sink, scratch, frames);
            Marshal.Copy(scratch, 0, data, frames * 2);
            return render.ReleaseBuffer((uint)frames, 0);
        }

        protected override void Release() { Native.Release(render); render = null; }
    }

    sealed class CaptureStream : AudioStream
    {
        public enum Kind { ProcessTree, Microphone, Return, RenderLoopback }
        readonly Kind kind;
        readonly string deviceId;
        readonly int processId, source;
        IAudioCaptureClient capture;
        readonly float[] scratch = new float[Rate * 2];

        // source: Mixer music slot or Mixer.Mic; ignored for Return/RenderLoopback (return meter only).
        public CaptureStream(Mixer mixer, Kind kind, int source, string deviceId, int processId, string label) : base(mixer, label)
        {
            this.kind = kind;
            this.source = source;
            this.deviceId = deviceId;
            this.processId = processId;
            if (kind == Kind.ProcessTree || kind == Kind.Microphone) mixer.ConfigureSource(source, Rate);
        }

        protected override void Open(IntPtr format)
        {
            uint flags = Native.StreamEventCallback | Native.StreamAutoConvertPcm | Native.StreamSrcDefaultQuality;
            if (kind == Kind.ProcessTree)
            {
                Client = ActivateProcessLoopback(processId);
                flags = Native.StreamLoopback | Native.StreamEventCallback | Native.StreamAutoConvertPcm;
            }
            else
            {
                Client = ActivateDevice(deviceId, "Opening an input");
                if (kind == Kind.RenderLoopback) flags |= Native.StreamLoopback;
            }
            int hr = Client.Initialize(Native.ShareModeShared, flags, 200000, 0, format, IntPtr.Zero);
            if (hr == unchecked((int)0x80070005))
                throw new AudioException("Windows blocked microphone access. Turn on Settings → Privacy & security → Microphone → Let desktop apps access your microphone.", hr);
            Native.Check(hr, kind == Kind.ProcessTree ? "Capturing the app's audio" : "Opening an input");
        }

        static IAudioClient ActivateProcessLoopback(int processId)
        {
            IntPtr parameters = Marshal.AllocHGlobal(12);
            try
            {
                Marshal.WriteInt32(parameters, 0, 1); // AUDIOCLIENT_ACTIVATION_TYPE_PROCESS_LOOPBACK
                Marshal.WriteInt32(parameters, 4, processId);
                Marshal.WriteInt32(parameters, 8, 0); // PROCESS_LOOPBACK_MODE_INCLUDE_TARGET_PROCESS_TREE
                var blob = new PropVariant { vt = 65, blobSize = 12, blobData = parameters };
                var handler = new ActivationHandler();
                IActivateAudioInterfaceAsyncOperation operation;
                int hr = Native.ActivateAudioInterfaceAsync("VAD\\Process_Loopback", ref Native.IAudioClientId, ref blob, handler, out operation);
                if (hr < 0)
                    throw new AudioException("App audio capture needs Windows 10 version 2004 (build 20348) or later. (0x" + hr.ToString("X8") + ")", hr);
                if (!handler.Done.WaitOne(10000)) throw new AudioException("Windows did not start app audio capture.", -1);
                Native.Release(operation);
                Native.Check(handler.Result, "Capturing the app's audio");
                return (IAudioClient)handler.Activated;
            }
            finally { Marshal.FreeHGlobal(parameters); }
        }

        protected override void Prepare()
        {
            object o;
            Native.Check(Client.GetService(ref Native.IAudioCaptureClientId, out o), "Opening an input");
            capture = (IAudioCaptureClient)o;
            // Session volume also scales capture streams. Keep OpenYap's own capture at unity so the
            // only volume in the music path is the app's, which the send divides back out.
            if (kind == Kind.ProcessTree && Client.GetService(ref Native.ISimpleAudioVolumeId, out o) >= 0)
            {
                ((ISimpleAudioVolume)o).SetMasterVolume(1, ref Native.OpenYapContext);
                Native.Release(o);
            }
        }

        protected override int Process()
        {
            uint packet;
            int hr;
            while ((hr = capture.GetNextPacketSize(out packet)) >= 0 && packet > 0)
            {
                IntPtr data;
                uint frames, flags;
                ulong position, qpc;
                hr = capture.GetBuffer(out data, out frames, out flags, out position, out qpc);
                if (hr < 0) return hr;
                int n = (int)Math.Min(frames, (uint)(scratch.Length / 2));
                bool silent = (flags & Native.BufferSilent) != 0;
                if (!silent) Marshal.Copy(data, scratch, 0, n * 2);
                if (kind == Kind.Return || kind == Kind.RenderLoopback) Mixer.ReturnMeter(scratch, n, silent);
                else Mixer.Capture(source, scratch, n, silent);
                hr = capture.ReleaseBuffer(frames);
                if (hr < 0) return hr;
            }
            return hr;
        }

        protected override void Release() { Native.Release(capture); capture = null; }
    }
}
