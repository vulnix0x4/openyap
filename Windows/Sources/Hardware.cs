using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;

namespace OpenYap
{
    sealed class AudioDevice
    {
        public string Id, Name, Interface, Enumerator;
        public bool IsRender;
        public int Channels;
        // VB-CABLE, VoiceMeeter and similar software endpoints. Never offered as headphones or microphones.
        public bool IsVirtual
        {
            get
            {
                string text = (Name + " " + Interface).ToLowerInvariant();
                return Enumerator == "ROOT" || text.Contains("vb-audio") || text.Contains("voicemeeter") ||
                    text.Contains("virtual") || text.StartsWith("cable ") || text.Contains("(cable") || text.Contains("blackhole");
            }
        }
        public bool IsBluetooth
        {
            get { return Enumerator != null && Enumerator.StartsWith("BTH", StringComparison.OrdinalIgnoreCase); }
        }
        public bool IsPhysical { get { return !IsVirtual; } }
        public override string ToString() { return Name; }
    }

    static class Hardware
    {
        static IMMDeviceEnumerator enumerator;
        static IMMDeviceEnumerator Enumerator
        {
            get
            {
                if (enumerator == null) enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();
                return enumerator;
            }
        }

        public static List<AudioDevice> Devices()
        {
            var result = new List<AudioDevice>();
            foreach (var flow in new[] { EDataFlow.Render, EDataFlow.Capture })
            {
                IMMDeviceCollection collection;
                if (Enumerator.EnumAudioEndpoints(flow, Native.DeviceStateActive, out collection) < 0) continue;
                uint count;
                collection.GetCount(out count);
                for (uint i = 0; i < count; i++)
                {
                    IMMDevice device;
                    if (collection.Item(i, out device) < 0) continue;
                    try { result.Add(Describe(device, flow == EDataFlow.Render)); }
                    catch (Exception) { }
                    finally { Native.Release(device); }
                }
                Native.Release(collection);
            }
            return result;
        }

        static AudioDevice Describe(IMMDevice device, bool render)
        {
            string id;
            Native.Check(device.GetId(out id), "Reading an audio device");
            IPropertyStore store;
            Native.Check(device.OpenPropertyStore(0, out store), "Reading an audio device");
            try
            {
                return new AudioDevice
                {
                    Id = id,
                    IsRender = render,
                    Name = StringProperty(store, Native.FriendlyName) ?? "Audio device",
                    Interface = StringProperty(store, Native.InterfaceName) ?? "",
                    Enumerator = StringProperty(store, Native.EnumeratorName) ?? "",
                    Channels = Channels(store),
                };
            }
            finally { Native.Release(store); }
        }

        static string StringProperty(IPropertyStore store, PropertyKey key)
        {
            PropVariant value;
            if (store.GetValue(ref key, out value) < 0) return null;
            string text = value.vt == 31 ? Marshal.PtrToStringUni(value.pointer) : null;
            Native.PropVariantClear(ref value);
            return text;
        }

        static int Channels(IPropertyStore store)
        {
            var key = Native.DeviceFormat;
            PropVariant value;
            if (store.GetValue(ref key, out value) < 0) return 0;
            int channels = value.vt == 65 && value.blobSize >= 4 ? Marshal.ReadInt16(value.blobData, 2) : 0;
            Native.PropVariantClear(ref value);
            return channels;
        }

        public static string DefaultId(EDataFlow flow, ERole role)
        {
            IMMDevice device;
            if (Enumerator.GetDefaultAudioEndpoint(flow, role, out device) < 0) return "";
            string id;
            device.GetId(out id);
            Native.Release(device);
            return id ?? "";
        }

        // Sets the default for ordinary playback (Console + Multimedia). Communications is left alone.
        public static void SetDefaultOutput(string id)
        {
            var policy = (IPolicyConfig)new PolicyConfigComObject();
            try
            {
                Native.Check(policy.SetDefaultEndpoint(id, ERole.Console), "Changing the default output");
                Native.Check(policy.SetDefaultEndpoint(id, ERole.Multimedia), "Changing the default output");
            }
            finally { Native.Release(policy); }
        }

