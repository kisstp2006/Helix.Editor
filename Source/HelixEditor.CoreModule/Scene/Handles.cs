using System;
using System.Collections.Generic;
using HelixEngine;
using HelixEngine.Rendering;

namespace HelixEditor
{
    /// <summary>The scene view's 3D GUI: lines, arcs, discs and solid shapes in Handles.color through Handles.matrix
    /// (drawn in the Repaint pass; over everything, or depth tested with zTest), and the handles that move, turn and
    /// scale values by dragging (a control each: its distance from the pointer in the Layout pass, the nearest takes the
    /// press, the hot one follows the drag), drawn by cap functions.</summary>
    public static partial class Handles
    {
        public delegate void CapFunction(int controlID, Vector3 position, Quaternion rotation, float size, EventType eventType);

        public static Color color { get; set; } = Color.white;
        public static Matrix4x4 matrix { get; set; } = Matrix4x4.identity;
        public static Matrix4x4 inverseMatrix => matrix.inverse;
        public static CompareFunction zTest { get; set; } = CompareFunction.Always;
        public static bool lighting { get; set; } = true;

        public static Color xAxisColor { get; } = new Color(219f / 255, 62f / 255, 29f / 255, 0.93f);
        public static Color yAxisColor { get; } = new Color(154f / 255, 243f / 255, 72f / 255, 0.93f);
        public static Color zAxisColor { get; } = new Color(58f / 255, 122f / 255, 248f / 255, 0.93f);
        public static Color centerColor { get; } = new Color(0.8f, 0.8f, 0.8f, 0.93f);
        public static Color selectedColor { get; } = new Color(246f / 255, 242f / 255, 50f / 255, 0.89f);
        public static Color preselectionColor { get; } = new Color(201f / 255, 200f / 255, 144f / 255, 0.89f);
        public static Color secondaryColor { get; } = new Color(0.5f, 0.5f, 0.5f, 0.2f);

        internal static void ResetInternal()
        {
            color = Color.white;
            matrix = Matrix4x4.identity;
            zTest = CompareFunction.Always;
        }

        /// <summary>The color and matrix set for a block, as they were after it.</summary>
        public struct DrawingScope : IDisposable
        {
            readonly Color m_Color;
            readonly Matrix4x4 m_Matrix;
            bool m_Disposed;

            public DrawingScope(Color color) : this(color, Handles.matrix) { }
            public DrawingScope(Matrix4x4 matrix) : this(Handles.color, matrix) { }

            public DrawingScope(Color color, Matrix4x4 matrix)
            {
                m_Color = Handles.color;
                m_Matrix = Handles.matrix;
                m_Disposed = false;
                Handles.color = color;
                Handles.matrix = matrix;
            }

            public Color originalColor => m_Color;
            public Matrix4x4 originalMatrix => m_Matrix;

            public void Dispose()
            {
                if (m_Disposed)
                    return;
                m_Disposed = true;
                Handles.color = m_Color;
                Handles.matrix = m_Matrix;
            }
        }

        // ------------------------------------------------------------------------------------------ drawing

        static bool Painting => Event.current != null && Event.current.type == EventType.Repaint && SceneView.currentDrawingSceneView != null;
        static SceneBatch Batch => SceneView.currentDrawingSceneView.handleBatchInternal;
        static bool Tested => zTest != CompareFunction.Always;
        static Vector3 W(Vector3 p) => matrix.MultiplyPoint(p);

        static void Seg(Vector3 a, Vector3 b) => Batch.Line(W(a), W(b), color, Tested);
        // A segment of a width in pixels (0: one pixel, not smoothed).
        static void Seg(Vector3 a, Vector3 b, float thickness)
        {
            if (thickness > 0)
                Batch.ThickLine(W(a), W(b), color, thickness, Tested);
            else
                Seg(a, b);
        }

        static void Tri(Vector3 a, Vector3 b, Vector3 c) => Batch.Triangle(W(a), W(b), W(c), color, Tested);

        // A triangle of a solid cap: shaded by its normals (of Handles.matrix's space) toward the camera while
        // Handles.lighting is on, flat in Handles.color else.
        static void Lit(Vector3 a, Vector3 b, Vector3 c, Vector3 na, Vector3 nb, Vector3 nc)
        {
            if (!lighting)
            {
                Tri(a, b, c);
                return;
            }
            Vector3 wa = W(a), wb = W(b), wc = W(c);
            // The side away from the camera is not drawn (the caps draw over everything: it would cover the front).
            if (Vector3.Dot(matrix.MultiplyVector(na + nb + nc), ToCamera((wa + wb + wc) / 3)) < 0)
                return;
            Batch.Triangle(wa, wb, wc, Shaded(wa, na), Shaded(wb, nb), Shaded(wc, nc), Tested);
        }

        static Vector3 ToCamera(Vector3 world)
        {
            Camera cam = HandleUtility.cameraInternal;
            if (cam == null)
                return Vector3.zero;
            return cam.orthographic ? -cam.transform.forward : (cam.transform.position - world).normalized;
        }

        // The color lit from the camera: a little of it in shade, all of it facing the camera.
        static Color Shaded(Vector3 world, Vector3 normal)
        {
            Camera cam = HandleUtility.cameraInternal;
            if (cam == null)
                return color;
            Vector3 n = matrix.MultiplyVector(normal).normalized;
            float k = 0.45f + 0.55f * Math.Max(0, Vector3.Dot(n, ToCamera(world)));
            Color c = color;
            return new Color(c.r * k, c.g * k, c.b * k, c.a);
        }

