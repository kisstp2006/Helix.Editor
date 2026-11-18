using System.Collections.Generic;
using HelixEditor.Docking;
using HelixEditor.Toolbars;
using HelixEngine;
using HelixEngine.UI;

namespace HelixEditor
{
    // The editor's shell on its view (UI/Shell.hxml): the title bar's title and caption buttons, the menu bar from the
    // menu registry, the main toolbar from the toolbar registry (built again when either changes), the status bar.
    public class EditorShell : GameBehaviour
    {
        static EditorShell s_Shell;
        static string s_Pending = "Ready";

        Element m_Root, m_MenuBar, m_Maximize;
        Label m_Title, m_Status;
        readonly Element[] m_Groups = new Element[3];
        uint m_MenuVersion, m_ToolbarVersion;
        bool m_Maximized;

        /// <summary>A message in the status bar.</summary>
        public static void Status(string message)
        {
            s_Pending = message ?? "";
            if (s_Shell != null && s_Shell.m_Status != null)
                s_Shell.m_Status.text = s_Pending;
        }

        void Start()
        {
            s_Shell = this;
            if (GetComponent<UIView>()?.rootElement == null)
                return;
            EditorMode.Apply("Default");
            // The shell's objects stay apart from the scenes the user edits; those open once the shell is up.
            foreach (GameObject root in HelixEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects())
                DontDestroyOnLoad(root);
            EditorApplication.delayCall += RestoreScenes;
            EditorApplication.playModeStateChanged += s =>
            {
                MainToolbar.Refresh("Editor/Play");
                MainToolbar.Refresh("Editor/Pause");
                Status(EditorApplication.isPlaying ? "Play mode (the game runs with step 8 of the editor)." : "Ready");
            };
            EditorApplication.pauseStateChanged += s => MainToolbar.Refresh("Editor/Pause");
            Bind(GetComponent<UIView>().rootElement);
            DockManager.LoadStartLayout();
            RegisterLayoutItems();
        }

        // The shell's parts in its view's elements: as it starts, and again when the view is built anew (its markup or
        // style sheets changed on disk): the title, the status, the caption buttons, the menu bar, the toolbar, the dock.
        void Bind(Element root)
        {
            m_Root = root;
            m_Title = m_Root.Find<Label>("title");
            m_Status = m_Root.Find<Label>("status-message");
            m_MenuBar = m_Root.Find("menu-bar");
            m_Groups[0] = m_Root.Find("toolbar-left");
            m_Groups[1] = m_Root.Find("toolbar-middle");
            m_Groups[2] = m_Root.Find("toolbar-right");
            UpdateTitle();
            Status(s_Pending);
            m_Root.Find<Button>("caption-minimize").clicked += EditorApplication.MinimizeWindowInternal;
            var maximize = m_Root.Find<Button>("caption-maximize");
            maximize.clicked += EditorApplication.ToggleMaximizeWindowInternal;
            m_Maximize = maximize.Find("icon") ?? FirstChild(maximize);
            m_Maximized = !EditorApplication.IsWindowMaximizedInternal();
            m_Root.Find<Button>("caption-close").clicked += EditorApplication.CloseWindowInternal;
            m_MenuVersion = m_ToolbarVersion = uint.MaxValue;
            Rebuild();
            Element dock = m_Root.Find("dock-root");
            if (dock != null)
                DockManager.Attach(dock);
        }

        static void RestoreScenes()
        {
            SceneManagement.EditorSceneManager.RestoreSetupInternal();
            SceneManagement.EditorSceneManager.sceneOpened += (scene, mode) => UpdateTitle();
            SceneManagement.EditorSceneManager.newSceneCreated += (scene, setup, mode) => UpdateTitle();
            SceneManagement.EditorSceneManager.sceneSaved += scene => UpdateTitle();
            SceneManagement.EditorSceneManager.sceneDirtied += scene => UpdateTitle();
            SceneManagement.EditorSceneManager.sceneClosed += scene => UpdateTitle();
            HelixEngine.SceneManagement.SceneManager.activeSceneChanged += (from, to) => UpdateTitle();
            UpdateTitle();
        }

