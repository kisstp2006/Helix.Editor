using HelixEngine;
using HelixEngine.UI;

namespace HelixEditor
{
    // The main windows as placeholders: each loads its UI (toolbar and content) from its markup in the editor's data
    // folder (UI/Windows/<Window>.hxml); their content comes with the later steps (the
    // game view with step 5, the project window and console with step 6).
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
