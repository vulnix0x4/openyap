using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace OpenYap
{
    sealed class MusicApp
    {
        public string Id, Name;
        public MusicApp(string id, string name) { Id = id; Name = name; }
        public override string ToString() { return Name; }
    }

    sealed class ProcessInfo
    {
        public int Id, Parent;
        public string App; // executable name without .exe, lowercase
    }

    // Windows apps are identified by executable name. A capture follows each top-level process of that
    // executable and all its children, the way a macOS tap follows a bundle and its helpers.
    static class Processes
    {
        public static readonly MusicApp[] Known =
        {
            new MusicApp("spotify", "Spotify"), new MusicApp("robloxplayerbeta", "Roblox"),
            new MusicApp("discord", "Discord"), new MusicApp("zoom", "Zoom"),
        };
        static readonly HashSet<string> Ignored = new HashSet<string>
        {
            "explorer", "applicationframehost", "textinputhost", "shellexperiencehost", "searchhost", "startmenuexperiencehost",
            "systemsettings", "lockapp", "dwm", "csrss", "svchost", "audiodg", "taskmgr", "openyap", "widgets", "searchapp",
        };
        static readonly Dictionary<string, string> names = new Dictionary<string, string>();

        public static List<ProcessInfo> Snapshot()
        {
            var result = new List<ProcessInfo>();
            IntPtr snapshot = CreateToolhelp32Snapshot(2, 0);
            if (snapshot == new IntPtr(-1)) return result;
            try
            {
                var entry = new ProcessEntry { size = Marshal.SizeOf(typeof(ProcessEntry)) };
                for (bool ok = Process32FirstW(snapshot, ref entry); ok; ok = Process32NextW(snapshot, ref entry))
                {
                    string exe = entry.exeFile ?? "";
                    if (exe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) exe = exe.Substring(0, exe.Length - 4);
                    result.Add(new ProcessInfo { Id = (int)entry.processId, Parent = (int)entry.parentProcessId, App = exe.ToLowerInvariant() });
                }
            }
            finally { Native.CloseHandle(snapshot); }
            return result;
        }

        // Top-level processes of an app: the executable whose parent is not the same executable.
        public static List<int> Roots(List<ProcessInfo> all, string app)
        {
            var byId = all.ToDictionary(p => p.Id);
            return all.Where(p => p.App == app && !(byId.ContainsKey(p.Parent) && byId[p.Parent].App == app && p.Parent != p.Id))
                .Select(p => p.Id).ToList();
        }

        // Every process in the trees of the app's roots, matching PROCESS_LOOPBACK_MODE_INCLUDE_TARGET_PROCESS_TREE.
        public static HashSet<int> Tree(List<ProcessInfo> all, string app)
        {
            var result = new HashSet<int>(Roots(all, app));
            var children = all.Where(p => p.Parent != p.Id).ToLookup(p => p.Parent);
            var queue = new Queue<int>(result);
            while (queue.Count > 0)
                foreach (var child in children[queue.Dequeue()])
                    if (result.Add(child.Id)) queue.Enqueue(child.Id);
            foreach (var p in all) if (p.App == app) result.Add(p.Id);
            return result;
        }

        // Running desktop apps: processes with visible top-level windows, plus anything with an audio session.
        public static List<MusicApp> RunningApps(List<ProcessInfo> all, IEnumerable<int> audioProcesses)
        {
            var pids = new HashSet<int>(audioProcesses);
            EnumWindows((hwnd, _) =>
            {
                if (IsWindowVisible(hwnd) && GetWindow(hwnd, 4) == IntPtr.Zero && GetWindowTextLength(hwnd) > 0)
                {
                    uint pid;
                    GetWindowThreadProcessId(hwnd, out pid);
                    pids.Add((int)pid);
                }
                return true;
            }, IntPtr.Zero);
            int self = Process.GetCurrentProcess().Id;
            var apps = new Dictionary<string, MusicApp>();
            foreach (var p in all)
            {
                if (!pids.Contains(p.Id) || p.Id == self || p.Id == 0 || string.IsNullOrEmpty(p.App) || Ignored.Contains(p.App) || apps.ContainsKey(p.App))
                    continue;
                apps[p.App] = new MusicApp(p.App, DisplayName(p));
            }
            return apps.Values.ToList();
        }

        static string DisplayName(ProcessInfo p)
        {
            string name;
            if (names.TryGetValue(p.App, out name)) return name;
            name = null;
            foreach (var known in Known) if (known.Id == p.App) name = known.Name;
            if (name == null)
            {
                try
                {
                    IntPtr handle = OpenProcess(0x1000, false, p.Id);
                    if (handle != IntPtr.Zero)
                    {
                        var path = new StringBuilder(1024);
                        int size = path.Capacity;
                        if (QueryFullProcessImageNameW(handle, 0, path, ref size))
                        {
                            var info = FileVersionInfo.GetVersionInfo(path.ToString());
                            name = !string.IsNullOrWhiteSpace(info.FileDescription) ? info.FileDescription.Trim()
                                : !string.IsNullOrWhiteSpace(info.ProductName) ? info.ProductName.Trim() : null;
                        }
                        Native.CloseHandle(handle);
                    }
                }
                catch (Exception) { }
            }
            if (string.IsNullOrEmpty(name)) name = p.App;
            names[p.App] = name;
            return name;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct ProcessEntry
        {
            public int size;
            public uint usage, processId;
            public IntPtr defaultHeapId;
            public uint moduleId, threads, parentProcessId;
            public int priorityClassBase;
            public uint flags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string exeFile;
        }

        delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr param);
        [DllImport("kernel32.dll", SetLastError = true)] static extern IntPtr CreateToolhelp32Snapshot(uint flags, uint processId);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern bool Process32FirstW(IntPtr snapshot, ref ProcessEntry entry);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern bool Process32NextW(IntPtr snapshot, ref ProcessEntry entry);
        [DllImport("kernel32.dll")] static extern IntPtr OpenProcess(uint access, bool inherit, int processId);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern bool QueryFullProcessImageNameW(IntPtr process, int flags, StringBuilder path, ref int size);
        [DllImport("user32.dll")] static extern bool EnumWindows(EnumWindowsProc callback, IntPtr param);
        [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr hwnd);
        [DllImport("user32.dll")] static extern IntPtr GetWindow(IntPtr hwnd, uint command);
        [DllImport("user32.dll")] static extern int GetWindowTextLength(IntPtr hwnd);
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);
    }

    // Process loopback selects whole process trees, not tabs or individual windows.
    static class AppRoutingPolicy
    {
        public static bool IsSeparate(string music, string destination)
        {
            if (string.IsNullOrEmpty(music) || string.IsNullOrEmpty(destination)) return false;
            return music != destination && music != "openyap" && destination != "openyap";
        }
    }

    // Device IDs identify the user's choice; a reconnected device must start a new session.
    static class RoutingPolicy
    {
        public static bool ShouldPause(bool outputPresent, bool micRequired, bool micPresent, bool virtualPresent, bool defaultSafe)
        {
            return !outputPresent || (micRequired && !micPresent) || !virtualPresent || !defaultSafe;
        }
    }

    // Persist BEFORE changing an app's volume. Windows remembers per-app volumes, so a forced quit would
    // otherwise leave Spotify or a game quiet. Entries are removed only once that app's volume is restored.
    sealed class VolumeJournal
    {
        readonly string path;
        readonly Dictionary<string, float> saved = new Dictionary<string, float>();

        public VolumeJournal(string path)
        {
            this.path = path;
            try
            {
                if (File.Exists(path))
                    foreach (var line in File.ReadAllLines(path))
                    {
                        int tab = line.LastIndexOf('\t');
                        float level;
                        if (tab > 0 && float.TryParse(line.Substring(tab + 1), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out level))
                            saved[line.Substring(0, tab)] = level;
                    }
            }
            catch (Exception) { }
        }

        public bool Pending { get { return saved.Count > 0; } }
        public int Count { get { return saved.Count; } }

        void Save()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            if (saved.Count == 0) { File.Delete(path); return; }
            string temporary = path + ".partial";
            File.WriteAllLines(temporary, saved.Select(p => p.Key + "\t" + p.Value.ToString("R", System.Globalization.CultureInfo.InvariantCulture)));
            if (File.Exists(path)) File.Replace(temporary, path, null);
            else File.Move(temporary, path);
        }

        // Applies level to every session owned by the given processes. Returns false if it could not journal.
        public bool Apply(List<AppSession> sessions, HashSet<int> processes, float level)
        {
            foreach (var s in sessions)
            {
                if (!processes.Contains(s.ProcessId)) continue;
                if (!saved.ContainsKey(s.Identifier))
                {
                    float original = s.Level;
                    if (original < 0) continue;
                    saved[s.Identifier] = original;
                    try { Save(); }
                    catch (Exception) { saved.Remove(s.Identifier); return false; }
                }
                if (Math.Abs(s.Level - level) > 0.0005f) s.SetLevel(level);
            }
            return true;
        }

        // Restores any journaled sessions that exist right now; the rest stay pending.
        public int Restore(List<AppSession> sessions)
        {
            bool changed = false;
            foreach (var s in sessions)
            {
                float original;
                if (!saved.TryGetValue(s.Identifier, out original)) continue;
                if (s.SetLevel(original)) { saved.Remove(s.Identifier); changed = true; }
            }
            if (changed) Save();
            return saved.Count;
        }

        // The loudest current volume among the app's sessions; muted sessions count as silent.
        public static float Loudest(List<AppSession> sessions, HashSet<int> processes)
        {
            float loudest = -1;
            foreach (var s in sessions)
                if (processes.Contains(s.ProcessId))
                {
                    float level = s.Muted ? 0 : s.Level;
                    if (level >= 0) loudest = Math.Max(loudest, level);
                }
            return loudest;
        }
    }
}
