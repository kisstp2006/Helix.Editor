using System;
using System.Collections.Generic;
using HelixEngine;
using HelixEngine.UI;

namespace HelixEditor
{
    /// <summary>The Inspector's Add Component list: a search field over the component classes the engine knows (the
    /// engine's first, then the scripts under their header), shown under the button in the window's panel. A click or
    /// Enter adds the class to the objects; Escape or a click outside closes it.</summary>
    internal sealed class AddComponentPopup : Element
    {
        readonly GameObject[] m_Targets;
        readonly TextField m_Search;
        readonly ScrollView m_List;
        // The classes listed: their engine name, shown name, group (a menu path's folders) and place in the group.
        readonly List<(string className, string shown, string group, int order)> m_Classes = new();
        readonly List<(Element row, string className)> m_Rows = new();
        int m_Highlight;
        Element m_Root;
        EventCallback<PointerDownEvent> m_Outside;

        internal static AddComponentPopup shownInternal;
        internal TextField searchInternal => m_Search;
        internal IReadOnlyList<string> shownClassesInternal
        {
            get
            {
                var l = new List<string>();
                foreach (var r in m_Rows)
                    l.Add(r.className);
                return l;
            }
        }

        AddComponentPopup(GameObject[] targets)
        {
            m_Targets = targets;
            name = "add-component-popup";
            AddToClassList("add-component-popup");
            var title = new Label("Component");
            title.AddToClassList("add-component-title");
            Add(title);
            var box = new Element();
            box.AddToClassList("window-search");
            var icon = new Element();
            icon.AddToClassList("icon");
            icon.AddToClassList("icon-search");
            box.Add(icon);
            m_Search = new TextField { name = "add-component-search" };
            m_Search.AddToClassList("window-search-field");
            box.Add(m_Search);
            Add(box);
            SearchBox.Attach(m_Search);
            m_List = new ScrollView { name = "add-component-list" };
            m_List.AddToClassList("add-component-list");
            Add(m_List);
            FindTypes();
            foreach (string c in InspectorNative.ComponentClasses())
            {
                bool script = c.StartsWith("Scripts/", StringComparison.Ordinal);
                string bare = script ? c.Substring(8) : c;
                // The editor's own scripts are not components of a project's objects; abstract classes are made by none.
                Type t = FindType(c);
                if (IsEditorClass(bare) || t != null && (t.IsAbstract || t == typeof(GameBehaviour)))
                    continue;
                // An AddComponentMenu path places it ("" leaves it out); else the engine's at the top, scripts in theirs.
                string group = script ? "Scripts" : "", shown = ObjectNames.NicifyVariableName(bare);
                int order = 0;
                string path = null;
                if (t != null && Attribute.GetCustomAttribute(t, typeof(AddComponentMenu)) is AddComponentMenu menu)
                {
                    path = menu.componentMenu ?? "";
                    order = menu.componentOrder;
                }
                else if (MenuPaths().TryGetValue(bare, out string known))
                    path = known;
                if (path != null)
                {
                    if (path.Length == 0)
                        continue;
                    int slash = path.LastIndexOf('/');
                    group = slash < 0 ? "" : path.Substring(0, slash);
                    shown = path.Substring(slash + 1);
                }
                m_Classes.Add((c, shown, group, order));
            }
            // The top first, then the groups by name (the scripts' last), each by order and name.
            m_Classes.Sort((a, b) =>
            {
                int Rank(string g) => g.Length == 0 ? 0 : g == "Scripts" ? 2 : 1;
                int r = Rank(a.group).CompareTo(Rank(b.group));
                if (r == 0)
                    r = string.CompareOrdinal(a.group, b.group);
                if (r == 0)
                    r = a.order.CompareTo(b.order);
                return r != 0 ? r : string.Compare(a.shown, b.shown, StringComparison.OrdinalIgnoreCase);
            });
            m_Search.RegisterValueChangedCallback(e => Fill());
            m_Search.RegisterCallback<KeyDownEvent>(OnKey, TrickleDown.TrickleDown);
            Fill();
        }

        // The component classes C# knows by name, and which of them are the editor's own.
        readonly Dictionary<string, Type> m_Types = new();
        readonly HashSet<string> m_EditorClasses = new();

