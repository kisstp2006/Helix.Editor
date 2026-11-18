using System;
using System.Collections.Generic;
using HelixEditor.Docking;
using HelixEngine;
using HelixEngine.UI;

namespace HelixEditor.Tests
{
    // The editor's C# tests, run in the editor (-batchmode -executeMethod HelixEditor.Tests.EditorTests.Run): the dock
    // model's operations and its text form first, then the session's windows and their docking by the view's events,
    // one step a frame (the view is laid out between them). The editor ends with the number of failures.
    public static class EditorTests
    {
        static int s_Failures, s_Checks;
        static readonly Queue<(string name, Action step)> s_Steps = new();

        static void Check(bool condition, string what)
        {
            s_Checks++;
            if (condition)
                return;
            s_Failures++;
            Debug.LogError($"FAILED: {what}");
        }

        static void Equal<T>(T expected, T actual, string what) =>
            Check(EqualityComparer<T>.Default.Equals(expected, actual), $"{what}: expected '{expected}', got '{actual}'");

        public static void Run()
        {
            s_Steps.Enqueue(("the layout's text round trip", TextRoundTrip));
            s_Steps.Enqueue(("docking among an area's tabs", DockCenter));
            s_Steps.Enqueue(("docking at an area's edges", DockEdges));
            s_Steps.Enqueue(("removing tabs", RemoveTabs));
            s_Steps.Enqueue(("floating", Floating));
            s_Steps.Enqueue(("moving splitters", Splitters));
            s_Steps.Enqueue(("the start layout's windows", StartWindows));
            s_Steps.Enqueue(("dragging a tab to an edge", DragToEdge));
            s_Steps.Enqueue(("after the drag", AfterDragToEdge));
            s_Steps.Enqueue(("clicking a tab", ClickTab));
            s_Steps.Enqueue(("after the click", AfterClickTab));
            s_Steps.Enqueue(("dragging a tab out", DragOut));
            s_Steps.Enqueue(("after dragging out", AfterDragOut));
            s_Steps.Enqueue(("dragging a splitter", DragSplitter));
            s_Steps.Enqueue(("the window requests", WindowRequests));
            s_Steps.Enqueue(("the presets", Presets));
            EditorApplication.update += Next;
        }

        static void Next()
        {
            if (s_Steps.Count == 0)
            {
                EditorApplication.update -= Next;
                Debug.Log($"Editor tests: {s_Checks} checks, {s_Failures} failed.");
                EditorApplication.Exit(s_Failures == 0 ? 0 : 1);
                return;
            }
            var (name, step) = s_Steps.Dequeue();
            int before = s_Failures;
            try
            {
                step();
            }
            catch (Exception e)
            {
                s_Failures++;
                Debug.LogError($"FAILED: {name} threw {e}");
            }
            Debug.Log($"{(s_Failures == before ? "ok" : "FAILED")}: {name}");
        }

        // ---------------------------------------------------------------------------------- the model

        const string kDefault = "layout 1\n" +
                                "split column 0.7 0.3\n" +
                                "  split row 0.22 0.52 0.26\n" +
                                "    area 0 HierarchyWindow\n" +
                                "    area 1 SceneView GameView\n" +
                                "    area 0 InspectorWindow\n" +
                                "  area 0 ProjectWindow ConsoleWindow\n" +
                                "float 10 20 300 200\n" +
                                "  area 0 Lighting\n";

        static float Sum(DockSplit s)
        {
            float t = 0;
            foreach (float f in s.shares)
                t += f;
            return t;
        }

        static void TextRoundTrip()
        {
            DockLayout l = DockLayout.Read(kDefault);
            Equal(kDefault, l.Write(), "the text written back");
            var top = l.root as DockSplit;
            Check(top != null && top.column && top.children.Count == 2, "the top split is a column of two");
            var areas = new List<DockArea>(l.Areas());
            Equal(5, areas.Count, "the areas");
            Check(l.AreaOf("SceneView") == l.AreaOf("GameView"), "the scene and the game share an area");
            Equal("GameView", l.AreaOf("GameView").selectedTab, "the selected tab");
            Equal(1, l.floating.Count, "the floating containers");
            Check(l.ContainerOf(l.AreaOf("Lighting")) == l.floating[0], "the floating tab's container");
            Check(l.ContainerOf(l.AreaOf("SceneView")) == null, "a docked tab has no container");
            Equal(300f, l.floating[0].rect.width, "the floating container's width");
            // An empty layout and its text.
            Equal("layout 1\n", new DockLayout().Write(), "an empty layout's text");
            Check(DockLayout.Read("layout 1\n").root == null, "an empty layout read");
        }

