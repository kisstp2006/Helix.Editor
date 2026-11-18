using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using HelixEngine;
using HelixEngine.UI;

namespace HelixEditor
{
    /// <summary>The Game view: the game's cameras, canvases and screen panels of a display (Display 1 to 8) drawn into
    /// its texture at a size (Free Aspect, an aspect, a fixed resolution) and shown at a scale (fit at first, up to 5x;
    /// the wheel zooms about the pointer, the middle button pans); the stats of that drawing; what entering the play
    /// mode does to it (focus it, maximize it, nothing).</summary>
    public sealed class GameView : EditorWindow
    {
        internal enum SizeKind
        {
            Free,
            Aspect,
            Fixed,
        }

        internal sealed class Size
        {
            public string name;
            public SizeKind kind;
            public int width, height;
            public bool custom;
        }

        internal enum PlayMode
        {
            Focused,
            Maximized,
            Unfocused,
        }

        const float kMaxScale = 5;
        const string kDisplay = "GameView.display", kSize = "GameView.size", kStats = "GameView.stats", kPlayMode = "GameView.playMode",
                     kCustom = "GameView.custom";

        static readonly List<Size> s_Sizes = new();
        static bool s_SizesRead;
        static GameView s_Main;

        Element m_Viewport, m_Image, m_StatsPanel;
        Label m_Message, m_StatsText, m_ScaleValue;
        Slider m_ScaleSlider;
        Button m_DisplayButton, m_SizeButton, m_PlayModeButton, m_StatsButton;
        RenderTexture m_Target;
        int m_Display, m_SetDisplay = -1;
        int m_SizeIndex;
        float m_Scale = 1, m_MinScale = 1;
        bool m_ScaleChosen;
        Vector2 m_Pan;
        bool m_Panning;
        int m_PanPointer = -1;
        bool m_MaximizedOnPlay;
        float m_Fps;

        // ------------------------------------------------------------------------------------------- the sizes

        internal static IReadOnlyList<Size> sizesInternal
        {
            get
            {
                ReadSizes();
                return s_Sizes;
            }
        }

        static void ReadSizes()
        {
            if (s_SizesRead || string.IsNullOrEmpty(EditorApplication.editorDataPathInternal))
                return;
            s_SizesRead = true;
            try
            {
                string file = System.IO.Path.Combine(EditorApplication.editorDataPathInternal, "Settings/GameViewSizes.json");
                using JsonDocument doc = JsonDocument.Parse(System.IO.File.ReadAllText(file));
                foreach (JsonElement e in doc.RootElement.GetProperty("sizes").EnumerateArray())
                {
                    var s = new Size { name = e.GetProperty("name").GetString() ?? "" };
                    s.kind = e.GetProperty("kind").GetString() switch
                    {
                        "aspect" => SizeKind.Aspect,
                        "fixed" => SizeKind.Fixed,
                        _ => SizeKind.Free,
                    };
                    if (e.TryGetProperty("width", out JsonElement w))
                        s.width = w.GetInt32();
                    if (e.TryGetProperty("height", out JsonElement h))
                        s.height = h.GetInt32();
                    s_Sizes.Add(s);
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"Settings/GameViewSizes.json could not be read: {ex.Message}");
            }
            if (s_Sizes.Count == 0)
                s_Sizes.Add(new Size { name = "Free Aspect", kind = SizeKind.Free });
            // The project's custom ones: "name|width|height;...".
            foreach (string entry in UserState.GetString(kCustom, "").Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                string[] p = entry.Split('|');
                if (p.Length == 3 && int.TryParse(p[1], out int cw) && int.TryParse(p[2], out int ch))
                    s_Sizes.Add(new Size { name = Uri.UnescapeDataString(p[0]), kind = SizeKind.Fixed, width = cw, height = ch, custom = true });
            }
        }

        static void SaveCustom()
        {
            var parts = new List<string>();
            foreach (Size s in s_Sizes)
                if (s.custom)
                    parts.Add($"{Uri.EscapeDataString(s.name)}|{s.width}|{s.height}");
            UserState.SetString(kCustom, string.Join(";", parts));
        }

