using System;
using System.Collections.Generic;
using HelixEngine;
using HelixEngine.UI;

namespace HelixEditor
{
    // The Scene view's navigation: orbit (Alt and the left button), pan (the middle button; the left with the View
    // tool), zoom (the wheel; Alt and the right button), fly (the right button: look around, W A S D Q E move, Shift
    // faster, longer flights faster still); F frames the selection; LookAt eases with the camera settings.
    public partial class SceneView
    {
        enum Drag
        {
            None,
            Orbit,
            Pan,
            Zoom,
            Fly,
        }

        // How far the pointer turns the view (degrees a pixel) and the wheel zooms (a line's share of the size).
        const float kOrbitSpeed = 0.3f, kWheelZoom = 0.03f, kDragZoom = 0.005f;

        Drag m_Drag;
        int m_DragPointer = -1;
        readonly HashSet<KeyCode> m_FlyKeys = new();
        float m_FlyStart, m_SaveAt = -1;
        bool m_FlyFast;
        OrientationGizmo m_Orientation;

        // An easing in progress: from where to where, since when.
        sealed class Ease
        {
            public Vector3 fromPivot, toPivot;
            public Quaternion fromRotation, toRotation;
            public float fromSize, toSize, start;
            public bool orthographic;
        }

        Ease m_Ease;

        internal bool easingInternal => m_Ease != null;
        internal bool flyingInternal => m_Drag == Drag.Fly;

        // The navigation's drag as a ViewTool.
        internal ViewTool viewToolInternal => m_Drag switch
        {
            Drag.Orbit => ViewTool.Orbit,
            Drag.Pan => ViewTool.Pan,
            Drag.Zoom => ViewTool.Zoom,
            Drag.Fly => ViewTool.FPS,
            _ => ViewTool.None,
        };

        // The View tool's left drag: what Tools.viewTool says (no orbit in a locked 2D view).
        static Drag ViewDrag(bool locked) => Tools.viewToolDragInternal switch
        {
            ViewTool.Orbit when !locked => Drag.Orbit,
            ViewTool.Zoom => Drag.Zoom,
            _ => Drag.Pan,
        };

        void InitNavigation()
        {
            m_Viewport.focusable = true;
            m_Viewport.RegisterCallback<PointerDownEvent>(OnPointerDown);
            m_Viewport.RegisterCallback<PointerMoveEvent>(OnPointerMove);
            m_Viewport.RegisterCallback<PointerUpEvent>(OnPointerUp);
            m_Viewport.RegisterCallback<WheelEvent>(OnWheel);
            m_Viewport.RegisterCallback<KeyDownEvent>(OnKeyDown);
            m_Viewport.RegisterCallback<KeyUpEvent>(e => m_FlyKeys.Remove(e.keyCode));
            m_Viewport.RegisterCallback<FocusOutEvent>(e => m_FlyKeys.Clear());
            // Shown by the Orientation overlay.
            m_Orientation = new OrientationGizmo(this) { name = "scene-orientation" };
        }

        // ---------------------------------------------------------------------------------------- the pointer

        void OnPointerDown(PointerDownEvent e)
        {
            s_LastActive = this;
            m_Viewport.Focus();
            bool locked = m_2DMode || isRotationLocked;
            Drag mode = e.button switch
            {
                2 => Drag.Pan,
                0 when e.altKey && !locked => Drag.Orbit,
                0 when e.altKey => Drag.Pan,
                0 when Tools.current == Tool.View => ViewDrag(locked),
                1 when e.altKey => Drag.Zoom,
                1 => locked ? Drag.Pan : Drag.Fly,
                _ => Drag.None,
            };
            if (mode == Drag.None)
                return;
            m_Ease = null;
            m_Drag = mode;
            m_DragPointer = e.pointerId;
            m_FlyStart = Time.realtimeSinceStartup;
            m_Viewport.CapturePointer(e.pointerId);
            e.StopPropagation();
        }

