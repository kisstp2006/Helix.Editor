using System;
using System.Collections;
using System.Globalization;
using System.Linq;
using HelixEngine;
using HelixEngine.UI;
using Object = HelixEngine.Object;

namespace HelixEditor
{
    /// <summary>The Scene window: the open scenes seen by a hidden camera of its own (a scene view camera) drawn into a
    /// render texture its viewport shows, on the active pipeline. Its view is a pivot, a rotation and a size (the half
    /// extent it shows); perspective or orthographic, 2D (looking down the z axis). The view's state is kept in the
    /// project's user settings (UserSettings/EditorState.txt).</summary>
    public partial class SceneView : EditorWindow
    {
        /// <summary>The scene view camera's settings: fly speed, easing, acceleration, field of view and clip planes.</summary>
        [Serializable]
        public class CameraSettings
        {
            float m_Speed = 1, m_SpeedMin = 0.01f, m_SpeedMax = 2;

            public float speed { get => m_Speed; set => m_Speed = Math.Clamp(value, m_SpeedMin, m_SpeedMax); }
            public float speedNormalized
            {
                get => (m_Speed - m_SpeedMin) / Math.Max(1e-6f, m_SpeedMax - m_SpeedMin);
                set => speed = m_SpeedMin + Math.Clamp(value, 0, 1) * (m_SpeedMax - m_SpeedMin);
            }
            public float speedMin { get => m_SpeedMin; set => m_SpeedMin = Math.Max(1e-4f, value); }
            public float speedMax { get => m_SpeedMax; set => m_SpeedMax = Math.Max(m_SpeedMin, value); }
            public bool easingEnabled { get; set; } = true;
            public float easingDuration { get; set; } = 0.4f;
            public bool accelerationEnabled { get; set; } = true;
            public float fieldOfView { get; set; } = 60;
            public float nearClip { get; set; } = 0.03f;
            public float farClip { get; set; } = 10000;
            public bool dynamicClip { get; set; } = true;
            public bool occlusionCulling { get; set; }
        }

        /// <summary>What the view shows besides the scenes' objects.</summary>
        [Serializable]
        public class SceneViewState
        {
            public bool showFog = true, showSkybox = true, showFlares = true, showImageEffects = true, alwaysRefresh, fxEnabled = true;
        }

        /// <summary>A draw mode of the view.</summary>
        public struct CameraMode
        {
            public DrawCameraMode drawMode;
            public string name;
            public string section;
        }

        static readonly ArrayList s_SceneViews = new();
        static SceneView s_LastActive;

        public static SceneView lastActiveSceneView => s_LastActive != null ? s_LastActive : s_SceneViews.Count > 0 ? (SceneView)s_SceneViews[0] : null;
        public static ArrayList sceneViews => s_SceneViews;
        public static SceneView currentDrawingSceneView { get; internal set; }
        public static event Action<SceneView> duringSceneGui;
        public static event Action<SceneView> beforeSceneGui;

        Camera m_Camera;
        RenderTexture m_Target;
        Element m_Viewport;
        Vector3 m_Pivot;
        Quaternion m_Rotation = Quaternion.LookRotation(new Vector3(-1, -0.7f, -1));
        float m_Size = 10;
        bool m_Orthographic, m_2DMode, m_Loaded;
        int m_Index;

        public Camera camera => m_Camera;
        public CameraSettings cameraSettings { get; set; } = new CameraSettings();
        public SceneViewState sceneViewState { get; } = new SceneViewState();
        public CameraMode cameraMode { get; set; } = new CameraMode { drawMode = DrawCameraMode.Textured, name = "Shaded", section = "Shading Mode" };
        public bool drawGizmos { get; set; } = true;
        public bool sceneLighting { get; set; } = true;
        public bool isRotationLocked { get; set; }

        public Vector3 pivot
        {
            get => m_Pivot;
            set
            {
                m_Pivot = value;
                Changed();
            }
        }

        public Quaternion rotation
        {
            get => m_Rotation;
            set
            {
                m_Rotation = value;
                Changed();
            }
        }

