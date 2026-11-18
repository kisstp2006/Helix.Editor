using System;
using System.Collections.Generic;
using HelixEngine;

namespace HelixEditor
{
    /// <summary>The scene view GUI's measures: points between the world and the GUI, a handle's size on the screen,
    /// distances from the pointer, the control nearest to it (each handle adds its distance in the Layout pass; the
    /// nearest within a few pixels takes the next pointer press).</summary>
    public static class HandleUtility
    {
        // Within this many pixels a handle is under the pointer.
        const float kPickDistance = 5;

        static Camera s_Camera;
        static SceneView s_View;
        static float s_Height = 1;
        static float s_NearestDistance;
        static int s_NearestLayout, s_Default, s_Nearest;

        public static int nearestControl
        {
            get => s_Nearest;
            set => s_Nearest = value;
        }

        /// <summary>A drag's speed: 4 with Shift, a quarter with Alt.</summary>
        public static float acceleration
        {
            get
            {
                Event e = Event.current;
                return e == null ? 1 : e.shift ? 4 : e.alt ? 0.25f : 1;
            }
        }

        /// <summary>The pointer's last movement as one signed number (its larger axis; down and left negative).</summary>
        public static float niceMouseDelta
        {
            get
            {
                Event e = Event.current;
                if (e == null)
                    return 0;
                Vector2 d = e.delta;
                return Math.Abs(d.x) > Math.Abs(d.y) ? d.x : -d.y;
            }
        }

        internal static Camera cameraInternal => s_Camera;

        // The view whose GUI runs now: its camera and height; the Layout pass starts the nearest control anew.
        internal static void BeginInternal(SceneView view)
        {
            s_Camera = view.camera;
            s_View = view;
            s_Height = Math.Max(1, view.viewportInternal.layout.height);
            Handles.ResetInternal();
            if (Event.current != null && Event.current.type == EventType.Layout)
            {
                s_NearestDistance = kPickDistance;
                s_NearestLayout = 0;
                s_Default = 0;
            }
        }

        internal static void EndInternal()
        {
            if (Event.current != null && Event.current.type == EventType.Layout)
                s_Nearest = s_NearestLayout != 0 ? s_NearestLayout : s_Default;
        }

        // For tests: a view's measures without its GUI running.
        internal static void SetCameraInternal(Camera camera, float height)
        {
            s_Camera = camera;
            s_View = null;
            foreach (SceneView v in SceneView.sceneViews)
                if (v.camera == camera)
                    s_View = v;
            s_Height = Math.Max(1, height);
        }

        /// <summary>The control's distance from the pointer in this Layout pass (the nearest one wins).</summary>
        public static void AddControl(int controlId, float distance)
        {
            if (Event.current != null && Event.current.type == EventType.Layout && distance <= s_NearestDistance)
            {
                s_NearestDistance = distance;
                s_NearestLayout = controlId;
            }
        }

        /// <summary>The control the pointer is on when it is on no other (a tool's: a click in the empty view).</summary>
        public static void AddDefaultControl(int controlId)
        {
            if (Event.current != null && Event.current.type == EventType.Layout)
                s_Default = controlId;
        }

        public static void Repaint() => SceneView.RepaintAll();

        // ------------------------------------------------------------------------------------------ the points

        /// <summary>The ray from the camera through a GUI point of the view.</summary>
        public static Ray GUIPointToWorldRay(Vector2 position) =>
            s_Camera != null ? s_Camera.ScreenPointToRay(new Vector3(position.x, s_Height - position.y, 0)) : new Ray(Vector3.zero, Vector3.forward);

        public static Vector2 WorldToGUIPoint(Vector3 world)
        {
            Vector3 p = WorldToGUIPointWithDepth(world);
            return new Vector2(p.x, p.y);
        }

        /// <summary>A point of Handles.matrix's space as a GUI point of the view and its depth (negative behind the camera).</summary>
        public static Vector3 WorldToGUIPointWithDepth(Vector3 world)
        {
            if (s_Camera == null)
                return Vector3.zero;
            Vector3 s = s_Camera.WorldToScreenPoint(Handles.matrix.MultiplyPoint(world));
            return new Vector3(s.x, s_Height - s.y, s.z);
        }

