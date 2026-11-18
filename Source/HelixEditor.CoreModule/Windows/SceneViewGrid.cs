using System;
using System.Linq;
using HelixEngine;
using Object = HelixEngine.Object;

namespace HelixEditor
{
    // The Scene view's grid: a quad drawn for the view's camera only (Graphics.DrawMesh) with the editor's grid shader,
    // on the plane the view looks at most (XZ; XY or YZ for an orthographic view along Z or X), about the point under
    // the camera; its cells GridSettings.size, ten times larger per decade the view is zoomed out, faded with distance.
    public partial class SceneView
    {
        static Mesh s_GridQuad;
        Material m_GridMaterial;
        bool m_ShowGrid = true;

        /// <summary>Whether the view draws its grid.</summary>
        public bool showGrid
        {
            get => m_ShowGrid;
            set
            {
                m_ShowGrid = value;
                Repaint();
            }
        }

        // The grid as drawn this frame (for tests): its plane's normal axis, cell size and fade distance.
        internal Vector3 gridNormalInternal { get; private set; } = Vector3.up;
        internal float gridCellInternal { get; private set; } = 1;
        internal float gridFadeInternal { get; private set; }
        internal bool gridDrawnInternal { get; private set; }

        static Mesh GridQuad()
        {
            if (s_GridQuad != null)
                return s_GridQuad;
            s_GridQuad = new Mesh { name = "SceneGrid", hideFlags = HideFlags.HideAndDontSave };
            s_GridQuad.vertices = new[] { new Vector3(-1, 0, -1), new Vector3(1, 0, -1), new Vector3(1, 0, 1), new Vector3(-1, 0, 1) };
            s_GridQuad.triangles = new[] { 0, 2, 1, 0, 3, 2 };
            // Bounds that never cull it (the quad is scaled to the fade distance).
            s_GridQuad.bounds = new Bounds(Vector3.zero, new Vector3(2, 0.01f, 2));
            return s_GridQuad;
        }

        void DrawGrid()
        {
            gridDrawnInternal = false;
            if (!m_ShowGrid || m_Camera == null)
                return;
            if (m_GridMaterial == null)
            {
                Shader shader = Shader.Find("Hidden/Editor/SceneGrid");
                if (shader == null)
                    return;
                m_GridMaterial = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            }
            // The plane: XZ, else the one an orthographic view looks straight at.
            Vector3 forward = m_Rotation * Vector3.forward;
            Vector3 normal = Vector3.up, u = Vector3.right, v = Vector3.forward;
            if (m_Orthographic && Math.Abs(forward.z) > 0.99f)
            {
                normal = Vector3.forward;
                v = Vector3.up;
            }
            else if (m_Orthographic && Math.Abs(forward.x) > 0.99f)
            {
                normal = Vector3.right;
                u = Vector3.forward;
                v = Vector3.up;
            }
            // Cells a decade larger for each decade the view is farther (the grid's size the smallest).
            float baseCell = Math.Max(1e-3f, GridSettings.size.x);
            float reach = m_Orthographic ? m_Size : cameraDistance;
            float decades = (float)Math.Floor(Math.Log10(Math.Max(1, reach / (baseCell * 10))));
            float cell = baseCell * (float)Math.Pow(10, Math.Max(0, decades));
            float fade = Math.Max(cell * 50, reach * 12);
            // About the camera's point on the plane, snapped to the major cells so the lines do not swim.
            Vector3 eye = m_Camera.transform.position;
            float major = cell * 10;
            Vector3 onPlane = eye - normal * Vector3.Dot(eye, normal);
            Vector3 center = new Vector3((float)Math.Round(onPlane.x / major) * major, (float)Math.Round(onPlane.y / major) * major,
                                         (float)Math.Round(onPlane.z / major) * major);
            center -= normal * Vector3.Dot(center, normal);
            Quaternion plane = Quaternion.FromToRotation(Vector3.up, normal);
            Matrix4x4 m = Matrix4x4.TRS(center, plane, new Vector3(fade, 1, fade));
            m_GridMaterial.SetVector("_AxisU", new Vector4(u.x, u.y, u.z, 0));
            m_GridMaterial.SetVector("_AxisV", new Vector4(v.x, v.y, v.z, 0));
            m_GridMaterial.SetFloat("_CellSize", cell);
            m_GridMaterial.SetFloat("_FadeDistance", fade);
            Graphics.DrawMesh(GridQuad(), m, m_GridMaterial, 0, m_Camera);
            gridNormalInternal = normal;
            gridCellInternal = cell;
            gridFadeInternal = fade;
            gridDrawnInternal = true;
        }

