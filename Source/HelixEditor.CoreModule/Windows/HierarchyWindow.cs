using System;
using System.Collections.Generic;
using HelixEditor.SceneManagement;
using HelixEngine;
using HelixEngine.SceneManagement;
using HelixEngine.UI;
using Object = HelixEngine.Object;

namespace HelixEditor
{
    /// <summary>The Hierarchy window: the open scenes and their GameObjects as a tree. Clicks select (ctrl adds, shift
    /// spans) as the editor's Selection does, F2 renames, Delete deletes, a drag moves objects into another (onto its
    /// row) or between others; the search field lists the objects whose names hold its text; the Create menu and the
    /// context menu come from the menu registry (GameObject). It follows the scenes' changes
    /// (EditorApplication.hierarchyChanged, the scene events).</summary>
    public sealed class HierarchyWindow : EditorWindow
    {
        // A row: a scene's header, or a GameObject.
        internal sealed class Row
        {
            public Scene scene;
            public GameObject gameObject;
        }

        TreeView m_Tree;
        TextField m_Search;
        bool m_Dirty = true, m_Syncing, m_Rebuilding;
        // Stable ids of the rows across rebuilds (their expansion is kept by them).
        readonly Dictionary<EntityId, int> m_ObjectIds = new();
        readonly Dictionary<Scene, int> m_SceneIds = new();
        readonly HashSet<int> m_Expanded = new(), m_CollapsedScenes = new();
        int m_NextId = 1;
        GameObject m_Renaming;

        // A new object's name is edited at once; the parents of new children open.
        internal static GameObject renameNext;
        internal static readonly HashSet<EntityId> revealNext = new();

        public TreeView treeInternal => m_Tree;
        public TextField searchFieldInternal => m_Search;
        public static Element RowElementInternal(Element item) => RowElement(item);
        public void BeginRenameInternal(GameObject go) => BeginRename(go);

        void OnEnable()
        {
            titleContent = new GuiContent("Hierarchy");
            WindowMarkup.Load(this, "UI/Windows/HierarchyWindow.hxml");
            Button create = rootElement.Find<Button>("hierarchy-create");
            if (create != null)
                create.clicked += () => OpenMenu(create, create.worldBound, null, false);
            m_Search = rootElement.Find<TextField>("hierarchy-search");
            m_Search?.RegisterValueChangedCallback(e => m_Dirty = true);
            SearchBox.Attach(m_Search);
            Element content = rootElement.Find("hierarchy-content") ?? rootElement;
            m_Tree = new TreeView { name = "hierarchy-tree", selectionType = SelectionType.Multiple, reorderable = true, fixedItemHeight = 18 };
            m_Tree.AddToClassList("hierarchy-tree");
            m_Tree.makeItem = MakeRow;
            m_Tree.bindItem = BindRow;
            m_Tree.selectionChanged += OnTreeSelection;
            m_Tree.itemExpandedChanged += OnExpanded;
            m_Tree.canStartDrag += a => DraggedObjects().Count > 0;
            m_Tree.dragAndDropUpdate += a => Target(a, out _, out _) ? DragVisualMode.Move : DragVisualMode.Rejected;
            m_Tree.handleDrop += OnDrop;
            m_Tree.RegisterCallback<KeyDownEvent>(OnKeyDown);
            m_Tree.RegisterCallback<PointerDownEvent>(OnPointerDown);
            content.Add(m_Tree);
            // The rows' height from the skin's metric.
            m_Tree.RegisterCallback<CustomStyleResolvedEvent>(e =>
            {
                if (m_Tree.customStyle.TryGetValue(new CustomStyleProperty<float>("--hx-hierarchy-row-height"), out float h) && h > 0 &&
                    Math.Abs(h - m_Tree.fixedItemHeight) > 0.01f)
                    m_Tree.fixedItemHeight = h;
            });
            EditorApplication.hierarchyChanged += MarkDirty;
            Selection.selectionChanged += OnSelection;
            EditorSceneManager.sceneOpened += OnSceneOpened;
            EditorSceneManager.newSceneCreated += OnSceneCreated;
            EditorSceneManager.sceneSaved += OnSceneEvent;
            EditorSceneManager.sceneDirtied += OnSceneEvent;
            EditorSceneManager.sceneClosed += OnSceneEvent;
        }