        /// <summary>The world size that is about 80 pixels on the screen at the point (handles keep their size so).</summary>
        public static float GetHandleSize(Vector3 position)
        {
            if (s_Camera == null)
                return 1;
            position = Handles.matrix.MultiplyPoint(position);
            Transform t = s_Camera.transform;
            Vector3 eye = t.position, forward = t.forward;
            float distance = Vector3.Dot(position - eye, forward);
            Vector3 a = s_Camera.WorldToScreenPoint(eye + forward * distance);
            Vector3 b = s_Camera.WorldToScreenPoint(eye + forward * distance + t.right);
            float pixels = new Vector2(a.x - b.x, a.y - b.y).magnitude;
            return 80f / Math.Max(pixels, 1e-4f);
        }

        // ------------------------------------------------------------------------------------------- picking

        /// <summary>The object under a GUI point of the view: a gizmo's icon, else the nearest renderer (the id
        /// pass), else the smallest canvas rect. selectPrefabRoot is taken (prefabs come with step 7).</summary>
        public static GameObject PickGameObject(Vector2 position, bool selectPrefabRoot) => PickGameObject(position, selectPrefabRoot, null);

        public static GameObject PickGameObject(Vector2 position, bool selectPrefabRoot, GameObject[] ignore) => Pick(position, ignore, out _);

        public static GameObject PickGameObject(Vector2 position, out int materialIndex) => Pick(position, null, out materialIndex);

        public static GameObject PickGameObject(Vector2 position, GameObject[] ignore, out int materialIndex) => Pick(position, ignore, out materialIndex);

        static GameObject Pick(Vector2 position, GameObject[] ignore, out int materialIndex)
        {
            materialIndex = -1;
            var skip = ignore != null ? new HashSet<GameObject>(ignore) : null;
            GameObject icon = s_View != null ? s_View.IconOwnerAtInternal(position, skip) : null;
            if (icon != null)
                return icon;
            if (ScenePicking.Pick(s_Camera, position, skip) is ScenePicking.Hit hit)
            {
                materialIndex = hit.submesh;
                return hit.renderer.gameObject;
            }
            return ScenePicking.CanvasRectAt(position, skip);
        }

        /// <summary>The objects with a renderer's pixel or a gizmo's icon inside a GUI rectangle of the view.</summary>
        public static GameObject[] PickRectObjects(Rect rect) => PickRectObjects(rect, false);

        public static GameObject[] PickRectObjects(Rect rect, bool selectPrefabRootsOnly)
        {
            var list = new List<GameObject>();
            foreach (Renderer r in ScenePicking.PickRect(s_Camera, rect))
                if (!list.Contains(r.gameObject))
                    list.Add(r.gameObject);
            if (s_View != null)
                foreach (GameObject go in s_View.IconOwnersInInternal(rect))
                    if (!list.Contains(go))
                        list.Add(go);
            return list.ToArray();
        }

        // ---------------------------------------------------------------------------------------- the distances

        static Vector2 Mouse => Event.current != null ? Event.current.mousePosition : Vector2.zero;

        /// <summary>Pixels from the pointer to the line between two points of Handles.matrix's space.</summary>
        public static float DistanceToLine(Vector3 p1, Vector3 p2)
        {
            Vector3 a = WorldToGUIPointWithDepth(p1), b = WorldToGUIPointWithDepth(p2);
            if (a.z <= 0 && b.z <= 0)
                return float.MaxValue;
            return DistancePointLine(Mouse, new Vector2(a.x, a.y), new Vector2(b.x, b.y));
        }

        /// <summary>Pixels from the pointer to a disc facing the camera (0 inside it).</summary>
        public static float DistanceToCircle(Vector3 position, float radius)
        {
            Vector3 c = WorldToGUIPointWithDepth(position);
            if (c.z <= 0)
                return float.MaxValue;
            float r = ScreenRadius(position, radius);
            return Math.Max(0, (Mouse - new Vector2(c.x, c.y)).magnitude - r);
        }

        // Pixels from the pointer to a rectangle of half extents in the rotation's x y plane (0 inside).
        internal static float DistanceToRectangleInternal(Vector3 position, Quaternion rotation, Vector2 half)
        {
            Vector3 r = rotation * Vector3.right * half.x, u = rotation * Vector3.up * half.y;
            Vector2[] p =
            {
                WorldToGUIPoint(position - r - u), WorldToGUIPoint(position + r - u), WorldToGUIPoint(position + r + u), WorldToGUIPoint(position - r + u),
            };
            Vector2 m = Mouse;
            if (Inside(m, p))
                return 0;
            float d = float.MaxValue;
            for (int i = 0; i < 4; i++)
                d = Math.Min(d, DistancePointLine(m, p[i], p[(i + 1) % 4]));
            return d;
        }

