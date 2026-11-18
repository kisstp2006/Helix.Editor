using System;
using System.Collections.Generic;
using System.Reflection;
using HelixEditor.SceneManagement;
using HelixEditor.UIElements;
using HelixEngine;
using HelixEngine.SceneManagement;
using HelixEngine.UI;
using Object = HelixEngine.Object;

namespace HelixEditor
{
    /// <summary>How the Inspector shows objects: through their editors, or every serialized field (Debug).</summary>
    public enum InspectorMode
    {
        Normal = 0,
        Debug = 1,
        DebugInternal = 2,
    }

    /// <summary>The Inspector window: the selection's objects. GameObjects show their header (active, name, static, tag,
    /// layer) and the components they all have, each under a title bar (its foldout, icon, enabled toggle, name and
    /// menu: reset, remove, move, copy and paste values) with its editor below; Add Component lists the classes the
    /// engine knows. Assets show their main object's editor. Locked, it keeps its objects as the selection changes;
    /// in Debug mode every serialized field shows through the default inspector.</summary>
    public sealed class InspectorWindow : EditorWindow, IHasCustomMenu
    {
        Element m_Content;
        ScrollView m_Scroll;
        bool m_Dirty = true, m_Locked;
        InspectorMode m_Mode;
        Object[] m_LockedObjects = Array.Empty<Object>();
        // What the shown content was built for: its objects and their components.
        string m_Signature = "", m_Objects = "";
        readonly List<Editor> m_Editors = new();
        readonly List<Action> m_Refresh = new();

        // Which component classes are open (closed ones listed), kept while the editor runs.
        static readonly HashSet<string> s_Closed = new();
        // Copy Component's values: the class and its leaves.
        static Type s_CopiedType;
        static List<(string path, int kind, string text, Object obj)> s_Copied;

        public bool isLocked
        {
            get => m_Locked;
            set
            {
                if (m_Locked == value)
                    return;
                m_Locked = value;
                m_LockedObjects = value ? Selection.objects : Array.Empty<Object>();
                m_Dirty = true;
                Docking.DockManager.Changed();
            }
        }

        public InspectorMode inspectorMode
        {
            get => m_Mode;
            set
            {
                if (m_Mode == value)
                    return;
                m_Mode = value;
                m_Dirty = true;
            }
        }

        public Element contentInternal => m_Content;
        public ScrollView scrollInternal => m_Scroll;
        // How many times the content was built (a test sees it is not built again while nothing changes).
        public int rebuildsInternal { get; private set; }
        public IReadOnlyList<Editor> editorsInternal => m_Editors;
        public void RebuildInternal() => Rebuild();

        /// <summary>A component's menu as its title bar opens it (for tests).</summary>
        public GenericMenu ComponentMenuInternal(Component component)
        {
            Component[] all = component.gameObject.GetComponents<Component>();
            return ComponentMenu(new Object[] { component }, Array.IndexOf(all, component), all.Length);
        }

        /// <summary>The Add Component list opened as its button opens it (null without GameObjects shown).</summary>
        public Element ShowAddComponentInternal()
        {
            Button add = m_Content.Find<Button>("inspector-add-component");
            var gos = new List<GameObject>();
            foreach (Object o in Targets())
                if (o is GameObject go)
                    gos.Add(go);
            return add == null || gos.Count == 0 ? null : AddComponentPopup.Show(add, gos.ToArray());
        }

        void OnEnable()
        {
            titleContent = new GuiContent("Inspector");
            WindowMarkup.Load(this, "UI/Windows/InspectorWindow.hxml");
            m_Scroll = rootElement.Find<ScrollView>("inspector-scroll");
            m_Content = rootElement.Find("inspector-content") ?? rootElement;
            Selection.selectionChanged += OnSelection;
            EditorApplication.hierarchyChanged += MarkDirty;
        }

        void OnDisable()
        {
            Selection.selectionChanged -= OnSelection;
            EditorApplication.hierarchyChanged -= MarkDirty;
            ReleaseEditors();
        }

        void OnSelection()
        {
            if (!m_Locked)
                m_Dirty = true;
        }

        void MarkDirty() => m_Dirty = true;

