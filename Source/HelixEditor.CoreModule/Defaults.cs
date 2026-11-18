using HelixEditor.Toolbars;
using HelixEngine;

namespace HelixEditor
{
    // The editor's own menu items. Items of later steps are registered now and greyed out by their validation until
    // their windows and systems come (the asset database, scenes, undo, play mode).
    internal static class DefaultMenus
    {
        internal static void RegisterTitles()
        {
            string[] titles = { "File", "Edit", "Assets", "GameObject", "Component", "Window", "Help" };
            for (int i = 0; i < titles.Length; i++)
                EditorMenus.AddTitle(titles[i], i * 10);
        }

        static bool Later() => false;

        [MenuItem("File/New Scene %n", false, 0)] static void NewScene() { }
        [MenuItem("File/New Scene %n", true)] static bool NewSceneValid() => Later();
        [MenuItem("File/Open Scene %o", false, 1)] static void OpenScene() { }
        [MenuItem("File/Open Scene %o", true)] static bool OpenSceneValid() => Later();
        [MenuItem("File/Save %s", false, 11)] static void Save() { }
        [MenuItem("File/Save %s", true)] static bool SaveValid() => Later();
        [MenuItem("File/Save As... %#s", false, 12)] static void SaveAs() { }
        [MenuItem("File/Save As... %#s", true)] static bool SaveAsValid() => Later();
        [MenuItem("File/Build Profiles %#b", false, 30)] static void BuildProfiles() { }
        [MenuItem("File/Build Profiles %#b", true)] static bool BuildProfilesValid() => Later();
        [MenuItem("File/Exit", false, 50)] static void Exit() => EditorApplication.Exit(0);

        [MenuItem("Edit/Undo %z", false, 0)] static void Undo() { }
        [MenuItem("Edit/Undo %z", true)] static bool UndoValid() => Later();
        [MenuItem("Edit/Redo %y", false, 1)] static void Redo() { }
        [MenuItem("Edit/Redo %y", true)] static bool RedoValid() => Later();
        [MenuItem("Edit/Play %p", false, 20)] static void Play() => EditorApplication.isPlaying = !EditorApplication.isPlaying;
        [MenuItem("Edit/Pause %#p", false, 21)] static void Pause() => EditorApplication.isPaused = !EditorApplication.isPaused;
        [MenuItem("Edit/Step %&p", false, 22)] static void Step() => EditorApplication.Step();
        [MenuItem("Edit/Project Settings...", false, 40)] static void ProjectSettings() { }
        [MenuItem("Edit/Project Settings...", true)] static bool ProjectSettingsValid() => Later();
        [MenuItem("Edit/Preferences...", false, 41)] static void Preferences() { }
        [MenuItem("Edit/Preferences...", true)] static bool PreferencesValid() => Later();

        [MenuItem("Assets/Create/Folder", false, 0)] static void CreateFolder() { }
        [MenuItem("Assets/Create/Folder", true)] static bool CreateFolderValid() => Later();
        [MenuItem("Assets/Create/C# Script", false, 1)] static void CreateScript() { }
        [MenuItem("Assets/Create/C# Script", true)] static bool CreateScriptValid() => Later();
        [MenuItem("Assets/Refresh %r", false, 20)] static void Refresh() { }
        [MenuItem("Assets/Refresh %r", true)] static bool RefreshValid() => Later();

        [MenuItem("GameObject/Create Empty %#n", false, 0)] static void CreateEmpty() { }
        [MenuItem("GameObject/Create Empty %#n", true)] static bool CreateEmptyValid() => Later();
        [MenuItem("GameObject/3D Object/Cube", false, 11)] static void Cube() { }
        [MenuItem("GameObject/3D Object/Cube", true)] static bool CubeValid() => Later();
        [MenuItem("GameObject/Light/Directional Light", false, 12)] static void DirectionalLight() { }
        [MenuItem("GameObject/Light/Directional Light", true)] static bool DirectionalLightValid() => Later();
        [MenuItem("GameObject/Camera", false, 13)] static void Camera() { }
        [MenuItem("GameObject/Camera", true)] static bool CameraValid() => Later();

