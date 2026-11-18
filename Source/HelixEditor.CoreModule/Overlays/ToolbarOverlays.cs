using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using HelixEditor.Overlays;
using HelixEngine;
using HelixEngine.Internal;
using HelixEngine.UI;

namespace HelixEditor.Toolbars
{
    /// <summary>An element class as a toolbar element of the overlays by id (for the windows given; none: any).</summary>
    [AttributeUsage(AttributeTargets.Class)]
    public sealed class EditorToolbarElementAttribute : Attribute
    {
        public EditorToolbarElementAttribute(string id, params Type[] targetWindows)
        {
            this.id = id;
            this.targetWindows = targetWindows ?? Array.Empty<Type>();
        }

        public string id { get; }
        public Type[] targetWindows { get; }
    }

    // The toolbar elements as data: every [EditorToolbarElement] element class of the loaded assemblies by id.
    internal static class EditorToolbarElements
    {
        static readonly Dictionary<string, List<(Type type, Type[] windows)>> s_Elements = new();

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
            foreach (Type t in types)
            {
                if (t.IsAbstract || !typeof(Element).IsAssignableFrom(t) || t.GetConstructor(Type.EmptyTypes) == null)
                    continue;
                var a = t.GetCustomAttribute<EditorToolbarElementAttribute>(false);
                if (a == null || string.IsNullOrEmpty(a.id))
                    continue;
                if (!s_Elements.TryGetValue(a.id, out var list))
                    s_Elements[a.id] = list = new List<(Type, Type[])>();
                list.Add((t, a.targetWindows));
            }
        }

        /// <summary>The element of an id for a window (one whose targets name the window's type, else one for any).</summary>
        internal static Element Make(string id, EditorWindow window)
        {
            if (!s_Elements.TryGetValue(id, out var list))
                return null;
            Type pick = null;
            foreach ((Type type, Type[] windows) in list)
            {
                if (windows.Length == 0)
                    pick ??= type;
                else if (window != null && Array.Exists(windows, w => w.IsAssignableFrom(window.GetType())))
                {
                    pick = type;
                    break;
                }
            }
            if (pick == null)
                return null;
            try
            {
                var e = (Element)Activator.CreateInstance(pick);
                e.name = id;
                if (e is IAccessContainerWindow access)
                    access.containerWindow = window;
                return e;
            }
            catch (Exception ex)
            {
                Debug.LogException(ex.InnerException ?? ex);
                return null;
            }
        }
    }

    /// <summary>A toolbar button of the overlays: an icon (a name of the skin's icons) or a text, clicked.</summary>
    public class EditorToolbarButton : Button
    {
        Element m_Icon;
        Label m_Text;

        public event Action clickedInternal;

        public EditorToolbarButton() : this("", null, null) { }
        public EditorToolbarButton(Action clickEvent) : this("", null, clickEvent) { }
        public EditorToolbarButton(string text, Action clickEvent) : this(text, null, clickEvent) { }

        public EditorToolbarButton(string text, string icon, Action clickEvent) : base(clickEvent)
        {
            AddToClassList("window-tool");
            AddToClassList("editor-toolbar-button");
            clicked += () => clickedInternal?.Invoke();
            this.icon = icon;
            this.text = text;
        }

        /// <summary>The skin icon's name (Data/Skin/Icons), or none.</summary>
        public string icon
        {
            get => m_IconName;
            set
            {
                m_IconName = value;
                m_Icon?.RemoveFromHierarchy();
                m_Icon = null;
                if (!string.IsNullOrEmpty(value))
                {
                    m_Icon = new Element { pickingMode = PickingMode.Ignore };
                    m_Icon.AddToClassList("icon");
                    m_Icon.AddToClassList("icon-" + Kebab(value));
                    Insert(0, m_Icon);
                }
            }
        }

        string m_IconName;

        public new string text
        {
            get => m_Text?.text ?? "";
            set
            {
                if (string.IsNullOrEmpty(value))
                {
                    m_Text?.RemoveFromHierarchy();
                    m_Text = null;
                    return;
                }
                if (m_Text == null)
                {
                    m_Text = new Label { pickingMode = PickingMode.Ignore };
                    Add(m_Text);
                }
                m_Text.text = value;
            }
        }

        internal static string Kebab(string name)
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
    }

    /// <summary>A toolbar toggle of the overlays: a button lit while on; a click flips it.</summary>
    public class EditorToolbarToggle : EditorToolbarButton
    {
        bool m_Value;

        public event Action<bool> valueChanged;

        public EditorToolbarToggle() : this("", null) { }

        public EditorToolbarToggle(string text, string icon) : base(text, icon, null)
        {
            AddToClassList("editor-toolbar-toggle");
            clicked += () => value = !value;
        }

        public bool value
        {
            get => m_Value;
            set
            {
                if (m_Value == value)
                    return;
                SetValueWithoutNotify(value);
                valueChanged?.Invoke(value);
            }
        }

        public void SetValueWithoutNotify(bool v)
        {
            m_Value = v;
            EnableInClassList("on", v);
        }
    }

    /// <summary>A toolbar dropdown of the overlays: a text (and an icon) with an arrow; a click opens its menu.</summary>
    public class EditorToolbarDropdown : Button
    {
        readonly Label m_Text;

        public event Action<Rect> dropdownClicked;

        public EditorToolbarDropdown() : this("", null) { }

        public EditorToolbarDropdown(string text, Action<Rect> open) : base()
        {
            AddToClassList("window-dropdown");
            AddToClassList("editor-toolbar-dropdown");
            m_Text = new Label(text) { pickingMode = PickingMode.Ignore };
            Add(m_Text);
            var arrow = new Element { pickingMode = PickingMode.Ignore };
            arrow.AddToClassList("icon");
            arrow.AddToClassList("arrow-down");
            Add(arrow);
            if (open != null)
                dropdownClicked += open;
            clicked += () => dropdownClicked?.Invoke(worldBound);
        }

        public new string text
        {
            get => m_Text.text;
            set => m_Text.text = value;
        }
    }
}

namespace HelixEditor.Overlays
{
    /// <summary>An overlay of toolbar elements by id (EditorToolbarElement classes), a row at the top and bottom, a
    /// column at the sides.</summary>
    public class ToolbarOverlay : Overlay
    {
        readonly string[] m_Ids;

        public ToolbarOverlay(params string[] toolbarElementIds) => m_Ids = toolbarElementIds ?? Array.Empty<string>();

        internal IReadOnlyList<string> idsInternal => m_Ids;
        internal override bool toolbarInternal => true;

        public override Element CreatePanelContent()
        {
            var row = new Element();
            row.AddToClassList("overlay-toolbar-content");
            foreach (string id in m_Ids)
            {
                Element e = Toolbars.EditorToolbarElements.Make(id, containerWindow);
                if (e != null)
                    row.Add(e);
            }
            return row;
        }
    }
}