        void Update()
        {
            // The objects' components may change without an event (a script adds one): their signature is polled.
            if (m_Dirty || Signature(Targets()) != m_Signature)
                Rebuild();
            else
                foreach (Action r in m_Refresh)
                    r();
        }

        [MenuItem("Window/General/Inspector %3", false, 23)] static void Open() => GetWindow<InspectorWindow>();

        // The lock in the tab strip.
        void ShowButton(Rect r)
        {
            if (Gui.Button(r, GuiContent.none, GuiStyle.none))
                isLocked = !isLocked;
            Texture2D icon = EditorGuiUtility.FindTexture("Lock");
            if (icon == null)
                return;
            Color was = Gui.color;
            Gui.color = m_Locked ? new Color(0.36f, 0.62f, 0.95f, 1) : new Color(0.6f, 0.6f, 0.6f, 1);
            Gui.DrawTexture(r, icon);
            Gui.color = was;
        }

        public void AddItemsToMenu(GenericMenu menu)
        {
            menu.AddItem(new GuiContent("Normal"), m_Mode == InspectorMode.Normal, () => inspectorMode = InspectorMode.Normal);
            menu.AddItem(new GuiContent("Debug"), m_Mode != InspectorMode.Normal, () => inspectorMode = InspectorMode.Debug);
            menu.AddSeparator("");
            menu.AddItem(new GuiContent("Lock"), m_Locked, () => isLocked = !isLocked);
        }

        // ------------------------------------------------------------------------------------------- the content

        Object[] Targets()
        {
            Object[] all = m_Locked ? m_LockedObjects : Selection.objects;
            var alive = new List<Object>();
            foreach (Object o in all)
                if (o != null)
                    alive.Add(o);
            return alive.ToArray();
        }

        static string Signature(Object[] targets)
        {
            var sb = new System.Text.StringBuilder();
            foreach (Object o in targets)
            {
                sb.Append(o.GetEntityId().value).Append(':');
                if (o is GameObject go)
                    foreach (Component c in go.GetComponents<Component>())
                        sb.Append(c != null ? c.GetEntityId().value : 0).Append(',');
                sb.Append(';');
            }
            return sb.ToString();
        }

        void ReleaseEditors()
        {
            foreach (Editor e in m_Editors)
                if (e != null)
                    DestroyImmediate(e);
            m_Editors.Clear();
            m_Refresh.Clear();
        }

        void Rebuild()
        {
            m_Dirty = false;
            rebuildsInternal++;
            Object[] targets = Targets();
            // The same objects keep their place in the scroll (another selection starts at the top).
            string objects = ObjectsKey(targets);
            Vector2 scroll = m_Scroll != null && objects == m_Objects ? m_Scroll.scrollOffset : Vector2.zero;
            m_Objects = objects;
            m_Signature = Signature(targets);
            m_Content.Clear();
            ReleaseEditors();
            if (targets.Length == 0)
                return;
            var gameObjects = new List<GameObject>();
            foreach (Object o in targets)
                if (o is GameObject go)
                    gameObjects.Add(go);
            if (gameObjects.Count == targets.Length)
                ShowGameObjects(gameObjects.ToArray());
            else if (SameClass(targets))
                ShowObjects(targets);
            else
                m_Content.Add(Note("Narrow the selection to objects of one type to edit them together."));
            // Put back once the new content is laid out (before, the offset is clamped to the empty content's).
            if (m_Scroll != null)
            {
                m_Scroll.scrollOffset = scroll;
                if (scroll != Vector2.zero)
                    m_Content.RegisterCallbackOnce<GeometryChangedEvent>(e => m_Scroll.scrollOffset = scroll);
            }
        }

        static string ObjectsKey(Object[] targets)
        {
            var sb = new System.Text.StringBuilder();
            foreach (Object o in targets)
                sb.Append(o.GetEntityId().value).Append(',');
            return sb.ToString();
        }

        static bool SameClass(Object[] targets)
        {
            foreach (Object o in targets)
                if (o.GetType() != targets[0].GetType())
                    return false;
            return true;
        }

        static Label Note(string text)
        {
            var l = new Label(text);
            l.AddToClassList("inspector-note");
            return l;
        }

