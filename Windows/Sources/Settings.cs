using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace OpenYap
{
    // Tiny key=value store in %APPDATA%\OpenYap. OPENYAP_DATA overrides the folder for tests.
    static class Settings
    {
        public static readonly string Folder = Environment.GetEnvironmentVariable("OPENYAP_DATA") ??
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "OpenYap");
        static string FilePath { get { return Path.Combine(Folder, "settings.txt"); } }
        static Dictionary<string, string> values;

        static Dictionary<string, string> Values
        {
            get
            {
                if (values != null) return values;
                values = new Dictionary<string, string>();
                try
                {
                    if (File.Exists(FilePath))
                        foreach (var line in File.ReadAllLines(FilePath))
                        {
                            int eq = line.IndexOf('=');
                            if (eq > 0) values[line.Substring(0, eq)] = line.Substring(eq + 1);
                        }
                }
                catch (Exception) { }
                return values;
            }
        }

        public static string Get(string key)
        {
            string value;
            return Values.TryGetValue(key, out value) ? value : null;
        }

        public static void Set(string key, string value)
        {
            if (value == null) Values.Remove(key);
            else Values[key] = value;
            try
            {
                Directory.CreateDirectory(Folder);
                File.WriteAllLines(FilePath, Values.Select(p => p.Key + "=" + p.Value));
            }
            catch (Exception) { }
        }
    }
}
