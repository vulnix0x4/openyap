using System;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

[assembly: AssemblyTitle("OpenYap")]
[assembly: AssemblyProduct("OpenYap")]
[assembly: AssemblyDescription("Share music, voice, and a soundboard into games and calls through a virtual microphone.")]
[assembly: AssemblyVersion("1.5.0.0")]
[assembly: AssemblyFileVersion("1.5.0.0")]

namespace OpenYap
{
    static class Program
    {
        [STAThread]
        static int Main(string[] args)
        {
            if (args.Contains("--self-test")) return Mixer.SelfTest(Console.WriteLine);
            if (args.Contains("--devices"))
            {
                foreach (var d in Hardware.Devices())
                    Console.WriteLine((d.IsRender ? "out" : "in ") + " | " + d.Name + " | ch:" + d.Channels + " | " + d.Enumerator +
                        (d.IsVirtual ? " | virtual" : "") + (d.IsBluetooth ? " | bluetooth" : "") + " | " + d.Id);
                return 0;
            }
            if (args.Contains("--apps"))
            {
                var sessions = Hardware.Sessions();
                var processes = Processes.Snapshot();
                foreach (var app in Processes.RunningApps(processes, sessions.Select(s => s.ProcessId)).OrderBy(a => a.Name))
                {
                    var tree = Processes.Tree(processes, app.Id);
                    Console.WriteLine(app.Id + " | " + app.Name + " | roots " + string.Join(",", Processes.Roots(processes, app.Id)) +
                        " | sessions " + sessions.Count(s => tree.Contains(s.ProcessId)) + " | volume " + VolumeJournal.Loudest(sessions, tree));
                }
                return 0;
            }
            if (args.Contains("--decode"))
            {
                // Decode files exactly as an import would, without copying them anywhere.
                int failures = 0;
                foreach (var file in args.SkipWhile(a => a != "--decode").Skip(1))
                {
                    try
                    {
                        var samples = SoundAudio.Decode(file);
                        Console.WriteLine("OK   " + System.IO.Path.GetFileName(file) + " | " + (samples.Length / 2 / 48000.0).ToString("0.00") + " s | peak " + samples.Max(x => Math.Abs(x)).ToString("0.000"));
                    }
                    catch (Exception e) { failures++; Console.WriteLine("FAIL " + System.IO.Path.GetFileName(file) + " | " + e.Message); }
                }
                return failures;
            }
            if (args.Contains("--speech-test"))
            {
                // Renders to memory only; nothing is played.
                var voices = SpeechRenderer.Voices();
                Console.WriteLine("Voices: " + string.Join(", ", voices));
                if (voices.Count == 0) return 1;
                var renderer = new SpeechRenderer();
                int failures = 0;
                var pitch = new double[3];
                foreach (int style in new[] { 0, 1, 2 })
                {
                    try
                    {
                        var samples = renderer.Render("OpenYap speech test, one two three.", voices[0], style).Result;
                        pitch[style] = MedianPitch(samples);
                        Console.WriteLine("style " + style + " | " + (samples.Length / 2 / 48000.0).ToString("0.00") + " s | peak " +
                            samples.Max(x => Math.Abs(x)).ToString("0.000") + " | median pitch " + pitch[style].ToString("0") + " Hz");
                    }
                    catch (Exception e) { failures++; Console.WriteLine("style " + style + " FAIL " + (e.InnerException ?? e).Message); }
                }
                if (!(pitch[1] < pitch[0] && pitch[0] < pitch[2])) { failures++; Console.WriteLine("FAIL expected Deep < Natural < Tiny"); }
                try { renderer.Render(new string('a', 221), voices[0], 0).Wait(); failures++; Console.WriteLine("FAIL accepted 221 characters"); }
                catch (Exception) { Console.WriteLine("OK rejects 221 characters"); }
                return failures;
            }
            if (args.Contains("--restore"))
            {
                var journal = new VolumeJournal(System.IO.Path.Combine(Settings.Folder, "volume-journal.txt"));
                var sessions = Hardware.Sessions();
                int left = journal.Restore(sessions);
                Console.WriteLine(left == 0 ? "Audio restored" : left + " app volume(s) are waiting for their apps to play audio again.");
                return left == 0 ? 0 : 1;
            }

            bool created;
            using (var single = new Mutex(true, "Local\\OpenYap.Windows", out created))
            {
                if (!created && !args.Contains("--hardware-test"))
                {
                    MessageBox.Show("OpenYap is already open. Do not run two copies together.", "OpenYap", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return 0;
                }
                try { SetProcessDpiAwarenessContext(new IntPtr(-4)); } catch (Exception) { try { SetProcessDPIAware(); } catch (Exception) { } }
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                // Fonts follow the real DPI, so every pixel size must too.
                try { Theme.Scale = GetDpiForSystem() / 96f; }
                catch (Exception) { using (var g = System.Drawing.Graphics.FromHwnd(IntPtr.Zero)) Theme.Scale = g.DpiX / 96f; }
                var model = new SessionModel();
                int snapshot = Array.IndexOf(args, "--snapshot");
                if (snapshot >= 0 && snapshot + 1 < args.Length)
                {
                    // Renders the full scrollable window to a PNG for design review, then exits.
                    var form = new MainForm(model);
                    form.Shown += async (s, e) =>
                    {
                        await System.Threading.Tasks.Task.Delay(1500);
                        form.SaveSnapshot(args[snapshot + 1], args.Contains("--expand"));
                        form.Close();
                    };
                    Application.Run(form);
                    return 0;
                }
                if (args.Contains("--hardware-test"))
                {
                    AttachConsole(-1);
                    int result = 1;
                    var form = new Form { ShowInTaskbar = false, WindowState = FormWindowState.Minimized, Opacity = 0 };
                    form.Load += async (s, e) =>
                    {
                        try { result = await model.HardwareTest(args, Console.WriteLine); }
                        catch (Exception ex) { Console.WriteLine("ERROR " + ex); }
                        model.Shutdown();
                        form.Close();
                    };
                    Application.Run(form);
                    return result;
                }
                Application.Run(new MainForm(model));
            }
            return 0;
        }

        // Median fundamental frequency of voiced 40 ms frames (autocorrelation), for the speech test only.
        static double MedianPitch(float[] stereo)
        {
            const int rate = 48000, win = rate / 25;
            int n = stereo.Length / 2;
            var found = new System.Collections.Generic.List<double>();
            for (int s = 0; s + win * 2 < n; s += win)
            {
                double energy = 0;
                for (int i = 0; i < win; i++) energy += stereo[(s + i) * 2] * stereo[(s + i) * 2];
                if (energy / win < 1e-3) continue;
                double best = 0;
                int lag = 0;
                for (int l = rate / 500; l < rate / 60; l += 2)
                {
                    double c = 0;
                    for (int i = 0; i < win; i += 2) c += stereo[(s + i) * 2] * stereo[(s + i + l) * 2];
                    if (c > best) { best = c; lag = l; }
                }
                if (lag > 0 && best * 2 / energy > 0.5) found.Add((double)rate / lag);
            }
            found.Sort();
            return found.Count == 0 ? 0 : found[found.Count / 2];
        }

        [DllImport("user32.dll")] static extern bool SetProcessDpiAwarenessContext(IntPtr value);
        [DllImport("user32.dll")] static extern bool SetProcessDPIAware();
        [DllImport("user32.dll")] static extern uint GetDpiForSystem();
        [DllImport("kernel32.dll")] static extern bool AttachConsole(int processId);
    }
}
