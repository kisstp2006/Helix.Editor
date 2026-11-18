using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using HelixEngine;
using HelixEngine.Internal;
using Object = HelixEngine.Object;

namespace HelixEditor.EditorTools
{
    /// <summary>A tool of the scene view: its handles drawn in OnToolGUI while it is active. A global tool, or a tool of a
    /// component type (componentToolTarget: available while such a component is selected, its targets those).</summary>
    public abstract class EditorTool : ScriptableObject
    {
        internal Object[] m_Targets = Array.Empty<Object>();

        public virtual GuiContent toolbarIcon => null;
        public Object target => m_Targets.Length > 0 ? m_Targets[0] : null;
        public IEnumerable<Object> targets => m_Targets;
        public virtual bool gridSnapEnabled => false;

        public virtual void OnToolGUI(EditorWindow window) { }
        public virtual bool IsAvailable() => true;
        public virtual void OnActivated() { }
        public virtual void OnWillBeDeactivated() { }
    }

    /// <summary>A class as a tool: its name in the tools overlay, the component type it is a tool of (null: global), the
    /// context it belongs to (null: the GameObject context).</summary>
    [AttributeUsage(AttributeTargets.Class)]
    public sealed class EditorToolAttribute : Attribute
    {
        public EditorToolAttribute(string displayName, Type componentToolTarget = null, Type editorToolContext = null)
        {
            this.displayName = displayName;
            this.componentToolTarget = componentToolTarget;
            this.editorToolContext = editorToolContext;
        }

        public string displayName { get; }
        public Type componentToolTarget { get; }
        public Type editorToolContext { get; }
    }

    /// <summary>A mode of the scene view's tools: what Move, Rotate, Scale, Rect and Transform are in it (null: none).</summary>
    public abstract class EditorToolContext : ScriptableObject
    {
        public virtual Type ResolveTool(Tool tool) => null;
        public virtual void OnToolGUI(EditorWindow window) { }
        public virtual void OnActivated() { }
        public virtual void OnWillBeDeactivated() { }
    }

    /// <summary>The default context: the built-in tools of Data/Settings/Tools.json on GameObjects' Transforms.</summary>
    public sealed class GameObjectToolContext : EditorToolContext
    {
        public override Type ResolveTool(Tool tool) => ToolRegistry.BuiltinType(tool);
    }

    /// <summary>The active tool and context: set by the toolbar, the shortcut keys and scripts; the previous tool kept to go
    /// back to (a component tool given up when its component is deselected).</summary>
    public static class ToolManager
    {
        static EditorTool s_Active;
        static EditorToolContext s_Context;
        static Type s_Previous, s_PreviousPersistent;
        static readonly Dictionary<Type, EditorTool> s_Instances = new();
        static readonly Dictionary<Type, EditorToolContext> s_Contexts = new();

        public static event Action activeToolChanging, activeToolChanged, activeContextChanging, activeContextChanged;

        public static Type activeToolType => Active()?.GetType();
        public static Type activeContextType => Context().GetType();

        internal static EditorTool activeToolInternal => Active();

        internal static EditorToolContext Context()
        {
            if (s_Context == null)
                s_Context = ContextOf(typeof(GameObjectToolContext));
            return s_Context;
        }

        static EditorToolContext ContextOf(Type t)
        {
            if (!s_Contexts.TryGetValue(t, out EditorToolContext c) || c == null)
            {
                c = (EditorToolContext)ScriptableObject.CreateInstance(t);
                c.hideFlags = HideFlags.HideAndDontSave;
                s_Contexts[t] = c;
            }
            return c;
        }

        static EditorTool Active()
        {
            if (s_Active == null)
            {
                // The Move tool at first (once the registry is read).
                Type move = Context().ResolveTool(Tool.Move);
                if (move != null)
                    SetActiveTool(move);
            }
            return s_Active;
        }

        internal static EditorTool InstanceOf(Type t)
        {
            if (t == null)
                return null;
            if (!s_Instances.TryGetValue(t, out EditorTool tool) || tool == null)
            {
                tool = (EditorTool)ScriptableObject.CreateInstance(t);
                tool.hideFlags = HideFlags.HideAndDontSave;
                s_Instances[t] = tool;
            }
            return tool;
        }

        public static void SetActiveTool<T>() where T : EditorTool => SetActiveTool(typeof(T));

        public static void SetActiveTool(Type type)
        {
            if (type == null || !typeof(EditorTool).IsAssignableFrom(type) || type.IsAbstract)
            {
                Debug.LogError($"ToolManager.SetActiveTool: {type?.Name ?? "null"} is no EditorTool class.");
                return;
            }
            SetActiveTool(InstanceOf(type));
        }