        /// <summary>A custom fixed size added (or the one of that name changed) and chosen in the Game view.</summary>
        internal static void SetCustomInternal(int width, int height, string baseName)
        {
            ReadSizes();
            string name = string.IsNullOrEmpty(baseName) ? $"{width}x{height}" : baseName;
            int index = s_Sizes.FindIndex(s => s.custom && s.name == name);
            if (index < 0)
            {
                s_Sizes.Add(new Size { name = name, kind = SizeKind.Fixed, width = width, height = height, custom = true });
                index = s_Sizes.Count - 1;
            }
            else
            {
                s_Sizes[index].width = width;
                s_Sizes[index].height = height;
            }
            SaveCustom();
            GameView v = s_Main != null ? s_Main : GetWindow<GameView>();
            v.ChooseSize(index);
        }

        // ------------------------------------------------------------------------------------------- the window

        void OnEnable()
        {
            titleContent = new GuiContent("Game");
            WindowMarkup.Load(this, "UI/Windows/GameView.hxml");
            ReadSizes();
            m_Viewport = rootElement.Find("game-viewport") ?? rootElement;
            m_Image = rootElement.Find("game-image");
            m_Message = rootElement.Find<Label>("game-message");
            m_StatsPanel = rootElement.Find("game-stats-panel");
            m_StatsText = rootElement.Find<Label>("game-stats-text");
            m_ScaleValue = rootElement.Find<Label>("game-scale-value");
            m_ScaleSlider = rootElement.Find<Slider>("game-scale");
            m_DisplayButton = rootElement.Find<Button>("game-display");
            m_SizeButton = rootElement.Find<Button>("game-size");
            m_PlayModeButton = rootElement.Find<Button>("game-play-mode");
            m_StatsButton = rootElement.Find<Button>("game-stats");
            m_Display = Math.Clamp((int)UserState.GetFloat(kDisplay, 0), 0, 7);
            m_SizeIndex = Math.Clamp((int)UserState.GetFloat(kSize, 0), 0, Math.Max(0, s_Sizes.Count - 1));
            if (m_DisplayButton != null)
                m_DisplayButton.clicked += () => OpenDisplayMenu(m_DisplayButton.worldBound);
            if (m_SizeButton != null)
                m_SizeButton.clicked += () => OpenSizeMenu(m_SizeButton.worldBound);
            if (m_PlayModeButton != null)
                m_PlayModeButton.clicked += () => OpenPlayModeMenu(m_PlayModeButton.worldBound);
            if (m_StatsButton != null)
                m_StatsButton.clicked += () => showStats = !showStats;
            if (rootElement.Find<Button>("game-gizmos") is Button gizmos)
                gizmos.clicked += () =>
                {
                    var menu = new GenericDropdownMenu();
                    menu.AddDisabledItem("Show Gizmos (later)", false);
                    menu.DropDown(gizmos.worldBound, gizmos, DropdownMenuSizeMode.Auto);
                };
            if (m_ScaleSlider != null)
            {
                m_ScaleSlider.lowValue = 0;
                m_ScaleSlider.highValue = 1;
                m_ScaleSlider.RegisterValueChangedCallback(e =>
                {
                    // The slider's 0..1 over the scales from the fitting one to 5x, logarithmic.
                    SetScale(ScaleOfSlider(e.newValue), null);
                });
            }
            m_Viewport.RegisterCallback<WheelEvent>(OnWheel);
            m_Viewport.RegisterCallback<PointerDownEvent>(OnPointerDown);
            m_Viewport.RegisterCallback<PointerMoveEvent>(OnPointerMove);
            m_Viewport.RegisterCallback<PointerUpEvent>(OnPointerUp);
            EditorApplication.playModeStateChanged += OnPlayMode;
            s_Main = this;
            ShowLabels();
        }

        void OnDisable()
        {
            EditorApplication.playModeStateChanged -= OnPlayMode;
            if (m_SetDisplay >= 0)
                GameViewNative.SetDisplayTexture(m_SetDisplay, null);
            m_SetDisplay = -1;
            if (m_Target != null)
            {
                m_Target.Release();
                DestroyImmediate(m_Target);
            }
            m_Target = null;
            if (s_Main == this)
                s_Main = null;
        }