        Editor MakeEditor(Object[] targets)
        {
            Type type = m_Mode != InspectorMode.Normal ? typeof(GenericInspector) : null;
            // Several objects take a registered editor only if it edits several.
            if (type == null && targets.Length > 1)
            {
                Type own = Editors.For(targets[0].GetType());
                if (own != typeof(GenericInspector) && own.GetCustomAttribute<CanEditMultipleObjects>() == null)
                    return null;
            }
            Editor e = Editor.CreateEditor(targets, type);
            if (e != null)
            {
                if (e.serializedObject != null)
                    e.serializedObject.debugModeInternal = m_Mode != InspectorMode.Normal;
                m_Editors.Add(e);
            }
            return e;
        }

        // An editor's body: its inspector, or the note of objects it does not edit together.
        Element Body(Object[] targets)
        {
            Editor e = MakeEditor(targets);
            if (e == null)
                return Note("Multi-object editing not supported.");
            var inspector = new InspectorElement(e) { name = "inspector:" + targets[0].GetType().Name };
            return inspector;
        }

        // ------------------------------------------------------------------------------------ GameObjects

        void ShowGameObjects(GameObject[] gos)
        {
            m_Content.Add(GameObjectHeader(gos));
            // The components all of them have: the first one's, matched by class (the n-th of a class with the n-th).
            Component[] first = gos[0].GetComponents<Component>();
            var seen = new Dictionary<Type, int>();
            bool skipped = false;
            for (int i = 0; i < first.Length; i++)
            {
                Component c = first[i];
                if (c == null)
                    continue;
                Type t = c.GetType();
                int nth = seen.TryGetValue(t, out int n) ? n : 0;
                seen[t] = nth + 1;
                var matched = new List<Object> { c };
                for (int k = 1; k < gos.Length && matched != null; k++)
                {
                    Component other = Nth(gos[k], t, nth);
                    if (other == null)
                        matched = null;
                    else
                        matched.Add(other);
                }
                if (matched == null)
                {
                    skipped = true;
                    continue;
                }
                AddComponentSection(matched.ToArray(), i, first.Length);
            }
            if (!skipped && gos.Length > 1)
                foreach (GameObject go in gos)
                    if (go.GetComponents<Component>().Length != first.Length)
                        skipped = true;
            if (skipped)
                m_Content.Add(Note("Components that are only on some of the selected objects cannot be multi-edited."));
            var add = new Button { name = "inspector-add-component", text = "Add Component" };
            add.AddToClassList("inspector-add-component");
            add.clicked += () => AddComponentPopup.Show(add, gos);
            var row = new Element();
            row.AddToClassList("inspector-add-row");
            row.Add(add);
            m_Content.Add(row);
        }

        static Component Nth(GameObject go, Type t, int nth)
        {
            foreach (Component c in go.GetComponents<Component>())
                if (c != null && c.GetType() == t && nth-- == 0)
                    return c;
            return null;
        }

