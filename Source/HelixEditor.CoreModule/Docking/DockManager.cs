using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using HelixEngine;
using HelixEngine.Internal;
using HelixEngine.UI;

namespace HelixEditor.Docking
{
    // The layout of the session's windows: the windows made for its tabs (one per window type), the view drawing it in
    // the shell's dock area, loading and saving it (presets of the editor's data folder, the project's own layouts).
    public static class DockManager
    {
        static DockLayout s_Layout = new();
        static readonly Dictionary<string, Type> s_Types = new();
        static readonly Dictionary<string, EditorWindow> s_Windows = new();
        static DockView s_View;
        static bool s_Scanned;

        public static DockLayout layout => s_Layout;

        /// <summary>The window types by name (every class below EditorWindow of the loaded assemblies).</summary>
        public static IEnumerable<string> WindowTypes()
        {
            ScanTypes();
            return s_Types.Keys;
        }

        static void ScanTypes()
        {
            if (s_Scanned)
                return;
            s_Scanned = true;
            ScriptBridge.OnAssembly(a =>
            {
                Type[] types;
                try
                {
                    types = a.GetTypes();
                }
                catch (ReflectionTypeLoadException e)
                {
                    types = Array.FindAll(e.Types, t => t != null);
                }
                foreach (Type t in types)
                    if (!t.IsAbstract && typeof(EditorWindow).IsAssignableFrom(t))
                        s_Types[t.Name] = t;
            });
        }

        internal static Type TypeOf(string name)
        {
            ScanTypes();
            return s_Types.TryGetValue(name, out Type t) ? t : null;
        }

        /// <summary>The window of a tab (made when the layout first shows it).</summary>
        public static EditorWindow WindowOf(string tab)
        {
            if (s_Windows.TryGetValue(tab, out EditorWindow w) && w != null)
                return w;
            Type t = TypeOf(tab);
            if (t == null)
                return null;
            w = EditorWindow.FindOpen(t);
            if (w == null)
                // Known before its OnEnable runs (which may set its title and ask for the view again).
                w = EditorWindow.Make(t, made =>
                {
                    EditorWindow.Register(made);
                    made.m_Docked = true;
                    s_Windows[tab] = made;
                });
            EditorWindow.Register(w);
            w.m_Docked = true;
            s_Windows[tab] = w;
            return w;
        }

        internal static void Attach(Element dockRoot)
        {
            s_View = new DockView(dockRoot);
            s_View.Build(s_Layout);
        }

        // The element the layout is drawn in (the shell's dock area).
        internal static Element viewRoot => s_View?.root;

        /// <summary>The layout drawn again (its tree or a title changed).</summary>
        public static void Changed() => s_View?.Build(s_Layout);

        public static void Load(DockLayout layout)
        {
            s_Layout = layout ?? new DockLayout();
            // Windows of tabs no longer in the layout close.
            foreach (string tab in new List<string>(s_Windows.Keys))
                if (s_Layout.AreaOf(tab) == null)
                {
                    EditorWindow w = s_Windows[tab];
                    s_Windows.Remove(tab);
                    w?.Close();
                }
            Changed();
        }

        public static bool LoadText(string text)
        {
            try
            {
                Load(DockLayout.Read(text));
                return true;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"The layout could not be read: {e.Message}");
                return false;
            }
        }

        // ------------------------------------------------------------------------------- the files

        /// <summary>The presets of the editor's data folder (Data/Layouts/*.layout) by name.</summary>
        public static IEnumerable<string> Presets()
        {
            string folder = Path.Combine(EditorApplication.editorDataPathInternal, "Layouts");
            if (!Directory.Exists(folder))
                yield break;
            foreach (string f in Directory.GetFiles(folder, "*.layout"))
                yield return Path.GetFileNameWithoutExtension(f);
        }

