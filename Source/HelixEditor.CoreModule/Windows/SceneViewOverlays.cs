using System;
using HelixEditor.EditorTools;
using HelixEditor.Overlays;
using HelixEditor.Toolbars;
using HelixEngine;
using HelixEngine.UI;

namespace HelixEditor
{
    // The Scene view's overlays. In the top row, as the classic toolbar: View Options (draw mode, RGB, lighting, 2D,
    // effects), Grid and Snap, then Gizmos at the row's end. Orientation in the right column. Tools and Tool Settings
    // made but hidden at first (the main toolbar has their buttons).

    [Overlay(typeof(SceneView), "View Options", "View Options", null, true, DockZone.TopToolbar, DockPosition.Top, Layout.HorizontalToolbar, defaultDockIndex = 0)]
    internal sealed class SceneViewOptionsOverlay : ToolbarOverlay
    {
        public SceneViewOptionsOverlay() : base("SceneView/Draw Mode", "SceneView/Render Mode", "SceneView/Separator", "SceneView/Lighting", "SceneView/2D", "SceneView/Effects") { }
    }

    [Overlay(typeof(SceneView), "Grid and Snap", "Grid and Snap", null, true, DockZone.TopToolbar, DockPosition.Top, Layout.HorizontalToolbar, defaultDockIndex = 1)]
    internal sealed class GridAndSnapOverlay : ToolbarOverlay
    {
        public GridAndSnapOverlay() : base("SceneView/Grid", "SceneView/Snap Settings") { }
    }

    [Overlay(typeof(SceneView), "Gizmos", "Gizmos", null, true, DockZone.TopToolbar, DockPosition.Bottom, Layout.HorizontalToolbar, defaultDockIndex = 0)]
    internal sealed class GizmosOverlay : ToolbarOverlay
    {
        public GizmosOverlay() : base("SceneView/Gizmos") { }
    }

    [Overlay(typeof(SceneView), "Tools", "Tools", null, false, DockZone.LeftToolbar, DockPosition.Top, Layout.VerticalToolbar, defaultDockIndex = 0)]
    internal sealed class ToolsOverlay : ToolbarOverlay
    {
        public ToolsOverlay() : base("Tools/Builtin Tools") { }
    }

    [Overlay(typeof(SceneView), "Tool Settings", "Tool Settings", null, false, DockZone.TopToolbar, DockPosition.Top, Layout.HorizontalToolbar, defaultDockIndex = 2)]
    internal sealed class ToolSettingsOverlay : ToolbarOverlay
    {
        public ToolSettingsOverlay() : base("Tools/Pivot Mode", "Tools/Pivot Rotation") { }
    }

    /// <summary>The axis gizmo (a click on an axis looks along it; on its label: perspective or orthographic).</summary>
    [Overlay(typeof(SceneView), "Orientation", "Orientation", null, true, DockZone.RightColumn, DockPosition.Top, Layout.Panel, defaultDockIndex = 0)]
    internal sealed class OrientationOverlay : Overlay
    {
        internal override bool headerInternal => false;

        public override Element CreatePanelContent() => (containerWindow as SceneView)?.orientationInternal ?? new Element();
    }

    // ----------------------------------------------------------------------------------- the elements

    // A toggle of a Scene view's flag: its value read when it is put in its window, written on a click.
    internal abstract class SceneViewToggle : EditorToolbarToggle, IAccessContainerWindow
    {
        EditorWindow m_Window;

        protected SceneViewToggle(string text, string icon, string tooltip) : base(text, icon)
        {
            this.tooltip = tooltip;
            valueChanged += v =>
            {
                if (m_Window is SceneView view)
                {
                    Set(view, v);
                    view.Repaint();
                }
            };
        }

        public EditorWindow containerWindow
        {
            get => m_Window;
            set
            {
                m_Window = value;
                if (value is SceneView view)
                    SetValueWithoutNotify(Get(view));
            }
        }

        protected abstract bool Get(SceneView view);
        protected abstract void Set(SceneView view, bool value);
    }

    [EditorToolbarElement("SceneView/Lighting", typeof(SceneView))]
    internal sealed class SceneLightingToggle : SceneViewToggle
    {
        public SceneLightingToggle() : base("", "Light", "The scene's lights (off: a light from the view)") { }
        protected override bool Get(SceneView v) => v.sceneLighting;
        protected override void Set(SceneView v, bool on) => v.sceneLighting = on;
    }

    [EditorToolbarElement("SceneView/2D", typeof(SceneView))]
    internal sealed class Scene2DToggle : SceneViewToggle
    {
        public Scene2DToggle() : base("2D", null, "2D mode: the view looks along z, orthographic") { }
        protected override bool Get(SceneView v) => v.in2DMode;
        protected override void Set(SceneView v, bool on) => v.in2DMode = on;
    }