        Element GameObjectHeader(GameObject[] gos)
        {
            var header = new Element { name = "inspector-game-object" };
            header.AddToClassList("inspector-header");
            var top = new Element();
            top.AddToClassList("inspector-header-row");
            var icon = new Element();
            icon.AddToClassList("inspector-header-icon");
            icon.AddToClassList("icon-game-object");
            top.Add(icon);
            var active = new Toggle { name = "inspector-active" };
            active.AddToClassList("inspector-active");
            active.SetValueWithoutNotify(gos[0].activeSelf);
            active.RegisterValueChangedCallback(e =>
            {
                foreach (GameObject go in gos)
                    if (go != null && go.activeSelf != e.newValue)
                    {
                        go.SetActive(e.newValue);
                        EditorSceneManager.MarkSceneDirty(go.scene);
                    }
            });
            top.Add(active);
            var name = new TextField { name = "inspector-name", isDelayed = true };
            name.AddToClassList("inspector-name");
            name.SetValueWithoutNotify(gos.Length == 1 ? gos[0].name : SameName(gos));
            name.RegisterValueChangedCallback(e =>
            {
                foreach (GameObject go in gos)
                    if (go != null && go.name != e.newValue)
                    {
                        go.name = e.newValue;
                        EditorSceneManager.MarkSceneDirty(go.scene);
                    }
            });
            top.Add(name);
            var isStatic = new Toggle("Static") { name = "inspector-static" };
            isStatic.AddToClassList("inspector-static");
            isStatic.SetValueWithoutNotify(gos[0].isStatic);
            isStatic.RegisterValueChangedCallback(e =>
            {
                foreach (GameObject go in gos)
                    if (go != null && go.isStatic != e.newValue)
                    {
                        go.isStatic = e.newValue;
                        EditorSceneManager.MarkSceneDirty(go.scene);
                    }
            });
            top.Add(isStatic);
            header.Add(top);

            var bottom = new Element();
            bottom.AddToClassList("inspector-header-row");
            var tag = new TagField("Tag") { name = "inspector-tag" };
            tag.AddToClassList("inspector-tag");
            tag.SetValueWithoutNotify(gos[0].tag);
            tag.RegisterValueChangedCallback(e =>
            {
                foreach (GameObject go in gos)
                    if (go != null && go.tag != e.newValue)
                    {
                        go.tag = e.newValue;
                        EditorSceneManager.MarkSceneDirty(go.scene);
                    }
            });
            bottom.Add(tag);
            var layer = new LayerField("Layer") { name = "inspector-layer" };
            layer.AddToClassList("inspector-layer");
            layer.SetValueWithoutNotify(gos[0].layer);
            layer.RegisterValueChangedCallback(e =>
            {
                foreach (GameObject go in gos)
                    if (go != null && go.layer != e.newValue)
                    {
                        go.layer = e.newValue;
                        EditorSceneManager.MarkSceneDirty(go.scene);
                    }
            });
            bottom.Add(layer);
            header.Add(bottom);

            // The header follows the objects' own changes (a rename in the Hierarchy).
            m_Refresh.Add(() =>
            {
                GameObject g = gos[0];
                if (g == null)
                    return;
                if (active.value != g.activeSelf)
                    active.SetValueWithoutNotify(g.activeSelf);
                string n = gos.Length == 1 ? g.name : SameName(gos);
                if (name.focusController?.focusedElement != name && name.value != n)
                    name.SetValueWithoutNotify(n);
                if (isStatic.value != g.isStatic)
                    isStatic.SetValueWithoutNotify(g.isStatic);
                if (tag.value != g.tag)
                    tag.SetValueWithoutNotify(g.tag);
                if (layer.value != g.layer)
                    layer.SetValueWithoutNotify(g.layer);
            });
            return header;
        }

        // The objects' name when they share it, else a dash (the mixed value).
        static string SameName(GameObject[] gos)
        {
            foreach (GameObject g in gos)
                if (g.name != gos[0].name)
                    return "-";
            return gos[0].name;
        }

        // ------------------------------------------------------------------------------------ components