        public static void DrawLine(Vector3 p1, Vector3 p2)
        {
            if (Painting)
                Seg(p1, p2);
        }

        /// <summary>A line of a width in pixels, its edges smoothed (0: one pixel).</summary>
        public static void DrawLine(Vector3 p1, Vector3 p2, float thickness)
        {
            if (Painting)
                Seg(p1, p2, thickness);
        }

        public static void DrawLines(Vector3[] lineSegments)
        {
            if (!Painting || lineSegments == null)
                return;
            for (int i = 0; i + 1 < lineSegments.Length; i += 2)
                Seg(lineSegments[i], lineSegments[i + 1]);
        }

        public static void DrawLines(Vector3[] points, int[] segmentIndices)
        {
            if (!Painting || points == null || segmentIndices == null)
                return;
            for (int i = 0; i + 1 < segmentIndices.Length; i += 2)
                Seg(points[segmentIndices[i]], points[segmentIndices[i + 1]]);
        }

        public static void DrawPolyLine(params Vector3[] points)
        {
            if (!Painting || points == null)
                return;
            for (int i = 0; i + 1 < points.Length; i++)
                Seg(points[i], points[i + 1]);
        }

        /// <summary>A polyline one pixel wide, smoothed.</summary>
        public static void DrawAAPolyLine(params Vector3[] points) => DrawAAPolyLine(1, points);

        /// <summary>A polyline of a width in pixels, smoothed.</summary>
        public static void DrawAAPolyLine(float width, params Vector3[] points)
        {
            if (!Painting || points == null)
                return;
            for (int i = 0; i + 1 < points.Length; i++)
                Seg(points[i], points[i + 1], Math.Max(width, 0.5f));
        }

        /// <summary>A line of dashes about screenSpaceSize pixels long.</summary>
        public static void DrawDottedLine(Vector3 p1, Vector3 p2, float screenSpaceSize)
        {
            if (!Painting)
                return;
            float pixels = (HandleUtility.WorldToGUIPoint(p1) - HandleUtility.WorldToGUIPoint(p2)).magnitude;
            int dashes = Math.Clamp((int)(pixels / Math.Max(1, screenSpaceSize) / 2), 1, 2000);
            for (int i = 0; i < dashes; i++)
                Seg(Vector3.Lerp(p1, p2, (2f * i) / (2 * dashes)), Vector3.Lerp(p1, p2, (2f * i + 1) / (2 * dashes)));
        }

        // Two axes of the plane of a normal.
        static void Basis(Vector3 normal, out Vector3 a, out Vector3 b)
        {
            normal = normal.normalized;
            a = Math.Abs(normal.y) > 0.99f ? Vector3.right : Vector3.Cross(normal, Vector3.up).normalized;
            b = Vector3.Cross(normal, a);
        }

        public static void DrawWireDisc(Vector3 center, Vector3 normal, float radius)
        {
            Basis(normal, out Vector3 a, out _);
            DrawWireArc(center, normal, a, 360, radius);
        }

        public static void DrawWireDisc(Vector3 center, Vector3 normal, float radius, float thickness)
        {
            Basis(normal, out Vector3 a, out _);
            DrawWireArc(center, normal, a, 360, radius, thickness);
        }

        /// <summary>An arc of the normal's plane from a direction over an angle (degrees, turning about the normal).</summary>
        public static void DrawWireArc(Vector3 center, Vector3 normal, Vector3 from, float angle, float radius) => DrawWireArc(center, normal, from, angle, radius, 0);

        /// <summary>An arc of a width in pixels (0: one pixel).</summary>
        public static void DrawWireArc(Vector3 center, Vector3 normal, Vector3 from, float angle, float radius, float thickness)
        {
            if (!Painting)
                return;
            int n = Math.Max(2, (int)(Math.Abs(angle) / 360 * 48));
            Vector3 prev = center + Quaternion.AngleAxis(0, normal) * from.normalized * radius;
            for (int i = 1; i <= n; i++)
            {
                Vector3 p = center + Quaternion.AngleAxis(angle * i / n, normal) * from.normalized * radius;
                Seg(prev, p, thickness);
                prev = p;
            }
        }


        public static void DrawSolidDisc(Vector3 center, Vector3 normal, float radius)
        {
            Basis(normal, out Vector3 a, out _);
            DrawSolidArc(center, normal, a, 360, radius);
        }

        public static void DrawSolidArc(Vector3 center, Vector3 normal, Vector3 from, float angle, float radius)
        {
            if (!Painting)
                return;
            int n = Math.Max(2, (int)(Math.Abs(angle) / 360 * 48));
            Vector3 prev = center + from.normalized * radius;
            for (int i = 1; i <= n; i++)
            {
                Vector3 p = center + Quaternion.AngleAxis(angle * i / n, normal) * from.normalized * radius;
                Tri(center, prev, p);
                prev = p;
            }
        }

        public static void DrawWireCube(Vector3 center, Vector3 size)
        {
            if (!Painting)
                return;
            Vector3 h = size * 0.5f;
            Vector3 C(int i) => center + new Vector3((i & 1) != 0 ? h.x : -h.x, (i & 2) != 0 ? h.y : -h.y, (i & 4) != 0 ? h.z : -h.z);
            for (int i = 0; i < 8; i++)
                for (int bit = 1; bit < 8; bit <<= 1)
                    if ((i & bit) == 0)
                        Seg(C(i), C(i | bit));
        }

