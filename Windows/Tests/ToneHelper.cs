// Test-only "music app": plays an inaudible −80 dBFS 440 Hz tone to the default output so the
// hardware test can capture a real process without touching the user's own apps.
using System;
using System.Runtime.InteropServices;
using System.Threading;
using OpenYap;

static class ToneHelper
{
    [MTAThread]
    static int Main(string[] args)
    {
        int seconds = args.Length > 0 ? int.Parse(args[0]) : 30;
        var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();
        IMMDevice device;
        Native.Check(enumerator.GetDefaultAudioEndpoint(EDataFlow.Render, ERole.Multimedia, out device), "default output");
        object o;
        Native.Check(device.Activate(ref Native.IAudioClientId, Native.ClsCtxAll, IntPtr.Zero, out o), "activate");
        var client = (IAudioClient)o;
        Native.Check(client.Initialize(0, Native.StreamAutoConvertPcm | Native.StreamSrcDefaultQuality, 2000000, 0, Native.FloatFormat(48000, 2), IntPtr.Zero), "initialize");
        Native.Check(client.GetService(ref Native.IAudioRenderClientId, out o), "render client");
        var render = (IAudioRenderClient)o;
        uint size;
        client.GetBufferSize(out size);
        var buffer = new float[size * 2];
        double phase = 0;
        Native.Check(client.Start(), "start");
        var end = DateTime.Now.AddSeconds(seconds);
        while (DateTime.Now < end)
        {
            uint padding;
            client.GetCurrentPadding(out padding);
            int n = (int)(size - padding);
            IntPtr data;
            if (n > 0 && render.GetBuffer((uint)n, out data) == 0)
            {
                for (int i = 0; i < n; i++)
                {
                    float s = (float)(0.0001 * Math.Sin(phase));
                    phase += 2 * Math.PI * 440 / 48000;
                    buffer[i * 2] = buffer[i * 2 + 1] = s;
                }
                Marshal.Copy(buffer, 0, data, n * 2);
                render.ReleaseBuffer((uint)n, 0);
            }
            Thread.Sleep(10);
        }
        client.Stop();
        return 0;
    }
}
