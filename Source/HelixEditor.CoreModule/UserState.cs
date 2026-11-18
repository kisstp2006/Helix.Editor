using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using HelixEngine;

namespace HelixEditor
{
    // The project's editor state of its user (the scene views, the grid and the snapping): key = value lines in
    // UserSettings/EditorState.txt, read once, written as a value changes. The editor's own preferences (EditorPrefs) are
    // the user's across projects; this is one project's.
    internal static class UserState
    {
        static Dictionary<string, string> s_Values;
        static string s_Path;

        static string FilePath => Path.Combine(EditorApplication.projectPathInternal, "UserSettings", "EditorState.txt");

        static Dictionary<string, string> Values()
        {
            string path = FilePath;
            if (s_Values != null && s_Path == path)
                return s_Values;
            s_Path = path;
            s_Values = new Dictionary<string, string>();
            try
            {
                if (File.Exists(path))
                    foreach (string line in File.ReadAllLines(path))
                    {
                        int eq = line.IndexOf(" = ", StringComparison.Ordinal);
                        if (eq > 0)
                            s_Values[line.Substring(0, eq)] = line.Substring(eq + 3);
                    }
            }
            catch (IOException e)
            {
                Debug.LogWarning($"UserSettings/EditorState.txt could not be read: {e.Message}");
            }
            return s_Values;
        }

        internal static string GetString(string key, string fallback) => Values().TryGetValue(key, out string v) ? v : fallback;

        internal static void SetString(string key, string value)
        {
            Dictionary<string, string> values = Values();
            if (values.TryGetValue(key, out string old) && old == value)
                return;
            values[key] = value;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(s_Path));
                var lines = new List<string>();
                foreach (var kv in values)
                    lines.Add(kv.Key + " = " + kv.Value);
                lines.Sort(StringComparer.Ordinal);
                File.WriteAllLines(s_Path, lines);
            }
            catch (IOException e)
            {
                Debug.LogWarning($"UserSettings/EditorState.txt could not be written: {e.Message}");
            }
        }

        internal static float GetFloat(string key, float fallback) =>
            float.TryParse(GetString(key, ""), NumberStyles.Float, CultureInfo.InvariantCulture, out float v) ? v : fallback;

        internal static void SetFloat(string key, float value) => SetString(key, value.ToString("R", CultureInfo.InvariantCulture));
        internal static bool GetBool(string key, bool fallback) => GetString(key, fallback ? "1" : "0") == "1";
        internal static void SetBool(string key, bool value) => SetString(key, value ? "1" : "0");
    }
}