        void AddComponentSection(Object[] components, int index, int count)
        {
            Type type = components[0].GetType();
            string key = type.FullName;
            var section = new Element { name = "component:" + type.Name };
            section.AddToClassList("inspector-component");
            var bar = new Element { name = "component-title" };
            bar.AddToClassList("inspector-titlebar");
            var arrow = new Element { name = "foldout" };
            arrow.AddToClassList("inspector-foldout");
            arrow.AddToClassList("icon");
            bar.Add(arrow);
            var icon = new Element();
            icon.AddToClassList("inspector-component-icon");
            if (ComponentIcon(type) is Texture2D tex)
                icon.style.backgroundImage = tex;
            bar.Add(icon);
            PropertyInfo enabled = type.GetProperty("enabled", BindingFlags.Instance | BindingFlags.Public);
            if (enabled != null && enabled.PropertyType == typeof(bool) && enabled.CanWrite)
            {
                var toggle = new Toggle { name = "component-enabled" };
                toggle.AddToClassList("inspector-component-enabled");
                toggle.SetValueWithoutNotify((bool)enabled.GetValue(components[0]));
                toggle.RegisterValueChangedCallback(e =>
                {
                    foreach (Object c in components)
                        if (c != null)
                        {
                            enabled.SetValue(c, e.newValue);
                            EditorUtility.SetDirty(c);
                        }
                });
                m_Refresh.Add(() =>
                {
                    if (components[0] != null && toggle.value != (bool)enabled.GetValue(components[0]))
                        toggle.SetValueWithoutNotify((bool)enabled.GetValue(components[0]));
                });
                bar.Add(toggle);
            }
            var title = new Label(ObjectNames.GetInspectorTitle(components[0])) { name = "component-name" };
            title.AddToClassList("inspector-component-name");
            bar.Add(title);
            var fill = new Element();
            fill.AddToClassList("window-fill");
            bar.Add(fill);
            var menuButton = new Element { name = "component-menu" };
            menuButton.AddToClassList("inspector-component-menu");
            menuButton.AddToClassList("icon");
            menuButton.AddToClassList("icon-menu");
            bar.Add(menuButton);
            section.Add(bar);

            Element body = Body(components);
            body.AddToClassList("inspector-component-body");
            section.Add(body);

            void Show(bool open)
            {
                arrow.EnableInClassList("icon-arrow-down", open);
                arrow.EnableInClassList("icon-arrow-right", !open);
                body.style.display = open ? DisplayStyle.Flex : DisplayStyle.None;
            }
            Show(!s_Closed.Contains(key));
            bar.RegisterCallback<PointerDownEvent>(e =>
            {
                for (Element t = e.target as Element; t != null && t != bar; t = t.parent)
                    if (t is Toggle)
                        return;
                bool onMenu = false;
                for (Element t = e.target as Element; t != null && t != bar; t = t.parent)
                    onMenu |= t == menuButton;
                if (e.button == 1 || onMenu)
                {
                    ComponentMenu(components, index, count).DropDownInternal(onMenu ? menuButton : bar);
                    e.StopPropagation();
                    return;
                }
                if (e.button != 0)
                    return;
                bool open = s_Closed.Contains(key);
                if (open)
                    s_Closed.Remove(key);
                else
                    s_Closed.Add(key);
                Show(open);
                e.StopPropagation();
            });
            m_Content.Add(section);
        }

        // The component's icon: the editor's icon of its class (or a base class), a script's, else a component's.
        internal static Texture2D ComponentIcon(Type type)
        {
            if (ObjectNames.IsScript(type))
                return EditorGuiUtility.FindTexture("ComponentScript") ?? EditorGuiUtility.FindTexture("Component");
            for (Type t = type; t != null && t != typeof(Object); t = t.BaseType)
                if (EditorGuiUtility.FindTexture("Component" + t.Name) is Texture2D tex)
                    return tex;
            return EditorGuiUtility.FindTexture("Component");
        }

        internal static GenericMenu ComponentMenu(Object[] components, int index, int count)
        {
            var menu = new GenericMenu();
            bool isTransform = components[0] is Transform;
            menu.AddItem(new GuiContent("Reset"), false, () => ComponentCommands.Reset(components));
            menu.AddSeparator("");
            if (isTransform)
                menu.AddDisabledItem(new GuiContent("Remove Component"));
            else
                menu.AddItem(new GuiContent("Remove Component"), false, () => ComponentCommands.Remove(components));
            if (isTransform || index <= 1)
                menu.AddDisabledItem(new GuiContent("Move Up"));
            else
                menu.AddItem(new GuiContent("Move Up"), false, () => ComponentCommands.Move(components, -1));
            if (isTransform || index >= count - 1)
                menu.AddDisabledItem(new GuiContent("Move Down"));
            else
                menu.AddItem(new GuiContent("Move Down"), false, () => ComponentCommands.Move(components, 1));
            menu.AddSeparator("");
            menu.AddItem(new GuiContent("Copy Component"), false, () =>
            {
                using var so = new SerializedObject(components[0]);
                s_CopiedType = components[0].GetType();
                s_Copied = SerializedProperty.SnapshotInternal(so);
            });
            bool canPaste = s_Copied != null;
            if (canPaste && !isTransform)
                menu.AddItem(new GuiContent("Paste Component As New"), false, () => ComponentCommands.PasteAsNew(components, s_CopiedType, s_Copied));
            else
                menu.AddDisabledItem(new GuiContent("Paste Component As New"));
            if (canPaste && s_CopiedType == components[0].GetType())
                menu.AddItem(new GuiContent("Paste Component Values"), false, () => ComponentCommands.PasteValues(components, s_Copied));
            else
                menu.AddDisabledItem(new GuiContent("Paste Component Values"));
            return menu;
        }

