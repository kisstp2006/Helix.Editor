using System;
using System.Collections.Generic;
using System.Reflection;
using HelixEngine;
using HelixEngine.Internal;
using HelixEngine.UI;
using Object = HelixEngine.Object;

namespace HelixEditor
{
    /// <summary>Lines and triangles in their colors for one camera: depth tested or drawn over everything; made into
    /// meshes and drawn for that camera only.</summary>
    internal sealed class SceneBatch
    {
        // [depth tested, always] x [lines, triangles, thick lines]: the points, their colors; a thick line's quad also
        // has each vertex's other end and its side with half the width (negative at the segment's end).
        const int kKinds = 3;
        readonly List<Vector3>[] m_Points = New<Vector3>();
        readonly List<Color>[] m_Colors = New<Color>();
        readonly List<Vector3>[] m_Others = New<Vector3>();
        readonly List<Vector2>[] m_Sides = New<Vector2>();
        readonly Mesh[] m_Meshes = new Mesh[2 * kKinds];

        static List<T>[] New<T>()
        {
            var lists = new List<T>[2 * kKinds];
            for (int i = 0; i < lists.Length; i++)
                lists[i] = new List<T>();
            return lists;
        }

        static int Slot(bool depthTested, int kind) => (depthTested ? 0 : kKinds) + kind;

        public int lineCount => (m_Points[0].Count + m_Points[kKinds].Count) / 2;
        public int triangleCount => (m_Points[1].Count + m_Points[kKinds + 1].Count) / 3;
        public int thickLineCount => (m_Points[2].Count + m_Points[kKinds + 2].Count) / 6;

        // For tests: how many colors the triangles have.
        internal int distinctTriangleColorsInternal
        {
            get
            {
                var seen = new HashSet<Color>();
                foreach (Color c in m_Colors[1])
                    seen.Add(c);
                foreach (Color c in m_Colors[kKinds + 1])
                    seen.Add(c);
                return seen.Count;
            }
        }

        public void Line(Vector3 a, Vector3 b, Color color, bool depthTested)
        {
            int s = Slot(depthTested, 0);
            m_Points[s].Add(a);
            m_Points[s].Add(b);
            m_Colors[s].Add(color);
            m_Colors[s].Add(color);
        }

        public void Triangle(Vector3 a, Vector3 b, Vector3 c, Color color, bool depthTested) => Triangle(a, b, c, color, color, color, depthTested);

        public void Triangle(Vector3 a, Vector3 b, Vector3 c, Color ca, Color cb, Color cc, bool depthTested)
        {
            int s = Slot(depthTested, 1);
            m_Points[s].Add(a);
            m_Points[s].Add(b);
            m_Points[s].Add(c);
            m_Colors[s].Add(ca);
            m_Colors[s].Add(cb);
            m_Colors[s].Add(cc);
        }

        /// <summary>A line of a width in pixels, its edges smoothed (two triangles widened on the screen).</summary>
        public void ThickLine(Vector3 a, Vector3 b, Color color, float width, bool depthTested)
        {
            int s = Slot(depthTested, 2);
            float half = Math.Max(0.25f, width * 0.5f);
            // The quad's corners: at a (its side -1, +1), at b (+1, -1); b's halves negative.
            Vector3[] at = { a, a, b, a, b, b };
            Vector3[] other = { b, b, a, b, a, a };
            Vector2[] side = { new(-1, half), new(1, half), new(1, -half), new(-1, half), new(1, -half), new(-1, -half) };
            for (int i = 0; i < 6; i++)
            {
                m_Points[s].Add(at[i]);
                m_Others[s].Add(other[i]);
                m_Sides[s].Add(side[i]);
                m_Colors[s].Add(color);
            }
        }

        public void Clear()
        {
            for (int i = 0; i < 2 * kKinds; i++)
            {
                m_Points[i].Clear();
                m_Colors[i].Clear();
                m_Others[i].Clear();
                m_Sides[i].Clear();
            }
        }

        /// <summary>Drawn for the camera: lines and triangles with SceneLines' materials, thick lines with
        /// SceneThickLines' (depth tested, over everything).</summary>
        public void Draw(Camera camera, Material depthTested, Material always, Material thickTested, Material thickAlways)
        {
            for (int s = 0; s < 2 * kKinds; s++)
            {
                List<Vector3> points = m_Points[s];
                int kind = s % kKinds;
                bool tested = s < kKinds;
                Material material = kind == 2 ? (tested ? thickTested : thickAlways) : (tested ? depthTested : always);
                if (points.Count == 0 || material == null)
                    continue;
                Mesh m = m_Meshes[s] ??= new Mesh { name = "SceneBatch", hideFlags = HideFlags.HideAndDontSave };
                m.Clear();
                m.vertices = points.ToArray();
                m.colors = m_Colors[s].ToArray();
                if (kind == 2)
                {
                    m.normals = m_Others[s].ToArray();
                    m.uv = m_Sides[s].ToArray();
                }
                var indices = new int[points.Count];
                for (int i = 0; i < indices.Length; i++)
                    indices[i] = i;
                m.SetIndices(indices, kind == 0 ? MeshTopology.Lines : MeshTopology.Triangles, 0);
                // Bounds that never cull it: it is the camera's own.
                m.bounds = new Bounds(camera.transform.position, Vector3.one * 1e7f);
                Graphics.DrawMesh(m, Matrix4x4.identity, material, 0, camera);
            }
        }