        public static void SetActiveTool(EditorTool tool)
        {
            if (tool == null || tool == s_Active)
                return;
            activeToolChanging?.Invoke();
            if (s_Active != null)
            {
                s_Previous = s_Active.GetType();
                if (ToolRegistry.ComponentTarget(s_Previous) == null)
                    s_PreviousPersistent = s_Previous;
                try
                {
                    s_Active.OnWillBeDeactivated();
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                }
            }
            s_Active = tool;
            tool.m_Targets = ToolRegistry.TargetsOf(tool.GetType());
            try
            {
                tool.OnActivated();
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
            activeToolChanged?.Invoke();
            ToolRegistry.RefreshButtons();
            SceneView.RepaintAll();
        }

        public static void RestorePreviousTool() => SetActiveTool(s_Previous ?? Context().ResolveTool(Tool.Move));
        public static void RestorePreviousPersistentTool() => SetActiveTool(s_PreviousPersistent ?? Context().ResolveTool(Tool.Move));
        public static bool IsActiveTool(EditorTool tool) => tool != null && tool == s_Active;

        public static void SetActiveContext<T>() where T : EditorToolContext => SetActiveContext(typeof(T));

        public static void SetActiveContext(Type context)
        {
            context ??= typeof(GameObjectToolContext);
            if (!typeof(EditorToolContext).IsAssignableFrom(context) || context.IsAbstract)
            {
                Debug.LogError($"ToolManager.SetActiveContext: {context.Name} is no EditorToolContext class.");
                return;
            }
            EditorToolContext next = ContextOf(context);
            if (next == s_Context)
                return;
            activeContextChanging?.Invoke();
            Tool was = Tools.current;
            s_Context?.OnWillBeDeactivated();
            s_Context = next;
            next.OnActivated();
            activeContextChanged?.Invoke();
            // The same tool in the new context (none there: the View tool).
            Type t = next.ResolveTool(was) ?? ToolRegistry.BuiltinType(Tool.View);
            if (t != null)
                SetActiveTool(t);
        }

        // A component tool whose component left the selection gives way to the last tool that was not one.
        internal static void SelectionChangedInternal()
        {
            if (s_Active == null)
                return;
            Type target = ToolRegistry.ComponentTarget(s_Active.GetType());
            s_Active.m_Targets = ToolRegistry.TargetsOf(s_Active.GetType());
            if (target != null && s_Active.m_Targets.Length == 0)
                RestorePreviousPersistentTool();
        }
    }

    // The tools as data: the built-in ones from Data/Settings/Tools.json (their Tool, class, icon, key, tooltip), every
    // [EditorTool] class of the loaded assemblies; the main toolbar's buttons and the shortcut keys made from them.
    internal static class ToolRegistry
    {
        internal sealed class Entry
        {
            public Tool tool = Tool.Custom;
            public Type type;
            public string icon, tooltip, displayName;
            public KeyCode key = KeyCode.None;
            public Type componentTarget, context;
        }

        static readonly List<Entry> s_Builtin = new();
        static readonly Dictionary<Type, Entry> s_ByType = new();
        static bool s_Loaded;

        internal static IReadOnlyList<Entry> builtinInternal
        {
            get
            {
                Load();
                return s_Builtin;
            }
        }

#pragma warning disable CA2255
        [ModuleInitializer]
#pragma warning restore CA2255
        internal static void Init() => ScriptBridge.WhenBindingsLoaded(Start);

        static void Start()
        {
            // The editor's data folder is known once the host set it (the first frame at the latest).
            if (!Load())
                EditorApplication.delayCall += () => Load();
            ScriptBridge.OnAssembly(Scan);
            Selection.selectionChanged += () =>
            {
                Tools.SelectionChangedInternal();
                ToolManager.SelectionChangedInternal();
                SceneGuiEditors.SelectionChangedInternal();
            };
            SceneView.SceneGuiHooksInternal += OnSceneGui;
            EditorShell.shortcutKeyInternal += OnKey;
        }