        /// <summary>Pixels from the pointer to a square of the size about the point turned by the rotation (0 inside).</summary>
        public static float DistanceToRectangle(Vector3 position, Quaternion rotation, float size) =>
            DistanceToRectangleInternal(position, rotation, new Vector2(size, size));

        /// <summary>Pixels from the pointer to a ring (a circle of the normal's plane).</summary>
        public static float DistanceToDisc(Vector3 center, Vector3 normal, float radius)
        {
            Vector3 a = Math.Abs(Vector3.Dot(normal.normalized, Vector3.up)) > 0.99f ? Vector3.right : Vector3.Cross(normal, Vector3.up).normalized;
            Vector3 b = Vector3.Cross(normal.normalized, a);
            float d = float.MaxValue;
            Vector3 prev = center + a * radius;
            for (int i = 1; i <= 32; i++)
            {
                double t = i * 2 * Math.PI / 32;
                Vector3 p = center + (a * (float)Math.Cos(t) + b * (float)Math.Sin(t)) * radius;
                d = Math.Min(d, DistanceToLine(prev, p));
                prev = p;
            }
            return d;
        }

        public static float DistancePointLine(Vector2 point, Vector2 lineStart, Vector2 lineEnd) => (ProjectPointLine(point, lineStart, lineEnd) - point).magnitude;

        public static Vector2 ProjectPointLine(Vector2 point, Vector2 lineStart, Vector2 lineEnd)
        {
            Vector2 d = lineEnd - lineStart;
            float len2 = Vector2.Dot(d, d);
            if (len2 < 1e-9f)
                return lineStart;
            float t = Math.Clamp(Vector2.Dot(point - lineStart, d) / len2, 0, 1);
            return lineStart + d * t;
        }

        static bool Inside(Vector2 p, Vector2[] poly)
        {
            bool inside = false;
            for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
                if ((poly[i].y > p.y) != (poly[j].y > p.y) && p.x < (poly[j].x - poly[i].x) * (p.y - poly[i].y) / (poly[j].y - poly[i].y) + poly[i].x)
                    inside = !inside;
            return inside;
        }

        // A world radius at a point as pixels.
        internal static float ScreenRadius(Vector3 position, float radius)
        {
            if (s_Camera == null)
                return 0;
            Vector3 world = Handles.matrix.MultiplyPoint(position);
            Vector3 a = s_Camera.WorldToScreenPoint(world), b = s_Camera.WorldToScreenPoint(world + s_Camera.transform.right * radius);
            return new Vector2(a.x - b.x, a.y - b.y).magnitude;
        }

        /// <summary>How far along a line (of Handles.matrix's space) the pointer moved from src to dest: the points on the
        /// line nearest the rays through them.</summary>
        public static float CalcLineTranslation(Vector2 src, Vector2 dest, Vector3 srcPosition, Vector3 constraintDir)
        {
            Vector3 origin = Handles.matrix.MultiplyPoint(srcPosition), dir = Handles.matrix.MultiplyVector(constraintDir).normalized;
            float scale = Handles.matrix.MultiplyVector(constraintDir).magnitude / Math.Max(1e-6f, constraintDir.magnitude);
            return (LineParameter(GUIPointToWorldRay(dest), origin, dir) - LineParameter(GUIPointToWorldRay(src), origin, dir)) / Math.Max(1e-6f, scale);
        }

        // The parameter of the point on the line (origin + t dir) nearest the ray.
        internal static float LineParameter(Ray ray, Vector3 origin, Vector3 dir)
        {
            Vector3 w = origin - ray.origin;
            float b = Vector3.Dot(dir, ray.direction), d = Vector3.Dot(dir, w), e = Vector3.Dot(ray.direction, w);
            float denom = 1 - b * b;
            if (Math.Abs(denom) < 1e-6f)
                return 0;
            return (b * e - d) / denom;
        }

        // Where the ray meets the plane (false when parallel or behind).
        internal static bool RayPlane(Ray ray, Vector3 point, Vector3 normal, out Vector3 hit)
        {
            float d = Vector3.Dot(ray.direction, normal);
            hit = Vector3.zero;
            if (Math.Abs(d) < 1e-6f)
                return false;
            float t = Vector3.Dot(point - ray.origin, normal) / d;
            if (t < 0)
                return false;
            hit = ray.origin + ray.direction * t;
            return true;
        }
    }
}