        public void Release()
        {
            foreach (Mesh m in m_Meshes)
                if (m != null)
                    Object.DestroyImmediate(m);
        }
    }

    // The Scene view's drawing: the scenes' gizmos (OnDrawGizmos, OnDrawGizmosSelected, the built-in ones of cameras and
    // lights) each frame, the handles of its GUI (an ImguiContainer over the viewport, where beforeSceneGui and
    // duringSceneGui run), both drawn for its camera; the gizmos' icons drawn by the GUI.
    public partial class SceneView
    {
        readonly SceneBatch m_GizmoBatch = new(), m_HandleBatch = new();
        readonly List<GizmoRecorder.IconMark> m_Icons = new();
        // The object that drew each icon (a click on an icon selects it).
        readonly List<GameObject> m_IconOwners = new();
        Material m_LinesTested, m_LinesAlways, m_ThickTested, m_ThickAlways;
        ImguiContainer m_SceneGui;

        internal SceneBatch gizmoBatchInternal => m_GizmoBatch;
        internal SceneBatch handleBatchInternal => m_HandleBatch;
        internal IReadOnlyList<GizmoRecorder.IconMark> iconsInternal => m_Icons;
        internal ImguiContainer sceneGuiInternal => m_SceneGui;

        void InitDrawing()
        {
            m_SceneGui = new ImguiContainer(OnSceneGui) { name = "scene-gui" };
            m_SceneGui.AddToClassList("scene-gui");
            m_SceneGui.style.position = Position.Absolute;
            m_SceneGui.style.left = 0;
            m_SceneGui.style.top = 0;
            m_SceneGui.style.right = 0;
            m_SceneGui.style.bottom = 0;
            // Under the orientation gizmo.
            m_Viewport.Insert(0, m_SceneGui);
        }

        bool EnsureLineMaterials()
        {
            if (m_LinesTested == null || m_LinesAlways == null)
            {
                Shader shader = Shader.Find("Hidden/Editor/SceneLines");
                if (shader == null)
                    return false;
                m_LinesTested = Lines(shader, HelixEngine.Rendering.CompareFunction.LessEqual);
                m_LinesAlways = Lines(shader, HelixEngine.Rendering.CompareFunction.Always);
            }
            if ((m_ThickTested == null || m_ThickAlways == null) && Shader.Find("Hidden/Editor/SceneThickLines") is Shader thick)
            {
                m_ThickTested = Lines(thick, HelixEngine.Rendering.CompareFunction.LessEqual);
                m_ThickAlways = Lines(thick, HelixEngine.Rendering.CompareFunction.Always);
            }
            // The thick lines widen by the view's pixels.
            Vector4 size = new Vector4(Math.Max(1, m_Target != null ? m_Target.width : 1), Math.Max(1, m_Target != null ? m_Target.height : 1), 0, 0);
            m_ThickTested?.SetVector("_ViewportSize", size);
            m_ThickAlways?.SetVector("_ViewportSize", size);
            return true;
        }

        static Material Lines(Shader shader, HelixEngine.Rendering.CompareFunction test)
        {
            var m = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            m.SetFloat("_ZTest", (float)test);
            return m;
        }

        // Each frame, before the camera renders: the gizmos gathered, both batches drawn.
        void DrawGizmosAndHandles()
        {
            m_GizmoBatch.Clear();
            m_Icons.Clear();
            m_IconOwners.Clear();
            if (drawGizmos)
                CollectGizmos();
            if (!EnsureLineMaterials())
                return;
            m_GizmoBatch.Draw(m_Camera, m_LinesTested, m_LinesAlways, m_ThickTested, m_ThickAlways);
            m_HandleBatch.Draw(m_Camera, m_LinesTested, m_LinesAlways, m_ThickTested, m_ThickAlways);
        }

        void ReleaseDrawing()
        {
            m_GizmoBatch.Release();
            m_HandleBatch.Release();
            if (m_LinesTested != null)
                DestroyImmediate(m_LinesTested);
            if (m_LinesAlways != null)
                DestroyImmediate(m_LinesAlways);
            if (m_ThickTested != null)
                DestroyImmediate(m_ThickTested);
            if (m_ThickAlways != null)
                DestroyImmediate(m_ThickAlways);
        }

