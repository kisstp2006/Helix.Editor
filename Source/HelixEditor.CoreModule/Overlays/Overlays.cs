using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using HelixEngine;
using HelixEngine.Internal;
using HelixEngine.UI;

namespace HelixEditor.Overlays
{
    /// <summary>Where an overlay docks: a toolbar row or strip at an edge of its window, a column of panels at a side,
    /// or floating over the view.</summary>
    public enum DockZone
    {
        LeftToolbar = 0,
        RightToolbar = 1,
        TopToolbar = 2,
        BottomToolbar = 3,
        LeftColumn = 4,
        RightColumn = 5,
        Floating = 6,
    }

    /// <summary>The end of a zone an overlay docks at: Top (a row's start), Bottom (a row's end).</summary>
    public enum DockPosition
    {
        Top = 0,
        Bottom = 1,
    }

    /// <summary>The shapes an overlay takes: a row of a toolbar, a column of one, a panel.</summary>
    [Flags]
    public enum Layout
    {
        HorizontalToolbar = 1 << 0,
        VerticalToolbar = 1 << 1,
        Panel = 1 << 2,
    }

    /// <summary>A class as an overlay of a window type: its id, name, whether it shows at first and where.</summary>
    [AttributeUsage(AttributeTargets.Class)]
    public sealed class OverlayAttribute : Attribute
    {
        public OverlayAttribute() { }

        public OverlayAttribute(Type editorWindowType, string id, string displayName, bool defaultDisplay = false)
        {
            this.editorWindowType = editorWindowType;
            this.id = id;
            this.displayName = displayName;
            this.defaultDisplay = defaultDisplay;
        }

        public OverlayAttribute(Type editorWindowType, string id, string displayName, string ussName, bool defaultDisplay = false,
                                DockZone defaultDockZone = DockZone.RightColumn, DockPosition defaultDockPosition = DockPosition.Bottom,
                                Layout defaultLayout = Layout.Panel)
            : this(editorWindowType, id, displayName, defaultDisplay)
        {
            this.ussName = ussName;
            this.defaultDockZone = defaultDockZone;
            this.defaultDockPosition = defaultDockPosition;
            this.defaultLayout = defaultLayout;
        }

        public OverlayAttribute(Type editorWindowType, string displayName, bool defaultDisplay = false) : this(editorWindowType, displayName, displayName, defaultDisplay) { }

        public Type editorWindowType { get; set; }
        public string id { get; set; }
        public string displayName { get; set; }
        public string ussName { get; set; }
        public bool defaultDisplay { get; set; }
        public DockZone defaultDockZone { get; set; } = DockZone.RightColumn;
        public DockPosition defaultDockPosition { get; set; } = DockPosition.Bottom;
        public int defaultDockIndex { get; set; } = int.MaxValue;
        public Layout defaultLayout { get; set; } = Layout.Panel;
        public float defaultWidth { get; set; } = float.NegativeInfinity;
        public float defaultHeight { get; set; } = float.NegativeInfinity;
    }

    /// <summary>A panel or toolbar of a window's overlay canvas: its content made by CreatePanelContent, docked in a
    /// zone or floating, shown or not, collapsed to its header or not.</summary>
    public abstract class Overlay
    {
        internal OverlayCanvas m_Canvas;
        internal OverlayAttribute m_Attribute;
        internal DockZone m_Zone;
        internal DockPosition m_Position;
        internal int m_Order;
        internal bool m_Displayed, m_Collapsed;
        internal Vector2 m_FloatingPosition;
        internal Layout m_Layout = Layout.Panel;
        internal string m_Id = "";
        string m_DisplayName = "";

        public event Action<bool> displayedChanged, collapsedChanged;
        public event Action<Layout> layoutChanged;

        public string id => m_Id;

        public string displayName
        {
            get => m_DisplayName;
            set
            {
                m_DisplayName = value ?? "";
                m_Canvas?.RebuildInternal();
            }
        }