        void FindTypes()
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type[] types;
                try
                {
                    types = asm.GetTypes();
                }
                catch (System.Reflection.ReflectionTypeLoadException)
                {
                    continue;
                }
                foreach (Type t in types)
                    if (typeof(Component).IsAssignableFrom(t))
                    {
                        m_Types[t.Name] = t;
                        if (asm == typeof(AddComponentPopup).Assembly)
                            m_EditorClasses.Add(t.Name);
                    }
            }
        }

        bool IsEditorClass(string name) => m_EditorClasses.Contains(name);

        static Dictionary<string, string> s_Paths;

        // The engine's classes' places (Data/Settings/AddComponentMenu.json).
        static Dictionary<string, string> MenuPaths()
        {
            if (s_Paths != null)
                return s_Paths;
            s_Paths = new Dictionary<string, string>();
            try
            {
                string file = System.IO.Path.Combine(EditorApplication.editorDataPathInternal, "Settings/AddComponentMenu.json");
                using var doc = System.Text.Json.JsonDocument.Parse(System.IO.File.ReadAllText(file));
                foreach (var p in doc.RootElement.GetProperty("paths").EnumerateObject())
                    s_Paths[p.Name] = p.Value.GetString() ?? "";
            }
            catch (Exception e)
            {
                Debug.LogError($"Settings/AddComponentMenu.json could not be read: {e.Message}");
            }
            return s_Paths;
        }

        internal static AddComponentPopup Show(Element anchor, GameObject[] targets)
        {
            shownInternal?.Close();
            Element tree = anchor?.panel?.visualTree;
            if (tree == null)
                return null;
            // Into the element below the panel's tree that holds the anchor (the shell's root: its style sheets).
            Element root = anchor;
            while (root.parent != null && root.parent != tree)
                root = root.parent;
            var p = new AddComponentPopup(targets);
            Rect r = anchor.worldBound, space = root.worldBound;
            float width = Math.Max(230, r.width), height = Math.Min(340, Math.Max(140, space.height - 8));
            // Under the anchor, else over it; inside the root either way.
            float top = r.yMax - space.y;
            if (top + height > space.height)
                top = r.y - space.y - height;
            top = Math.Clamp(top, 0, Math.Max(0, space.height - height));
            p.style.position = Position.Absolute;
            p.style.width = width;
            p.style.height = height;
            p.style.left = Math.Clamp(r.center.x - space.x - width / 2, 0, Math.Max(0, space.width - width));
            p.style.top = top;
            p.m_Root = root;
            root.Add(p);
            p.m_Outside = e =>
            {
                for (Element t = e.target as Element; t != null; t = t.parent)
                    if (t == p)
                        return;
                p.Close();
            };
            root.RegisterCallback(p.m_Outside, TrickleDown.TrickleDown);
            shownInternal = p;
            p.schedule.Execute(() => p.m_Search.Focus());
            return p;
        }

        internal void Close()
        {
            if (m_Root != null && m_Outside != null)
                m_Root.UnregisterCallback(m_Outside, TrickleDown.TrickleDown);
            RemoveFromHierarchy();
            if (shownInternal == this)
                shownInternal = null;
        }

        void Fill()
        {
            m_List.Clear();
            m_Rows.Clear();
            string search = m_Search.value?.Trim() ?? "";
            string lastGroup = "";
            foreach (var (className, shown, group, _) in m_Classes)
            {
                if (search.Length > 0 && shown.IndexOf(search, StringComparison.OrdinalIgnoreCase) < 0 &&
                    className.IndexOf(search, StringComparison.OrdinalIgnoreCase) < 0)
                    continue;
                if (group != lastGroup)
                {
                    lastGroup = group;
                    var header = new Label(group.Replace("/", " / "));
                    header.AddToClassList("add-component-group");
                    m_List.Add(header);
                }
                var row = new Element { name = "add:" + className };
                row.AddToClassList("add-component-row");
                var icon = new Element();
                icon.AddToClassList("add-component-icon");
                Type t = FindType(className);
                if (InspectorWindow.ComponentIcon(t ?? typeof(Component)) is Texture2D tex)
                    icon.style.backgroundImage = tex;
                row.Add(icon);
                var label = new Label(shown);
                label.AddToClassList("add-component-name");
                row.Add(label);
                string chosen = className;
                row.RegisterCallback<PointerDownEvent>(e =>
                {
                    if (e.button == 0)
                    {
                        Choose(chosen);
                        e.StopPropagation();
                    }
                });
                row.RegisterCallback<PointerEnterEvent>(e => Highlight(m_Rows.FindIndex(x => x.row == row), false));
                m_Rows.Add((row, className));
                m_List.Add(row);
            }
            if (m_Rows.Count == 0)
            {
                var none = new Label("No component of that name.");
                none.AddToClassList("inspector-note");
                m_List.Add(none);
            }
            m_List.scrollOffset = Vector2.zero;
            Highlight(0, false);
        }

        Type FindType(string className) =>
            m_Types.TryGetValue(className.StartsWith("Scripts/", StringComparison.Ordinal) ? className.Substring(8) : className, out Type t) ? t : null;

        // The row lit; brought into view when the keys moved to it (the pointer's rows are in view, and bringing
        // one in while the wheel turns would fight the wheel).
        void Highlight(int i, bool reveal)
        {
            if (m_Rows.Count == 0)
                return;
            m_Highlight = Math.Clamp(i, 0, m_Rows.Count - 1);
            for (int k = 0; k < m_Rows.Count; k++)
                m_Rows[k].row.EnableInClassList("add-component-row--highlight", k == m_Highlight);
            if (reveal)
                m_List.ScrollTo(m_Rows[m_Highlight].row);
        }

        void OnKey(KeyDownEvent e)
        {
            switch (e.keyCode)
            {
            case KeyCode.Escape: Close(); break;
            case KeyCode.DownArrow: Highlight(m_Highlight + 1, true); break;
            case KeyCode.UpArrow: Highlight(m_Highlight - 1, true); break;
            case KeyCode.Return:
            case KeyCode.KeypadEnter:
                if (m_Rows.Count > 0)
                    Choose(m_Rows[m_Highlight].className);
                break;
            default: return;
            }
            e.StopPropagation();
        }

        internal void Choose(string className)
        {
            Close();
            List<Component> made = ComponentCommands.Add(m_Targets, className);
            if (made.Count == 0)
                Debug.LogWarning($"Add Component: '{className}' could not be added.");
        }
    }
}