        static void DockCenter()
        {
            DockLayout l = DockLayout.Read(kDefault);
            DockArea scene = l.AreaOf("SceneView");
            // Reordered within its area.
            l.Dock("GameView", scene, DropZone.Center, 0);
            Equal("GameView SceneView", string.Join(" ", scene.tabs), "reordered");
            Equal(0, scene.selected, "the moved tab is selected");
            // From another area: that area goes, the row keeps its share.
            l.Dock("InspectorWindow", scene, DropZone.Center, 1);
            Equal("GameView InspectorWindow SceneView", string.Join(" ", scene.tabs), "taken in");
            var row = (DockSplit)((DockSplit)l.root).children[0];
            Equal(2, row.children.Count, "the row without the inspector's area");
            Check(Math.Abs(Sum(row) - 1) < 1e-4f, "the row's shares still add to one");
            // At the end (-1).
            l.Dock("ConsoleWindow", scene, DropZone.Center);
            Equal("ConsoleWindow", scene.tabs[scene.tabs.Count - 1], "at the end");
            Equal("area 0 ProjectWindow", l.Write().Split('\n')[5].Trim(), "the project's area keeps its other tab");
        }

        static void DockEdges()
        {
            DockLayout l = DockLayout.Read(kDefault);
            DockArea hierarchy = l.AreaOf("HierarchyWindow");
            // Below the hierarchy: a column split in the row's place of the hierarchy.
            DockArea made = l.Dock("ConsoleWindow", hierarchy, DropZone.Bottom);
            Check(made != null && made.tabs.Count == 1 && made.tabs[0] == "ConsoleWindow", "a new area for the tab");
            DockSplit column = hierarchy.parent;
            Check(column != null && column.column && column.children.Count == 2, "a column of the hierarchy and the console");
            Check(column.children[0] == hierarchy && column.children[1] == made, "the console below");
            Equal(0.22f, ((DockSplit)l.root).children[0] is DockSplit row ? row.shares[0] : -1, "the row's share for them is the hierarchy's");
            // Right of the inspector: the row's own split takes it, halving the inspector's share.
            DockArea inspector = l.AreaOf("InspectorWindow");
            DockArea right = l.Dock("ProjectWindow", inspector, DropZone.Right);
            DockSplit r = inspector.parent;
            Check(r == right.parent && !r.column, "beside it in the row");
            Equal(r.children.IndexOf(inspector) + 1, r.children.IndexOf(right), "after it");
            Check(Math.Abs(r.shares[r.children.IndexOf(inspector)] - 0.13f) < 1e-4f, "the inspector's share halved");
            Check(Math.Abs(Sum(r) - 1) < 1e-4f, "the row's shares still add to one");
            // The project's area emptied: the top column holds the row alone, so the row is the root.
            Check(l.root == r, "a split of one child is replaced by it");
            // An area's only tab onto its own edge stays.
            Check(l.Dock("ProjectWindow", right, DropZone.Left) == right, "its own only tab onto its edge stays");
            // Left of the root area of a floating container.
            DockArea lighting = l.AreaOf("Lighting");
            l.Dock("GameView", lighting, DropZone.Left);
            Check(l.ContainerOf(l.AreaOf("GameView")) == l.floating[0], "docked into the floating container");
            Check(l.floating[0].root is DockSplit fs && !fs.column, "its root is a row now");
        }

        static void RemoveTabs()
        {
            DockLayout l = DockLayout.Read(kDefault);
            Check(!l.RemoveTab("Nothing"), "an unknown tab");
            DockArea scene = l.AreaOf("SceneView");
            Check(l.RemoveTab("GameView"), "a tab out");
            Equal(0, scene.selected, "the selection moves back from the end");
            l.RemoveTab("HierarchyWindow");
            var row = (DockSplit)((DockSplit)l.root).children[0];
            Equal(2, row.children.Count, "the emptied area goes");
            Check(Math.Abs(row.shares[0] - 0.74f) < 1e-4f, "its share to its neighbor");
            l.RemoveTab("Lighting");
            Equal(0, l.floating.Count, "an emptied floating container goes");
            l.maximized = l.AreaOf("InspectorWindow");
            l.RemoveTab("InspectorWindow");
            Check(l.maximized == null, "a maximized area that goes is no longer maximized");
            foreach (string t in new[] { "SceneView", "ProjectWindow", "ConsoleWindow" })
                l.RemoveTab(t);
            Check(l.root == null, "all tabs out");
        }