        // ------------------------------------------------------------------------------------------- gizmos

        void CollectGizmos()
        {
            var selected = new HashSet<GameObject>(Selection.gameObjects);
            // The edited scenes' objects only (not the shell's, in the DontDestroyOnLoad scene).
            var scenes = new HashSet<HelixEngine.SceneManagement.Scene>();
            for (int i = 0; i < HelixEngine.SceneManagement.SceneManager.sceneCount; i++)
                scenes.Add(HelixEngine.SceneManagement.SceneManager.GetSceneAt(i));
            GizmoRecorder.Clear();
            GizmoRecorder.recording = true;
            currentDrawingSceneView = this;
            try
            {
                foreach (GameObject go in Object.FindObjectsByType<GameObject>(FindObjectsSortMode.None))
                {
                    if (go == null || !go.activeInHierarchy || (go.hideFlags & HideFlags.HideInHierarchy) != 0 || !scenes.Contains(go.scene))
                        continue;
                    int icons = GizmoRecorder.icons.Count;
                    BuiltinGizmos(go, selected.Contains(go) || IsChildOfSelection(go, selected));
                    if (go.GetComponent<GameBehaviour>() != null)
                        ScriptGizmos(go, selected.Contains(go));
                    for (int i = icons; i < GizmoRecorder.icons.Count; i++)
                        m_IconOwners.Add(go);
                }
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
            finally
            {
                GizmoRecorder.recording = false;
                Gizmos.matrix = Matrix4x4.identity;
                Gizmos.color = Color.white;
            }
            List<GizmoRecorder.Vertex> lines = GizmoRecorder.lines, tris = GizmoRecorder.triangles;
            for (int i = 0; i + 1 < lines.Count; i += 2)
                m_GizmoBatch.Line(lines[i].position, lines[i + 1].position, lines[i].color, true);
            for (int i = 0; i + 2 < tris.Count; i += 3)
                m_GizmoBatch.Triangle(tris[i].position, tris[i + 1].position, tris[i + 2].position, tris[i].color, true);
            m_Icons.AddRange(GizmoRecorder.icons);
            GizmoRecorder.Clear();
        }

        static void ScriptGizmos(GameObject go, bool selected)
        {
            Gizmos.matrix = Matrix4x4.identity;
            Gizmos.color = Color.white;
            go.SendMessage("OnDrawGizmos", SendMessageOptions.DontRequireReceiver);
            if (selected)
            {
                Gizmos.matrix = Matrix4x4.identity;
                Gizmos.color = Color.white;
                go.SendMessage("OnDrawGizmosSelected", SendMessageOptions.DontRequireReceiver);
            }
        }

        /// <summary>The object whose icon is under a GUI point (the last drawn first), or null.</summary>
        internal GameObject IconOwnerAtInternal(Vector2 point, ICollection<GameObject> ignore)
        {
            if (!drawGizmos)
                return null;
            for (int i = Math.Min(m_Icons.Count, m_IconOwners.Count) - 1; i >= 0; i--)
            {
                GameObject owner = m_IconOwners[i];
                if (owner == null || (ignore != null && ignore.Contains(owner)))
                    continue;
                Vector3 g = HandleUtility.WorldToGUIPointWithDepth(m_Icons[i].position);
                if (g.z > 0 && Math.Abs(point.x - g.x) <= kIconSize / 2 && Math.Abs(point.y - g.y) <= kIconSize / 2)
                    return owner;
            }
            return null;
        }

        /// <summary>The objects whose icon's middle is inside a GUI rectangle.</summary>
        internal List<GameObject> IconOwnersInInternal(Rect rect)
        {
            var list = new List<GameObject>();
            if (!drawGizmos)
                return list;
            for (int i = 0; i < Math.Min(m_Icons.Count, m_IconOwners.Count); i++)
            {
                Vector3 g = HandleUtility.WorldToGUIPointWithDepth(m_Icons[i].position);
                if (g.z > 0 && rect.Contains(new Vector2(g.x, g.y)) && m_IconOwners[i] != null && !list.Contains(m_IconOwners[i]))
                    list.Add(m_IconOwners[i]);
            }
            return list;
        }

        static bool IsChildOfSelection(GameObject go, HashSet<GameObject> selected)
        {
            for (Transform t = go.transform.parent; t != null; t = t.parent)
                if (selected.Contains(t.gameObject))
                    return true;
            return false;
        }

        static readonly Color kCameraGizmo = new Color(0.8f, 0.8f, 0.8f, 0.9f), kLightGizmo = new Color(1f, 0.95f, 0.55f, 0.8f);

        // The engine's components' own gizmos: cameras (an icon; the frustum when selected), lights (an icon; their
        // direction, range or cone when selected).
        void BuiltinGizmos(GameObject go, bool selected)
        {
            if (go.GetComponent<Camera>() is Camera cam && cam.cameraType != CameraType.SceneView)
            {
                Gizmos.DrawIcon(go.transform.position, "ComponentCamera");
                if (selected)
                {
                    Gizmos.matrix = Matrix4x4.TRS(go.transform.position, go.transform.rotation, Vector3.one);
                    Gizmos.color = kCameraGizmo;
                    if (cam.orthographic)
                    {
                        float h = cam.orthographicSize, w = h * cam.aspect, d = (cam.farClipPlane + cam.nearClipPlane) / 2;
                        Gizmos.DrawWireCube(new Vector3(0, 0, d), new Vector3(2 * w, 2 * h, cam.farClipPlane - cam.nearClipPlane));
                    }
                    else
                        Gizmos.DrawFrustum(Vector3.zero, cam.fieldOfView, cam.farClipPlane, cam.nearClipPlane, cam.aspect);
                    Gizmos.matrix = Matrix4x4.identity;
                }
            }
            if (go.GetComponent<Light>() is Light light)
            {
                Gizmos.DrawIcon(go.transform.position, "ComponentLight");
                if (!selected)
                    return;
                Gizmos.color = kLightGizmo;
                Vector3 p = go.transform.position, f = go.transform.forward;
                switch (light.type)
                {
                case LightType.Directional:
                {
                    // A ring of rays along the light's direction.
                    Vector3 r = go.transform.right, u = go.transform.up;
                    float s = HandleUtility.GetHandleSize(p) * 0.4f;
                    for (int i = 0; i < 8; i++)
                    {
                        double a = i * Math.PI / 4;
                        Vector3 o = p + (r * (float)Math.Cos(a) + u * (float)Math.Sin(a)) * s;
                        Gizmos.DrawLine(o, o + f * s * 4);
                    }
                    break;
                }
                case LightType.Point:
                    Gizmos.DrawWireSphere(p, light.range);
                    break;
                case LightType.Spot:
                {
                    float r = light.range, t = (float)Math.Tan(light.spotAngle * 0.5 * Math.PI / 180) * r;
                    Vector3 end = p + f * r, x = go.transform.right * t, y = go.transform.up * t;
                    Gizmos.matrix = Matrix4x4.identity;
                    for (int i = 0; i < 4; i++)
                        Gizmos.DrawLine(p, end + (i == 0 ? x : i == 1 ? -x : i == 2 ? y : -y));
                    Gizmos.matrix = Matrix4x4.TRS(end, go.transform.rotation, Vector3.one);
                    var circle = new Vector3[33];
                    for (int i = 0; i <= 32; i++)
                    {
                        double a = i * 2 * Math.PI / 32;
                        circle[i] = new Vector3((float)Math.Cos(a) * t, (float)Math.Sin(a) * t, 0);
                    }
                    Gizmos.DrawLineStrip(circle, false);
                    Gizmos.matrix = Matrix4x4.identity;
                    break;
                }
                }
            }
        }

        // --------------------------------------------------------------------------------------------- the GUI

        const float kIconSize = 28;

        void OnSceneGui()
        {
            if (m_Camera == null)
                return;
            SceneView outer = currentDrawingSceneView;
            currentDrawingSceneView = this;
            Event e = Event.current;
            bool repaint = e != null && e.type == EventType.Repaint;
            if (repaint)
                m_HandleBatch.Clear();
            HandleUtility.BeginInternal(this);
            try
            {
                beforeSceneGui?.Invoke(this);
                SceneGuiHooksInternal?.Invoke(this);
                duringSceneGui?.Invoke(this);
                if (repaint && drawGizmos)
                    DrawIcons();
            }
            catch (Exception ex) when (!(ex is ExitGuiException))
            {
                Debug.LogException(ex);
            }
            finally
            {
                HandleUtility.EndInternal();
                currentDrawingSceneView = outer;
            }
        }

        // What the editor's own scene GUI draws between beforeSceneGui and duringSceneGui (the tools' handles).
        internal static event Action<SceneView> SceneGuiHooksInternal;

        void DrawIcons()
        {
            foreach (GizmoRecorder.IconMark icon in m_Icons)
            {
                Vector3 g = HandleUtility.WorldToGUIPointWithDepth(icon.position);
                if (g.z <= 0)
                    continue;
                Texture2D tex = EditorGuiUtility.FindTexture(icon.name) ?? AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Gizmos/" + icon.name + ".png");
                if (tex == null)
                    continue;
                Color was = Gui.color;
                Gui.color = icon.tint;
                Gui.DrawTexture(new Rect(g.x - kIconSize / 2, g.y - kIconSize / 2, kIconSize, kIconSize), tex);
                Gui.color = was;
            }
        }
    }
}