    [EditorToolbarElement("SceneView/Effects", typeof(SceneView))]
    internal sealed class SceneEffectsToggle : SceneViewToggle
    {
        public SceneEffectsToggle() : base("", "Effects", "Skybox, fog, flares and image effects in the view") { }
        protected override bool Get(SceneView v) => v.sceneViewState.fxEnabled;
        protected override void Set(SceneView v, bool on) => v.sceneViewState.fxEnabled = on;
    }

    [EditorToolbarElement("SceneView/Grid", typeof(SceneView))]
    internal sealed class SceneGridToggle : SceneViewToggle
    {
        public SceneGridToggle() : base("", "Grid", "The grid") { }
        protected override bool Get(SceneView v) => v.showGrid;
        protected override void Set(SceneView v, bool on) => v.showGrid = on;
    }

    [EditorToolbarElement("SceneView/Separator", typeof(SceneView))]
    internal sealed class SceneToolbarSeparator : Element
    {
        public SceneToolbarSeparator()
        {
            AddToClassList("window-separator");
            pickingMode = PickingMode.Ignore;
        }
    }

    [EditorToolbarElement("SceneView/Draw Mode", typeof(SceneView))]
    internal sealed class SceneDrawModeDropdown : EditorToolbarDropdown, IAccessContainerWindow
    {
        EditorWindow m_Window;

        public SceneDrawModeDropdown() : base("Textured", null)
        {
            AddToClassList("wide");
            tooltip = "The draw mode";
            dropdownClicked += r =>
            {
                var menu = new GenericDropdownMenu();
                foreach (DrawCameraMode m in Enum.GetValues(typeof(DrawCameraMode)))
                {
                    if (m == DrawCameraMode.Textured)
                        menu.AddItem(m.ToString(), Current() == m, () => Set(m));
                    else
                        menu.AddDisabledItem(SerializedFields.Nicify(m.ToString()), false);
                }
                menu.DropDown(r, this, DropdownMenuSizeMode.Auto);
            };
        }

        DrawCameraMode Current() => m_Window is SceneView v ? v.cameraMode.drawMode : DrawCameraMode.Textured;

        void Set(DrawCameraMode m)
        {
            if (m_Window is SceneView v)
            {
                SceneView.CameraMode mode = v.cameraMode;
                mode.drawMode = m;
                v.cameraMode = mode;
                text = SerializedFields.Nicify(m.ToString());
            }
        }

        public EditorWindow containerWindow
        {
            get => m_Window;
            set
            {
                m_Window = value;
                text = SerializedFields.Nicify(Current().ToString());
            }
        }
    }

    [EditorToolbarElement("SceneView/Render Mode", typeof(SceneView))]
    internal sealed class SceneRenderModeDropdown : EditorToolbarDropdown
    {
        public SceneRenderModeDropdown() : base("RGB", null)
        {
            tooltip = "The channels shown";
            dropdownClicked += r =>
            {
                var menu = new GenericDropdownMenu();
                menu.AddItem("RGB", true, () => { });
                menu.AddDisabledItem("Alpha", false);
                menu.AddDisabledItem("Overdraw", false);
                menu.AddDisabledItem("Mipmaps", false);
                menu.DropDown(r, this, DropdownMenuSizeMode.Auto);
            };
        }
    }

    [EditorToolbarElement("SceneView/Snap Settings", typeof(SceneView))]
    internal sealed class SceneSnapDropdown : EditorToolbarDropdown
    {
        public SceneSnapDropdown() : base("", null)
        {
            AddToClassList("plain");
            tooltip = "Snapping";
            dropdownClicked += r =>
            {
                var menu = new GenericDropdownMenu();
                menu.AddItem("Grid Snapping", EditorSnapSettings.gridSnapEnabled, () => EditorSnapSettings.gridSnapEnabled = !EditorSnapSettings.gridSnapEnabled);
                menu.AddItem("Increment Snapping", EditorSnapSettings.snapEnabled, () => EditorSnapSettings.snapEnabled = !EditorSnapSettings.snapEnabled);
                menu.AddSeparator("");
                Vector3 m = EditorSnapSettings.move;
                menu.AddDisabledItem($"Move {m.x:0.###}", false);
                menu.AddDisabledItem($"Rotate {EditorSnapSettings.rotate:0.###}", false);
                menu.AddDisabledItem($"Scale {EditorSnapSettings.scale:0.###}", false);
                menu.DropDown(r, this, DropdownMenuSizeMode.Auto);
            };
        }
    }