        // "<scene>* - <project> - Helix Engine": the active scene, its changes marked.
        static void UpdateTitle()
        {
            string project = EditorApplication.projectNameInternal;
            HelixEngine.SceneManagement.Scene scene = SceneManagement.EditorSceneManager.GetActiveScene();
            string sceneName = scene.IsValid() && scene.name != "Editor" ? scene.name + (scene.isDirty ? "*" : "") : null;
            var parts = new List<string>();
            if (!string.IsNullOrEmpty(sceneName))
                parts.Add(sceneName);
            if (!string.IsNullOrEmpty(project))
                parts.Add(project);
            parts.Add("Helix Engine");
            string title = string.Join(" - ", parts);
            if (s_Shell != null && s_Shell.m_Title != null)
                s_Shell.m_Title.text = title;
            EditorApplication.SetWindowTitleInternal(title);
        }

        static Element FirstChild(Element e)
        {
            foreach (Element c in e.Children())
                return c;
            return null;
        }

        void Update()
        {
            if (m_Root == null)
                return;
            // Built anew: another root, or the parts found before gone from it.
            Element root = GetComponent<UIView>()?.rootElement;
            if (root != null && (root != m_Root || m_MenuBar == null || m_MenuBar.panel == null))
                Bind(root);
            Rebuild();
            DockManager.Tick();
            PopupMenuWindow.Tick();
            EditorWindow.UpdateAll();
            bool maximized = EditorApplication.IsWindowMaximizedInternal();
            if (maximized != m_Maximized && m_Maximize != null)
            {
                m_Maximized = maximized;
                m_Maximize.EnableInClassList("maximize", !maximized);
                m_Maximize.EnableInClassList("restore", maximized);
            }
        }

        void Rebuild()
        {
            uint menus = EditorMenus.VersionInternal();
            if (menus != m_MenuVersion)
            {
                m_MenuVersion = menus;
                BuildMenuBar();
            }
            uint toolbar = EditorToolbar.VersionInternal();
            if (toolbar != m_ToolbarVersion)
            {
                m_ToolbarVersion = toolbar;
                BuildToolbar();
            }
        }

        // ------------------------------------------------------------------------------------------ layouts

        static readonly System.Collections.Generic.List<string> s_LayoutItems = new();

        // The layouts in the Window menu (the presets and the project's saved ones), made again when they change.
        static void RegisterLayoutItems()
        {
            foreach (string item in s_LayoutItems)
                Menu.RemoveMenuItem(item);
            s_LayoutItems.Clear();
            int priority = 0;
            foreach (string preset in DockManager.Presets())
            {
                string name = preset;
                string path = "Window/Layouts/" + name;
                Menu.AddMenuItem(path, "", false, priority++, () => DockManager.LoadPreset(name), null);
                s_LayoutItems.Add(path);
            }
            priority += 11;
            foreach (string saved in DockManager.SavedLayouts())
            {
                string name = saved;
                string path = "Window/Layouts/" + name;
                Menu.AddMenuItem(path, "", false, priority++, () => DockManager.LoadSaved(name), null);
                s_LayoutItems.Add(path);
            }
            Menu.AddMenuItem("Window/Layouts/Save Layout", "", false, priority + 11, SaveLayout, null);
            s_LayoutItems.Add("Window/Layouts/Save Layout");
        }

        static void SaveLayout()
        {
            int n = 1;
            foreach (string s in DockManager.SavedLayouts())
                n++;
            DockManager.Save($"Layout {n}");
            RegisterLayoutItems();
            Status($"Saved the layout as Layout {n}.");
        }

        /// <summary>The Layout dropdown of the main toolbar: the presets, the project's layouts, Save Layout.</summary>
        internal static void OpenLayoutMenu(Rect anchor)
        {
            if (s_Shell == null || s_Shell.m_Root == null)
                return;
            var menu = new GenericDropdownMenu();
            foreach (string preset in DockManager.Presets())
            {
                string name = preset;
                menu.AddItem(name, false, () => DockManager.LoadPreset(name));
            }
            bool any = false;
            foreach (string saved in DockManager.SavedLayouts())
            {
                if (!any)
                    menu.AddSeparator("");
                any = true;
                string name = saved;
                menu.AddItem(name, false, () => DockManager.LoadSaved(name));
            }
            menu.AddSeparator("");
            menu.AddItem("Save Layout", false, SaveLayout);
            menu.DropDown(anchor, s_Shell.m_Root, DropdownMenuSizeMode.Auto);
        }

        // ------------------------------------------------------------------------------------------- menus

