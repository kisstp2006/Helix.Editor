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
        // The floating containers' windows (where the host has windows of its own), and when the layout is saved after
        // a window moved.
        static readonly List<FloatingWindow> s_FloatingWindows = new();
        static double s_SaveAt = -1;

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

        /// <summary>The layout drawn again (its tree or a title changed): the main view, and the floating containers'
        /// windows (opened for new containers, closed for those gone).</summary>
        public static void Changed()
        {
            SyncWindows();
            s_View?.Build(s_Layout);
            foreach (FloatingWindow w in s_FloatingWindows)
                w.Build(s_Layout);
        }

        internal static DockView mainView => s_View;

        // Floating containers in windows of their own: where the host has them.
        internal static bool osWindows => EditorApplication.toolWindowsInternal;

        internal static bool HasWindow(FloatingContainer f) => s_FloatingWindows.Exists(w => w.container == f);

        static void SyncWindows()
        {
            if (s_View == null)
                return;
            for (int i = s_FloatingWindows.Count - 1; i >= 0; i--)
                if (!s_Layout.floating.Contains(s_FloatingWindows[i].container))
                {
                    s_FloatingWindows[i].Close();
                    s_FloatingWindows.RemoveAt(i);
                }
            if (!osWindows)
                return;
            foreach (FloatingContainer f in s_Layout.floating)
                if (!HasWindow(f))
                {
                    FloatingWindow w = FloatingWindow.Open(f);
                    if (w != null)
                        s_FloatingWindows.Add(w);
                }
        }

        /// <summary>Once a frame: the floating windows' moves and sizes into the layout (saved once they rest), a window
        /// closed closing its tabs.</summary>
        internal static void Tick()
        {
            bool moved = false;
            List<FloatingContainer> closed = null;
            foreach (FloatingWindow w in s_FloatingWindows)
            {
                moved |= w.Tick(s_Layout, out bool closeAsked);
                if (closeAsked)
                    (closed ??= new List<FloatingContainer>()).Add(w.container);
            }
            if (closed != null)
            {
                foreach (FloatingContainer f in closed)
                    foreach (DockArea a in new List<DockArea>(AreasOf(f.root)))
                        foreach (string tab in new List<string>(a.tabs))
                        {
                            if (WindowOf(tab) is EditorWindow ew)
                                ew.Close();
                            else
                                s_Layout.RemoveTab(tab);
                        }
                Changed();
                Saved();
            }
            if (moved)
                s_SaveAt = EditorApplication.timeSinceStartup + 0.5;
            if (s_SaveAt >= 0 && EditorApplication.timeSinceStartup >= s_SaveAt)
            {
                s_SaveAt = -1;
                Saved();
            }
        }

        static IEnumerable<DockArea> AreasOf(DockNode node)
        {
            if (node is DockArea a)
                yield return a;
            else if (node is DockSplit s)
                foreach (DockNode c in s.children)
                    foreach (DockArea x in AreasOf(c))
                        yield return x;
        }

        /// <summary>A floating container moved and sized (its window with it).</summary>
        internal static void MoveFloating(FloatingContainer f, Rect rect)
        {
            f.rect = rect;
            Changed();
        }

        // A desktop point's view by place: the floating windows over the main one.
        internal static Vector2 Origin(int display)
        {
            if (!osWindows)
                return Vector2.zero;
            // A floating window's: its container's rectangle (set at once as it moves; the display's comes a frame later).
            foreach (FloatingWindow w in s_FloatingWindows)
                if (w.display == display)
                    return w.container.rect.position;
            return EditorApplication.DisplayRectInternal(display).position;
        }

        internal static IEnumerable<DockView> Views()
        {
            for (int i = s_FloatingWindows.Count - 1; i >= 0; i--)
                if (s_FloatingWindows[i].view != null)
                    yield return s_FloatingWindows[i].view;
            if (s_View != null)
                yield return s_View;
        }

        // The area of any view under a desktop point (the rect in that view's space).
        internal static bool FindTarget(Vector2 desktop, DockArea except, out DockView view, out DockArea area, out DropZone zone, out Rect rect, out int index)
        {
            foreach (DockView v in Views())
            {
                Vector2 local = v.FromDesktop(desktop);
                Rect bounds = v.root.worldBound;
                if (!bounds.Contains(local))
                    continue;
                // The area of a dragged only tab is no target of it: the views under it are.
                if (v.Target(local, out area, out zone, out rect, out index) && area != except)
                {
                    view = v;
                    return true;
                }
            }
            view = null;
            area = null;
            zone = DropZone.Center;
            rect = default;
            index = -1;
            return false;
        }

        internal static void HideIndicators(DockView except = null)
        {
            foreach (DockView v in Views())
                if (v != except)
                    v.HideIndicator();
        }

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
                if (w.m_Position.width <= 0 && s_View != null)
                    w.m_Position = s_View.NewWindowRect();
                Rect r = w.m_Position;
                s_Layout.Float(tab, r);
                w.m_Docked = false;
            }
            Changed();
        }

        // A window not shown yet goes into the area of another's tab (selected there); false when that one is not open.
        internal static bool DockBeside(EditorWindow w, string otherTab)
        {
            string tab = w.GetType().Name;
            DockArea area = s_Layout.AreaOf(otherTab);
            if (area == null || s_Layout.AreaOf(tab) != null)
                return false;
            area.tabs.Add(tab);
            area.selected = area.tabs.Count - 1;
            s_Windows[tab] = w;
            w.m_Docked = s_Layout.ContainerOf(area) == null;
            Changed();
            return true;
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
