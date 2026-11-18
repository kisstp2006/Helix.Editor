using System;
using HelixEngine;
using HelixEditor.EditorTools;
using HelixEditor.SceneManagement;

namespace HelixEditor
{
    // A tool of the selection's Transforms (the top-level selected ones): their state when the drag began, each change
    // of the handle applied to that state (a drag never drifts), the scenes marked changed.
    internal abstract class TransformToolBase : EditorTool
    {
        Transform[] m_Transforms = Array.Empty<Transform>();
        Vector3[] m_Positions = Array.Empty<Vector3>(), m_Scales = Array.Empty<Vector3>();
        Quaternion[] m_Rotations = Array.Empty<Quaternion>();

        protected Vector3 startPosition;
        protected Quaternion startRotation, dragRotation;
        protected Vector3 dragScale = Vector3.one;

        public override void OnToolGUI(EditorWindow window)
        {
            if (Tools.hidden || Selection.transforms.Length == 0)
                return;
            // Between drags: the state anew.
            if (GuiUtility.hotControl == 0)
                Capture();
            Handles.matrix = Matrix4x4.identity;
            DoHandles();
        }

        protected abstract void DoHandles();

        void Capture()
        {
            m_Transforms = Selection.transforms;
            int n = m_Transforms.Length;
            m_Positions = new Vector3[n];
            m_Rotations = new Quaternion[n];
            m_Scales = new Vector3[n];
            for (int i = 0; i < n; i++)
            {
                m_Positions[i] = m_Transforms[i].position;
                m_Rotations[i] = m_Transforms[i].rotation;
                m_Scales[i] = m_Transforms[i].localScale;
            }
            startPosition = Tools.handlePosition;
            startRotation = dragRotation = Tools.handleRotation;
            dragScale = Vector3.one;
        }

        /// <summary>The drag's change: the pivot from startPosition to pivot, a turn, a scale along the frame's axes;
        /// the objects keep their places about the pivot (Center), or each its own (Pivot).</summary>
        protected void Apply(Vector3 pivot, Quaternion turn, Vector3 scale, Quaternion frame, bool aboutPivot)
        {
            Quaternion inverse = Quaternion.Inverse(frame);
            Vector3 fx = frame * Vector3.right, fy = frame * Vector3.up, fz = frame * Vector3.forward;
            for (int i = 0; i < m_Transforms.Length; i++)
            {
                Transform t = m_Transforms[i];
                if (t == null)
                    continue;
                Vector3 offset = m_Positions[i] - startPosition;
                if (aboutPivot)
                    offset = turn * (frame * Vector3.Scale(inverse * offset, scale));
                t.position = pivot + offset;
                t.rotation = turn * m_Rotations[i];
                if (scale != Vector3.one)
                {
                    // The frame's scale on the object's own axes (exact when they line up).
                    Quaternion r = m_Rotations[i];
                    Vector3 ax = r * Vector3.right, ay = r * Vector3.up, az = r * Vector3.forward;
                    t.localScale = Vector3.Scale(m_Scales[i], new Vector3(Along(ax, fx, fy, fz, scale), Along(ay, fx, fy, fz, scale), Along(az, fx, fy, fz, scale)));
                }
                EditorSceneManager.MarkSceneDirty(t.gameObject.scene);
            }
        }

        static float Along(Vector3 axis, Vector3 fx, Vector3 fy, Vector3 fz, Vector3 scale)
        {
            float x = Vector3.Dot(axis, fx), y = Vector3.Dot(axis, fy), z = Vector3.Dot(axis, fz);
            return x * x * scale.x + y * y * scale.y + z * z * scale.z;
        }

        // The frame of the scale: the active object's own rotation (a scale is along its axes).
        protected static Quaternion ScaleFrame => Selection.activeTransform != null ? Selection.activeTransform.rotation : Quaternion.identity;
    }

    /// <summary>The View tool: no handle; its left drag pans (or what Tools.viewTool says).</summary>
    internal sealed class ViewModeTool : EditorTool
    {
    }

    internal sealed class MoveTool : TransformToolBase
    {
        protected override void DoHandles()
        {
            Vector3 p = Tools.handlePosition;
            Vector3 n = Handles.PositionHandle(p, startRotation);
            if (n != p)
                Apply(n, Quaternion.identity, Vector3.one, Quaternion.identity, false);
        }
    }

    internal sealed class RotateTool : TransformToolBase
    {
        protected override void DoHandles()
        {
            Quaternion n = Handles.RotationHandle(dragRotation, startPosition);
            if (n != dragRotation)
            {
                dragRotation = n;
                Apply(startPosition, n * Quaternion.Inverse(startRotation), Vector3.one, Quaternion.identity, Tools.pivotMode == PivotMode.Center);
            }
        }
    }