        /// <summary>A quad of four points: its face in one color, its outline in another.</summary>
        public static void DrawSolidRectangleWithOutline(Vector3[] verts, Color faceColor, Color outlineColor)
        {
            if (!Painting || verts == null || verts.Length < 4)
                return;
            Color was = color;
            color = faceColor;
            Tri(verts[0], verts[1], verts[2]);
            Tri(verts[0], verts[2], verts[3]);
            color = outlineColor;
            for (int i = 0; i < 4; i++)
                Seg(verts[i], verts[(i + 1) % 4]);
            color = was;
        }

        public static void DrawSolidRectangleWithOutline(Rect rectangle, Color faceColor, Color outlineColor) =>
            DrawSolidRectangleWithOutline(new[]
            {
                new Vector3(rectangle.xMin, rectangle.yMin, 0), new Vector3(rectangle.xMax, rectangle.yMin, 0), new Vector3(rectangle.xMax, rectangle.yMax, 0),
                new Vector3(rectangle.xMin, rectangle.yMax, 0),
            }, faceColor, outlineColor);

        /// <summary>A convex polygon, filled.</summary>
        public static void DrawAAConvexPolygon(params Vector3[] points)
        {
            if (!Painting || points == null)
                return;
            for (int i = 1; i + 1 < points.Length; i++)
                Tri(points[0], points[i], points[i + 1]);
        }

        /// <summary>Text at a point of the scene (drawn by the view's GUI).</summary>
        public static void Label(Vector3 position, string text) => Label(position, new GuiContent(text), EditorStyles.label);
        public static void Label(Vector3 position, GuiContent content) => Label(position, content, EditorStyles.label);
        public static void Label(Vector3 position, string text, GuiStyle style) => Label(position, new GuiContent(text), style);

        public static void Label(Vector3 position, GuiContent content, GuiStyle style)
        {
            if (!Painting)
                return;
            Vector3 g = HandleUtility.WorldToGUIPointWithDepth(position);
            if (g.z <= 0)
                return;
            Vector2 size = (style ?? EditorStyles.label).CalcSize(content);
            Gui.Label(new Rect(g.x, g.y, size.x, size.y), content, style ?? EditorStyles.label);
        }

        public static void BeginGUI() { }
        public static void EndGUI() { }

        /// <summary>The Game view's rendering size (the screen's without one).</summary>
        public static Vector2 GetMainGameViewSize()
        {
            PlayModeWindow.GetRenderingResolution(out uint w, out uint h);
            return new Vector2(w, h);
        }

        // --------------------------------------------------------------------------------------------- the caps

        static Vector3 Forward(Quaternion r) => r * Vector3.forward;

        // A cone's sides and base: its apex at position + forward * size, its base at position.
        static void Cone(Vector3 basePoint, Vector3 dir, float length, float radius)
        {
            Basis(dir, out Vector3 a, out Vector3 b);
            Vector3 axis = dir.normalized, apex = basePoint + axis * length;
            const int n = 16;
            for (int i = 0; i < n; i++)
            {
                double t0 = i * 2 * Math.PI / n, t1 = (i + 1) * 2 * Math.PI / n, tm = (i + 0.5) * 2 * Math.PI / n;
                Vector3 e0 = a * (float)Math.Cos(t0) + b * (float)Math.Sin(t0), e1 = a * (float)Math.Cos(t1) + b * (float)Math.Sin(t1);
                Vector3 em = a * (float)Math.Cos(tm) + b * (float)Math.Sin(tm);
                Vector3 p0 = basePoint + e0 * radius, p1 = basePoint + e1 * radius;
                // The side's normals lean toward the apex by the cone's slope.
                Vector3 N(Vector3 e) => (e * length + axis * radius).normalized;
                Lit(apex, p0, p1, N(em), N(e0), N(e1));
                Lit(basePoint, p1, p0, -axis, -axis, -axis);
            }
        }

        public static void ConeHandleCap(int controlID, Vector3 position, Quaternion rotation, float size, EventType eventType)
        {
            if (eventType == EventType.Layout)
                HandleUtility.AddControl(controlID, HandleUtility.DistanceToCircle(position, size));
            else if (eventType == EventType.Repaint && Painting)
                Cone(position - Forward(rotation) * size * 0.7f, Forward(rotation), size * 1.4f, size * 0.5f);
        }

        public static void ArrowHandleCap(int controlID, Vector3 position, Quaternion rotation, float size, EventType eventType)
        {
            Vector3 dir = Forward(rotation);
            if (eventType == EventType.Layout)
            {
                HandleUtility.AddControl(controlID, Math.Min(HandleUtility.DistanceToLine(position, position + dir * size * 0.9f),
                                                             HandleUtility.DistanceToCircle(position + dir * size, size * 0.2f)));
                return;
            }
            if (eventType != EventType.Repaint || !Painting)
                return;
            Seg(position, position + dir * size * 0.9f);
            Cone(position + dir * size * 0.9f, dir, size * 0.2f, size * 0.07f);
        }