        static void Floating()
        {
            DockLayout l = DockLayout.Read(kDefault);
            FloatingContainer f = l.Float("InspectorWindow", new Rect(40, 50, 320, 240));
            Equal(2, l.floating.Count, "a container of its own");
            Check(l.ContainerOf(l.AreaOf("InspectorWindow")) == f, "the tab's container");
            Check(l.Write().Contains("float 40 50 320 240\n  area 0 InspectorWindow\n"), "written");
            DockLayout back = DockLayout.Read(l.Write());
            Equal(l.Write(), back.Write(), "read back");
            // Floated again: out of its own container into a new one.
            l.Float("InspectorWindow", new Rect(0, 0, 100, 100));
            Equal(2, l.floating.Count, "the emptied container went");
        }

        static void Splitters()
        {
            DockLayout l = DockLayout.Read(kDefault);
            var top = (DockSplit)l.root;
            DockLayout.MoveSplitter(top, 0, -0.2f);
            Check(Math.Abs(top.shares[0] - 0.5f) < 1e-4f && Math.Abs(top.shares[1] - 0.5f) < 1e-4f, "moved");
            DockLayout.MoveSplitter(top, 0, 0.48f);
            Check(Math.Abs(top.shares[0] - 0.5f) < 1e-4f, "not below the minimum");
            Equal("split column 0.5 0.5", l.Write().Split('\n')[1], "written");
        }

        // -------------------------------------------------------------------------------- the session

        static Element Root => DockManager.viewRoot;

        static Element AreaElement(string tab)
        {
            foreach (Element a in Root.FindAll(null, "dock-area"))
                foreach (Element t in a.FindAll(null, "dock-tab"))
                    if (t.Find<Label>(null, "dock-tab-label")?.text == TitleOf(tab))
                        return a;
            return null;
        }

        static Element TabElement(string tab)
        {
            foreach (Element t in Root.FindAll(null, "dock-tab"))
                if (t.Find<Label>(null, "dock-tab-label")?.text == TitleOf(tab))
                    return t;
            return null;
        }

        static string TitleOf(string tab) => DockManager.WindowOf(tab)?.titleContent.text ?? tab;

        static Vector2 Center(Rect r) => new Vector2(r.x + r.width * 0.5f, r.y + r.height * 0.5f);

        static void StartWindows()
        {
            Check(Root != null, "the view is attached to the shell");
            DockLayout l = DockManager.layout;
            Equal(4, new List<DockArea>(l.Areas()).Count, "the default layout's areas");
            foreach (string t in new[] { "HierarchyWindow", "SceneView", "GameView", "InspectorWindow", "ProjectWindow", "ConsoleWindow" })
            {
                EditorWindow w = DockManager.WindowOf(t);
                Check(w != null && w.GetType().Name == t, $"the window of {t}");
                Check(w != null && w.docked, $"{t} is docked");
            }
            Check(EditorWindow.HasOpenInstances<SceneView>(), "the scene view is open");
            Equal(4, Root.FindAll(null, "dock-area").Count, "the view's areas");
            Equal(3, Root.FindAll(null, "dock-splitter").Count, "the view's splitters");
            Equal(6, Root.FindAll(null, "dock-tab").Count, "the view's tabs");
            Check(TabElement("SceneView") != null, "the scene's tab shows its title");
            Check(TabElement("SceneView")?.ClassListContains("active") == true, "the scene's tab is the selected one");
            Check(AreaElement("SceneView")?.Find(null, "editor-window-root")?.parent?.ClassListContains("dock-content") == true,
                  "the selected window's content is in its area");
            Check(Root.worldBound.width > 100 && Root.worldBound.height > 100, "the view is laid out");
        }

