using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using HelixEngine;
using HelixEngine.Internal;

namespace HelixEditor.Toolbars
{
    /// <summary>Where an element of the main toolbar goes by default (the user moves it; presets keep it).</summary>
    public enum MainToolbarDockPosition
    {
        Left = 0,
        Middle = 1,
        Right = 2,
    }

    /// <summary>A static method answering a MainToolbarElement: an element of the main toolbar by its path.</summary>
    [AttributeUsage(AttributeTargets.Method)]
    public sealed class MainToolbarElementAttribute : Attribute
    {
        public string path;
        public MainToolbarDockPosition defaultDockPosition;
        public int defaultDockIndex;

        public MainToolbarElementAttribute(string path, MainToolbarDockPosition defaultDockPosition = MainToolbarDockPosition.Left,
                                           int defaultDockIndex = 0)
        {
            this.path = path;
            this.defaultDockPosition = defaultDockPosition;
            this.defaultDockIndex = defaultDockIndex;
        }
    }

    /// <summary>What an element shows: its text, its icon (a name of the skin's icons) and tooltip.</summary>
    public struct MainToolbarContent
    {
        public string text;
        public string icon;
        public string tooltip;

        public MainToolbarContent(string text, string icon = null, string tooltip = null)
        {
            this.text = text;
            this.icon = icon;
            this.tooltip = tooltip;
        }
    }

    public abstract class MainToolbarElement
    {
        public MainToolbarContent content;
        public bool enabled = true;
        /// <summary>Elements of one strip are drawn joined (the tools).</summary>
        public string strip;
        internal abstract ToolbarElementKind kind { get; }
        internal virtual bool on => false;
        internal abstract void Click();
    }

    public sealed class MainToolbarButton : MainToolbarElement
    {
        readonly Action m_Action;

        public MainToolbarButton(MainToolbarContent content, Action action)
        {
            this.content = content;
            m_Action = action;
        }

        internal override ToolbarElementKind kind => ToolbarElementKind.Button;
        internal override void Click() => m_Action?.Invoke();
    }

    public sealed class MainToolbarToggle : MainToolbarElement
    {
        readonly Action<bool> m_ValueChanged;
        readonly bool m_Value;

        public MainToolbarToggle(MainToolbarContent content, bool value, Action<bool> valueChanged)
        {
            this.content = content;
            m_Value = value;
            m_ValueChanged = valueChanged;
        }

        internal override ToolbarElementKind kind => ToolbarElementKind.Toggle;
        internal override bool on => m_Value;
        internal string id;
        internal override void Click() => m_ValueChanged?.Invoke(EditorToolbar.GetOn(id));
    }

    public sealed class MainToolbarDropdown : MainToolbarElement
    {
        readonly Action<Rect> m_Open;

        public MainToolbarDropdown(MainToolbarContent content, Action<Rect> open)
        {
            this.content = content;
            m_Open = open;
        }

        internal override ToolbarElementKind kind => ToolbarElementKind.Dropdown;
        internal Rect anchor;
        internal override void Click() => m_Open?.Invoke(anchor);
    }

    /// <summary>The main toolbar: its elements are made again (Refresh) when what they show changes.</summary>
    public static class MainToolbar
    {
        // Each element's maker and its default place: a method of the attribute, or one a registry gave (the tools').
        static readonly Dictionary<string, (Func<MainToolbarElement> make, MainToolbarDockPosition position, int index)> s_Methods = new();
        static readonly Dictionary<string, MainToolbarElement> s_Elements = new();
        static bool s_Hooked;

#pragma warning disable CA2255
        [ModuleInitializer]
#pragma warning restore CA2255
        internal static void Init() => ScriptBridge.WhenBindingsLoaded(Start);

        static void Start()
        {
            if (s_Hooked)
                return;
            s_Hooked = true;
            EditorToolbar.managedClick += id =>
            {
                if (s_Elements.TryGetValue(id, out MainToolbarElement e))
                    try
                    {
                        e.Click();
                    }
                    catch (Exception ex)
                    {
                        Debug.LogException(ex);
                    }
            };
            ScriptBridge.OnAssembly(Scan);
        }

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
            foreach (Type t in types)
                foreach (MethodInfo m in t.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
                {
                    var a = m.GetCustomAttribute<MainToolbarElementAttribute>(false);
                    if (a == null || !typeof(MainToolbarElement).IsAssignableFrom(m.ReturnType))
                        continue;
                    MethodInfo method = m;
                    s_Methods[a.path] = (() => method.Invoke(null, null) as MainToolbarElement, a.defaultDockPosition, a.defaultDockIndex);
                    Make(a.path, true);
                }
        }

        static void Make(string id, bool place)
        {
            if (!s_Methods.TryGetValue(id, out var entry))
                return;
            MainToolbarElement e;
            try
            {
                e = entry.make();
            }
            catch (TargetInvocationException ex)
            {
                Debug.LogException(ex.InnerException ?? ex);
                return;
            }
            if (e == null)
                return;
            if (e is MainToolbarToggle toggle)
                toggle.id = id;
            s_Elements[id] = e;
            var group = (ToolbarGroup)(int)entry.position;
            int order = entry.index;
            if (!place && EditorToolbar.Exists(id))
            {
                // A refresh keeps the element's place.
                foreach (ToolbarGroup g in new[] { ToolbarGroup.Left, ToolbarGroup.Middle, ToolbarGroup.Right })
                    foreach (string text in EditorToolbar.ElementsInternal(g))
                    {
                        ToolbarEntry d = ToolbarEntry.Parse(text);
                        if (d.id == id)
                        {
                            group = d.group;
                            order = d.order;
                        }
                    }
            }
            EditorToolbar.AddManagedInternal(id, group, order, e.kind, e.strip ?? "", e.content.icon ?? "", e.content.text ?? "", e.content.tooltip ?? "",
                                             e.on, e.enabled);
        }

        // An element a registry makes (not a method with the attribute): placed now, made again by Refresh.
        internal static void RegisterInternal(string path, MainToolbarDockPosition position, int index, Func<MainToolbarElement> make)
        {
            s_Methods[path] = (make, position, index);
            Make(path, true);
        }

        // The element gone from the toolbar.
        internal static void RemoveInternal(string path)
        {
            if (s_Methods.Remove(path))
                s_Elements.Remove(path);
            EditorToolbar.Remove(path);
        }

        /// <summary>The element of the path made again from its method (what it shows changed).</summary>
        public static void Refresh(string path) => Make(path, false);

        internal static void SetAnchor(string id, Rect rect)
        {
            if (s_Elements.TryGetValue(id, out MainToolbarElement e) && e is MainToolbarDropdown d)
                d.anchor = rect;
        }
    }
}
