using HelixEngine;
using HelixEngine.UI;

namespace HelixEditor
{
    // The main windows as placeholders: each loads its UI (toolbar and content) from its markup in the editor's data
    // folder (UI/Windows/<Window>.hxml); their content comes with the later steps (hierarchy and inspector with step 4,
    // the scene and game views with step 5, the project window and console with step 6).
    internal static class WindowMarkup
    {
        internal static void Load(EditorWindow window, string path)
        {
            if (EditorGuiUtility.Load(path) is UITemplate template)
                template.CloneTree(window.rootElement);
            else
                Debug.LogWarning($"{window.GetType().Name}: no markup at {path} in the editor's data folder.");
        }
    }

    public sealed class HierarchyWindow : EditorWindow
    {
        void OnEnable()
        {
            titleContent = new GuiContent("Hierarchy");
            WindowMarkup.Load(this, "UI/Windows/HierarchyWindow.hxml");
        }

        [MenuItem("Window/General/Hierarchy %4", false, 20)] static void Open() => GetWindow<HierarchyWindow>();
    }

    public sealed class SceneView : EditorWindow
    {
        void OnEnable()
        {
            titleContent = new GuiContent("Scene");
            WindowMarkup.Load(this, "UI/Windows/SceneView.hxml");
        }

        [MenuItem("Window/General/Scene %1", false, 21)] static void Open() => GetWindow<SceneView>();
    }

    public sealed class GameView : EditorWindow
    {
        void OnEnable()
        {
            titleContent = new GuiContent("Game");
            WindowMarkup.Load(this, "UI/Windows/GameView.hxml");
        }

        [MenuItem("Window/General/Game %2", false, 22)] static void Open() => GetWindow<GameView>();
    }

    public sealed class InspectorWindow : EditorWindow
    {
        void OnEnable()
        {
            titleContent = new GuiContent("Inspector");
            WindowMarkup.Load(this, "UI/Windows/InspectorWindow.hxml");
        }

        [MenuItem("Window/General/Inspector %3", false, 23)] static void Open() => GetWindow<InspectorWindow>();
    }

    public sealed class ProjectWindow : EditorWindow
    {
        void OnEnable()
        {
            titleContent = new GuiContent("Project");
            WindowMarkup.Load(this, "UI/Windows/ProjectWindow.hxml");
        }

        [MenuItem("Window/General/Project %5", false, 24)] static void Open() => GetWindow<ProjectWindow>();
    }

    public sealed class ConsoleWindow : EditorWindow
    {
        void OnEnable()
        {
            titleContent = new GuiContent("Console");
            WindowMarkup.Load(this, "UI/Windows/ConsoleWindow.hxml");
        }

        [MenuItem("Window/General/Console %#c", false, 25)] static void Open() => GetWindow<ConsoleWindow>();
    }
}