        void ReleaseGrid()
        {
            if (m_GridMaterial != null)
                DestroyImmediate(m_GridMaterial);
            m_GridMaterial = null;
        }
    }

    /// <summary>The scene view's grid: its cell size (the snapping's grid), kept with the project's user settings.</summary>
    public static class GridSettings
    {
        static Vector3? s_Size;

        public static event Action<Vector3> sizeChanged;

        public static Vector3 size
        {
            get => s_Size ??= Prefs.GetVector("GridSettings.size", Vector3.one);
            set
            {
                value = new Vector3(Math.Max(1e-4f, value.x), Math.Max(1e-4f, value.y), Math.Max(1e-4f, value.z));
                if (s_Size == value)
                    return;
                s_Size = value;
                Prefs.SetVector("GridSettings.size", value);
                sizeChanged?.Invoke(value);
                SceneView.RepaintAll();
            }
        }

        public static void ResetGridSettings() => size = Vector3.one;
    }

    /// <summary>How the tools snap: the increments of moving, rotating and scaling (with Ctrl held, or always when
    /// snapEnabled), and moving to the grid (gridSnapEnabled); kept with the project's user settings.</summary>
    public static class EditorSnapSettings
    {
        public static Vector3 move
        {
            get => Prefs.GetVector("EditorSnapSettings.move", Vector3.one);
            set => Prefs.SetVector("EditorSnapSettings.move", value);
        }

        public static float rotate
        {
            get => UserState.GetFloat("EditorSnapSettings.rotate", 15);
            set => UserState.SetFloat("EditorSnapSettings.rotate", value);
        }

        public static float scale
        {
            get => UserState.GetFloat("EditorSnapSettings.scale", 1);
            set => UserState.SetFloat("EditorSnapSettings.scale", value);
        }

        public static bool gridSnapEnabled
        {
            get => UserState.GetBool("EditorSnapSettings.gridSnapEnabled", false);
            set => UserState.SetBool("EditorSnapSettings.gridSnapEnabled", value);
        }

        /// <summary>Snapping by the increments always (not only with Ctrl).</summary>
        public static bool snapEnabled
        {
            get => UserState.GetBool("EditorSnapSettings.snapEnabled", false);
            set => UserState.SetBool("EditorSnapSettings.snapEnabled", value);
        }

        /// <summary>Whether the increments apply now: always, or while Ctrl is held.</summary>
        public static bool incrementalSnapActive => snapEnabled || (Event.current != null && (Event.current.control || Event.current.command)) || ctrlHeldInternal;

        // Ctrl as the retained views saw it last (the scene view's tools read it outside OnGui).
        internal static bool ctrlHeldInternal { get; set; }

        public static void ResetSnapSettings()
        {
            move = Vector3.one;
            rotate = 15;
            scale = 1;
            gridSnapEnabled = false;
            snapEnabled = false;
        }
    }

    public static partial class Handles
    {
        /// <summary>The value at the nearest multiple of snap (snap 0: as it is).</summary>
        public static float SnapValue(float value, float snap) => snap > 0 ? (float)Math.Round(value / snap) * snap : value;

        public static Vector3 SnapValue(Vector3 value, Vector3 snap) =>
            new Vector3(SnapValue(value.x, snap.x), SnapValue(value.y, snap.y), SnapValue(value.z, snap.z));

        /// <summary>Each axis to the grid's cells (GridSettings.size).</summary>
        public static Vector3 SnapToGrid(Vector3 position) => SnapValue(position, GridSettings.size);
    }

    // Vectors in the project's user state ("x,y,z").
    static class Prefs
    {
        internal static Vector3 GetVector(string key, Vector3 fallback)
        {
            string[] p = UserState.GetString(key, "").Split(',');
            if (p.Length == 3 && float.TryParse(p[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float x) &&
                float.TryParse(p[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float y) &&
                float.TryParse(p[2], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float z))
                return new Vector3(x, y, z);
            return fallback;
        }

        internal static void SetVector(string key, Vector3 v) =>
            UserState.SetString(key, string.Join(",", new[] { v.x, v.y, v.z }.Select(f => f.ToString("R", System.Globalization.CultureInfo.InvariantCulture))));
    }
}