    internal sealed class ScaleTool : TransformToolBase
    {
        protected override void DoHandles()
        {
            Quaternion frame = ScaleFrame;
            Vector3 n = Handles.ScaleHandle(dragScale, startPosition, frame);
            if (n != dragScale)
            {
                dragScale = n;
                Apply(startPosition, Quaternion.identity, n, frame, Tools.pivotMode == PivotMode.Center);
            }
        }
    }

    /// <summary>The Transform tool: move, turn and scale at once.</summary>
    internal sealed class TransformTool : TransformToolBase
    {
        protected override void DoHandles()
        {
            Vector3 p = Tools.handlePosition, scale = dragScale;
            Quaternion r = dragRotation;
            Vector3 was = p;
            Handles.TransformHandle(ref p, ref r, ref scale);
            if (p != was || r != dragRotation || scale != dragScale)
            {
                dragRotation = r;
                dragScale = scale;
                Apply(p, r * Quaternion.Inverse(startRotation), scale, ScaleFrame, Tools.pivotMode == PivotMode.Center);
            }
        }
    }

    /// <summary>The Rect tool: the selection's bounds as a rectangle in the plane of the handle's axes most facing the
    /// camera; its corners and edges resize (the opposite ones stay), its inside moves.</summary>
    internal sealed class RectTool : TransformToolBase
    {
        Vector3 m_Center;
        Vector2 m_Size;
        Quaternion m_Frame;

        protected override void DoHandles()
        {
            if (GuiUtility.hotControl == 0)
                Measure();
            Vector3 c = m_Center;
            Vector2 s = m_Size;
            Handles.RectHandleInternal(ref c, ref s, m_Frame);
            if (c != m_Center || s != m_Size)
            {
                // The pivot's place in the rectangle stays; the objects scale with it along the frame's x and y.
                Vector3 scale = new Vector3(m_Size.x > 1e-6f ? s.x / m_Size.x : 1, m_Size.y > 1e-6f ? s.y / m_Size.y : 1, 1);
                Quaternion inverse = Quaternion.Inverse(m_Frame);
                Vector3 local = inverse * (startPosition - m_Center);
                Vector3 pivot = c + m_Frame * Vector3.Scale(local, scale);
                Apply(pivot, Quaternion.identity, scale, m_Frame, true);
            }
        }

        // The rectangle as the drag begins: the plane of the handle's two axes facing the camera most.
        void Measure()
        {
            Quaternion r = startRotation;
            Camera cam = HandleUtility.cameraInternal;
            Vector3 view = cam != null ? cam.transform.forward : Vector3.forward;
            Vector3[] axes = { r * Vector3.right, r * Vector3.up, r * Vector3.forward };
            int normal = 2;
            float best = -1;
            for (int i = 0; i < 3; i++)
            {
                float d = Math.Abs(Vector3.Dot(axes[i], view));
                if (d > best)
                {
                    best = d;
                    normal = i;
                }
            }
            Vector3 x = axes[(normal + 1) % 3], y = axes[(normal + 2) % 3];
            m_Frame = Quaternion.LookRotation(Vector3.Cross(x, y), y);
            Quaternion inverse = Quaternion.Inverse(m_Frame);
            Vector3 min = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue), max = -min;
            bool any = false;
            foreach (Transform t in Selection.transforms)
            {
                foreach (Renderer rd in t.GetComponentsInChildren<Renderer>())
                {
                    Bounds b = rd.bounds;
                    for (int k = 0; k < 8; k++)
                    {
                        Vector3 corner = b.center + Vector3.Scale(b.extents, new Vector3((k & 1) != 0 ? 1 : -1, (k & 2) != 0 ? 1 : -1, (k & 4) != 0 ? 1 : -1));
                        Vector3 l = inverse * corner;
                        min = Vector3.Min(min, l);
                        max = Vector3.Max(max, l);
                        any = true;
                    }
                }
                Vector3 p = inverse * t.position;
                min = Vector3.Min(min, p);
                max = Vector3.Max(max, p);
            }
            if (!any && (max - min).magnitude < 1e-4f)
            {
                // Nothing to measure: a small square about the pivot.
                float h = HandleUtility.GetHandleSize(startPosition) * 0.25f;
                min -= new Vector3(h, h, 0);
                max += new Vector3(h, h, 0);
            }
            m_Size = new Vector2(max.x - min.x, max.y - min.y);
            Vector3 center = (min + max) * 0.5f;
            // The rectangle lies in the plane through the pivot.
            center.z = (inverse * startPosition).z;
            m_Center = m_Frame * center;
        }
    }
}

namespace HelixEditor.EditorTools
{
    /// <summary>No tool (Tools.current = Tool.None): no handle.</summary>
    internal sealed class NoneTool : EditorTool
    {
    }
}