        sealed class Pointer : IPointerEvent
        {
            public int pointerId { get; set; }
            public string pointerType => HelixEngine.UI.PointerType.mouse;
            public bool isPrimary => true;
            public int button { get; set; }
            public int pressedButtons { get; set; }
            public Vector3 position { get; set; }
            public Vector3 localPosition => position;
            public Vector3 deltaPosition => Vector3.zero;
            public float deltaTime => 0;
            public int clickCount { get; set; }
            public float pressure => 0;
            public float tangentialPressure => 0;
            public float altitudeAngle => 0;
            public float azimuthAngle => 0;
            public float twist => 0;
            public Vector2 tilt => Vector2.zero;
            public HelixEngine.UI.PenStatus penStatus => HelixEngine.UI.PenStatus.None;
            public Vector2 radius => Vector2.zero;
            public Vector2 radiusVariance => Vector2.zero;
            public EventModifiers modifiers => EventModifiers.None;
            public bool shiftKey => false;
            public bool ctrlKey => false;
            public bool commandKey => false;
            public bool altKey => false;
            public bool actionKey => false;
        }

        static void Send<T>(Element target, Vector2 p, int button = 0) where T : PointerEventBase<T>, new()
        {
            var data = new Pointer { pointerId = 0, button = button, position = new Vector3(p.x, p.y, 0), pressedButtons = 1, clickCount = 1 };
            using T e = PointerEventBase<T>.GetPooled(data);
            target.SendEvent(e);
        }

        // A drag of an element from a point to another, through a point between them (past the drag's start).
        static void Drag(Element e, Vector2 from, Vector2 to)
        {
            Send<PointerDownEvent>(e, from);
            Send<PointerMoveEvent>(e, (from + to) * 0.5f);
            Send<PointerMoveEvent>(e, to);
            Send<PointerUpEvent>(e, to);
        }

        static void DragToEdge()
        {
            Element tab = TabElement("GameView"), hierarchy = AreaElement("HierarchyWindow");
            Check(tab != null && hierarchy != null, "the game's tab and the hierarchy's area");
            Rect h = hierarchy.worldBound;
            Drag(tab, Center(tab.worldBound), new Vector2(h.x + h.width * 0.5f, h.y + h.height * 0.9f));
        }

        static void AfterDragToEdge()
        {
            DockLayout l = DockManager.layout;
            DockArea game = l.AreaOf("GameView"), hierarchy = l.AreaOf("HierarchyWindow");
            Check(game != null && game.tabs.Count == 1, "the game has an area of its own");
            Check(game?.parent != null && game.parent == hierarchy.parent && game.parent.column, "below the hierarchy");
            Equal("SceneView", l.AreaOf("SceneView").selectedTab, "the scene is its area's tab");
            Equal(5, Root.FindAll(null, "dock-area").Count, "the view drew the new area");
            Check(DockManager.WindowOf("GameView").docked, "the game is docked");
            string saved = System.IO.Path.Combine(EditorApplication.projectPathInternal, "UserSettings", "Layouts", "CurrentLayout.layout");
            Check(System.IO.File.Exists(saved) && System.IO.File.ReadAllText(saved) == l.Write(), "the layout is saved as the project's current one");
        }

        static void ClickTab()
        {
            // The project and the console share an area: a click selects the console's tab.
            Element tab = TabElement("ConsoleWindow");
            Check(tab != null && !tab.ClassListContains("active"), "the console's tab is not selected");
            Vector2 p = Center(tab.worldBound);
            Send<PointerDownEvent>(tab, p);
            Send<PointerUpEvent>(tab, p);
        }

        static void AfterClickTab()
        {
            Equal("ConsoleWindow", DockManager.layout.AreaOf("ConsoleWindow").selectedTab, "the console is selected");
            Check(TabElement("ConsoleWindow")?.ClassListContains("active") == true, "its tab is drawn selected");
            Check(EditorWindow.focusedWindow is ConsoleWindow, "the console has the focus");
            Check(AreaElement("ConsoleWindow")?.Find(null, "dock-content")?.Find(null, "editor-window-root") ==
                  DockManager.WindowOf("ConsoleWindow").rootElement, "the console's content shows");
        }

        static void DragOut()
        {
            // Out of every area: the view's own corner (the floating layer holds no area there).
            Element tab = TabElement("InspectorWindow");
            Rect root = Root.worldBound;
            Rect scene = AreaElement("SceneView").worldBound;
            // The scene's middle is a drop on it; past the root's bottom edge is no area.
            Drag(tab, Center(tab.worldBound), new Vector2(scene.x + scene.width * 0.5f, root.y + root.height + 40));
        }