        public static void CubeHandleCap(int controlID, Vector3 position, Quaternion rotation, float size, EventType eventType)
        {
            if (eventType == EventType.Layout)
            {
                HandleUtility.AddControl(controlID, HandleUtility.DistanceToCircle(position, size * 0.5f));
                return;
            }
            if (eventType != EventType.Repaint || !Painting)
                return;
            Vector3 x = rotation * Vector3.right * size * 0.5f, y = rotation * Vector3.up * size * 0.5f, z = rotation * Vector3.forward * size * 0.5f;
            Vector3 C(int i) => position + ((i & 1) != 0 ? x : -x) + ((i & 2) != 0 ? y : -y) + ((i & 4) != 0 ? z : -z);
            int[] faces = { 0, 2, 3, 1, 4, 5, 7, 6, 0, 1, 5, 4, 2, 6, 7, 3, 0, 4, 6, 2, 1, 3, 7, 5 };
            for (int f = 0; f < 24; f += 4)
            {
                // The face's normal: from the cube's middle to the face's.
                Vector3 nf = ((C(faces[f]) + C(faces[f + 2])) * 0.5f - position).normalized;
                Lit(C(faces[f]), C(faces[f + 1]), C(faces[f + 2]), nf, nf, nf);
                Lit(C(faces[f]), C(faces[f + 2]), C(faces[f + 3]), nf, nf, nf);
            }
        }

        public static void SphereHandleCap(int controlID, Vector3 position, Quaternion rotation, float size, EventType eventType)
        {
            if (eventType == EventType.Layout)
            {
                HandleUtility.AddControl(controlID, HandleUtility.DistanceToCircle(position, size * 0.5f));
                return;
            }
            if (eventType != EventType.Repaint || !Painting)
                return;
            const int rings = 8, sectors = 16;
            float r = size * 0.5f;
            Vector3 S(int i, int j)
            {
                double th = Math.PI * i / rings, ph = 2 * Math.PI * j / sectors;
                return position + new Vector3((float)(Math.Sin(th) * Math.Cos(ph)), (float)Math.Cos(th), (float)(Math.Sin(th) * Math.Sin(ph))) * r;
            }
            for (int i = 0; i < rings; i++)
                for (int j = 0; j < sectors; j++)
                {
                    Vector3 a = S(i, j), b = S(i + 1, j), c = S(i + 1, j + 1), d = S(i, j + 1);
                    Lit(a, b, c, (a - position).normalized, (b - position).normalized, (c - position).normalized);
                    Lit(a, c, d, (a - position).normalized, (c - position).normalized, (d - position).normalized);
                }
        }

        public static void CylinderHandleCap(int controlID, Vector3 position, Quaternion rotation, float size, EventType eventType)
        {
            if (eventType == EventType.Layout)
            {
                HandleUtility.AddControl(controlID, HandleUtility.DistanceToCircle(position, size * 0.5f));
                return;
            }
            if (eventType != EventType.Repaint || !Painting)
                return;
            Vector3 dir = Forward(rotation), h = dir * size * 0.5f;
            Basis(dir, out Vector3 a, out Vector3 b);
            float r = size * 0.5f;
            const int n = 16;
            for (int i = 0; i < n; i++)
            {
                double t0 = i * 2 * Math.PI / n, t1 = (i + 1) * 2 * Math.PI / n;
                Vector3 e0 = (a * (float)Math.Cos(t0) + b * (float)Math.Sin(t0)) * r, e1 = (a * (float)Math.Cos(t1) + b * (float)Math.Sin(t1)) * r;
                Vector3 n0 = e0.normalized, n1 = e1.normalized, up = dir.normalized;
                Lit(position - h + e0, position + h + e0, position + h + e1, n0, n0, n1);
                Lit(position - h + e0, position + h + e1, position - h + e1, n0, n1, n1);
                Lit(position + h, position + h + e0, position + h + e1, up, up, up);
                Lit(position - h, position - h + e1, position - h + e0, -up, -up, -up);
            }
        }

        public static void CircleHandleCap(int controlID, Vector3 position, Quaternion rotation, float size, EventType eventType)
        {
            if (eventType == EventType.Layout)
                HandleUtility.AddControl(controlID, HandleUtility.DistanceToDisc(position, Forward(rotation), size));
            else if (eventType == EventType.Repaint && Painting)
                DrawWireDisc(position, Forward(rotation), size);
        }

        public static void RectangleHandleCap(int controlID, Vector3 position, Quaternion rotation, float size, EventType eventType)
        {
            if (eventType == EventType.Layout)
            {
                HandleUtility.AddControl(controlID, HandleUtility.DistanceToRectangle(position, rotation, size));
                return;
            }
            if (eventType != EventType.Repaint || !Painting)
                return;
            Vector3 r = rotation * Vector3.right * size, u = rotation * Vector3.up * size;
            Seg(position - r - u, position + r - u);
            Seg(position + r - u, position + r + u);
            Seg(position + r + u, position - r + u);
            Seg(position - r + u, position - r - u);
        }

        /// <summary>A small square facing the camera.</summary>
        public static void DotHandleCap(int controlID, Vector3 position, Quaternion rotation, float size, EventType eventType)
        {
            Camera cam = HandleUtility.cameraInternal;
            Quaternion facing = cam != null ? Quaternion.Inverse(matrix.rotation) * cam.transform.rotation : rotation;
            if (eventType == EventType.Layout)
            {
                HandleUtility.AddControl(controlID, HandleUtility.DistanceToRectangle(position, facing, size));
                return;
            }
            if (eventType != EventType.Repaint || !Painting)
                return;
            Vector3 r = facing * Vector3.right * size, u = facing * Vector3.up * size;
            Tri(position - r - u, position + r - u, position + r + u);
            Tri(position - r - u, position + r + u, position - r + u);
        }

        // --------------------------------------------------------------------------------------------- controls

