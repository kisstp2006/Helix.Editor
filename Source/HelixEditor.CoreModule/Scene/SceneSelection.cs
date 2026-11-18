using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using HelixEngine;
using HelixEngine.Internal;
using Object = HelixEngine.Object;

namespace HelixEditor
{
    // The scene view's selection control: the scene GUI's default control (a handle under the pointer wins). A click
    // selects what is under it (nothing: none), again at the same place the object behind; shift or ctrl toggles. A
    // drag past a few pixels selects by rectangle: shift adds, ctrl removes. Not with the View tool (its left drag pans).
    internal static class SceneSelection
    {
        const float kDragStart = 6;

        static readonly int s_Hint = "SceneViewSelection".GetHashCode();
        static Vector2 s_Start, s_End;
        static bool s_Dragging;
        static Object[] s_Before = Array.Empty<Object>();

        // The click cycle: where the last click was and what it went through.
        static Vector2 s_LastClick = new Vector2(float.NaN, float.NaN);
        static readonly List<GameObject> s_Cycle = new();

        internal static bool rectSelectingInternal => s_Dragging;
        internal static Rect rectInternal => FromPoints(s_Start, s_End);

#pragma warning disable CA2255
        [ModuleInitializer]
#pragma warning restore CA2255
        internal static void Init() => ScriptBridge.WhenBindingsLoaded(() => SceneView.SceneGuiHooksInternal += OnSceneGui);

        static Rect FromPoints(Vector2 a, Vector2 b) => new Rect(Math.Min(a.x, b.x), Math.Min(a.y, b.y), Math.Abs(a.x - b.x), Math.Abs(a.y - b.y));

        static void OnSceneGui(SceneView view)
        {
            Event e = Event.current;
            if (e == null || Tools.current == Tool.View)
                return;
            int id = GuiUtility.GetControlID(s_Hint, FocusType.Passive);
            switch (e.GetTypeForControl(id))
            {
            case EventType.Layout:
                HandleUtility.AddDefaultControl(id);
                break;
            case EventType.MouseDown:
                if (HandleUtility.nearestControl == id && e.button == 0 && !e.alt)
                {
                    GuiUtility.hotControl = id;
                    s_Start = s_End = e.mousePosition;
                    s_Dragging = false;
                    s_Before = Selection.objects;
                    e.Use();
                }
                break;
            case EventType.MouseDrag:
                if (GuiUtility.hotControl == id)
                {
                    s_End = e.mousePosition;
                    if (!s_Dragging && (s_End - s_Start).magnitude > kDragStart)
                        s_Dragging = true;
                    if (s_Dragging)
                        SelectRect(rectInternal, e.shift ? 1 : e.control || e.command ? -1 : 0);
                    e.Use();
                }
                break;
            case EventType.MouseUp:
                if (GuiUtility.hotControl == id && e.button == 0)
                {
                    GuiUtility.hotControl = 0;
                    if (s_Dragging)
                    {
                        s_Dragging = false;
                        s_LastClick = new Vector2(float.NaN, float.NaN);
                    }
                    else
                        Click(e.mousePosition, e.shift || e.control || e.command);
                    e.Use();
                }
                break;
            case EventType.Repaint:
                if (s_Dragging)
                    DrawRect(rectInternal);
                break;
            }
        }

        /// <summary>A click at a GUI point: the object under it selected (toggled with additive), the next one behind
        /// when clicked again at the same place.</summary>
        internal static void Click(Vector2 point, bool additive)
        {
            bool again = (point - s_LastClick).magnitude < 3;
            if (!again)
                s_Cycle.Clear();
            GameObject picked = HandleUtility.PickGameObject(point, false, again ? s_Cycle.ToArray() : null);
            if (picked == null && again && s_Cycle.Count > 0)
            {
                // Through all of them: from the front again.
                s_Cycle.Clear();
                picked = HandleUtility.PickGameObject(point, false);
            }
            s_LastClick = point;
            if (picked != null)
                s_Cycle.Add(picked);
            if (additive)
            {
                if (picked == null)
                    return;
                var list = new List<Object>(Selection.objects);
                if (list.Contains(picked))
                    list.Remove(picked);
                else
                {
                    // The added one active (the selection keeps its active object when it stays in the list).
                    list.Add(picked);
                    Selection.activeGameObject = picked;
                }
                Selection.objects = list.ToArray();
                return;
            }
            Selection.activeGameObject = picked;
        }

        // For tests: a rectangle selection begins (with the selection as it is).
        internal static void SelectRectBeginInternal() => s_Before = Selection.objects;

        /// <summary>The selection by a GUI rectangle: replaced (mode 0), added to (1) or taken from (-1) the one the drag began with.</summary>
        internal static void SelectRect(Rect rect, int mode)
        {
            GameObject[] inside = HandleUtility.PickRectObjects(rect);
            var list = new List<Object>(mode == 0 ? Array.Empty<Object>() : s_Before);
            foreach (GameObject go in inside)
            {
                if (mode < 0)
                    list.Remove(go);
                else if (!list.Contains(go))
                    list.Add(go);
            }
            Selection.objects = list.ToArray();
        }

        // The rectangle: a light fill and a frame in the selection color.
        static void DrawRect(Rect r)
        {
            Texture2D white = Texture2D.whiteTexture;
            Color was = Gui.color;
            Gui.color = new Color(0.55f, 0.7f, 1f, 0.15f);
            Gui.DrawTexture(r, white);
            Gui.color = new Color(0.55f, 0.7f, 1f, 0.9f);
            Gui.DrawTexture(new Rect(r.x, r.y, r.width, 1), white);
            Gui.DrawTexture(new Rect(r.x, r.yMax - 1, r.width, 1), white);
            Gui.DrawTexture(new Rect(r.x, r.y, 1, r.height), white);
            Gui.DrawTexture(new Rect(r.xMax - 1, r.y, 1, r.height), white);
            Gui.color = was;
        }
    }
}