        /// <summary>The half extent the view shows about its pivot.</summary>
        public float size
        {
            get => m_Size;
            set
            {
                m_Size = Math.Clamp(Math.Abs(value), 1e-4f, 1e6f);
                Changed();
            }
        }

        public bool orthographic
        {
            get => m_Orthographic;
            set
            {
                m_Orthographic = value;
                Changed();
            }
        }

        /// <summary>Orthographic, looking down the z axis, its rotation locked.</summary>
        public bool in2DMode
        {
            get => m_2DMode;
            set
            {
                if (m_2DMode == value)
                    return;
                m_2DMode = value;
                if (value)
                {
                    m_Rotation = Quaternion.identity;
                    m_Orthographic = true;
                }
                else
                {
                    m_Rotation = Quaternion.LookRotation(new Vector3(-1, -0.7f, -1));
                    m_Orthographic = false;
                }
                Changed();
            }
        }

        /// <summary>The camera's distance from the pivot: the size seen through the field of view.</summary>
        public float cameraDistance
        {
            get
            {
                float fov = m_Orthographic ? 90 : cameraSettings.fieldOfView;
                return m_Size / (float)Math.Tan(fov * 0.5 * Math.PI / 180);
            }
        }

        public Element viewportInternal => m_Viewport;
        public RenderTexture targetTextureInternal => m_Target;

        void OnEnable()
        {
            titleContent = new GuiContent("Scene");
            WindowMarkup.Load(this, "UI/Windows/SceneView.hxml");
            m_Viewport = rootElement.Find("scene-viewport") ?? rootElement;
            m_Viewport.RegisterCallback<PointerDownEvent>(e => s_LastActive = this, TrickleDown.TrickleDown);
            m_Index = 0;
            while (IndexTaken(m_Index))
                m_Index++;
            s_SceneViews.Add(this);
            Load();
            InitNavigation();
            InitDrawing();
            InitOutline();
            m_Overlays = Overlays.OverlayCanvas.Attach(this, rootElement.Find("overlay-top"), m_Viewport);
        }

        Overlays.OverlayCanvas m_Overlays;
        internal Overlays.OverlayCanvas overlayCanvasInternal => m_Overlays;

        bool IndexTaken(int index)
        {
            foreach (SceneView v in s_SceneViews)
                if (v != this && v.m_Index == index)
                    return true;
            return false;
        }

        void OnDisable()
        {
            ReleaseGrid();
            ReleaseDrawing();
            ReleaseOutline();
            m_Overlays?.Detach();
            m_Overlays = null;
            s_SceneViews.Remove(this);
            if (s_LastActive == this)
                s_LastActive = null;
            if (m_Camera != null)
                DestroyImmediate(m_Camera.gameObject);
            m_Camera = null;
            if (m_Target != null)
            {
                m_Target.Release();
                DestroyImmediate(m_Target);
            }
            m_Target = null;
        }

        [MenuItem("Window/General/Scene %1", false, 21)] static void Open() => GetWindow<SceneView>();

        // ---------------------------------------------------------------------------------------------- the camera

        // The camera brought up to date now (for tests; the window's Update does it each frame).
        public void UpdateInternal() => Update();

        void Update()
        {
            TickNavigation();
            if (m_Viewport == null)
                return;
            bool shown = m_Viewport.panel != null && m_Viewport.layout.width >= 1 && m_Viewport.layout.height >= 1;
            if (!shown)
            {
                if (m_Camera != null)
                    m_Camera.enabled = false;
                return;
            }
            EnsureCamera();
            int w = Math.Max(1, (int)Math.Round(m_Viewport.layout.width)), h = Math.Max(1, (int)Math.Round(m_Viewport.layout.height));
            if (m_Target == null || m_Target.width != w || m_Target.height != h)
            {
                if (m_Target != null)
                {
                    m_Camera.targetTexture = null;
                    m_Target.Release();
                    DestroyImmediate(m_Target);
                }
                m_Target = new RenderTexture(w, h, 24) { name = "SceneView", hideFlags = HideFlags.HideAndDontSave };
                m_Target.Create();
                m_Camera.targetTexture = m_Target;
                m_Viewport.style.backgroundImage = Background.FromRenderTexture(m_Target);
            }
            // A camera made anew (the window enabled again) draws into the texture it has.
            if (m_Camera.targetTexture != m_Target)
                m_Camera.targetTexture = m_Target;
            m_Camera.enabled = true;
            Place();
            UpdateOutline(w, h);
            DrawGrid();
            DrawGizmosAndHandles();
        }

