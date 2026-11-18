using HelixEditor.Toolbars;
using HelixEngine;

namespace HelixEditor
{
    // The editor's own menu items. Items of later steps are registered now and greyed out by their validation until
    // their windows and systems come (the asset database, scenes, undo, play mode).
    internal static class DefaultMenus
    {
        static bool Later() => false;

        [MenuItem("File/New Scene %n", false, 0)]
        static void NewScene()
        {
            if (SceneManagement.EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                SceneManagement.EditorSceneManager.NewScene(SceneManagement.NewSceneSetup.DefaultGameObjects, SceneManagement.NewSceneMode.Single);
        }

        [MenuItem("File/Open Scene %o", false, 1)]
        static void OpenScene()
        {
            string chosen = EditorUtility.OpenFilePanel("Open Scene", "Assets", "helix");
            string path = AssetPathOf(chosen);
            if (path != null && SceneManagement.EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                SceneManagement.EditorSceneManager.OpenScene(path);
            else if (!string.IsNullOrEmpty(chosen) && path == null)
                Debug.LogError($"Open Scene: '{chosen}' is no scene of the project.");
        }

        [MenuItem("File/Save %s", false, 11)]
        static void Save() => SceneManagement.EditorSceneManager.SaveScene(SceneManagement.EditorSceneManager.GetActiveScene());

        [MenuItem("File/Save As... %#s", false, 12)]
        static void SaveAs()
        {
            HelixEngine.SceneManagement.Scene scene = SceneManagement.EditorSceneManager.GetActiveScene();
            string path = EditorUtility.SaveFilePanelInProject("Save Scene", scene.name, "helix", "Save the scene in the project.");
            if (!string.IsNullOrEmpty(path))
                SceneManagement.EditorSceneManager.SaveScene(scene, path);
        }

        // A file of the project as its project path (null for one outside it).
        static string AssetPathOf(string file)
        {
            if (string.IsNullOrEmpty(file))
                return null;
            string root = System.IO.Path.GetFullPath(EditorApplication.projectPathInternal).Replace('\\', '/').TrimEnd('/');
            string full = System.IO.Path.GetFullPath(file).Replace('\\', '/');
            if (!full.StartsWith(root + "/", System.StringComparison.OrdinalIgnoreCase))
                return null;
            string path = full.Substring(root.Length + 1);
            return AssetDatabase.AssetPathExists(path) ? path : null;
        }
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
        [MenuItem("Assets/Refresh %r", false, 20)] static void Refresh() => AssetDatabase.Refresh();

        [MenuItem("GameObject/Create Empty %#n", false, 0)] static void CreateEmpty() => HierarchyCommands.Create("GameObject", null);
        [MenuItem("GameObject/Create Empty Child &#n", false, 1)] static void CreateEmptyChild() => HierarchyCommands.Create("GameObject", Selection.activeTransform);
        [MenuItem("GameObject/Create Empty Child &#n", true)] static bool CreateEmptyChildValid() => Selection.activeTransform != null;
        [MenuItem("GameObject/3D Object/Cube", false, 11)] static void Cube() { }
        [MenuItem("GameObject/3D Object/Cube", true)] static bool CubeValid() => Later();
        [MenuItem("GameObject/Light/Directional Light", false, 12)]
        static void DirectionalLight()
        {
            GameObject go = HierarchyCommands.Create("Directional Light", null, typeof(Light));
            go.GetComponent<Light>().type = LightType.Directional;
            go.transform.rotation = Quaternion.Euler(50, -30, 0);
        }

        [MenuItem("GameObject/Camera", false, 13)] static void Camera() => HierarchyCommands.Create("Camera", null, typeof(Camera));

        [MenuItem("Edit/Duplicate %d", false, 10)] static void Duplicate() => HierarchyCommands.DuplicateSelection();
        [MenuItem("Edit/Duplicate %d", true)] static bool DuplicateValid() => Selection.activeTransform != null;
        [MenuItem("Edit/Delete", false, 11)] static void Delete() => HierarchyCommands.DeleteSelection();
        [MenuItem("Edit/Delete", true)] static bool DeleteValid() => Selection.activeTransform != null;

        [MenuItem("Component/Add... %#a", false, 0)] static void AddComponent() { }
        [MenuItem("Component/Add... %#a", true)] static bool AddComponentValid() => Later();


        [MenuItem("Help/About Helix", false, 0)] static void About() => EditorShell.Status("Helix Engine: an engine of its own with the same API and editor.");
        [MenuItem("Help/Helix Manual", false, 1)] static void Manual() { }
        [MenuItem("Help/Helix Manual", true)] static bool ManualValid() => Later();
    }

    // The main toolbar's own elements: the tools (a strip), play, pause and step, Layers and Layout.
    internal static class DefaultToolbar
    {
        // The tools' buttons come from the tool registry (Data/Settings/Tools.json); after them where the handle is and
        // how it turns.
        internal const string PivotModeId = "Tools/PivotMode", PivotRotationId = "Tools/PivotRotation";

        [MainToolbarElement(PivotModeId, MainToolbarDockPosition.Left, 20)]
        static MainToolbarElement PivotModeButton() =>
            Tools.pivotMode == PivotMode.Pivot
                ? new MainToolbarButton(new MainToolbarContent("Pivot", "ToolPivot", "The handle at the active object's pivot"), () => Tools.pivotMode = PivotMode.Center) { strip = "pivot" }
                : new MainToolbarButton(new MainToolbarContent("Center", "ToolCenter", "The handle at the selection's center"), () => Tools.pivotMode = PivotMode.Pivot) { strip = "pivot" };

        [MainToolbarElement(PivotRotationId, MainToolbarDockPosition.Left, 21)]
        static MainToolbarElement PivotRotationButton() =>
            Tools.pivotRotation == PivotRotation.Local
                ? new MainToolbarButton(new MainToolbarContent("Local", "ToolLocal", "The handle turned with the active object"), () => Tools.pivotRotation = PivotRotation.Global) { strip = "pivot" }
                : new MainToolbarButton(new MainToolbarContent("Global", "ToolGlobal", "The handle along the world's axes"), () => Tools.pivotRotation = PivotRotation.Local) { strip = "pivot" };

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
        static MainToolbarElement Layout() => new MainToolbarDropdown(new MainToolbarContent("Layout", null, "The window layout"), EditorShell.OpenLayoutMenu);
    }
}
