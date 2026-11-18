using HelixEngine;
using HelixEngine.UI;

namespace HelixEditor.Docking
{
    // A floating container in a window of its own (a tool window of the host, a display of its own): a view of the
    // window's display loads UI/Floating.hxml, whose dock root draws the container. The container's rectangle is the
    // window's client area on the desktop: moving or sizing the window changes it, a rectangle set in code moves the
    // window. Closing the window closes its tabs.
    internal sealed class FloatingWindow
    {
        public readonly FloatingContainer container;
        public readonly int display;
        readonly GameObject m_Object;
        readonly UIView m_View;
        DockView m_Dock;
        // The rectangle last given to the window (or taken from it).
        Rect m_Placed;

        public DockView view => m_Dock;

        FloatingWindow(FloatingContainer container, int display, GameObject obj, UIView view)
        {
            this.container = container;
            this.display = display;
            m_Object = obj;
            m_View = view;
            m_Placed = container.rect;
        }

        /// <summary>A window for the container at its rectangle (null where the host has no windows of its own).</summary>
        public static FloatingWindow Open(FloatingContainer container)
        {
            if (!EditorApplication.toolWindowsInternal)
                return null;
            Rect r = container.rect;
            int display = EditorApplication.OpenToolWindowInternal("Helix", r);
            if (display < 0)
                return null;
            var obj = new GameObject($"Floating Window {display}") { hideFlags = HideFlags.DontSave };
            var settings = ScriptableObject.CreateInstance<UIPanelSettings>();
            settings.hideFlags = HideFlags.DontSave;
            settings.targetDisplay = display;
            var view = obj.AddComponent<UIView>();
            view.panelSettings = settings;
            view.source = EditorGuiUtility.Load("UI/Floating.hxml") as UITemplate;
            return new FloatingWindow(container, display, obj, view);
        }

        /// <summary>The container drawn again (its tree or a title changed); the window placed where the container
        /// was set in code.</summary>
        public void Build(DockLayout layout)
        {
            if (m_Dock == null)
            {
                Element root = m_View.rootElement?.Find("dock-root");
                if (root == null)
                    return;
                m_Dock = new DockView(root, display, container);
            }
            if (container.rect != m_Placed && container.rect.width > 0 && container.rect.height > 0)
            {
                m_Placed = container.rect;
                EditorApplication.SetDisplayRectInternal(display, container.rect);
            }
            m_Dock.Build(layout);
        }

        /// <summary>Once a frame: the container follows the window (true when it moved or was sized); a close asked of
        /// the window closes the container's tabs.</summary>
        public bool Tick(DockLayout layout, out bool closeAsked)
        {
            closeAsked = EditorApplication.ToolWindowCloseRequestedInternal(display);
            if (m_Dock == null)
                Build(layout);
            Rect r = EditorApplication.DisplayRectInternal(display);
            if (r.width <= 0 || r.height <= 0 || r == m_Placed)
                return false;
            m_Placed = r;
            container.rect = r;
            return true;
        }

        public void Close()
        {
            EditorApplication.CloseToolWindowInternal(display);
            Object.Destroy(m_Object);
        }
    }
}