        [MenuItem("Window/General/Game %2", false, 22)] static void Open() => GetWindow<GameView>();

        internal static GameView mainInternal => s_Main;
        internal RenderTexture targetInternal => m_Target;
        internal Element imageInternal => m_Image;
        internal Element viewportInternal => m_Viewport;
        internal float scaleInternal => m_Scale;
        internal float minScaleInternal => m_MinScale;
        internal Vector2 panInternal => m_Pan;
        internal string messageInternal => m_Message != null && m_Message.style.display != DisplayStyle.None ? m_Message.text : "";
        internal string statsTextInternal => m_StatsText?.text ?? "";

        /// <summary>The display shown (0: Display 1).</summary>
        internal int displayInternal
        {
            get => m_Display;
            set
            {
                m_Display = Math.Clamp(value, 0, 7);
                UserState.SetFloat(kDisplay, m_Display);
                ShowLabels();
            }
        }

        internal int sizeIndexInternal => m_SizeIndex;

        internal bool showStats
        {
            get => UserState.GetBool(kStats, false);
            set
            {
                UserState.SetBool(kStats, value);
                ShowLabels();
            }
        }

        internal PlayMode playModeInternal
        {
            get => (PlayMode)(int)UserState.GetFloat(kPlayMode, (int)PlayMode.Focused);
            set
            {
                UserState.SetFloat(kPlayMode, (int)value);
                ShowLabels();
            }
        }

        internal void ChooseSize(int index)
        {
            m_SizeIndex = Math.Clamp(index, 0, s_Sizes.Count - 1);
            UserState.SetFloat(kSize, m_SizeIndex);
            m_ScaleChosen = false;
            m_Pan = Vector2.zero;
            ShowLabels();
            UpdateInternal();
        }

        void ShowLabels()
        {
            if (FirstLabel(m_DisplayButton) is Label d)
                d.text = $"Display {m_Display + 1}";
            if (FirstLabel(m_SizeButton) is Label s && s_Sizes.Count > 0)
                s.text = s_Sizes[Math.Clamp(m_SizeIndex, 0, s_Sizes.Count - 1)].name;
            if (FirstLabel(m_PlayModeButton) is Label p)
                p.text = PlayModeName(playModeInternal);
            m_StatsButton?.EnableInClassList("on", showStats);
            if (m_StatsPanel != null)
                m_StatsPanel.style.display = showStats ? DisplayStyle.Flex : DisplayStyle.None;
        }

        static Label FirstLabel(Element e)
        {
            if (e == null)
                return null;
            foreach (Element c in e.Children())
                if (c is Label l)
                    return l;
            return null;
        }

        static string PlayModeName(PlayMode m) => m switch
        {
            PlayMode.Maximized => "Play Maximized",
            PlayMode.Unfocused => "Play Unfocused",
            _ => "Play Focused",
        };

        // ------------------------------------------------------------------------------------------- the menus

        void OpenDisplayMenu(Rect at)
        {
            var menu = new GenericDropdownMenu();
            for (int i = 0; i < 8; i++)
            {
                int d = i;
                menu.AddItem($"Display {i + 1}", m_Display == i, () => displayInternal = d);
            }
            menu.DropDown(at, m_DisplayButton, DropdownMenuSizeMode.Auto);
        }

        void OpenSizeMenu(Rect at)
        {
            var menu = new GenericDropdownMenu();
            for (int i = 0; i < s_Sizes.Count; i++)
            {
                int index = i;
                Size s = s_Sizes[i];
                string label = s.kind == SizeKind.Aspect ? s.name : s.kind == SizeKind.Fixed && !s.name.Contains('x') ? $"{s.name} ({s.width}x{s.height})" : s.name;
                menu.AddItem(label, m_SizeIndex == i, () => ChooseSize(index));
            }
            menu.DropDown(at, m_SizeButton, DropdownMenuSizeMode.Auto);
        }