        void OnDisable()
        {
            EditorApplication.hierarchyChanged -= MarkDirty;
            Selection.selectionChanged -= OnSelection;
            EditorSceneManager.sceneOpened -= OnSceneOpened;
            EditorSceneManager.newSceneCreated -= OnSceneCreated;
            EditorSceneManager.sceneSaved -= OnSceneEvent;
            EditorSceneManager.sceneDirtied -= OnSceneEvent;
            EditorSceneManager.sceneClosed -= OnSceneEvent;
        }

        void MarkDirty() => m_Dirty = true;
        void OnSceneOpened(Scene s, OpenSceneMode m) => m_Dirty = true;
        void OnSceneCreated(Scene s, NewSceneSetup setup, NewSceneMode m) => m_Dirty = true;
        void OnSceneEvent(Scene s) => m_Dirty = true;

        void Update()
        {
            if (m_Dirty)
                RebuildInternal();
        }

        [MenuItem("Window/General/Hierarchy %4", false, 20)] static void Open() => GetWindow<HierarchyWindow>();

        // ------------------------------------------------------------------------------------------------- the tree

        int IdOf(GameObject go)
        {
            EntityId e = go.GetEntityId();
            if (!m_ObjectIds.TryGetValue(e, out int id))
                m_ObjectIds[e] = id = m_NextId++;
            return id;
        }

        int IdOf(Scene s)
        {
            if (!m_SceneIds.TryGetValue(s, out int id))
                m_SceneIds[s] = id = m_NextId++;
            return id;
        }

        static bool Hidden(GameObject go) => (go.hideFlags & HideFlags.HideInHierarchy) != 0;

        TreeViewItemData<Row> Build(GameObject go)
        {
            var children = new List<TreeViewItemData<Row>>();
            Transform t = go.transform;
            for (int i = 0; i < t.childCount; i++)
                if (!Hidden(t.GetChild(i).gameObject))
                    children.Add(Build(t.GetChild(i).gameObject));
            return new TreeViewItemData<Row>(IdOf(go), new Row { gameObject = go, scene = go.scene }, children);
        }

        static void Matching(GameObject go, string filter, List<GameObject> into)
        {
            if (Hidden(go))
                return;
            if (go.name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0)
                into.Add(go);
            Transform t = go.transform;
            for (int i = 0; i < t.childCount; i++)
                Matching(t.GetChild(i).gameObject, filter, into);
        }

        /// <summary>The rows again from the open scenes (the editor's update does it after their changes).</summary>
        public void RebuildInternal()
        {
            m_Dirty = false;
            if (m_Tree == null)
                return;
            string filter = m_Search?.value?.Trim() ?? "";
            var roots = new List<TreeViewItemData<Row>>();
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                var children = new List<TreeViewItemData<Row>>();
                foreach (GameObject root in scene.GetRootGameObjects())
                {
                    if (Hidden(root))
                        continue;
                    if (filter.Length == 0)
                        children.Add(Build(root));
                    else
                    {
                        var found = new List<GameObject>();
                        Matching(root, filter, found);
                        foreach (GameObject f in found)
                            children.Add(new TreeViewItemData<Row>(IdOf(f), new Row { gameObject = f, scene = scene }));
                    }
                }
                roots.Add(new TreeViewItemData<Row>(IdOf(scene), new Row { scene = scene }, children));
            }
            // The view's own expansion changes while it takes the rows are not the user's.
            m_Rebuilding = true;
            m_Tree.SetRootItems(roots);
            m_Tree.Rebuild();
            foreach (TreeViewItemData<Row> r in roots)
                if (!m_CollapsedScenes.Contains(r.id))
                    m_Tree.ExpandItem(r.id);
            foreach (EntityId e in revealNext)
                if (m_ObjectIds.TryGetValue(e, out int rid))
                    m_Expanded.Add(rid);
            revealNext.Clear();
            foreach (int id in m_Expanded)
                if (m_Tree.GetItemDataForId<Row>(id) != null)
                    m_Tree.ExpandItem(id);
            m_Rebuilding = false;
            SyncSelection();
            if (renameNext != null)
            {
                GameObject go = renameNext;
                renameNext = null;
                BeginRename(go);
            }
        }