        // ------------------------------------------------------------------------------------ assets

        void ShowObjects(Object[] objects)
        {
            var header = new Element { name = "inspector-object" };
            header.AddToClassList("inspector-header");
            var row = new Element();
            row.AddToClassList("inspector-header-row");
            var icon = new Element();
            icon.AddToClassList("inspector-header-icon");
            Texture2D tex = EditorGuiUtility.ObjectContent(objects[0], objects[0].GetType()).image as Texture2D;
            if (tex != null)
                icon.style.backgroundImage = tex;
            row.Add(icon);
            var title = new Label(objects.Length == 1 ? ObjectNames.GetInspectorTitle(objects[0]) : $"{objects.Length} {ObjectNames.NicifyVariableName(objects[0].GetType().Name)}s")
            { name = "inspector-object-name" };
            title.AddToClassList("inspector-object-name");
            row.Add(title);
            var kind = new Label(ObjectNames.NicifyVariableName(objects[0].GetType().Name));
            kind.AddToClassList("inspector-object-type");
            row.Add(kind);
            header.Add(row);
            m_Content.Add(header);
            Element body = Body(objects);
            body.AddToClassList("inspector-object-body");
            m_Content.Add(body);
        }
    }

    /// <summary>What a component's menu does (each change marks the objects' scenes changed).</summary>
    internal static class ComponentCommands
    {
        static void Dirty(Component c)
        {
            if (c != null)
                EditorSceneManager.MarkSceneDirty(c.gameObject.scene);
        }

        /// <summary>The components' values set back to a new component's of their class (a script's Reset run after).</summary>
        internal static void Reset(Object[] components)
        {
            Type t = components[0].GetType();
            var temp = new GameObject("Reset Defaults") { hideFlags = HideFlags.HideAndDontSave };
            try
            {
                Component fresh = t == typeof(Transform) ? temp.transform : temp.AddComponent(t);
                if (fresh == null)
                    return;
                List<(string path, int kind, string text, Object obj)> values;
                using (var from = new SerializedObject(fresh))
                    values = SerializedProperty.SnapshotInternal(from);
                PasteValues(components, values);
            }
            finally
            {
                Object.DestroyImmediate(temp);
            }
            foreach (Object o in components)
                t.GetMethod("Reset", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null)?.Invoke(o, null);
        }

        internal static void PasteValues(Object[] components, List<(string path, int kind, string text, Object obj)> values)
        {
            foreach (Object o in components)
            {
                if (o == null)
                    continue;
                using var so = new SerializedObject(o);
                SerializedProperty.RestoreInternal(so, values);
                so.ApplyModifiedProperties();
                Dirty(o as Component);
            }
        }

        internal static void PasteAsNew(Object[] components, Type type, List<(string path, int kind, string text, Object obj)> values)
        {
            var made = new List<Object>();
            foreach (Object o in components)
                if (o is Component c && c.gameObject.AddComponent(type) is Component added)
                    made.Add(added);
            if (made.Count > 0)
                PasteValues(made.ToArray(), values);
        }

        internal static void Remove(Object[] components)
        {
            foreach (Object o in components)
                if (o is Component c && !(c is Transform))
                {
                    Scene scene = c.gameObject.scene;
                    Object.DestroyImmediate(c);
                    EditorSceneManager.MarkSceneDirty(scene);
                }
        }

        internal static void Move(Object[] components, int by)
        {
            foreach (Object o in components)
                if (o is Component c)
                {
                    Component[] all = c.gameObject.GetComponents<Component>();
                    int at = Array.IndexOf(all, c);
                    if (at > 0 && InspectorNative.MoveComponent(c, at + by))
                        Dirty(c);
                }
        }

        /// <summary>A component of the class (by the engine's name; "Scripts/Name" for scripts) added to each object.</summary>
        internal static List<Component> Add(GameObject[] gameObjects, string className)
        {
            var made = new List<Component>();
            foreach (GameObject go in gameObjects)
                if (go != null && InspectorNative.AddComponent(go, className) is Component c)
                {
                    ObjectFactory.ComponentWasAddedInternal(c);
                    made.Add(c);
                    EditorSceneManager.MarkSceneDirty(go.scene);
                }
            return made;
        }
    }
}