        static readonly int s_SliderHint = "HandlesSlider".GetHashCode(), s_FreeMoveHint = "HandlesFreeMove".GetHashCode(), s_ButtonHint = "HandlesButton".GetHashCode(),
                            s_DiscHint = "HandlesDisc".GetHashCode(), s_ScaleHint = "HandlesScale".GetHashCode(), s_PlaneHint = "HandlesPlane".GetHashCode(),
                            s_RotateHint = "HandlesFreeRotate".GetHashCode(), s_RectHint = "HandlesRect".GetHashCode();

        // The drag in progress: where it started.
        static Vector2 s_StartMouse;
        static Vector3 s_StartPosition, s_StartHit, s_StartScale;
        static Vector2 s_StartSize;
        static Quaternion s_StartRotation;
        static float s_StartValue;

        static int Id(int hint) => GuiUtility.GetControlID(hint, FocusType.Passive);

        // A control's color: selected while dragged, lit while the pointer is on it.
        static Color Shade(int id, Color c) =>
            GuiUtility.hotControl == id ? selectedColor : GuiUtility.hotControl == 0 && HandleUtility.nearestControl == id ? preselectionColor : c;

        static void Paint(int id, CapFunction cap, Vector3 position, Quaternion rotation, float size)
        {
            Color was = color;
            color = Shade(id, was);
            cap?.Invoke(id, position, rotation, size, EventType.Repaint);
            color = was;
        }

        static bool Press(Event e, int id)
        {
            if (HandleUtility.nearestControl != id || e.button != 0 || e.alt)
                return false;
            GuiUtility.hotControl = id;
            s_StartMouse = e.mousePosition;
            e.Use();
            return true;
        }

        static bool Release(Event e, int id)
        {
            if (GuiUtility.hotControl != id || e.button != 0)
                return false;
            GuiUtility.hotControl = 0;
            e.Use();
            return true;
        }

        public static Vector3 Slider(Vector3 position, Vector3 direction) =>
            Slider(position, direction, HandleUtility.GetHandleSize(position), ArrowHandleCap, -1);

        public static Vector3 Slider(Vector3 position, Vector3 direction, float size, CapFunction capFunction, float snap) =>
            Slider(Id(s_SliderHint), position, direction, size, capFunction, snap);

        /// <summary>A handle that moves the position along the direction (the cap drawn facing it).</summary>
        public static Vector3 Slider(int controlID, Vector3 position, Vector3 direction, float size, CapFunction capFunction, float snap)
        {
            Event e = Event.current;
            if (e == null || direction == Vector3.zero)
                return position;
            Quaternion facing = Quaternion.LookRotation(direction);
            switch (e.GetTypeForControl(controlID))
            {
            case EventType.Layout:
            case EventType.MouseMove:
                capFunction?.Invoke(controlID, position, facing, size, EventType.Layout);
                break;
            case EventType.MouseDown:
                if (Press(e, controlID))
                    s_StartPosition = position;
                break;
            case EventType.MouseDrag:
                if (GuiUtility.hotControl == controlID)
                {
                    float t = HandleUtility.CalcLineTranslation(s_StartMouse, e.mousePosition, s_StartPosition, direction);
                    t = SnapIncrement(t, snap);
                    Vector3 p = s_StartPosition + direction.normalized * t;
                    if (p != position)
                    {
                        position = p;
                        Gui.changed = true;
                    }
                    e.Use();
                }
                break;
            case EventType.MouseUp:
                Release(e, controlID);
                break;
            case EventType.Repaint:
                Paint(controlID, capFunction, position, facing, size);
                break;
            }
            return position;
        }

        // A drag's amount snapped: by the given step (a negative step: the snap settings' move while they apply).
        static float SnapIncrement(float value, float snap)
        {
            if (snap < 0)
                snap = EditorSnapSettings.incrementalSnapActive ? EditorSnapSettings.move.x : 0;
            return SnapValue(value, snap);
        }

        public static Vector3 FreeMoveHandle(Vector3 position, float size, Vector3 snap, CapFunction capFunction) =>
            FreeMoveHandle(Id(s_FreeMoveHint), position, size, snap, capFunction);

        /// <summary>A handle that moves the position in the camera's plane.</summary>
        public static Vector3 FreeMoveHandle(int controlID, Vector3 position, float size, Vector3 snap, CapFunction capFunction)
        {
            Event e = Event.current;
            if (e == null)
                return position;
            Camera cam = HandleUtility.cameraInternal;
            Quaternion facing = cam != null ? Quaternion.Inverse(matrix.rotation) * cam.transform.rotation : Quaternion.identity;
            switch (e.GetTypeForControl(controlID))
            {
            case EventType.Layout:
            case EventType.MouseMove:
                capFunction?.Invoke(controlID, position, facing, size, EventType.Layout);
                break;
            case EventType.MouseDown:
                if (Press(e, controlID))
                {
                    s_StartPosition = position;
                    HandleUtility.RayPlane(HandleUtility.GUIPointToWorldRay(e.mousePosition), W(position), cam != null ? cam.transform.forward : Vector3.forward, out s_StartHit);
                }
                break;
            case EventType.MouseDrag:
                if (GuiUtility.hotControl == controlID && cam != null)
                {
                    if (HandleUtility.RayPlane(HandleUtility.GUIPointToWorldRay(e.mousePosition), s_StartHit, cam.transform.forward, out Vector3 hit))
                    {
                        Vector3 delta = matrix.inverse.MultiplyVector(hit - s_StartHit);
                        Vector3 p = s_StartPosition + SnapValue(delta, snap);
                        if (p != position)
                        {
                            position = p;
                            Gui.changed = true;
                        }
                    }
                    e.Use();
                }
                break;
            case EventType.MouseUp:
                Release(e, controlID);
                break;
            case EventType.Repaint:
                Paint(controlID, capFunction, position, facing, size);
                break;
            }
            return position;
        }