        void OpenPlayModeMenu(Rect at)
        {
            var menu = new GenericDropdownMenu();
            foreach (PlayMode m in new[] { PlayMode.Focused, PlayMode.Maximized, PlayMode.Unfocused })
            {
                PlayMode x = m;
                menu.AddItem(PlayModeName(m), playModeInternal == m, () => playModeInternal = x);
            }
            menu.DropDown(at, m_PlayModeButton, DropdownMenuSizeMode.Auto);
        }

        // The play mode: focused or maximized as chosen while it lasts.
        void OnPlayMode(PlayModeStateChange change)
        {
            if (change == PlayModeStateChange.EnteredPlayMode)
            {
                PlayMode m = playModeInternal;
                if (m == PlayMode.Maximized && !maximized)
                {
                    maximized = true;
                    m_MaximizedOnPlay = true;
                }
                if (m != PlayMode.Unfocused)
                    Focus();
            }
            else if (change == PlayModeStateChange.ExitingPlayMode || change == PlayModeStateChange.EnteredEditMode)
            {
                if (m_MaximizedOnPlay && maximized)
                    maximized = false;
                m_MaximizedOnPlay = false;
            }
        }

        // ------------------------------------------------------------------------------------------- each frame

        // The view brought up to date now (for tests; the window's Update does it each frame).
        internal void UpdateInternal() => Update();

        void Update()
        {
            if (m_Viewport == null)
                return;
            bool shown = m_Viewport.panel != null && m_Viewport.layout.width >= 1 && m_Viewport.layout.height >= 1;
            if (!shown)
            {
                if (m_SetDisplay >= 0)
                    GameViewNative.SetDisplayTexture(m_SetDisplay, null);
                m_SetDisplay = -1;
                return;
            }
            Vector2 view = new Vector2(m_Viewport.layout.width, m_Viewport.layout.height);
            Vector2Int res = Resolution(view);
            if (m_Target == null || m_Target.width != res.x || m_Target.height != res.y)
            {
                if (m_Target != null)
                {
                    GameViewNative.SetDisplayTexture(m_Display, null);
                    m_Target.Release();
                    DestroyImmediate(m_Target);
                }
                m_Target = new RenderTexture(res.x, res.y, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear)
                {
                    name = "GameView",
                    hideFlags = HideFlags.HideAndDontSave,
                };
                m_Target.Create();
                if (m_Image != null)
                    m_Image.style.backgroundImage = Background.FromRenderTexture(m_Target);
            }
            if (m_SetDisplay != m_Display)
            {
                if (m_SetDisplay >= 0)
                    GameViewNative.SetDisplayTexture(m_SetDisplay, null);
                m_SetDisplay = m_Display;
            }
            GameViewNative.SetDisplayTexture(m_Display, m_Target);
            Place(view, res);
            bool none = GameViewNative.CameraCount(m_Display) == 0;
            if (m_Message != null)
            {
                m_Message.style.display = none ? DisplayStyle.Flex : DisplayStyle.None;
                m_Message.text = $"Display {m_Display + 1}\nNo cameras rendering";
            }
            if (showStats)
                UpdateStats(res);
            Repaint();
        }

        /// <summary>The texture's size for a view of a size: the view's (Free Aspect), the largest of the aspect in it,
        /// or the fixed resolution.</summary>
        internal Vector2Int Resolution(Vector2 view)
        {
            Size s = s_Sizes.Count > 0 ? s_Sizes[Math.Clamp(m_SizeIndex, 0, s_Sizes.Count - 1)] : new Size();
            switch (s.kind)
            {
            case SizeKind.Aspect when s.width > 0 && s.height > 0:
                float aspect = (float)s.width / s.height;
                float w = view.x, h = view.x / aspect;
                if (h > view.y)
                {
                    h = view.y;
                    w = view.y * aspect;
                }
                return new Vector2Int(Math.Max(1, (int)Math.Floor(w)), Math.Max(1, (int)Math.Floor(h)));
            case SizeKind.Fixed when s.width > 0 && s.height > 0:
                return new Vector2Int(s.width, s.height);
            default:
                return new Vector2Int(Math.Max(1, (int)Math.Round(view.x)), Math.Max(1, (int)Math.Round(view.y)));
            }
        }