    [EditorToolbarElement("SceneView/Gizmos", typeof(SceneView))]
    internal sealed class SceneGizmosDropdown : EditorToolbarDropdown, IAccessContainerWindow
    {
        public SceneGizmosDropdown() : base("Gizmos", null)
        {
            tooltip = "Gizmos";
            dropdownClicked += r =>
            {
                if (!(containerWindow is SceneView v))
                    return;
                var menu = new GenericDropdownMenu();
                menu.AddItem("Show Gizmos", v.drawGizmos, () =>
                {
                    v.drawGizmos = !v.drawGizmos;
                    EnableInClassList("on", v.drawGizmos);
                    v.Repaint();
                });
                menu.DropDown(r, this, DropdownMenuSizeMode.Auto);
            };
        }

        public EditorWindow containerWindow { get; set; }
    }

    /// <summary>The tool registry's tools as toggles (the built-in ones, then the custom tools available now: a
    /// selected component's), the active one lit; made again when the tool or the selection changes.</summary>
    [EditorToolbarElement("Tools/Builtin Tools")]
    internal sealed class BuiltinToolsElement : Element
    {
        public BuiltinToolsElement()
        {
            AddToClassList("overlay-tools");
            Build();
            RegisterCallback<AttachToPanelEvent>(e =>
            {
                ToolManager.activeToolChanged += Build;
                Selection.selectionChanged += Build;
                Build();
            });
            RegisterCallback<DetachFromPanelEvent>(e =>
            {
                ToolManager.activeToolChanged -= Build;
                Selection.selectionChanged -= Build;
            });
        }

        void Build()
        {
            Clear();
            foreach (ToolRegistry.Entry entry in ToolRegistry.builtinInternal)
                Add(Toggle(entry.type, entry.icon, entry.key != KeyCode.None ? $"{entry.tooltip} ({entry.key})" : entry.tooltip, null));
            foreach (Type t in ToolRegistry.AvailableInternal())
                if (ToolRegistry.ToolOf(t) == Tool.Custom)
                    Add(Toggle(t, null, ToolRegistry.DisplayNameOf(t), ToolRegistry.DisplayNameOf(t)));
        }

        static EditorToolbarToggle Toggle(Type type, string icon, string tooltip, string text)
        {
            EditorTool instance = ToolManager.InstanceOf(type);
            string iconName = icon;
            string label = text;
            if (instance?.toolbarIcon is GuiContent c)
            {
                label = string.IsNullOrEmpty(c.text) ? label : c.text;
                tooltip = string.IsNullOrEmpty(c.tooltip) ? tooltip : c.tooltip;
            }
            var t = new EditorToolbarToggle(iconName == null ? Short(label) : "", iconName) { tooltip = tooltip, name = "tool:" + type.Name };
            t.SetValueWithoutNotify(ToolManager.activeToolType == type);
            t.valueChanged += on => ToolManager.SetActiveTool(type);
            return t;
        }

        // A custom tool without an icon: its name's first letters.
        static string Short(string name)
        {
            if (string.IsNullOrEmpty(name))
                return "?";
            return name.Length <= 2 ? name : name.Substring(0, 2);
        }
    }

    [EditorToolbarElement("Tools/Pivot Mode")]
    internal sealed class PivotModeElement : EditorToolbarButton
    {
        public PivotModeElement()
        {
            clicked += () => Tools.pivotMode = Tools.pivotMode == PivotMode.Pivot ? PivotMode.Center : PivotMode.Pivot;
            RegisterCallback<AttachToPanelEvent>(e =>
            {
                Tools.pivotModeChanged += Show;
                Show();
            });
            RegisterCallback<DetachFromPanelEvent>(e => Tools.pivotModeChanged -= Show);
        }

        void Show()
        {
            bool pivot = Tools.pivotMode == PivotMode.Pivot;
            icon = pivot ? "ToolPivot" : "ToolCenter";
            text = pivot ? "Pivot" : "Center";
        }
    }

    [EditorToolbarElement("Tools/Pivot Rotation")]
    internal sealed class PivotRotationElement : EditorToolbarButton
    {
        public PivotRotationElement()
        {
            clicked += () => Tools.pivotRotation = Tools.pivotRotation == PivotRotation.Local ? PivotRotation.Global : PivotRotation.Local;
            RegisterCallback<AttachToPanelEvent>(e =>
            {
                Tools.pivotRotationChanged += Show;
                Show();
            });
            RegisterCallback<DetachFromPanelEvent>(e => Tools.pivotRotationChanged -= Show);
        }

        void Show()
        {
            bool local = Tools.pivotRotation == PivotRotation.Local;
            icon = local ? "ToolLocal" : "ToolGlobal";
            text = local ? "Local" : "Global";
        }
    }
}