        // A handle that moves the position in a plane of two axes (a small square at their corner).
        static Vector3 PlaneHandle(int controlID, Vector3 position, Vector3 axis1, Vector3 axis2, float size, Color c)
        {
            Event e = Event.current;
            Vector3 normal = Vector3.Cross(axis1, axis2).normalized;
            Vector3 center = position + (axis1 + axis2).normalized * size * 0.25f * 1.414f;
            Quaternion facing = Quaternion.LookRotation(normal, axis2);
            float half = size * 0.1f;
            switch (e.GetTypeForControl(controlID))
            {
            case EventType.Layout:
            case EventType.MouseMove:
                HandleUtility.AddControl(controlID, HandleUtility.DistanceToRectangle(center, facing, half));
                break;
            case EventType.MouseDown:
                if (Press(e, controlID))
                {
                    s_StartPosition = position;
                    HandleUtility.RayPlane(HandleUtility.GUIPointToWorldRay(e.mousePosition), W(position), matrix.MultiplyVector(normal).normalized, out s_StartHit);
                }
                break;
            case EventType.MouseDrag:
                if (GuiUtility.hotControl == controlID)
                {
                    if (HandleUtility.RayPlane(HandleUtility.GUIPointToWorldRay(e.mousePosition), s_StartHit, matrix.MultiplyVector(normal).normalized, out Vector3 hit))
                    {
                        Vector3 delta = matrix.inverse.MultiplyVector(hit - s_StartHit);
                        if (EditorSnapSettings.incrementalSnapActive)
                            delta = SnapValue(delta, EditorSnapSettings.move);
                        Vector3 p = s_StartPosition + delta;
                        if (p != position)
                        {
                            position = p;
                            Gui.changed = true;
                        }
                    }
                    e.Use();
                }
                break;
            case EventType.MouseUp:
                Release(e, controlID);
                break;
            case EventType.Repaint:
                Color face = Shade(controlID, c);
                Vector3 r = facing * Vector3.right * half, u = facing * Vector3.up * half;
                DrawSolidRectangleWithOutline(new[] { center - r - u, center + r - u, center + r + u, center - r + u },
                                              new Color(face.r, face.g, face.b, 0.3f), face);
                break;
            }
            return position;
        }

        /// <summary>The Move tool's handle: an arrow per axis, a square per plane, the free move in the middle.</summary>
        public static Vector3 PositionHandle(Vector3 position, Quaternion rotation)
        {
            float size = HandleUtility.GetHandleSize(position);
            Vector3 x = rotation * Vector3.right, y = rotation * Vector3.up, z = rotation * Vector3.forward;
            Color was = color;
            color = xAxisColor;
            position = Slider(Id(s_SliderHint), position, x, size, ArrowHandleCap, -1);
            color = yAxisColor;
            position = Slider(Id(s_SliderHint), position, y, size, ArrowHandleCap, -1);
            color = zAxisColor;
            position = Slider(Id(s_SliderHint), position, z, size, ArrowHandleCap, -1);
            position = PlaneHandle(Id(s_PlaneHint), position, y, z, size, xAxisColor);
            position = PlaneHandle(Id(s_PlaneHint), position, x, z, size, yAxisColor);
            position = PlaneHandle(Id(s_PlaneHint), position, x, y, size, zAxisColor);
            color = centerColor;
            position = FreeMoveHandle(Id(s_FreeMoveHint), position, size * 0.15f,
                                      EditorSnapSettings.incrementalSnapActive ? EditorSnapSettings.move : Vector3.zero, RectangleHandleCap);
            color = was;
            return position;
        }

        public static Quaternion Disc(Quaternion rotation, Vector3 position, Vector3 axis, float size, bool cutoffPlane, float snap) =>
            Disc(Id(s_DiscHint), rotation, position, axis, size, cutoffPlane, snap);

        /// <summary>A ring about the axis: dragging along it turns the rotation about the axis (snapped to snap degrees).</summary>
        public static Quaternion Disc(int id, Quaternion rotation, Vector3 position, Vector3 axis, float size, bool cutoffPlane, float snap)
        {
            Event e = Event.current;
            if (e == null)
                return rotation;
            switch (e.GetTypeForControl(id))
            {
            case EventType.Layout:
            case EventType.MouseMove:
                HandleUtility.AddControl(id, HandleUtility.DistanceToDisc(position, axis, size));
                break;
            case EventType.MouseDown:
                if (Press(e, id))
                {
                    s_StartRotation = rotation;
                    HandleUtility.RayPlane(HandleUtility.GUIPointToWorldRay(e.mousePosition), W(position), matrix.MultiplyVector(axis).normalized, out s_StartHit);
                }
                break;
            case EventType.MouseDrag:
                if (GuiUtility.hotControl == id)
                {
                    Vector3 worldAxis = matrix.MultiplyVector(axis).normalized, center = W(position);
                    if (HandleUtility.RayPlane(HandleUtility.GUIPointToWorldRay(e.mousePosition), center, worldAxis, out Vector3 hit))
                    {
                        Vector3 a = (s_StartHit - center).normalized, b = (hit - center).normalized;
                        float angle = (float)(Math.Atan2(Vector3.Dot(Vector3.Cross(a, b), worldAxis), Vector3.Dot(a, b)) * 180 / Math.PI);
                        if (snap > 0 || EditorSnapSettings.incrementalSnapActive)
                            angle = SnapValue(angle, snap > 0 ? snap : EditorSnapSettings.rotate);
                        Quaternion r = Quaternion.AngleAxis(angle, axis) * s_StartRotation;
                        if (r != rotation)
                        {
                            rotation = r;
                            Gui.changed = true;
                        }
                    }
                    e.Use();
                }
                break;
            case EventType.MouseUp:
                Release(e, id);
                break;
            case EventType.Repaint:
                Color was = color;
                color = Shade(id, was);
                DrawWireDisc(position, axis, size);
                color = was;
                break;
            }
            return rotation;
        }