        public static bool LoadPreset(string name)
        {
            string path = Path.Combine(EditorApplication.editorDataPathInternal, "Layouts", name + ".layout");
            return File.Exists(path) && LoadText(File.ReadAllText(path));
        }

        static string ProjectLayouts => Path.Combine(EditorApplication.projectPathInternal, "UserSettings", "Layouts");

        /// <summary>The layouts the project saved (UserSettings/Layouts), the current one aside.</summary>
        public static IEnumerable<string> SavedLayouts()
        {
            if (!Directory.Exists(ProjectLayouts))
                yield break;
            foreach (string f in Directory.GetFiles(ProjectLayouts, "*.layout"))
            {
                string n = Path.GetFileNameWithoutExtension(f);
                if (n != "CurrentLayout")
                    yield return n;
            }
        }

        public static void Save(string name)
        {
            try
            {
                Directory.CreateDirectory(ProjectLayouts);
                File.WriteAllText(Path.Combine(ProjectLayouts, name + ".layout"), s_Layout.Write());
            }
            catch (Exception e)
            {
                Debug.LogWarning($"The layout could not be saved: {e.Message}");
            }
        }

        public static bool LoadSaved(string name)
        {
            string path = Path.Combine(ProjectLayouts, name + ".layout");
            return File.Exists(path) && LoadText(File.ReadAllText(path));
        }

        /// <summary>The project's last layout, else the Default preset.</summary>
        internal static void LoadStartLayout()
        {
            if (!LoadSaved("CurrentLayout") && !LoadPreset("Default"))
                Load(new DockLayout());
        }

        // ------------------------------------------------------------------------- window requests

        internal static void Show(EditorWindow w)
        {
            string tab = w.GetType().Name;
            if (s_Windows.TryGetValue(tab, out EditorWindow existing) && existing == w && s_Layout.AreaOf(tab) != null)
                return;
            s_Windows[tab] = w;
            if (s_Layout.AreaOf(tab) == null)
            {
                Rect r = w.m_Position;
                s_Layout.Float(tab, r);
                w.m_Docked = false;
            }
            Changed();
        }

        internal static void Remove(EditorWindow w)
        {
            string tab = w.GetType().Name;
            if (s_Windows.TryGetValue(tab, out EditorWindow existing) && existing == w)
            {
                s_Windows.Remove(tab);
                s_Layout.RemoveTab(tab);
                Changed();
            }
        }

        internal static void Select(EditorWindow w)
        {
            DockArea area = s_Layout.AreaOf(w.GetType().Name);
            if (area == null)
                return;
            int i = area.tabs.IndexOf(w.GetType().Name);
            if (area.selected != i)
            {
                area.selected = i;
                Changed();
            }
        }

        internal static void SetMaximized(EditorWindow w, bool value)
        {
            DockArea area = s_Layout.AreaOf(w.GetType().Name);
            s_Layout.maximized = value ? area : null;
            w.m_Maximized = value && area != null;
            Changed();
        }

        internal static void MovedFloating(EditorWindow w)
        {
            DockArea area = s_Layout.AreaOf(w.GetType().Name);
            FloatingContainer f = area != null ? s_Layout.ContainerOf(area) : null;
            if (f != null)
            {
                f.rect = w.m_Position;
                Changed();
            }
        }

        // ------------------------------------------------------------------------- view requests

        internal static void DockTab(string tab, DockArea target, DropZone zone, int index)
        {
            s_Layout.Dock(tab, target, zone, index);
            if (WindowOf(tab) is EditorWindow w)
                w.m_Docked = s_Layout.ContainerOf(s_Layout.AreaOf(tab)) == null;
            Changed();
        }

        internal static void FloatTab(string tab, Rect rect)
        {
            s_Layout.Float(tab, rect);
            if (WindowOf(tab) is EditorWindow w)
            {
                w.m_Docked = false;
                w.m_Position = rect;
            }
            Changed();
        }

        internal static void Saved() => Save("CurrentLayout");
    }
}