        // The image's place: its size at the scale, in the middle, moved by the pan (kept over the view).
        void Place(Vector2 view, Vector2Int res)
        {
            m_MinScale = Math.Min(1, Math.Min(view.x / res.x, view.y / res.y));
            if (!m_ScaleChosen)
                m_Scale = m_MinScale;
            m_Scale = Math.Clamp(m_Scale, m_MinScale, kMaxScale);
            Vector2 size = new Vector2(res.x * m_Scale, res.y * m_Scale);
            ClampPan(view, size);
            if (m_Image != null)
            {
                m_Image.style.left = (view.x - size.x) / 2 + m_Pan.x;
                m_Image.style.top = (view.y - size.y) / 2 + m_Pan.y;
                m_Image.style.width = size.x;
                m_Image.style.height = size.y;
            }
            m_ScaleSlider?.SetValueWithoutNotify(SliderOfScale(m_Scale));
            if (m_ScaleValue != null)
                m_ScaleValue.text = m_Scale.ToString(m_Scale < 10 ? "0.##" : "0", CultureInfo.InvariantCulture) + "x";
        }

        // A larger image than the view moves at most to its edges; a smaller one stays in the middle.
        void ClampPan(Vector2 view, Vector2 size)
        {
            float mx = Math.Max(0, (size.x - view.x) / 2), my = Math.Max(0, (size.y - view.y) / 2);
            m_Pan = new Vector2(Math.Clamp(m_Pan.x, -mx, mx), Math.Clamp(m_Pan.y, -my, my));
        }

        float ScaleOfSlider(float t) => (float)(m_MinScale * Math.Pow(kMaxScale / m_MinScale, Math.Clamp(t, 0, 1)));

        float SliderOfScale(float s) =>
            kMaxScale / m_MinScale <= 1 ? 0 : (float)(Math.Log(s / m_MinScale) / Math.Log(kMaxScale / m_MinScale));

        /// <summary>The scale set, the point (a viewport point) staying where it is (null: the middle).</summary>
        internal void SetScale(float scale, Vector2? about)
        {
            if (m_Viewport == null || m_Target == null)
                return;
            Vector2 view = new Vector2(m_Viewport.layout.width, m_Viewport.layout.height);
            float old = m_Scale;
            m_Scale = Math.Clamp(scale, m_MinScale, kMaxScale);
            m_ScaleChosen = true;
            if (about is Vector2 p && old > 0)
            {
                // The image point under p before, under p after: the pan moves by (p - middle - pan) times the change.
                Vector2 fromMiddle = p - view / 2 - m_Pan;
                m_Pan -= fromMiddle * (m_Scale / old - 1);
            }
            Place(view, new Vector2Int(m_Target.width, m_Target.height));
        }

        void OnWheel(WheelEvent e)
        {
            float factor = e.delta.y < 0 ? 1.1f : 1 / 1.1f;
            Vector2 local = m_Viewport.WorldToLocal(new Vector2(e.mousePosition.x, e.mousePosition.y));
            SetScale(m_Scale * factor, local);
            e.StopPropagation();
        }

        void OnPointerDown(PointerDownEvent e)
        {
            if (e.button != 2)
                return;
            m_Panning = true;
            m_PanPointer = e.pointerId;
            m_Viewport.CapturePointer(e.pointerId);
            e.StopPropagation();
        }

        void OnPointerMove(PointerMoveEvent e)
        {
            if (!m_Panning || e.pointerId != m_PanPointer || m_Target == null)
                return;
            m_Pan += new Vector2(e.deltaPosition.x, e.deltaPosition.y);
            Place(new Vector2(m_Viewport.layout.width, m_Viewport.layout.height), new Vector2Int(m_Target.width, m_Target.height));
            e.StopPropagation();
        }