        /// <summary>The Rotate tool's handle: a ring per axis.</summary>
        public static Quaternion RotationHandle(Quaternion rotation, Vector3 position)
        {
            float size = HandleUtility.GetHandleSize(position);
            Color was = color;
            color = xAxisColor;
            rotation = Disc(Id(s_DiscHint), rotation, position, rotation * Vector3.right, size, true, 0);
            color = yAxisColor;
            rotation = Disc(Id(s_DiscHint), rotation, position, rotation * Vector3.up, size, true, 0);
            color = zAxisColor;
            rotation = Disc(Id(s_DiscHint), rotation, position, rotation * Vector3.forward, size, true, 0);
            color = new Color(0.8f, 0.8f, 0.8f, 0.6f);
            rotation = FreeRotateHandle(Id(s_RotateHint), rotation, position, size * 1.1f);
            color = was;
            return rotation;
        }

        public static Quaternion FreeRotateHandle(Quaternion rotation, Vector3 position, float size) => FreeRotateHandle(Id(s_RotateHint), rotation, position, size);

        /// <summary>A ring facing the camera: dragging turns the rotation about the camera's axes.</summary>
        public static Quaternion FreeRotateHandle(int id, Quaternion rotation, Vector3 position, float size)
        {
            Event e = Event.current;
            Camera cam = HandleUtility.cameraInternal;
            if (e == null || cam == null)
                return rotation;
            Vector3 toCamera = Quaternion.Inverse(matrix.rotation) * -cam.transform.forward;
            switch (e.GetTypeForControl(id))
            {
            case EventType.Layout:
            case EventType.MouseMove:
                HandleUtility.AddControl(id, HandleUtility.DistanceToDisc(position, toCamera, size));
                break;
            case EventType.MouseDown:
                if (Press(e, id))
                    s_StartRotation = rotation;
                break;
            case EventType.MouseDrag:
                if (GuiUtility.hotControl == id)
                {
                    Vector2 d = e.mousePosition - s_StartMouse;
                    Vector3 up = Quaternion.Inverse(matrix.rotation) * cam.transform.up, right = Quaternion.Inverse(matrix.rotation) * cam.transform.right;
                    rotation = Quaternion.AngleAxis(-d.x * 0.5f, up) * Quaternion.AngleAxis(-d.y * 0.5f, right) * s_StartRotation;
                    Gui.changed = true;
                    e.Use();
                }
                break;
            case EventType.MouseUp:
                Release(e, id);
                break;
            case EventType.Repaint:
                Color was = color;
                color = Shade(id, was);
                DrawWireDisc(position, toCamera, size);
                color = was;
                break;
            }
            return rotation;
        }

        public static float ScaleValueHandle(float value, Vector3 position, Quaternion rotation, float size, CapFunction capFunction, float snap) =>
            ScaleValueHandle(Id(s_ScaleHint), value, position, rotation, size, capFunction, snap);

        /// <summary>A handle whose drag scales a value (right and up larger).</summary>
        static float ScaleValueHandle(int id, float value, Vector3 position, Quaternion rotation, float size, CapFunction capFunction, float snap)
        {
            Event e = Event.current;
            if (e == null)
                return value;
            switch (e.GetTypeForControl(id))
            {
            case EventType.Layout:
            case EventType.MouseMove:
                capFunction?.Invoke(id, position, rotation, size, EventType.Layout);
                break;
            case EventType.MouseDown:
                if (Press(e, id))
                    s_StartValue = value;
                break;
            case EventType.MouseDrag:
                if (GuiUtility.hotControl == id)
                {
                    Vector2 d = e.mousePosition - s_StartMouse;
                    float v = s_StartValue * (1 + (d.x - d.y) / 100f);
                    v = SnapValue(v, snap > 0 ? snap : EditorSnapSettings.incrementalSnapActive ? EditorSnapSettings.scale * 0.1f : 0);
                    if (v != value)
                    {
                        value = v;
                        Gui.changed = true;
                    }
                    e.Use();
                }
                break;
            case EventType.MouseUp:
                Release(e, id);
                break;
            case EventType.Repaint:
                Paint(id, capFunction, position, rotation, size);
                break;
            }
            return value;
        }