        void EnsureCamera()
        {
            if (m_Camera != null)
                return;
            var go = new GameObject("SceneCamera") { hideFlags = HideFlags.HideAndDontSave };
            // With the shell's objects, out of the scenes the user edits.
            DontDestroyOnLoad(go);
            m_Camera = go.AddComponent<Camera>();
            m_Camera.cameraType = CameraType.SceneView;
            m_Camera.clearFlags = CameraClearFlags.SolidColor;
            m_Camera.backgroundColor = new Color(0.19f, 0.2f, 0.22f, 1);
            // Before the editor's own camera (the shell draws over every display after).
            m_Camera.depth = -100;
        }

        // The camera at the view's place.
        void Place()
        {
            Transform t = m_Camera.transform;
            float distance = cameraDistance;
            t.localRotation = m_Rotation;
            t.localPosition = m_Pivot - m_Rotation * Vector3.forward * distance;
            m_Camera.orthographic = m_Orthographic;
            m_Camera.orthographicSize = m_Size;
            m_Camera.fieldOfView = cameraSettings.fieldOfView;
            if (cameraSettings.dynamicClip)
            {
                // Clip planes that keep depth precision about the pivot at any size.
                m_Camera.nearClipPlane = Math.Max(1e-4f, distance * 0.002f);
                m_Camera.farClipPlane = Math.Max(distance * 2000, 1000);
            }
            else
            {
                m_Camera.nearClipPlane = cameraSettings.nearClip;
                m_Camera.farClipPlane = cameraSettings.farClip;
            }
        }

        void Changed()
        {
            if (m_Camera != null)
                Place();
            // Kept a moment after the view stops changing (not each frame of a drag).
            m_SaveAt = Time.realtimeSinceStartup + 0.5f;
            m_Orientation?.MarkDirtyRepaint();
            Repaint();
        }

        // ------------------------------------------------------------------------------------------- framing

        /// <summary>The bounds filling the view (false for empty bounds).</summary>
        public bool Frame(Bounds bounds, bool instant = true)
        {
            float radius = bounds.extents.magnitude;
            if (float.IsInfinity(radius) || float.IsNaN(radius))
                return false;
            LookAt(bounds.center, m_Rotation, Math.Max(radius, 0.5f), m_Orthographic, instant);
            return true;
        }

        public bool FrameSelected() => FrameSelected(false, false);
        public bool FrameSelected(bool lockView) => FrameSelected(lockView, false);

        /// <summary>The selected objects' bounds (their renderers', else their positions) filling the view.</summary>
        public bool FrameSelected(bool lockView, bool instant)
        {
            Transform[] selected = Selection.transforms;
            if (selected.Length == 0)
                return false;
            Bounds b = default;
            bool any = false;
            foreach (Transform t in selected)
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
            return Frame(b, instant);
        }

        public static bool FrameLastActiveSceneView() => lastActiveSceneView != null && lastActiveSceneView.FrameSelected();

        public void LookAt(Vector3 point) => LookAt(point, m_Rotation, m_Size, m_Orthographic, false);
        public void LookAt(Vector3 point, Quaternion direction) => LookAt(point, direction, m_Size, m_Orthographic, false);
        public void LookAt(Vector3 point, Quaternion direction, float newSize) => LookAt(point, direction, newSize, m_Orthographic, false);
        public void LookAt(Vector3 point, Quaternion direction, float newSize, bool ortho) => LookAt(point, direction, newSize, ortho, false);

