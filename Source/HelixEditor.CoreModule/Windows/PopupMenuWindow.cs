using HelixEngine;
using HelixEngine.UI;

namespace HelixEditor
{
    // A menu in a window of its own (a popup of no frame over the editor's windows): menus of the floating windows,
    // which their small windows would cut. The window grows to the menu's boxes (its submenus too) and goes when the
    // menu closes or another window takes the focus.
    internal sealed class PopupMenuWindow
    {
        static PopupMenuWindow s_Open;

        readonly int m_Display;
        readonly GameObject m_Object;
        readonly UIView m_View;
        readonly GenericDropdownMenu m_Menu;
        readonly Vector2 m_At;
        bool m_Shown, m_Focused, m_Closed;
        int m_Frames;
        Rect m_Size;

        PopupMenuWindow(int display, GameObject obj, UIView view, GenericDropdownMenu menu, Vector2 at)
        {
            m_Display = display;
            m_Object = obj;
            m_View = view;
            m_Menu = menu;
            m_At = at;
        }

        internal static bool isOpen => s_Open != null;
        // For the tests: the open popup's display and root, and closing it.
        internal static int displayInternal => s_Open != null ? s_Open.m_Display : -1;
        internal static Element rootInternal => s_Open?.m_View.rootElement?.panel?.visualTree;
        internal static void CloseInternal() => s_Open?.Close();

        /// <summary>The menu under a rectangle of the desktop (false where the host has no windows of its own).</summary>
        internal static bool Show(GenericDropdownMenu menu, Rect anchor)
        {
            s_Open?.Close();
            if (!EditorApplication.toolWindowsInternal)
                return false;
            // A first size; it follows the menu once it is laid out.
            var at = new Vector2(anchor.x, anchor.y + anchor.height);
            int display = EditorApplication.OpenPopupWindowInternal(new Rect(at.x, at.y, 200, 120));
            if (display < 0)
                return false;
            // The editor's own object: in no scene of the user's, in no hierarchy.
            var obj = new GameObject($"Popup {display}") { hideFlags = HideFlags.HideAndDontSave };
            HelixEngine.Object.DontDestroyOnLoad(obj);
            var settings = ScriptableObject.CreateInstance<UIPanelSettings>();
            settings.hideFlags = HideFlags.DontSave;
            settings.targetDisplay = display;
            var view = obj.AddComponent<UIView>();
            view.panelSettings = settings;
            view.source = EditorGuiUtility.Load("UI/Popup.hxml") as UITemplate;
            s_Open = new PopupMenuWindow(display, obj, view, menu, at);
            menu.onClose += () => s_Open?.Close();
            return true;
        }

        /// <summary>Once a frame: the menu shown once the window's view is there, the window fitted to its boxes,
        /// closed when the focus went elsewhere.</summary>
        internal static void Tick() => s_Open?.Update();

        void Update()
        {
            if (m_Closed)
                return;
            Element root = m_View.rootElement?.Find("popup");
            if (!m_Shown)
            {
                if (root == null)
                    return;
                m_Shown = true;
                m_Menu.DropDown(new Rect(0, 0, 0, 0), root, DropdownMenuSizeMode.Content);
                return;
            }
            // The boxes' union from the window's top left (the menu is on the panel's root).
            Element panelRoot = root.panel?.visualTree ?? root;
            float w = 0, h = 0;
            foreach (Element box in panelRoot.FindAll(null, GenericDropdownMenu.containerOuterHssClassName))
            {
                Rect r = box.worldBound;
                w = Mathf.Max(w, r.x + r.width);
                h = Mathf.Max(h, r.y + r.height);
            }
            if (w < 2 || h < 2)
                return;
            var size = new Rect(m_At.x, m_At.y, Mathf.Ceil(w), Mathf.Ceil(h));
            if (size != m_Size)
            {
                m_Size = size;
                EditorApplication.SetDisplayRectInternal(m_Display, size);
            }
            // Its window took the focus; once it loses it, the menu goes.
            bool focused = EditorApplication.ToolWindowFocusedInternal(m_Display);
            m_Focused |= focused;
            if ((m_Focused && !focused) || (!m_Focused && ++m_Frames > 120))
                Close();
        }

        void Close()
        {
            if (m_Closed)
                return;
            m_Closed = true;
            if (s_Open == this)
                s_Open = null;
            EditorApplication.CloseToolWindowInternal(m_Display);
            Object.Destroy(m_Object);
        }
    }
}