        void OnExpanded(TreeViewExpansionChangedArgs a)
        {
            if (m_Rebuilding)
                return;
            Row r = m_Tree.GetItemDataForId<Row>(a.id);
            if (r == null)
                return;
            bool expanded = m_Tree.IsExpanded(a.id);
            if (r.gameObject == null)
            {
                if (expanded)
                    m_CollapsedScenes.Remove(a.id);
                else
                    m_CollapsedScenes.Add(a.id);
            }
            else if (expanded)
                m_Expanded.Add(a.id);
            else
                m_Expanded.Remove(a.id);
        }

        static Element MakeRow()
        {
            var row = new Element();
            row.AddToClassList("hierarchy-row");
            var icon = new Element { name = "icon" };
            icon.AddToClassList("hierarchy-icon");
            row.Add(icon);
            var label = new Label { name = "name" };
            label.AddToClassList("hierarchy-name");
            row.Add(label);
            return row;
        }

        void BindRow(Element row, int index)
        {
            Row r = m_Tree.GetItemDataForIndex<Row>(index);
            var label = row.Find<Label>("name");
            Element icon = row.Find("icon");
            row.Find<TextField>("rename")?.RemoveFromHierarchy();
            if (label != null)
                label.style.display = DisplayStyle.Flex;
            bool scene = r?.gameObject == null;
            row.EnableInClassList("hierarchy-scene", scene);
            row.EnableInClassList("hierarchy-inactive", !scene && r.gameObject != null && !r.gameObject.activeInHierarchy);
            icon?.EnableInClassList("icon-scene", scene);
            icon?.EnableInClassList("icon-game-object", !scene);
            if (label != null && r != null)
                label.text = scene ? (r.scene.name + (r.scene.isDirty ? "*" : "")) : r.gameObject.name;
            if (!scene && r.gameObject == m_Renaming)
                ShowRenameField(row, r.gameObject);
        }

        // ---------------------------------------------------------------------------------------- the selection

        void OnTreeSelection(IEnumerable<object> items)
        {
            if (m_Syncing)
                return;
            var objects = new List<Object>();
            foreach (object o in items)
                if (o is Row r && r.gameObject != null)
                    objects.Add(r.gameObject);
            m_Syncing = true;
            try
            {
                Selection.objects = objects.ToArray();
            }
            finally
            {
                m_Syncing = false;
            }
        }

        void OnSelection()
        {
            if (!m_Syncing)
                SyncSelection();
        }

        // The tree's selection from the editor's (the selected objects' parents expanded).
        void SyncSelection()
        {
            if (m_Tree == null)
                return;
            var ids = new List<int>();
            foreach (GameObject go in Selection.gameObjects)
            {
                if (!m_ObjectIds.TryGetValue(go.GetEntityId(), out int id) || m_Tree.GetItemDataForId<Row>(id) == null)
                    continue;
                for (Transform p = go.transform.parent; p != null; p = p.parent)
                    if (m_ObjectIds.TryGetValue(p.gameObject.GetEntityId(), out int pid) && !m_Tree.IsExpanded(pid))
                    {
                        m_Expanded.Add(pid);
                        m_Tree.ExpandItem(pid);
                    }
                ids.Add(id);
            }
            m_Syncing = true;
            try
            {
                m_Tree.SetSelectionByIdWithoutNotify(ids);
            }
            finally
            {
                m_Syncing = false;
            }
        }

        // ------------------------------------------------------------------------------------------ the keyboard