        /// <summary>The view about a point (eased with the camera settings' easing; at once when instant).</summary>
        public void LookAt(Vector3 point, Quaternion direction, float newSize, bool ortho, bool instant)
        {
            Quaternion rotation = !isRotationLocked && !m_2DMode ? direction : m_Rotation;
            float size = Math.Clamp(Math.Abs(newSize), 1e-4f, 1e6f);
            bool orthographic = ortho || m_2DMode;
            // Eased with the camera settings' easing (at once when instant, or without easing).
            if (!instant && cameraSettings.easingEnabled && cameraSettings.easingDuration > 0)
            {
                BeginEase(point, rotation, size, orthographic);
                return;
            }
            m_Ease = null;
            m_Pivot = point;
            m_Rotation = rotation;
            m_Size = size;
            m_Orthographic = orthographic;
            Changed();
        }

        public void LookAtDirect(Vector3 point, Quaternion direction) => LookAt(point, direction, m_Size, m_Orthographic, true);
        public void LookAtDirect(Vector3 point, Quaternion direction, float newSize) => LookAt(point, direction, newSize, m_Orthographic, true);

        /// <summary>The view where the object is, looking as it does.</summary>
        public void AlignViewToObject(Transform t)
        {
            if (t == null)
                return;
            LookAt(t.position + t.rotation * Vector3.forward * cameraDistance, t.rotation, m_Size, m_Orthographic, true);
        }

        /// <summary>The selected objects placed and turned as the view's camera.</summary>
        public void AlignWithView()
        {
            if (m_Camera == null)
                return;
            foreach (Transform t in Selection.transforms)
            {
                t.position = m_Camera.transform.position;
                t.rotation = m_Camera.transform.rotation;
                SceneManagement.EditorSceneManager.MarkSceneDirty(t.gameObject.scene);
            }
        }

        /// <summary>The selected objects (or the target) moved to the view's pivot.</summary>
        public void MoveToView() => MoveToView(null);

        public void MoveToView(Transform target)
        {
            if (target != null)
            {
                target.position = m_Pivot;
                SceneManagement.EditorSceneManager.MarkSceneDirty(target.gameObject.scene);
                return;
            }
            foreach (Transform t in Selection.transforms)
            {
                t.position = m_Pivot;
                SceneManagement.EditorSceneManager.MarkSceneDirty(t.gameObject.scene);
            }
        }

        public static void RepaintAll()
        {
            foreach (SceneView v in s_SceneViews)
                v.Repaint();
        }

        // ------------------------------------------------------------------------------------- kept state

        string Key(string name) => $"SceneView.{m_Index}.{name}";

        void Save()
        {
            if (!m_Loaded)
                return;
            UserState.SetString(Key("view"), string.Join(",", new[]
            {
                m_Pivot.x, m_Pivot.y, m_Pivot.z, m_Rotation.x, m_Rotation.y, m_Rotation.z, m_Rotation.w, m_Size,
                m_Orthographic ? 1 : 0, m_2DMode ? 1 : 0,
            }.Select(f => f.ToString("R", CultureInfo.InvariantCulture))));
        }

        void Load()
        {
            string text = UserState.GetString(Key("view"), "");
            string[] parts = text.Split(',');
            if (parts.Length == 10)
            {
                var v = new float[10];
                bool ok = true;
                for (int i = 0; i < 10 && ok; i++)
                    ok = float.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out v[i]);
                if (ok)
                {
                    m_Pivot = new Vector3(v[0], v[1], v[2]);
                    m_Rotation = new Quaternion(v[3], v[4], v[5], v[6]);
                    m_Size = Math.Clamp(v[7], 1e-4f, 1e6f);
                    m_Orthographic = v[8] != 0;
                    m_2DMode = v[9] != 0;
                }
            }
            m_Loaded = true;
        }

        internal static void RaiseBeforeSceneGuiInternal(SceneView v) => beforeSceneGui?.Invoke(v);
        internal static void RaiseDuringSceneGuiInternal(SceneView v) => duringSceneGui?.Invoke(v);
    }

    /// <summary>A draw mode of the scene view.</summary>
    public enum DrawCameraMode
    {
        Normal = -1,
        Textured = 0,
        Wireframe = 1,
        TexturedWire = 2,
    }
}