        // A handle along an axis whose drag scales one component (a line ending in a cube).
        static float ScaleSlider(int id, float value, Vector3 position, Vector3 direction, float size)
        {
            Event e = Event.current;
            Vector3 end = position + direction * size * (GuiUtility.hotControl == id ? value / Math.Max(1e-6f, s_StartValue) : 1);
            switch (e.GetTypeForControl(id))
            {
            case EventType.Layout:
            case EventType.MouseMove:
                HandleUtility.AddControl(id, Math.Min(HandleUtility.DistanceToLine(position, end), HandleUtility.DistanceToCircle(end, size * 0.1f)));
                break;
            case EventType.MouseDown:
                if (Press(e, id))
                {
                    s_StartValue = value;
                    s_StartPosition = position;
                }
                break;
            case EventType.MouseDrag:
                if (GuiUtility.hotControl == id)
                {
                    float t = HandleUtility.CalcLineTranslation(s_StartMouse, e.mousePosition, s_StartPosition, direction);
                    float v = s_StartValue * (1 + t / Math.Max(1e-6f, size));
                    if (EditorSnapSettings.incrementalSnapActive)
                        v = SnapValue(v, EditorSnapSettings.scale * 0.1f);
                    if (v != value)
                    {
                        value = v;
                        Gui.changed = true;
                    }
                    e.Use();
                }
                break;
            case EventType.MouseUp:
                Release(e, id);
                break;
            case EventType.Repaint:
                Color was = color;
                color = Shade(id, was);
                DrawLine(position, end);
                CubeHandleCap(id, end, Quaternion.LookRotation(direction), size * 0.1f, EventType.Repaint);
                color = was;
                break;
            }
            return value;
        }

        public static Vector3 ScaleHandle(Vector3 scale, Vector3 position, Quaternion rotation) => ScaleHandle(scale, position, rotation, HandleUtility.GetHandleSize(position));

        /// <summary>The Scale tool's handle: a slider per axis, a cube in the middle scaling all three.</summary>
        public static Vector3 ScaleHandle(Vector3 scale, Vector3 position, Quaternion rotation, float size)
        {
            Color was = color;
            color = xAxisColor;
            scale.x = ScaleSlider(Id(s_ScaleHint), scale.x, position, rotation * Vector3.right, size);
            color = yAxisColor;
            scale.y = ScaleSlider(Id(s_ScaleHint), scale.y, position, rotation * Vector3.up, size);
            color = zAxisColor;
            scale.z = ScaleSlider(Id(s_ScaleHint), scale.z, position, rotation * Vector3.forward, size);
            color = centerColor;
            scale = UniformScale(Id(s_ScaleHint), scale, position, rotation, size * 0.15f);
            color = was;
            return scale;
        }

        /// <summary>The Transform tool's handle: moving, turning and scaling at once.</summary>
        public static void TransformHandle(ref Vector3 position, ref Quaternion rotation, ref Vector3 scale)
        {
            float size = HandleUtility.GetHandleSize(position);
            position = PositionHandle(position, rotation);
            Color was = color;
            color = xAxisColor;
            rotation = Disc(Id(s_DiscHint), rotation, position, rotation * Vector3.right, size * 1.2f, true, 0);
            color = yAxisColor;
            rotation = Disc(Id(s_DiscHint), rotation, position, rotation * Vector3.up, size * 1.2f, true, 0);
            color = zAxisColor;
            rotation = Disc(Id(s_DiscHint), rotation, position, rotation * Vector3.forward, size * 1.2f, true, 0);
            color = centerColor;
            scale = UniformScale(Id(s_ScaleHint), scale, position + rotation * new Vector3(1, 1, 1) * size * 0.75f, rotation, size * 0.12f);
            color = was;
        }

        // The cube in the middle: its drag's factor times the scale the drag began with (a caller feeding the result
        // back does not compound it).
        static Vector3 UniformScale(int id, Vector3 scale, Vector3 position, Quaternion rotation, float size)
        {
            if (GuiUtility.hotControl != id)
                s_StartScale = scale;
            float uniform = ScaleValueHandle(id, 1, position, rotation, size, CubeHandleCap, 0);
            return GuiUtility.hotControl == id && uniform != 1 ? s_StartScale * uniform : scale;
        }

        public static void TransformHandle(ref Vector3 position, ref Quaternion rotation)
        {
            Vector3 scale = Vector3.one;
            TransformHandle(ref position, ref rotation, ref scale);
        }

        /// <summary>A radius dragged by dots on the three axes (both ways), its sphere drawn as three rings.</summary>
        public static float RadiusHandle(Quaternion rotation, Vector3 position, float radius) => RadiusHandle(rotation, position, radius, false);

        public static float RadiusHandle(Quaternion rotation, Vector3 position, float radius, bool handlesOnly)
        {
            Vector3[] axes = { rotation * Vector3.right, rotation * Vector3.up, rotation * Vector3.forward };
            if (!handlesOnly)
                foreach (Vector3 a in axes)
                    DrawWireDisc(position, a, radius);
            foreach (Vector3 a in axes)
                for (int sign = -1; sign <= 1; sign += 2)
                {
                    Vector3 dir = a * sign;
                    Vector3 end = position + dir * radius;
                    Vector3 moved = Slider(Id(s_SliderHint), end, dir, HandleUtility.GetHandleSize(end) * 0.04f, DotHandleCap, 0);
                    if (moved != end)
                        radius = Math.Max(0, Vector3.Dot(moved - position, dir));
                }
            return radius;
        }

        /// <summary>A handle that is a button: true when clicked.</summary>
        public static bool Button(Vector3 position, Quaternion direction, float size, float pickSize, CapFunction capFunction)
        {
            int id = Id(s_ButtonHint);
            Event e = Event.current;
            if (e == null)
                return false;
            switch (e.GetTypeForControl(id))
            {
            case EventType.Layout:
            case EventType.MouseMove:
                HandleUtility.AddControl(id, HandleUtility.DistanceToCircle(position, pickSize));
                break;
            case EventType.MouseDown:
                Press(e, id);
                break;
            case EventType.MouseUp:
                if (Release(e, id))
                    return HandleUtility.nearestControl == id;
                break;
            case EventType.Repaint:
                Paint(id, capFunction, position, direction, size);
                break;
            }
            return false;
        }
    }
}