        void OnPointerMove(PointerMoveEvent e)
        {
            if (m_Drag == Drag.None || e.pointerId != m_DragPointer)
                return;
            Vector2 d = new Vector2(e.deltaPosition.x, e.deltaPosition.y);
            switch (m_Drag)
            {
            case Drag.Orbit: OrbitBy(d); break;
            case Drag.Pan: PanBy(d); break;
            case Drag.Zoom: ZoomBy((float)Math.Exp(-(d.x + d.y) * kDragZoom)); break;
            case Drag.Fly: LookBy(d); break;
            }
            e.StopPropagation();
        }

        void OnPointerUp(PointerUpEvent e)
        {
            if (e.pointerId != m_DragPointer)
                return;
            if (m_Viewport.HasPointerCapture(e.pointerId))
                m_Viewport.ReleasePointer(e.pointerId);
            m_Drag = Drag.None;
            m_DragPointer = -1;
            m_FlyKeys.Clear();
            e.StopPropagation();
        }

        void OnWheel(WheelEvent e)
        {
            m_Ease = null;
            // Down zooms out, up in.
            ZoomBy((float)Math.Pow(1 + kWheelZoom, e.delta.y));
            e.StopPropagation();
        }

        /// <summary>The view turned about its pivot by a drag of pixels (right turns it right, down tilts it down).</summary>
        public void OrbitByInternal(Vector2 pixels) => OrbitBy(pixels);

        void OrbitBy(Vector2 d)
        {
            m_Rotation = Quaternion.AngleAxis(d.x * kOrbitSpeed, Vector3.up) * m_Rotation * Quaternion.AngleAxis(d.y * kOrbitSpeed, Vector3.right);
            Changed();
        }

        // The camera turned where it is (its pivot goes round it).
        void LookBy(Vector2 d)
        {
            float distance = cameraDistance;
            Vector3 eye = m_Pivot - m_Rotation * Vector3.forward * distance;
            m_Rotation = Quaternion.AngleAxis(d.x * kOrbitSpeed, Vector3.up) * m_Rotation * Quaternion.AngleAxis(d.y * kOrbitSpeed, Vector3.right);
            m_Pivot = eye + m_Rotation * Vector3.forward * distance;
            Changed();
        }

        /// <summary>The pivot moved by a drag of pixels: what was under the pointer stays under it.</summary>
        public void PanByInternal(Vector2 pixels) => PanBy(pixels);

        void PanBy(Vector2 d)
        {
            float height = Math.Max(1, m_Viewport.layout.height);
            // The view shows 2 * size from top to bottom at the pivot's distance.
            float perPixel = 2 * m_Size / height;
            m_Pivot += m_Rotation * new Vector3(-d.x * perPixel, d.y * perPixel, 0);
            Changed();
        }

        /// <summary>The size times a factor (below 1 zooms in).</summary>
        public void ZoomByInternal(float factor) => ZoomBy(factor);

        void ZoomBy(float factor)
        {
            m_Size = Math.Clamp(m_Size * factor, 1e-4f, 1e6f);
            Changed();
        }

        // ------------------------------------------------------------------------------------------- keys

        void OnKeyDown(KeyDownEvent e)
        {
            if (e.keyCode == KeyCode.F && m_Drag != Drag.Fly && !e.ctrlKey && !e.altKey)
            {
                FrameSelected();
                e.StopPropagation();
                return;
            }
            if (m_Drag != Drag.Fly)
                return;
            switch (e.keyCode)
            {
            case KeyCode.W:
            case KeyCode.A:
            case KeyCode.S:
            case KeyCode.D:
            case KeyCode.Q:
            case KeyCode.E:
                m_FlyKeys.Add(e.keyCode);
                m_FlyFast = e.shiftKey;
                e.StopPropagation();
                break;
            }
        }

        /// <summary>The fly mode's movement over some seconds with keys held (for tests: W A S D Q E).</summary>
        public void FlyInternal(float seconds, params KeyCode[] keys)
        {
            m_FlyKeys.Clear();
            foreach (KeyCode k in keys)
                m_FlyKeys.Add(k);
            Fly(seconds, 0);
            m_FlyKeys.Clear();
        }