        static IAudioEndpointVolume EndpointVolume(string id)
        {
            IMMDevice device;
            if (Enumerator.GetDevice(id, out device) < 0) return null;
            try
            {
                uint state;
                if (device.GetState(out state) < 0 || state != Native.DeviceStateActive) return null;
                object o;
                if (device.Activate(ref Native.IAudioEndpointVolumeId, Native.ClsCtxAll, IntPtr.Zero, out o) < 0) return null;
                return (IAudioEndpointVolume)o;
            }
            finally { Native.Release(device); }
        }

        // Address the chosen physical device only, never the virtual cable.
        public static float? OutputVolume(AudioDevice device)
        {
            if (device == null || !device.IsPhysical) return null;
            var volume = EndpointVolume(device.Id);
            if (volume == null) return null;
            try
            {
                float level;
                if (volume.GetMasterVolumeLevelScalar(out level) < 0 || float.IsNaN(level) || level < 0 || level > 1) return null;
                return level;
            }
            finally { Native.Release(volume); }
        }

        public static void SetOutputVolume(AudioDevice device, float value)
        {
            if (device == null || !device.IsPhysical || float.IsNaN(value) || value < 0 || value > 1)
                throw new AudioException("Device volume is unavailable.", -1);
            var volume = EndpointVolume(device.Id);
            if (volume == null) throw new AudioException("Device volume is unavailable.", -1);
            try { Native.Check(volume.SetMasterVolumeLevelScalar(value, ref Native.OpenYapContext), "Changing listening device volume"); }
            finally { Native.Release(volume); }
        }

        // Every audio session on every active output, for per-app volume control.
        public static List<AppSession> Sessions()
        {
            var result = new List<AppSession>();
            IMMDeviceCollection collection;
            if (Enumerator.EnumAudioEndpoints(EDataFlow.Render, Native.DeviceStateActive, out collection) < 0) return result;
            uint count;
            collection.GetCount(out count);
            for (uint i = 0; i < count; i++)
            {
                IMMDevice device;
                if (collection.Item(i, out device) < 0) continue;
                object o;
                if (device.Activate(ref Native.IAudioSessionManager2Id, Native.ClsCtxAll, IntPtr.Zero, out o) >= 0)
                {
                    var manager = (IAudioSessionManager2)o;
                    IAudioSessionEnumerator sessions;
                    if (manager.GetSessionEnumerator(out sessions) >= 0)
                    {
                        int n;
                        sessions.GetCount(out n);
                        for (int s = 0; s < n; s++)
                        {
                            IAudioSessionControl2 control;
                            if (sessions.GetSession(s, out control) < 0) continue;
                            int state;
                            uint pid;
                            string identifier;
                            if (control.GetState(out state) < 0 || state == 2 || control.IsSystemSoundsSession() == 0 ||
                                control.GetProcessId(out pid) < 0 || control.GetSessionIdentifier(out identifier) < 0)
                            {
                                Native.Release(control);
                                continue;
                            }
                            var volume = control as ISimpleAudioVolume;
                            if (volume == null) { Native.Release(control); continue; }
                            result.Add(new AppSession { ProcessId = (int)pid, Identifier = identifier, Volume = volume, Control = control });
                        }
                        Native.Release(sessions);
                    }
                    Native.Release(manager);
                }
                Native.Release(device);
            }
            Native.Release(collection);
            return result;
        }

        public static void Release(List<AppSession> sessions)
        {
            foreach (var s in sessions) Native.Release(s.Control);
            sessions.Clear();
        }
    }

    sealed class AppSession
    {
        public int ProcessId;
        public string Identifier;
        public ISimpleAudioVolume Volume;
        public IAudioSessionControl2 Control;
        public float Level
        {
            get { float v; return Volume.GetMasterVolume(out v) >= 0 ? v : -1; }
        }
        public bool Muted
        {
            get { int m; return Volume.GetMute(out m) >= 0 && m != 0; }
        }
        public bool SetLevel(float level)
        {
            return Volume.SetMasterVolume(Math.Max(0, Math.Min(1, level)), ref Native.OpenYapContext) >= 0;
        }
    }
}
