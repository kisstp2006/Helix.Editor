using System;
using System.IO;
using System.Text.Json;
using HelixEngine;

namespace HelixEditor
{
    // An editor mode: the main menu's titles in order and where a plugin's new title goes, read from a mode file of
    // the editor's data folder (Data/Modes/<name>.mode, JSON).
    internal static class EditorMode
    {
        internal static string current { get; private set; }

        internal static bool Apply(string name)
        {
            string path = Path.Combine(EditorApplication.editorDataPathInternal, "Modes", name + ".mode");
            try
            {
                using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(path));
                JsonElement root = doc.RootElement;
                int before = -1, order = 0;
                string newBefore = root.TryGetProperty("newMenusBefore", out JsonElement nb) ? nb.GetString() : null;
                if (root.TryGetProperty("menus", out JsonElement menus))
                    foreach (JsonElement m in menus.EnumerateArray())
                    {
                        string title = m.GetString();
                        // Ten apart: a new title fits between two.
                        order += 10;
                        if (title == newBefore)
                            before = order;
                        EditorMenus.AddTitle(title, order);
                    }
                EditorMenus.SetNewTitleOrderInternal(before >= 0 ? before - 5 : order + 10);
                current = name;
                return true;
            }
            catch (Exception e) when (e is IOException || e is JsonException || e is InvalidOperationException || e is UnauthorizedAccessException)
            {
                Debug.LogWarning($"The editor mode '{name}' could not be read from {path}: {e.Message}");
                return false;
            }
        }
    }
}