        // Units a second: the camera settings' speed, faster with Shift and the longer the flight lasts.
        void Fly(float dt, float flying)
        {
            Vector3 dir = Vector3.zero;
            if (m_FlyKeys.Contains(KeyCode.W)) dir += Vector3.forward;
            if (m_FlyKeys.Contains(KeyCode.S)) dir += Vector3.back;
            if (m_FlyKeys.Contains(KeyCode.D)) dir += Vector3.right;
            if (m_FlyKeys.Contains(KeyCode.A)) dir += Vector3.left;
            if (m_FlyKeys.Contains(KeyCode.E)) dir += Vector3.up;
            if (m_FlyKeys.Contains(KeyCode.Q)) dir += Vector3.down;
            if (dir == Vector3.zero || dt <= 0)
                return;
            float speed = cameraSettings.speed * 5;
            if (cameraSettings.accelerationEnabled)
                speed *= Math.Min(4, 1 + flying * 0.75f);
            if (m_FlyFast)
                speed *= 4;
            m_Pivot += m_Rotation * dir.normalized * (speed * dt);
            Changed();
        }

        // ----------------------------------------------------------------------------------------- each frame

        float m_LastTick;

        void TickNavigation()
        {
            float now = Time.realtimeSinceStartup;
            float dt = m_LastTick > 0 ? Math.Min(0.1f, now - m_LastTick) : 0;
            m_LastTick = now;
            if (m_Drag == Drag.Fly)
                Fly(dt, now - m_FlyStart);
            if (m_Ease != null)
                StepEase(now);
            if (m_SaveAt > 0 && now >= m_SaveAt)
            {
                m_SaveAt = -1;
                Save();
            }
        }

        void BeginEase(Vector3 pivot, Quaternion rotation, float size, bool orthographic)
        {
            m_Ease = new Ease
            {
                fromPivot = m_Pivot, toPivot = pivot, fromRotation = m_Rotation, toRotation = rotation, fromSize = m_Size, toSize = size,
                orthographic = orthographic, start = Time.realtimeSinceStartup,
            };
            // A view going orthographic is so from the start; one going perspective at the end.
            if (orthographic)
                m_Orthographic = true;
        }

        void StepEase(float now)
        {
            Ease e = m_Ease;
            float t = Math.Clamp((now - e.start) / Math.Max(1e-3f, cameraSettings.easingDuration), 0, 1);
            // Smooth at both ends.
            float k = t * t * (3 - 2 * t);
            m_Pivot = Vector3.Lerp(e.fromPivot, e.toPivot, k);
            m_Rotation = Quaternion.Slerp(e.fromRotation, e.toRotation, k);
            m_Size = e.fromSize + (e.toSize - e.fromSize) * k;
            if (t >= 1)
            {
                m_Orthographic = e.orthographic;
                m_Ease = null;
            }
            Changed();
        }

        /// <summary>The easing finished at once (for tests).</summary>
        public void FinishEaseInternal()
        {
            if (m_Ease != null)
                StepEase(m_Ease.start + cameraSettings.easingDuration + 1);
        }

        // -------------------------------------------------------------------------------- the orientation gizmo

        /// <summary>The axes of the world as the view sees them, in the view's top right corner: a click on an axis
        /// looks along it (from that side), on the middle or the label below toggles perspective and orthographic.</summary>
        internal sealed class OrientationGizmo : Element
        {
            const float kSize = 90, kRadius = 30, kEnd = 7, kLabelHeight = 16;
            static readonly Color kX = new Color(0.86f, 0.27f, 0.27f, 1), kY = new Color(0.55f, 0.82f, 0.27f, 1), kZ = new Color(0.25f, 0.48f, 0.9f, 1);
            static readonly Color kGrey = new Color(0.7f, 0.7f, 0.7f, 1);
            readonly SceneView m_View;
            readonly Label m_Label;

            internal OrientationGizmo(SceneView view)
            {
                m_View = view;
                AddToClassList("scene-orientation");
                style.marginRight = 4;
                style.marginTop = 0;
                style.width = kSize;
                style.height = kSize + kLabelHeight;
                pickingMode = PickingMode.Position;
                generateVisualContent += Paint;
                m_Label = new Label { name = "scene-orientation-label" };
                m_Label.AddToClassList("scene-orientation-label");
                Add(m_Label);
                RegisterCallback<PointerDownEvent>(OnDown);
                m_Label.RegisterCallback<PointerDownEvent>(e =>
                {
                    if (e.button == 0)
                    {
                        m_View.ToggleOrthographicInternal();
                        e.StopPropagation();
                    }
                });
            }

