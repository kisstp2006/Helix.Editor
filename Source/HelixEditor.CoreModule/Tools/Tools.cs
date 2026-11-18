using System;
using HelixEngine;

namespace HelixEditor
{
    /// <summary>The scene view's tools; the current one shows lit in the main toolbar.</summary>
    public enum Tool
    {
        View = 0,
        Move = 1,
        Rotate = 2,
        Scale = 3,
        Rect = 4,
        Transform = 5,
        Custom = 6,
        None = -1,
    }

    /// <summary>Where the tools' handle is: the selection's center, or the active object's pivot.</summary>
    public enum PivotMode
    {
        Center = 0,
        Pivot = 1,
    }

    /// <summary>How the tools' handle turns: with the active object, or with the world.</summary>
    public enum PivotRotation
    {
        Local = 0,
        Global = 1,
    }

    /// <summary>What the view's navigation does now (the View tool's left drag when none).</summary>
    public enum ViewTool
    {
        None = -1,
        Orbit = 0,
        Pan = 1,
        Zoom = 2,
        FPS = 3,
    }

    /// <summary>The scene view's tools: the current one (the ToolManager's active tool as a Tool), where their handle
    /// is and how it turns, whether it is hidden.</summary>
    public static class Tools
    {
        const string kPivotMode = "Tools.pivotMode", kPivotRotation = "Tools.pivotRotation";

        static ViewTool s_ViewTool = ViewTool.Pan;
        static Quaternion? s_HandleRotation;
        static bool s_Hidden;

        public static event Action pivotModeChanged, pivotRotationChanged;

        public static Tool current
        {
            get => EditorTools.ToolRegistry.ToolOf(EditorTools.ToolManager.activeToolType);
            set
            {
                if (value == Tool.Custom)
                    return;
                Type t = value == Tool.None ? typeof(EditorTools.NoneTool) : EditorTools.ToolManager.Context().ResolveTool(value) ?? EditorTools.ToolRegistry.BuiltinType(value);
                if (t != null)
                    EditorTools.ToolManager.SetActiveTool(t);
            }
        }

        public static PivotMode pivotMode
        {
            get => (PivotMode)(int)UserState.GetFloat(kPivotMode, (int)PivotMode.Pivot);
            set
            {
                if (value == pivotMode)
                    return;
                UserState.SetFloat(kPivotMode, (int)value);
                pivotModeChanged?.Invoke();
                Toolbars.MainToolbar.Refresh(DefaultToolbar.PivotModeId);
                SceneView.RepaintAll();
            }
        }

        public static PivotRotation pivotRotation
        {
            get => (PivotRotation)(int)UserState.GetFloat(kPivotRotation, (int)PivotRotation.Local);
            set
            {
                if (value == pivotRotation)
                    return;
                UserState.SetFloat(kPivotRotation, (int)value);
                s_HandleRotation = null;
                pivotRotationChanged?.Invoke();
                Toolbars.MainToolbar.Refresh(DefaultToolbar.PivotRotationId);
                SceneView.RepaintAll();
            }
        }

        /// <summary>The tool's handle not drawn (the tool still active).</summary>
        public static bool hidden
        {
            get => s_Hidden;
            set
            {
                s_Hidden = value;
                SceneView.RepaintAll();
            }
        }

        /// <summary>The navigation's drag in the last active scene view; with none, the View tool's left drag.</summary>
        public static ViewTool viewTool
        {
            get
            {
                ViewTool v = SceneView.lastActiveSceneView != null ? SceneView.lastActiveSceneView.viewToolInternal : ViewTool.None;
                return v != ViewTool.None ? v : current == Tool.View ? s_ViewTool : ViewTool.None;
            }
            set => s_ViewTool = value == ViewTool.None ? ViewTool.Pan : value;
        }

        internal static ViewTool viewToolDragInternal => s_ViewTool;

        /// <summary>The handle's place: the active object's pivot, or the center of the selection's bounds.</summary>
        public static Vector3 handlePosition
        {
            get
            {
                Transform active = Selection.activeTransform;
                if (active == null)
                    return Vector3.zero;
                if (pivotMode == PivotMode.Pivot)
                    return active.position;
                Bounds b = default;
                bool any = false;
                foreach (Transform t in Selection.transforms)
                {
                    Bounds own = new Bounds(t.position, Vector3.zero);
                    foreach (Renderer r in t.GetComponentsInChildren<Renderer>())
                        own.Encapsulate(r.bounds);
                    if (!any)
                        b = own;
                    else
                        b.Encapsulate(own);
                    any = true;
                }
                return b.center;
            }
        }

        /// <summary>The handle's rotation: the active object's (Local) or the world's (Global; a tool may set it).</summary>
        public static Quaternion handleRotation
        {
            get
            {
                if (pivotRotation == PivotRotation.Global)
                    return s_HandleRotation ?? Quaternion.identity;
                Transform active = Selection.activeTransform;
                return active != null ? active.rotation : Quaternion.identity;
            }
            set => s_HandleRotation = value;
        }

        internal static void SelectionChangedInternal() => s_HandleRotation = null;
    }
}