        [MenuItem("Component/Add... %#a", false, 0)] static void AddComponent() { }
        [MenuItem("Component/Add... %#a", true)] static bool AddComponentValid() => Later();

        [MenuItem("Window/Layouts/Default", false, 0)] static void LayoutDefault() { }
        [MenuItem("Window/Layouts/Default", true)] static bool LayoutDefaultValid() => Later();
        [MenuItem("Window/General/Scene", false, 11)] static void SceneWindow() { }
        [MenuItem("Window/General/Scene", true)] static bool SceneWindowValid() => Later();
        [MenuItem("Window/General/Console %#c", false, 12)] static void ConsoleWindow() { }
        [MenuItem("Window/General/Console %#c", true)] static bool ConsoleWindowValid() => Later();

        [MenuItem("Help/About Helix", false, 0)] static void About() => EditorShell.Status("Helix Engine: an engine of its own with the same API and editor.");
        [MenuItem("Help/Helix Manual", false, 1)] static void Manual() { }
        [MenuItem("Help/Helix Manual", true)] static bool ManualValid() => Later();
    }

    // The main toolbar's own elements: the tools (a strip), play, pause and step, Layers and Layout.
    internal static class DefaultToolbar
    {
        internal static readonly string[] ToolIds = { "Tools/View", "Tools/Move", "Tools/Rotate", "Tools/Scale", "Tools/Rect" };

        static MainToolbarElement ToolButton(Tool tool, string icon, string tooltip)
        {
            return new MainToolbarToggle(new MainToolbarContent("", icon, tooltip), Tools.current == tool, on => Tools.current = tool) { strip = "tools" };
        }

        [MainToolbarElement("Tools/View", MainToolbarDockPosition.Left, 0)] static MainToolbarElement View() => ToolButton(Tool.View, "ToolView", "View Tool (Q)");
        [MainToolbarElement("Tools/Move", MainToolbarDockPosition.Left, 1)] static MainToolbarElement Move() => ToolButton(Tool.Move, "ToolMove", "Move Tool (W)");
        [MainToolbarElement("Tools/Rotate", MainToolbarDockPosition.Left, 2)] static MainToolbarElement Rotate() => ToolButton(Tool.Rotate, "ToolRotate", "Rotate Tool (E)");
        [MainToolbarElement("Tools/Scale", MainToolbarDockPosition.Left, 3)] static MainToolbarElement Scale() => ToolButton(Tool.Scale, "ToolScale", "Scale Tool (R)");
        [MainToolbarElement("Tools/Rect", MainToolbarDockPosition.Left, 4)] static MainToolbarElement Rect() => ToolButton(Tool.Rect, "ToolRect", "Rect Tool (T)");

        [MainToolbarElement("Editor/Play", MainToolbarDockPosition.Middle, 0)]
        static MainToolbarElement Play() =>
            new MainToolbarToggle(new MainToolbarContent("", "Play", "Play"), EditorApplication.isPlaying, on => EditorApplication.isPlaying = on) { strip = "playmode" };

        [MainToolbarElement("Editor/Pause", MainToolbarDockPosition.Middle, 1)]
        static MainToolbarElement Pause() =>
            new MainToolbarToggle(new MainToolbarContent("", "Pause", "Pause"), EditorApplication.isPaused, on => EditorApplication.isPaused = on) { strip = "playmode" };

        [MainToolbarElement("Editor/Step", MainToolbarDockPosition.Middle, 2)]
        static MainToolbarElement Step() => new MainToolbarButton(new MainToolbarContent("", "Step", "Step"), EditorApplication.Step) { strip = "playmode" };

        [MainToolbarElement("Editor/Layers", MainToolbarDockPosition.Right, 0)]
        static MainToolbarElement Layers() => new MainToolbarDropdown(new MainToolbarContent("Layers", null, "Visible and pickable layers"), r => EditorShell.Status("Layers come with the scene view."));

        [MainToolbarElement("Editor/Layout", MainToolbarDockPosition.Right, 1)]
        static MainToolbarElement Layout() => new MainToolbarDropdown(new MainToolbarContent("Layout", null, "The window layout"), r => EditorShell.Status("Layouts come with docking."));
    }
}