        public bool displayed
        {
            get => m_Displayed;
            set
            {
                if (m_Displayed == value)
                    return;
                m_Displayed = value;
                m_Canvas?.RebuildInternal();
                displayedChanged?.Invoke(value);
            }
        }

        public bool collapsed
        {
            get => m_Collapsed;
            set
            {
                if (m_Collapsed == value)
                    return;
                m_Collapsed = value;
                m_Canvas?.RebuildInternal();
                collapsedChanged?.Invoke(value);
            }
        }

        public bool floating => m_Zone == DockZone.Floating;

        public Vector2 floatingPosition
        {
            get => m_FloatingPosition;
            set
            {
                m_FloatingPosition = value;
                m_Canvas?.RebuildInternal();
            }
        }

        public EditorWindow containerWindow => m_Canvas?.window;

        /// <summary>The shape it has now (a toolbar's follows its zone: a row at the top and bottom, a column at the sides).</summary>
        public Layout layout
        {
            get => m_Layout;
            set
            {
                if (m_Layout == value)
                    return;
                m_Layout = value;
                m_Canvas?.RebuildInternal();
                layoutChanged?.Invoke(value);
            }
        }

        /// <summary>The overlay's content (made again when the overlay is placed anew).</summary>
        public abstract Element CreatePanelContent();

        public virtual void OnCreated() { }
        public virtual void OnWillBeDestroyed() { }

        /// <summary>Out of its zone, floating where it is.</summary>
        public void Undock() => m_Canvas?.MoveInternal(this, DockZone.Floating, DockPosition.Top, int.MaxValue, m_Canvas.PlaceOfInternal(this));

        /// <summary>Hidden (the overlay menu shows it again).</summary>
        public void Close() => displayed = false;

        // A toolbar overlay takes its zone's direction; a panel stays one.
        internal virtual bool toolbarInternal => false;

        // Its header shows (a panel's title and collapse button); the orientation gizmo has none.
        internal virtual bool headerInternal => true;

        internal void SetLayoutInternal(Layout l)
        {
            if (m_Layout == l)
                return;
            m_Layout = l;
            layoutChanged?.Invoke(l);
        }
    }

    /// <summary>An element that knows the window it is in (set as its toolbar overlay makes it).</summary>
    public interface IAccessContainerWindow
    {
        EditorWindow containerWindow { get; set; }
    }

    // The overlays as data: every [Overlay] class of the loaded assemblies by the window type it belongs to.
    internal static class OverlayRegistry
    {
        static readonly List<(Type type, OverlayAttribute attribute)> s_Overlays = new();
        internal static event Action changedInternal;

#pragma warning disable CA2255
        [ModuleInitializer]
#pragma warning restore CA2255
        internal static void Init() => ScriptBridge.WhenBindingsLoaded(() => ScriptBridge.OnAssembly(Scan));

        static void Scan(Assembly assembly)
        {
            Type[] types;
            try
            {
                types = assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException e)
            {
                types = Array.FindAll(e.Types, t => t != null);
            }
            bool any = false;
            foreach (Type t in types)
            {
                if (t.IsAbstract || !typeof(Overlay).IsAssignableFrom(t) || t.GetConstructor(Type.EmptyTypes) == null)
                    continue;
                var a = t.GetCustomAttribute<OverlayAttribute>(false);
                if (a == null || a.editorWindowType == null)
                    continue;
                a.id ??= a.displayName ?? t.Name;
                a.displayName ??= a.id;
                s_Overlays.Add((t, a));
                any = true;
            }
            if (any)
                changedInternal?.Invoke();
        }

        /// <summary>The overlays of a window type (and of its base types).</summary>
        internal static List<(Type type, OverlayAttribute attribute)> Of(Type window)
        {
            var list = new List<(Type, OverlayAttribute)>();
            foreach (var o in s_Overlays)
                if (o.attribute.editorWindowType.IsAssignableFrom(window))
                    list.Add(o);
            return list;
        }
    }
}