        static void AfterDragOut()
        {
            DockLayout l = DockManager.layout;
            Equal(1, l.floating.Count, "a floating container");
            Check(l.ContainerOf(l.AreaOf("InspectorWindow")) == l.floating[0], "the inspector floats");
            Check(!DockManager.WindowOf("InspectorWindow").docked, "the inspector's window is not docked");
            Equal(1, Root.FindAll(null, "dock-floating").Count, "the view draws the container");
            Check(Root.Find(null, "dock-floating")?.ClassListContains("hx-shadow") == true, "with a shadow");
        }

        static void DragSplitter()
        {
            DockLayout l = DockManager.layout;
            var top = (DockSplit)l.root;
            float before = top.shares[0];
            Element splitter = null;
            foreach (Element s in Root.FindAll(null, "dock-splitter", "column"))
                if (s.parent?.parent == Root.Find(null, "dock-docked"))
                    splitter = s;
            Check(splitter != null, "the top column's splitter");
            if (splitter == null)
                return;
            Rect owner = splitter.parent.worldBound;
            Vector2 p = Center(splitter.worldBound);
            Send<PointerDownEvent>(splitter, p);
            Send<PointerMoveEvent>(splitter, p - new Vector2(0, owner.height * 0.1f));
            Send<PointerUpEvent>(splitter, p - new Vector2(0, owner.height * 0.1f));
            Check(Math.Abs(top.shares[0] - (before - 0.1f * Sum(top))) < 1e-3f, $"the share moved by a tenth (from {before} to {top.shares[0]})");
        }

        static void WindowRequests()
        {
            // GetWindow of an open window focuses it and selects its tab.
            ProjectWindow project = EditorWindow.GetWindow<ProjectWindow>();
            Check(project == DockManager.WindowOf("ProjectWindow"), "the open window");
            Equal("ProjectWindow", DockManager.layout.AreaOf("ProjectWindow").selectedTab, "its tab selected");
            Check(project.hasFocus, "it has the focus");
            // Closing a window takes its tab out.
            DockManager.WindowOf("ConsoleWindow").Close();
            Check(DockManager.layout.AreaOf("ConsoleWindow") == null, "the closed window's tab is out");
            Check(!EditorWindow.HasOpenInstances<ConsoleWindow>(), "no console open");
            // A window not in the layout floats when shown.
            ConsoleWindow console = EditorWindow.GetWindow<ConsoleWindow>();
            Check(console != null && !console.docked, "a new console floats");
            Check(DockManager.layout.ContainerOf(DockManager.layout.AreaOf("ConsoleWindow")) != null, "in a container");
            // Maximized and back.
            SceneView scene = EditorWindow.GetWindow<SceneView>();
            scene.maximized = true;
            Equal(1, Root.FindAll(null, "dock-area").Count, "only the maximized area drawn");
            scene.maximized = false;
            Check(Root.FindAll(null, "dock-area").Count > 1, "all areas again");
            Check(DockManager.WindowTypes() is IEnumerable<string> types && new List<string>(types).Contains("HierarchyWindow"), "the window types");
        }

        static void Presets()
        {
            var presets = new List<string>(DockManager.Presets());
            foreach (string p in new[] { "Default", "2 by 3", "4 Split", "Tall", "Wide" })
                Check(presets.Contains(p), $"the preset {p}");
            foreach (string p in presets)
            {
                Check(DockManager.LoadPreset(p), $"the preset {p} loads");
                foreach (DockArea a in DockManager.layout.Areas())
                    foreach (string t in a.tabs)
                        Check(DockManager.WindowOf(t) != null, $"the preset {p}'s window {t}");
            }
            Check(DockManager.LoadPreset("Default"), "back to the default");
            Check(DockManager.layout.floating.Count == 0, "the default floats nothing");
            Check(!EditorWindow.HasOpenInstances<ConsoleWindow>() || DockManager.layout.AreaOf("ConsoleWindow") != null,
                  "a window whose tab the preset lacks closed");
            DockManager.Save("Mine");
            Check(new List<string>(DockManager.SavedLayouts()).Contains("Mine"), "a saved layout listed");
            Check(!new List<string>(DockManager.SavedLayouts()).Contains("CurrentLayout"), "the current one is not");
            Check(DockManager.LoadSaved("Mine"), "it loads");
            Check(!DockManager.LoadText("split sideways\n  area x"), "a broken layout is refused");
        }
    }
}
