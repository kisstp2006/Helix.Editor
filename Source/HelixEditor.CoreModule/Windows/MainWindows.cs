using HelixEngine;
using HelixEngine.UI;

namespace HelixEditor
{
    // The six main windows as placeholders: their toolbars drawn as the mockup's, their content coming with the steps of
    // the editor (the hierarchy and inspector with step 4, the scene and game views with step 5, the project window and
    // console with step 6).
    internal static class WindowParts
    {
        internal static Element Toolbar(Element root)
        {
            var bar = new Element();
            bar.AddToClassList("window-toolbar");
            bar.AddToClassList("hx-toolbar");
            root.Add(bar);
            return bar;
        }

        internal static Element Dropdown(Element bar, string text, float width = 0)
        {
            var e = new Button();
            e.AddToClassList("window-dropdown");
            e.AddToClassList("hx-dropdown");
            e.Add(new Label(text));
            var arrow = new Element();
            arrow.AddToClassList("icon");
            arrow.AddToClassList("arrow-down");
            e.Add(arrow);
            if (width > 0)
                e.style.width = width;
            bar.Add(e);
            return e;
        }

        internal static Element ToolButton(Element bar, string icon, string text = null, bool on = false)
        {
            var b = new Button();
            b.AddToClassList("window-tool");
            b.AddToClassList(on ? "hx-button-on" : "hx-button");
            if (on)
                b.AddToClassList("on");
            if (icon != null)
            {
                var i = new Element();
                i.AddToClassList("icon");
                i.AddToClassList("icon-" + icon);
                b.Add(i);
            }
            if (text != null)
                b.Add(new Label(text));
            bar.Add(b);
            return b;
        }

        internal static Element Search(Element bar, string placeholder = "")
        {
            var field = new Element();
            field.AddToClassList("window-search");
            field.AddToClassList("hx-field");
            var icon = new Element();
            icon.AddToClassList("icon");
            icon.AddToClassList("icon-search");
            field.Add(icon);
            var label = new Label(placeholder);
            label.AddToClassList("window-search-text");
            field.Add(label);
            bar.Add(field);
            return field;
        }

        internal static Element Fill(Element bar)
        {
            var f = new Element();
            f.AddToClassList("window-fill");
            bar.Add(f);
            return f;
        }

        internal static Element Content(Element root, string note)
        {
            var c = new Element();
            c.AddToClassList("window-content");
            root.Add(c);
            if (!string.IsNullOrEmpty(note))
            {
                var l = new Label(note);
                l.AddToClassList("window-note");
                c.Add(l);
            }
            return c;
        }
    }

    public sealed class HierarchyWindow : EditorWindow
    {
        void OnEnable()
        {
            titleContent = new GuiContent("Hierarchy");
            Element bar = WindowParts.Toolbar(rootElement);
            WindowParts.Dropdown(bar, "Create");
            WindowParts.Search(bar, "All");
            WindowParts.Content(rootElement, "The scene's objects come with step 4.");
        }

        [MenuItem("Window/General/Hierarchy %4", false, 20)] static void Open() => GetWindow<HierarchyWindow>();
    }

    public sealed class SceneView : EditorWindow
    {
        void OnEnable()
        {
            titleContent = new GuiContent("Scene");
            Element bar = WindowParts.Toolbar(rootElement);
            WindowParts.Dropdown(bar, "Textured", 120);
            WindowParts.Dropdown(bar, "RGB", 70);
            WindowParts.ToolButton(bar, "light", null, true);
            WindowParts.ToolButton(bar, null, "2D");
            WindowParts.ToolButton(bar, "effects");
            WindowParts.ToolButton(bar, "grid", null, true);
            WindowParts.Fill(bar);
            WindowParts.Dropdown(bar, "Gizmos", 90);
            WindowParts.Content(rootElement, "The scene view comes with step 5.").AddToClassList("viewport");
        }

        [MenuItem("Window/General/Scene %1", false, 21)] static void Open() => GetWindow<SceneView>();
    }

    public sealed class GameView : EditorWindow
    {
        void OnEnable()
        {
            titleContent = new GuiContent("Game");
            Element bar = WindowParts.Toolbar(rootElement);
            WindowParts.Dropdown(bar, "Display 1", 90);
            WindowParts.Dropdown(bar, "Free Aspect", 110);
            WindowParts.Dropdown(bar, "Scale 1x", 90);
            WindowParts.Fill(bar);
            WindowParts.Dropdown(bar, "Play Focused", 110);
            WindowParts.ToolButton(bar, null, "Stats");
            WindowParts.Dropdown(bar, "Gizmos", 90);
            WindowParts.Content(rootElement, "The game view comes with step 5.").AddToClassList("viewport");
        }

        [MenuItem("Window/General/Game %2", false, 22)] static void Open() => GetWindow<GameView>();
    }

    public sealed class InspectorWindow : EditorWindow
    {
        void OnEnable()
        {
            titleContent = new GuiContent("Inspector");
            Element c = WindowParts.Content(rootElement, "The selection's components come with step 4.");
            c.AddToClassList("inspector");
        }

        [MenuItem("Window/General/Inspector %3", false, 23)] static void Open() => GetWindow<InspectorWindow>();
    }

    public sealed class ProjectWindow : EditorWindow
    {
        void OnEnable()
        {
            titleContent = new GuiContent("Project");
            Element bar = WindowParts.Toolbar(rootElement);
            WindowParts.Dropdown(bar, "Create");
            WindowParts.Fill(bar);
            WindowParts.Search(bar);
            WindowParts.ToolButton(bar, "lock");
            WindowParts.ToolButton(bar, "menu");
            var body = new Element();
            body.AddToClassList("project-body");
            rootElement.Add(body);
            var tree = new Element();
            tree.AddToClassList("project-tree");
            foreach (string folder in new[] { "Favorites", "Assets" })
            {
                var l = new Label(folder);
                l.AddToClassList("project-folder");
                tree.Add(l);
            }
            body.Add(tree);
            var grid = new Element();
            grid.AddToClassList("project-grid");
            var crumb = new Label("Assets");
            crumb.AddToClassList("project-breadcrumb");
            grid.Add(crumb);
            var note = new Label("The project's assets come with step 6.");
            note.AddToClassList("window-note");
            grid.Add(note);
            body.Add(grid);
        }

        [MenuItem("Window/General/Project %5", false, 24)] static void Open() => GetWindow<ProjectWindow>();
    }

    public sealed class ConsoleWindow : EditorWindow
    {
        void OnEnable()
        {
            titleContent = new GuiContent("Console");
            Element bar = WindowParts.Toolbar(rootElement);
            WindowParts.Dropdown(bar, "Clear", 80);
            WindowParts.ToolButton(bar, null, "Collapse");
            WindowParts.ToolButton(bar, null, "Error Pause");
            WindowParts.Fill(bar);
            WindowParts.Search(bar);
            WindowParts.ToolButton(bar, "info", "0", true);
            WindowParts.ToolButton(bar, "warning", "0", true);
            WindowParts.ToolButton(bar, "error", "0", true);
            WindowParts.Content(rootElement, "The log comes with step 6.");
        }

        [MenuItem("Window/General/Console %#c", false, 25)] static void Open() => GetWindow<ConsoleWindow>();
    }
}