        void OnKeyDown(KeyDownEvent e)
        {
            if (m_Renaming != null)
                return;
            if (e.keyCode == KeyCode.F2 && Selection.activeGameObject != null)
            {
                BeginRename(Selection.activeGameObject);
                e.StopPropagation();
            }
            else if (e.keyCode == KeyCode.Delete && Selection.gameObjects.Length > 0)
            {
                HierarchyCommands.DeleteSelection();
                e.StopPropagation();
            }
        }

        // ----------------------------------------------------------------------------------------------- renaming

        /// <summary>The object's name edited in its row (Enter or leaving the field keeps it, Escape does not).</summary>
        internal void BeginRename(GameObject go)
        {
            if (go == null || !m_ObjectIds.TryGetValue(go.GetEntityId(), out int id))
                return;
            m_Renaming = go;
            m_Tree.ScrollToItemById(id);
            Element row = RowElement(m_Tree.GetRootElementForId(id));
            if (row != null)
                ShowRenameField(row, go);
        }

        public bool renamingInternal => m_Renaming != null;

        void ShowRenameField(Element row, GameObject go)
        {
            if (row.Find<TextField>("rename") != null)
                return;
            var label = row.Find<Label>("name");
            if (label != null)
                label.style.display = DisplayStyle.None;
            var field = new TextField { name = "rename", value = go.name };
            field.AddToClassList("hierarchy-rename");
            bool done = false;
            void End(bool keep)
            {
                if (done)
                    return;
                done = true;
                m_Renaming = null;
                if (keep && go != null && field.value.Length > 0 && field.value != go.name)
                {
                    go.name = field.value;
                    EditorSceneManager.MarkSceneDirty(go.scene);
                }
                m_Dirty = true;
            }
            field.RegisterCallback<KeyDownEvent>(e =>
            {
                if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter)
                {
                    End(true);
                    e.StopPropagation();
                }
                else if (e.keyCode == KeyCode.Escape)
                {
                    End(false);
                    e.StopPropagation();
                }
            });
            field.RegisterCallback<FocusOutEvent>(e => End(true));
            row.Add(field);
            field.Focus();
            field.SelectAll();
        }

        /// <summary>For tests: the field of the row being renamed.</summary>
        public TextField renameFieldInternal
        {
            get
            {
                if (m_Renaming == null || !m_ObjectIds.TryGetValue(m_Renaming.GetEntityId(), out int id))
                    return null;
                return RowElement(m_Tree.GetRootElementForId(id))?.Find<TextField>("rename");
            }
        }

        // ----------------------------------------------------------------------------------------- drag and drop

        List<GameObject> DraggedObjects()
        {
            var list = new List<GameObject>();
            foreach (object o in m_Tree.selectedItems)
                if (o is Row r && r.gameObject != null)
                    list.Add(r.gameObject);
            // The top level only (a child goes with its parent).
            list.RemoveAll(go =>
            {
                for (Transform p = go.transform.parent; p != null; p = p.parent)
                    if (list.Contains(p.gameObject))
                        return true;
                return false;
            });
            return list;
        }

        // Where a drop puts the dragged objects: under a parent (null: a scene's roots) of a scene; false where they
        // cannot go (into themselves).
        bool Target(HandleDragAndDropArgs a, out Scene scene, out Transform parent)
        {
            scene = SceneManager.GetActiveScene();
            parent = null;
            Row target = a.parentId >= 0 ? m_Tree.GetItemDataForId<Row>(a.parentId) : null;
            if (target != null && target.gameObject == null)
                scene = target.scene;
            else if (target != null)
            {
                parent = target.gameObject.transform;
                scene = target.gameObject.scene;
            }
            // Between two scenes' rows (a scene's header below): the end of the scene above.
            if (a.parentId < 0 && target == null && a.childIndex >= 0)
                return false;
            foreach (GameObject go in DraggedObjects())
                if (parent != null && (parent == go.transform || parent.IsChildOf(go.transform)))
                    return false;
            return true;
        }

        DragVisualMode OnDrop(HandleDragAndDropArgs a)
        {
            if (!Target(a, out Scene scene, out Transform parent))
                return DragVisualMode.Rejected;
            HierarchyCommands.Move(DraggedObjects(), scene, parent, a.childIndex);
            m_Dirty = true;
            return DragVisualMode.Move;
        }

        // -------------------------------------------------------------------------------------------- the menus

        void OnPointerDown(PointerDownEvent e)
        {
            if (e.button != 1)
                return;
            // The row under the pointer is selected first (unless it is among the selection).
            Row row = null;
            for (Element el = e.target as Element; el != null && el != m_Tree; el = el.parent)
                if (el.ClassListContains("hierarchy-row"))
                {
                    int index = IndexOfRow(el);
                    row = index >= 0 ? m_Tree.GetItemDataForIndex<Row>(index) : null;
                    break;
                }
            if (row?.gameObject != null && !Selection.Contains(row.gameObject))
                Selection.activeObject = row.gameObject;
            OpenMenu(m_Tree, new Rect(e.position.x, e.position.y, 0, 0), row, true);
            e.StopPropagation();
        }

        // The index of a shown row's element (-1 when it shows none).
        int IndexOfRow(Element row)
        {
            int count = m_Tree.viewController?.GetItemsCount() ?? 0;
            for (int i = 0; i < count; i++)
                if (RowElement(m_Tree.GetRootElementForIndex(i)) == row)
                    return i;
            return -1;
        }

        // The row this window made inside the view's item.
        internal static Element RowElement(Element item)
        {
            if (item == null || item.ClassListContains("hierarchy-row"))
                return item;
            foreach (Element c in item.Children())
                if (RowElement(c) is Element found)
                    return found;
            return null;
        }

        void OpenMenu(Element anchor, Rect at, Row row, bool context)
        {
            var menu = new GenericDropdownMenu();
            if (context && row != null && row.gameObject == null)
            {
                Scene scene = row.scene;
                menu.AddItem("Set Active Scene", false, () => EditorSceneManager.SetActiveScene(scene));
                menu.AddItem("Save Scene", false, () => EditorSceneManager.SaveScene(scene));
                if (SceneManager.sceneCount > 1)
                    menu.AddItem("Remove Scene", false, () => EditorSceneManager.CloseScene(scene, true));
                else
                    menu.AddDisabledItem("Remove Scene", false);
                menu.AddSeparator("");
            }
            else if (context)
            {
                bool any = Selection.gameObjects.Length > 0;
                if (any)
                {
                    menu.AddItem("Rename", false, () => BeginRename(Selection.activeGameObject));
                    menu.AddItem("Duplicate", false, HierarchyCommands.DuplicateSelection);
                    menu.AddItem("Delete", false, HierarchyCommands.DeleteSelection);
                }
                else
                {
                    menu.AddDisabledItem("Rename", false);
                    menu.AddDisabledItem("Duplicate", false);
                    menu.AddDisabledItem("Delete", false);
                }
                menu.AddSeparator("");
            }
            EditorShell.AddEntries(menu, "GameObject", "");
            EditorShell.ShowMenu(menu, anchor, at);
        }
    }

    /// <summary>What the Hierarchy and the GameObject and Edit menus do to the scenes' objects (each change marks its
    /// scene changed; undo comes with the undo step).</summary>
    internal static class HierarchyCommands
    {
        // A name no sibling has: "Name", else "Name (1)" ...
        internal static string UniqueName(Scene scene, Transform parent, string name, GameObject except = null)
        {
            var taken = new HashSet<string>();
            if (parent != null)
                for (int i = 0; i < parent.childCount; i++)
                {
                    GameObject c = parent.GetChild(i).gameObject;
                    if (c != except)
                        taken.Add(c.name);
                }
            else if (scene.IsValid())
                foreach (GameObject r in scene.GetRootGameObjects())
                    if (r != except)
                        taken.Add(r.name);
            if (!taken.Contains(name))
                return name;
            string stem = name;
            int n = 1;
            // "Name (3)" goes on from 3.
            int open = name.LastIndexOf(" (", StringComparison.Ordinal);
            if (open > 0 && name.EndsWith(")") && int.TryParse(name.Substring(open + 2, name.Length - open - 3), out int k))
            {
                stem = name.Substring(0, open);
                n = k + 1;
            }
            while (taken.Contains($"{stem} ({n})"))
                n++;
            return $"{stem} ({n})";
        }

        /// <summary>A new object in the active scene (under a parent), selected and named in the Hierarchy.</summary>
        internal static GameObject Create(string name, Transform parent, params Type[] components)
        {
            Scene scene = parent != null ? parent.gameObject.scene : SceneManager.GetActiveScene();
            var go = new GameObject(UniqueName(scene, parent, name));
            if (parent != null)
                go.transform.SetParent(parent, false);
            for (Transform p = parent; p != null; p = p.parent)
                HierarchyWindow.revealNext.Add(p.gameObject.GetEntityId());
            foreach (Type c in components)
                go.AddComponent(c);
            EditorSceneManager.MarkSceneDirty(go.scene);
            Selection.activeObject = go;
            HierarchyWindow.renameNext = go;
            return go;
        }

        internal static void DeleteSelection()
        {
            var scenes = new List<Scene>();
            foreach (Transform t in Selection.GetTransforms(SelectionMode.TopLevel | SelectionMode.ExcludePrefab))
            {
                scenes.Add(t.gameObject.scene);
                Object.DestroyImmediate(t.gameObject);
            }
            foreach (Scene s in scenes)
                EditorSceneManager.MarkSceneDirty(s);
            Selection.objects = Array.Empty<Object>();
        }

        internal static void DuplicateSelection()
        {
            var made = new List<Object>();
            foreach (Transform t in Selection.GetTransforms(SelectionMode.TopLevel | SelectionMode.ExcludePrefab))
            {
                GameObject original = t.gameObject;
                var copy = (GameObject)Object.Instantiate(original, t.parent);
                if (t.parent == null && copy.scene != original.scene)
                    SceneManager.MoveGameObjectToScene(copy, original.scene);
                copy.name = UniqueName(original.scene, t.parent, original.name, copy);
                copy.transform.SetSiblingIndex(t.GetSiblingIndex() + 1);
                EditorSceneManager.MarkSceneDirty(original.scene);
                made.Add(copy);
            }
            if (made.Count > 0)
                Selection.objects = made.ToArray();
        }

        /// <summary>Objects moved under a parent (null: the scene's roots), from a place among its children on (-1:
        /// last); their world positions stay.</summary>
        internal static void Move(List<GameObject> objects, Scene scene, Transform parent, int childIndex)
        {
            int index = childIndex;
            foreach (GameObject go in objects)
            {
                Scene from = go.scene;
                Transform t = go.transform;
                // A place counted with the object still there: one less when it moves down among the same siblings.
                if (index >= 0 && t.parent == parent && go.scene == scene && t.GetSiblingIndex() < index)
                    index--;
                if (parent == null)
                {
                    t.SetParent(null, true);
                    if (go.scene != scene)
                        SceneManager.MoveGameObjectToScene(go, scene);
                }
                else
                    t.SetParent(parent, true);
                if (index >= 0)
                    t.SetSiblingIndex(index++);
                else
                    t.SetAsLastSibling();
                EditorSceneManager.MarkSceneDirty(from);
                EditorSceneManager.MarkSceneDirty(go.scene);
            }
        }
    }
}

namespace HelixEditor
{
    /// <summary>A window's search box around its field: a click anywhere in it puts the focus into the field, which
    /// marks the box while it has it.</summary>
    internal static class SearchBox
    {
        internal static void Attach(TextField field)
        {
            Element box = field?.parent;
            if (box == null)
                return;
            box.RegisterCallback<PointerDownEvent>(e =>
            {
                for (Element t = e.target as Element; t != null && t != box; t = t.parent)
                    if (t == field)
                        return;
                field.Focus();
            });
            field.RegisterCallback<FocusInEvent>(e => box.AddToClassList("window-search--focused"));
            field.RegisterCallback<FocusOutEvent>(e => box.RemoveFromClassList("window-search--focused"));
        }
    }
}