        void BuildMenuBar()
        {
            if (m_MenuBar == null)
                return;
            m_MenuBar.Clear();
            foreach (string title in EditorMenus.TitlesInternal())
            {
                var label = new Label(title);
                label.AddToClassList("menu-title");
                string path = title;
                label.RegisterCallback<PointerDownEvent>(e => OpenMenu(path, label));
                m_MenuBar.Add(label);
            }
        }

        /// <summary>A menu at an element (in its own panel).</summary>
        internal static void ShowMenu(GenericDropdownMenu menu, Element anchor, Rect at)
        {
            Element root = anchor?.panel?.visualTree;
            if (root != null)
                menu.DropDown(at, root, DropdownMenuSizeMode.Auto);
        }

        internal static void AddEntries(GenericDropdownMenu menu, string path, string prefix)
        {
            foreach (string text in EditorMenus.EntriesInternal(path))
            {
                MenuEntry entry = MenuEntry.Parse(text);
                if (entry.separator)
                {
                    menu.AddSeparator(prefix);
                    continue;
                }
                if (entry.submenu)
                {
                    AddEntries(menu, entry.path, prefix + entry.name + "/");
                    continue;
                }
                string name = prefix + entry.name + (string.IsNullOrEmpty(entry.shortcut) ? "" : "	" + entry.shortcut);
                string itemPath = entry.path;
                if (entry.enabled)
                    menu.AddItem(name, entry.isChecked, () => EditorMenus.Execute(itemPath));
                else
                    menu.AddDisabledItem(name, entry.isChecked);
            }
        }

        void OpenMenu(string title, Label label)
        {
            var menu = new GenericDropdownMenu();
            AddEntries(menu, title, "");
            label.AddToClassList("open");
            Rect r = label.worldBound;
            // As wide as its items, at least its title's width.
            menu.DropDown(new Rect(r.x, r.y, r.width, r.height), m_Root, DropdownMenuSizeMode.Auto);
            label.schedule.Execute(() => label.RemoveFromClassList("open")).StartingIn(150);
        }

        // ----------------------------------------------------------------------------------------- toolbar

        void BuildToolbar()
        {
            for (int g = 0; g < 3; g++)
            {
                Element group = m_Groups[g];
                if (group == null)
                    continue;
                group.Clear();
                Element strip = null;
                string stripName = null;
                foreach (string text in EditorToolbar.ElementsInternal((ToolbarGroup)g))
                {
                    ToolbarEntry data = ToolbarEntry.Parse(text);
                    Element parent = group;
                    if (!string.IsNullOrEmpty(data.strip))
                    {
                        if (data.strip != stripName)
                        {
                            strip = new Element();
                            strip.AddToClassList("toolbar-strip");
                            group.Add(strip);
                            stripName = data.strip;
                        }
                        parent = strip;
                    }
                    else
                        stripName = null;
                    parent.Add(MakeElement(data));
                }
            }
        }

        static string Kebab(string name)
        {
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < name.Length; i++)
            {
                if (char.IsUpper(name[i]) && i > 0)
                    sb.Append('-');
                sb.Append(char.ToLowerInvariant(name[i]));
            }
            return sb.ToString();
        }

        static Element MakeElement(ToolbarEntry data)
        {
            string id = data.id;
            var button = new Button(() => EditorToolbar.Click(id));
            button.tooltip = data.tooltip;
            button.SetEnabled(data.enabled);
            if (data.kind == ToolbarElementKind.Dropdown)
            {
                button.AddToClassList("toolbar-dropdown");
                button.AddToClassList("hx-dropdown");
                button.Add(new Label(data.text));
                var arrow = new Element();
                arrow.AddToClassList("icon");
                arrow.AddToClassList("arrow-down");
                button.Add(arrow);
                button.RegisterCallback<PointerDownEvent>(e => MainToolbar.SetAnchor(id, button.worldBound));
                return button;
            }
            button.AddToClassList("tool-button");
            button.AddToClassList("hx-button");
            if (data.on)
                button.AddToClassList("on");
            if (!string.IsNullOrEmpty(data.icon))
            {
                var icon = new Element();
                icon.AddToClassList("icon");
                icon.AddToClassList("icon-" + Kebab(data.icon));
                button.Add(icon);
            }
            else
                button.text = data.text;
            return button;
        }
    }
}
