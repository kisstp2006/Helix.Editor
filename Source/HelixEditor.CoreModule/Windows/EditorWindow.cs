using System;
using System.Collections.Generic;
using HelixEngine;
using HelixEngine.UI;

namespace HelixEditor
{
    /// <summary>A window of the editor: docked in a dock area of the main window or floating. Its content is its
    /// rootElement (the retained UI) and, when its class has OnGui, the immediate GUI drawn by an ImguiContainer in it.</summary>
    public abstract class EditorWindow : ScriptableObject
    {
        static readonly List<EditorWindow> s_Windows = new();
        static EditorWindow s_Focused;

        GuiContent m_TitleContent;
        Element m_Root;
        ImguiContainer m_Gui;
        // Unset (zero) until given: a floating window then takes the skin's place for a new window.
        internal Rect m_Position;
        internal bool m_Docked, m_Maximized, m_Shown;

        /// <summary>The window's title on its tab.</summary>
        public GuiContent titleContent
        {
            get => m_TitleContent ??= new GuiContent(GetType().Name);
            set
            {
                m_TitleContent = value;
                // While it is made (its OnEnable) the view is drawn after.
                if (s_Creating == 0)
                    Docking.DockManager.Changed();
            }
        }

        public Element rootElement
        {
            get
            {
                if (m_Root == null)
                {
                    m_Root = new Element();
                    m_Root.AddToClassList("editor-window-root");
                    // A window with OnGui draws it in an ImguiContainer under the rest of its content.
                    var onGui = GetType().GetMethod("OnGui", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public |
                                                    System.Reflection.BindingFlags.NonPublic, null, Type.EmptyTypes, null);
                    if (onGui != null)
                    {
                        var handler = (Action)Delegate.CreateDelegate(typeof(Action), this, onGui);
                        m_Gui = new ImguiContainer(handler) { name = "onGui" };
                        m_Gui.AddToClassList("editor-window-gui");
                        m_Root.Add(m_Gui);
                    }
                }
                return m_Root;
            }
        }

        public Rect position
        {
            get => m_Position;
            set
            {
                m_Position = value;
                Docking.DockManager.MovedFloating(this);
            }
        }

        public Vector2 minSize { get; set; } = new Vector2(100, 100);
        public Vector2 maxSize { get; set; } = new Vector2(4000, 4000);
        public bool docked => m_Docked;
        public bool maximized
        {
            get => m_Maximized;
            set => Docking.DockManager.SetMaximized(this, value);
        }
        public bool hasFocus => s_Focused == this;
        public static EditorWindow focusedWindow => s_Focused;
        public static EditorWindow mouseOverWindow { get; internal set; }

        /// <summary>Shows the window: docked as its type was last, else in a new floating container.</summary>
        public void Show()
        {
            if (!m_Shown)
            {
                m_Shown = true;
                if (!s_Windows.Contains(this))
                    s_Windows.Add(this);
                Docking.DockManager.Show(this);
            }
            Focus();
        }

        public void Close()
        {
            Docking.DockManager.Remove(this);
            m_Shown = false;
            s_Windows.Remove(this);
            if (s_Focused == this)
                s_Focused = null;
            m_Gui?.Dispose();
            // OnDisable and OnDestroy run as it is destroyed (a ScriptableObject's messages).
            DestroyImmediate(this);
        }

        public void Focus()
        {
            if (s_Focused == this)
                return;
            EditorWindow previous = s_Focused;
            s_Focused = this;
            previous?.Invoke("OnLostFocus");
            Docking.DockManager.Select(this);
            Invoke("OnFocus");
        }

        public void Repaint() => rootElement.MarkDirtyRepaint();

        public static T GetWindow<T>() where T : EditorWindow => GetWindow<T>(false, null, true);
        public static T GetWindow<T>(string title) where T : EditorWindow => GetWindow<T>(false, title, true);

        public static T GetWindow<T>(bool utility, string title, bool focus) where T : EditorWindow
        {
            foreach (EditorWindow w in s_Windows)
                if (w is T found)
                {
                    if (focus)
                        found.Focus();
                    return found;
                }
            T made = CreateWindow<T>(title);
            return made;
        }

        public static T CreateWindow<T>(string title = null) where T : EditorWindow
        {
            T w = (T)Make(typeof(T));
            if (title != null)
                w.titleContent = new GuiContent(title);
            w.Show();
            return w;
        }

        public static bool HasOpenInstances<T>() where T : EditorWindow
        {
            foreach (EditorWindow w in s_Windows)
                if (w is T)
                    return true;
            return false;
        }

        // A window of a type by its name (the layouts name them), made and enabled but not shown.
        static int s_Creating;

        // A window of a type (its OnEnable runs as it is made, as a ScriptableObject's), then known to the caller.
        internal static EditorWindow Make(Type type, Action<EditorWindow> made = null)
        {
            s_Creating++;
            EditorWindow w;
            try
            {
                w = (EditorWindow)CreateInstance(type);
            }
            finally
            {
                s_Creating--;
            }
            w.hideFlags = HideFlags.DontSave;
            w.name = type.Name;
            made?.Invoke(w);
            return w;
        }

        internal static EditorWindow FindOpen(Type type)
        {
            foreach (EditorWindow w in s_Windows)
                if (w.GetType() == type)
                    return w;
            return null;
        }

        internal static void Register(EditorWindow w)
        {
            w.m_Shown = true;
            if (!s_Windows.Contains(w))
                s_Windows.Add(w);
        }

        internal static IReadOnlyList<EditorWindow> All => s_Windows;

        internal static void UpdateAll()
        {
            foreach (EditorWindow w in s_Windows.ToArray())
                w.Invoke("Update");
        }

        // A message of the window's class (OnEnable, Update ...) by name, when it has one.
        internal void Invoke(string message)
        {
            var m = GetType().GetMethod(message, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public |
                                                 System.Reflection.BindingFlags.NonPublic, null, Type.EmptyTypes, null);
            try
            {
                m?.Invoke(this, null);
            }
            catch (System.Reflection.TargetInvocationException e)
            {
                Debug.LogException(e.InnerException ?? e);
            }
        }
    }
}