        void OnPointerUp(PointerUpEvent e)
        {
            if (!m_Panning || e.pointerId != m_PanPointer)
                return;
            m_Panning = false;
            m_Viewport.ReleasePointer(e.pointerId);
            e.StopPropagation();
        }

        // For tests: a pan by pixels.
        internal void PanByInternal(Vector2 d)
        {
            m_Pan += d;
            if (m_Target != null)
                Place(new Vector2(m_Viewport.layout.width, m_Viewport.layout.height), new Vector2Int(m_Target.width, m_Target.height));
        }

        void UpdateStats(Vector2Int res)
        {
            float dt = Time.unscaledDeltaTime;
            if (dt > 0)
                m_Fps = m_Fps <= 0 ? 1 / dt : m_Fps * 0.9f + 0.1f / dt;
            int d = m_Display;
            double cpu = GameViewNative.StatsDrawListTime(d) + GameViewNative.StatsSubmitTime(d);
            if (m_StatsText != null)
                m_StatsText.text = string.Format(CultureInfo.InvariantCulture,
                                                 "Statistics\n" +
                                                 "Graphics: {0:0.0} FPS ({1:0.0} ms)\n" +
                                                 "CPU render: {2:0.00} ms\n" +
                                                 "Batches: {3}   Instanced: {4}\n" +
                                                 "Tris: {5}\n" +
                                                 "SetPass calls: {6}\n" +
                                                 "Cameras: {7}   Visible renderers: {8}\n" +
                                                 "Screen: {9}x{10}",
                                                 m_Fps, m_Fps > 0 ? 1000 / m_Fps : 0, cpu, GameViewNative.StatsDrawCalls(d), GameViewNative.StatsInstancedBatches(d),
                                                 Short(GameViewNative.StatsTriangles(d)), GameViewNative.StatsSetPassCalls(d), GameViewNative.StatsCameras(d),
                                                 GameViewNative.StatsVisible(d), res.x, res.y);
        }

        static string Short(long n) => n >= 1_000_000 ? (n / 1e6).ToString("0.0", CultureInfo.InvariantCulture) + "M"
                                    : n >= 10_000 ? (n / 1e3).ToString("0.0", CultureInfo.InvariantCulture) + "k" : n.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>The Game view as the play mode's window: its rendering size, a custom size, the view type, whether
    /// entering the play mode focuses it.</summary>
    public static class PlayModeWindow
    {
        public enum PlayModeViewTypes
        {
            GameView = 0,
            SimulatorView = 1,
        }

        public static void GetRenderingResolution(out uint width, out uint height)
        {
            GameView v = GameView.mainInternal;
            if (v != null && v.targetInternal != null)
            {
                width = (uint)v.targetInternal.width;
                height = (uint)v.targetInternal.height;
                return;
            }
            width = (uint)Math.Max(1, Screen.width);
            height = (uint)Math.Max(1, Screen.height);
        }

        public static void SetCustomRenderingResolution(uint width, uint height, string baseName) =>
            GameView.SetCustomInternal((int)Math.Max(1, width), (int)Math.Max(1, height), baseName);

        /// <summary>The Game view (the device simulator is not part of the editor yet).</summary>
        public static void SetViewType(PlayModeViewTypes type)
        {
            if (type == PlayModeViewTypes.SimulatorView)
                Debug.LogWarning("PlayModeWindow.SetViewType: the device simulator is not part of the editor yet; the Game view stays.");
            EditorWindow.GetWindow<GameView>();
        }

        public static bool GetPlayModeFocused() => GameView.mainInternal == null || PlayModeOf() != GameView.PlayMode.Unfocused;

        public static void SetPlayModeFocused(bool focused)
        {
            GameView v = GameView.mainInternal ?? EditorWindow.GetWindow<GameView>();
            v.playModeInternal = focused ? (v.playModeInternal == GameView.PlayMode.Unfocused ? GameView.PlayMode.Focused : v.playModeInternal) : GameView.PlayMode.Unfocused;
        }

        static GameView.PlayMode PlayModeOf() => GameView.mainInternal?.playModeInternal ?? GameView.PlayMode.Focused;
    }
}