            // The six axes' ends on the gizmo: where they are, how far into the view, their direction and color.
            internal List<(Vector2 at, float depth, Vector3 axis, Color color, string name)> Ends()
            {
                var ends = new List<(Vector2, float, Vector3, Color, string)>();
                Vector2 c = new Vector2(kSize / 2, kSize / 2);
                Quaternion toView = Quaternion.Inverse(m_View.m_Rotation);
                void Add(Vector3 axis, Color color, string name)
                {
                    Vector3 v = toView * axis;
                    ends.Add((c + new Vector2(v.x, -v.y) * kRadius, v.z, axis, color, name));
                }
                Add(Vector3.right, kX, "x");
                Add(Vector3.up, kY, "y");
                Add(Vector3.forward, kZ, "z");
                Add(Vector3.left, kGrey, "");
                Add(Vector3.down, kGrey, "");
                Add(Vector3.back, kGrey, "");
                // The far ones first, so the near ones cover them.
                ends.Sort((a, b) => b.Item2.CompareTo(a.Item2));
                return ends;
            }

            void Paint(MeshGenerationContext mgc)
            {
                m_Label.text = m_View.in2DMode ? "" : (m_View.orthographic ? "< Iso" : "< Persp");
                Painter2D p = mgc.painter2D;
                Vector2 c = new Vector2(kSize / 2, kSize / 2);
                foreach (var (at, depth, axis, color, name) in Ends())
                {
                    if (name.Length > 0)
                    {
                        p.strokeColor = color;
                        p.lineWidth = 3;
                        p.BeginPath();
                        p.MoveTo(c);
                        p.LineTo(at);
                        p.Stroke();
                    }
                    // A cone seen from far is a disc: its end.
                    p.fillColor = color;
                    p.BeginPath();
                    p.Arc(at, name.Length > 0 ? kEnd : kEnd - 2, Angle.Degrees(0), Angle.Degrees(360));
                    p.Fill();
                    if (name.Length > 0)
                        mgc.DrawText(name, at + (at - c).normalized * 12 - new Vector2(3, 7), 11, color);
                }
                // The middle: a box that toggles the projection.
                p.fillColor = new Color(0.85f, 0.85f, 0.85f, 1);
                p.BeginPath();
                p.Arc(c, 6, Angle.Degrees(0), Angle.Degrees(360));
                p.Fill();
            }

            void OnDown(PointerDownEvent e)
            {
                if (e.button != 0)
                    return;
                Vector2 local = new Vector2(e.localPosition.x, e.localPosition.y);
                Vector2 c = new Vector2(kSize / 2, kSize / 2);
                if ((local - c).magnitude <= 8)
                {
                    m_View.ToggleOrthographicInternal();
                    e.StopPropagation();
                    return;
                }
                // The nearest end under the pointer (the near ones win: they are last).
                (Vector2 at, float depth, Vector3 axis, Color color, string name)? hit = null;
                foreach (var end in Ends())
                    if ((local - end.at).magnitude <= kEnd + 2)
                        hit = end;
                if (hit is { } h)
                {
                    m_View.LookAlongInternal(h.axis);
                    e.StopPropagation();
                }
            }
        }

        internal OrientationGizmo orientationInternal => m_Orientation;

        /// <summary>The view looking along an axis from its side (the X axis: from the right, toward -X).</summary>
        public void LookAlongInternal(Vector3 axis)
        {
            Vector3 forward = -axis;
            Vector3 up = Math.Abs(forward.y) > 0.99f ? Vector3.forward * Math.Sign(forward.y) : Vector3.up;
            LookAt(m_Pivot, Quaternion.LookRotation(forward, up), m_Size, m_Orthographic, false);
        }

        public void ToggleOrthographicInternal() => LookAt(m_Pivot, m_Rotation, m_Size, !m_Orthographic, false);
    }
}