        // The active context's and tool's handles, then the selected objects' editors' OnSceneGui.
        static void OnSceneGui(SceneView view)
        {
            try
            {
                ToolManager.Context().OnToolGUI(view);
                EditorTool tool = ToolManager.activeToolInternal;
                if (tool != null && tool.IsAvailable())
                    tool.OnToolGUI(view);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
            SceneGuiEditors.OnSceneGuiInternal(view);
        }

        // Q W E R T Y (no modifiers): the built-in tool of the key.
        static void OnKey(HelixEngine.UI.KeyDownEvent e)
        {
            if (e.ctrlKey || e.altKey || e.shiftKey || e.commandKey)
                return;
            Type t = TypeOfKey(e.keyCode);
            if (t == null)
                return;
            ToolManager.SetActiveTool(t);
            e.StopPropagation();
        }

        static bool Load()
        {
            if (s_Loaded)
                return true;
            if (string.IsNullOrEmpty(EditorApplication.editorDataPathInternal))
                return false;
            s_Loaded = true;
            try
            {
                string file = System.IO.Path.Combine(EditorApplication.editorDataPathInternal, "Settings/Tools.json");
                using JsonDocument doc = JsonDocument.Parse(System.IO.File.ReadAllText(file));
                int order = 0;
                foreach (JsonElement t in doc.RootElement.GetProperty("tools").EnumerateArray())
                {
                    Type type = typeof(ToolRegistry).Assembly.GetType(t.GetProperty("class").GetString() ?? "", false);
                    if (type == null || !Enum.TryParse(t.GetProperty("tool").GetString(), out Tool tool))
                        continue;
                    var e = new Entry
                    {
                        tool = tool,
                        type = type,
                        icon = t.GetProperty("icon").GetString(),
                        tooltip = t.GetProperty("tooltip").GetString(),
                        displayName = t.GetProperty("tooltip").GetString(),
                        key = Enum.TryParse(t.GetProperty("key").GetString(), out KeyCode k) ? k : KeyCode.None,
                    };
                    s_Builtin.Add(e);
                    s_ByType[type] = e;
                    int index = order++;
                    Toolbars.MainToolbar.RegisterInternal(ButtonId(e), Toolbars.MainToolbarDockPosition.Left, index, () => Button(e));
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"Settings/Tools.json could not be read: {ex.Message}");
            }
            return true;
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
            {
                if (t.IsAbstract || !typeof(EditorTool).IsAssignableFrom(t) || s_ByType.ContainsKey(t))
                    continue;
                var a = t.GetCustomAttribute<EditorToolAttribute>(false);
                if (a == null)
                    continue;
                s_ByType[t] = new Entry { type = t, displayName = a.displayName, componentTarget = a.componentToolTarget, context = a.editorToolContext };
            }
        }

        static string ButtonId(Entry e) => "Tools/" + e.tool;

        static Toolbars.MainToolbarElement Button(Entry e) =>
            new Toolbars.MainToolbarToggle(new Toolbars.MainToolbarContent("", e.icon, e.key != KeyCode.None ? $"{e.tooltip} ({e.key})" : e.tooltip), ToolManager.activeToolType == e.type,
                                           on => ToolManager.SetActiveTool(e.type)) { strip = "tools" };

        internal static void RefreshButtons()
        {
            foreach (Entry e in s_Builtin)
                Toolbars.MainToolbar.Refresh(ButtonId(e));
        }

        internal static Type BuiltinType(Tool tool)
        {
            Load();
            foreach (Entry e in s_Builtin)
                if (e.tool == tool)
                    return e.type;
            return null;
        }

        internal static Tool ToolOf(Type type)
        {
            if (type == typeof(NoneTool))
                return Tool.None;
            if (type != null && s_ByType.TryGetValue(type, out Entry e))
                return e.tool;
            return type == null ? Tool.None : Tool.Custom;
        }

        internal static string DisplayNameOf(Type type) =>
            type != null && s_ByType.TryGetValue(type, out Entry e) && !string.IsNullOrEmpty(e.displayName) ? e.displayName : type?.Name ?? "";

        internal static Type ComponentTarget(Type type) => type != null && s_ByType.TryGetValue(type, out Entry e) ? e.componentTarget : null;

        /// <summary>The tools of the registry that are available now: the global ones, and those of a selected component type.</summary>
        internal static List<Type> AvailableInternal()
        {
            var list = new List<Type>();
            foreach (var kv in s_ByType)
                if (kv.Value.componentTarget == null || TargetsOf(kv.Key).Length > 0)
                    list.Add(kv.Key);
            return list;
        }

        // A tool's targets: the selected components of its target type, else the selected objects.
        internal static Object[] TargetsOf(Type type)
        {
            Type target = ComponentTarget(type);
            if (target == null)
                return Selection.objects;
            var list = new List<Object>();
            foreach (GameObject go in Selection.gameObjects)
                foreach (Component c in go.GetComponents(target))
                    list.Add(c);
            return list.ToArray();
        }

        // The tool of a key (no modifiers): the built-in tools' keys.
        internal static Type TypeOfKey(KeyCode key)
        {
            Load();
            foreach (Entry e in s_Builtin)
                if (e.key == key && key != KeyCode.None)
                    return ToolManager.Context().ResolveTool(e.tool) ?? e.type;
            return null;
        }
    }
}
