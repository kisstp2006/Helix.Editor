using System;
using HelixEngine;
using Object = HelixEngine.Object;

namespace HelixEditor
{
    /// <summary>The editor's fields in the layout (the reference's EditorGUILayout): each takes a line's rect of the
    /// layout and draws the EditorGui field in it; the layout's groups and their scopes.</summary>
    public static partial class EditorGuiLayout
    {
        static GuiContent C(string text) => text == null ? null : new GuiContent(text);

        /// <summary>A rect of the layout for a field: the view's width, a line's height (or the given one).</summary>
        public static Rect GetControlRect(params GuiLayoutOption[] options) => GetControlRect(true, EditorGuiUtility.singleLineHeight, null, options);
        public static Rect GetControlRect(bool hasLabel, params GuiLayoutOption[] options) => GetControlRect(hasLabel, EditorGuiUtility.singleLineHeight, null, options);
        public static Rect GetControlRect(bool hasLabel, float height, params GuiLayoutOption[] options) => GetControlRect(hasLabel, height, null, options);

        public static Rect GetControlRect(bool hasLabel, float height, GuiStyle style, params GuiLayoutOption[] options)
        {
            Rect r = GuiLayoutUtility.GetRect(EditorGuiUtility.fieldWidth, 100000, height + EditorGuiUtility.standardVerticalSpacing,
                                              height + EditorGuiUtility.standardVerticalSpacing, options);
            return new Rect(r.x, r.y + EditorGuiUtility.standardVerticalSpacing / 2, r.width, height);
        }

        public static void LabelField(string label, params GuiLayoutOption[] options) => EditorGui.LabelField(GetControlRect(options), label);
        public static void LabelField(GuiContent label, params GuiLayoutOption[] options) => EditorGui.LabelField(GetControlRect(options), label);
        public static void LabelField(string label, GuiStyle style, params GuiLayoutOption[] options) => EditorGui.LabelField(GetControlRect(options), label, style);
        public static void LabelField(string label, string label2, params GuiLayoutOption[] options) => EditorGui.LabelField(GetControlRect(options), label, label2);
        public static void LabelField(GuiContent label, GuiContent label2, params GuiLayoutOption[] options) => EditorGui.LabelField(GetControlRect(options), label, label2);

        public static void PrefixLabel(string label) => PrefixLabel(C(label));

        public static void PrefixLabel(GuiContent label)
        {
            Rect r = GuiLayoutUtility.GetRect(EditorGuiUtility.labelWidth, EditorGuiUtility.labelWidth, EditorGuiUtility.singleLineHeight,
                                              EditorGuiUtility.singleLineHeight, GuiLayout.ExpandWidth(false));
            EditorGui.LabelField(r, label);
        }

        public static void HelpBox(string message, MessageType type) => HelpBox(new GuiContent(message ?? ""), type, true);
        public static void HelpBox(string message, MessageType type, bool wide) => HelpBox(new GuiContent(message ?? ""), type, wide);

        public static void HelpBox(GuiContent message, MessageType type, bool wide = true)
        {
            float h = Math.Max(EditorGuiUtility.singleLineHeight * 2, EditorStyles.helpBox.CalcHeight(message, EditorGuiUtility.currentViewWidth - 40) + 8);
            EditorGui.HelpBox(GetControlRect(false, h), message, type);
        }

        public static string TextField(string text, params GuiLayoutOption[] options) => EditorGui.TextField(GetControlRect(false, options), text);
        public static string TextField(string label, string text, params GuiLayoutOption[] options) => EditorGui.TextField(GetControlRect(options), label, text);
        public static string TextField(GuiContent label, string text, params GuiLayoutOption[] options) => EditorGui.TextField(GetControlRect(options), label, text);
        public static string DelayedTextField(string text, params GuiLayoutOption[] options) => EditorGui.DelayedTextField(GetControlRect(false, options), text);
        public static string DelayedTextField(string label, string text, params GuiLayoutOption[] options) => EditorGui.DelayedTextField(GetControlRect(options), label, text);
        public static string DelayedTextField(GuiContent label, string text, params GuiLayoutOption[] options) => EditorGui.DelayedTextField(GetControlRect(options), label, text);

        public static string TextArea(string text, params GuiLayoutOption[] options) =>
            EditorGui.TextArea(GetControlRect(false, EditorGuiUtility.singleLineHeight * 3, options), text);

        public static int IntField(int value, params GuiLayoutOption[] options) => EditorGui.IntField(GetControlRect(false, options), value);
        public static int IntField(string label, int value, params GuiLayoutOption[] options) => EditorGui.IntField(GetControlRect(options), label, value);
        public static int IntField(GuiContent label, int value, params GuiLayoutOption[] options) => EditorGui.IntField(GetControlRect(options), label, value);
        public static long LongField(string label, long value, params GuiLayoutOption[] options) => EditorGui.LongField(GetControlRect(options), label, value);
        public static long LongField(GuiContent label, long value, params GuiLayoutOption[] options) => EditorGui.LongField(GetControlRect(options), label, value);
        public static float FloatField(float value, params GuiLayoutOption[] options) => EditorGui.FloatField(GetControlRect(false, options), value);
        public static float FloatField(string label, float value, params GuiLayoutOption[] options) => EditorGui.FloatField(GetControlRect(options), label, value);
        public static float FloatField(GuiContent label, float value, params GuiLayoutOption[] options) => EditorGui.FloatField(GetControlRect(options), label, value);
        public static double DoubleField(string label, double value, params GuiLayoutOption[] options) => EditorGui.DoubleField(GetControlRect(options), label, value);
        public static double DoubleField(GuiContent label, double value, params GuiLayoutOption[] options) => EditorGui.DoubleField(GetControlRect(options), label, value);
        public static int DelayedIntField(string label, int value, params GuiLayoutOption[] options) => EditorGui.DelayedIntField(GetControlRect(options), label, value);
        public static int DelayedIntField(GuiContent label, int value, params GuiLayoutOption[] options) => EditorGui.DelayedIntField(GetControlRect(options), label, value);
        public static float DelayedFloatField(string label, float value, params GuiLayoutOption[] options) => EditorGui.DelayedFloatField(GetControlRect(options), label, value);
        public static float DelayedFloatField(GuiContent label, float value, params GuiLayoutOption[] options) => EditorGui.DelayedFloatField(GetControlRect(options), label, value);

        public static bool Toggle(bool value, params GuiLayoutOption[] options) => EditorGui.Toggle(GetControlRect(false, options), value);
        public static bool Toggle(string label, bool value, params GuiLayoutOption[] options) => EditorGui.Toggle(GetControlRect(options), label, value);
        public static bool Toggle(GuiContent label, bool value, params GuiLayoutOption[] options) => EditorGui.Toggle(GetControlRect(options), label, value);
        public static bool ToggleLeft(string label, bool value, params GuiLayoutOption[] options) => EditorGui.ToggleLeft(GetControlRect(options), label, value);
        public static bool ToggleLeft(GuiContent label, bool value, params GuiLayoutOption[] options) => EditorGui.ToggleLeft(GetControlRect(options), label, value);

        public static float Slider(float value, float leftValue, float rightValue, params GuiLayoutOption[] options) =>
            EditorGui.Slider(GetControlRect(false, options), value, leftValue, rightValue);
        public static float Slider(string label, float value, float leftValue, float rightValue, params GuiLayoutOption[] options) =>
            EditorGui.Slider(GetControlRect(options), label, value, leftValue, rightValue);
        public static float Slider(GuiContent label, float value, float leftValue, float rightValue, params GuiLayoutOption[] options) =>
            EditorGui.Slider(GetControlRect(options), label, value, leftValue, rightValue);
        public static int IntSlider(string label, int value, int leftValue, int rightValue, params GuiLayoutOption[] options) =>
            EditorGui.IntSlider(GetControlRect(options), label, value, leftValue, rightValue);
        public static int IntSlider(GuiContent label, int value, int leftValue, int rightValue, params GuiLayoutOption[] options) =>
            EditorGui.IntSlider(GetControlRect(options), label, value, leftValue, rightValue);

        public static int Popup(int selectedIndex, string[] displayedOptions, params GuiLayoutOption[] options) =>
            EditorGui.Popup(GetControlRect(false, options), selectedIndex, displayedOptions);
        public static int Popup(string label, int selectedIndex, string[] displayedOptions, params GuiLayoutOption[] options) =>
            EditorGui.Popup(GetControlRect(options), label, selectedIndex, displayedOptions);
        public static int Popup(GuiContent label, int selectedIndex, string[] displayedOptions, params GuiLayoutOption[] options) =>
            EditorGui.Popup(GetControlRect(options), label, selectedIndex, displayedOptions);
        public static int IntPopup(string label, int selectedValue, string[] displayedOptions, int[] optionValues, params GuiLayoutOption[] options) =>
            EditorGui.IntPopup(GetControlRect(options), label, selectedValue, displayedOptions, optionValues);
        public static Enum EnumPopup(Enum selected, params GuiLayoutOption[] options) => EditorGui.EnumPopup(GetControlRect(false, options), selected);
        public static Enum EnumPopup(string label, Enum selected, params GuiLayoutOption[] options) => EditorGui.EnumPopup(GetControlRect(options), label, selected);
        public static Enum EnumPopup(GuiContent label, Enum selected, params GuiLayoutOption[] options) => EditorGui.EnumPopup(GetControlRect(options), label, selected);
        public static Enum EnumFlagsField(string label, Enum enumValue, params GuiLayoutOption[] options) => EditorGui.EnumFlagsField(GetControlRect(options), label, enumValue);
        public static Enum EnumFlagsField(GuiContent label, Enum enumValue, params GuiLayoutOption[] options) => EditorGui.EnumFlagsField(GetControlRect(options), label, enumValue);
        public static int MaskField(string label, int mask, string[] displayedOptions, params GuiLayoutOption[] options) =>
            EditorGui.MaskField(GetControlRect(options), label, mask, displayedOptions);
        public static int LayerField(string label, int layer, params GuiLayoutOption[] options) => EditorGui.LayerField(GetControlRect(options), label, layer);
        public static int LayerField(GuiContent label, int layer, params GuiLayoutOption[] options) => EditorGui.LayerField(GetControlRect(options), label, layer);
        public static string TagField(string label, string tag, params GuiLayoutOption[] options) => EditorGui.TagField(GetControlRect(options), label, tag);
        public static string TagField(GuiContent label, string tag, params GuiLayoutOption[] options) => EditorGui.TagField(GetControlRect(options), label, tag);

        public static Color ColorField(string label, Color value, params GuiLayoutOption[] options) => EditorGui.ColorField(GetControlRect(options), label, value);
        public static Color ColorField(GuiContent label, Color value, params GuiLayoutOption[] options) => EditorGui.ColorField(GetControlRect(options), label, value);
        public static Vector2 Vector2Field(string label, Vector2 value, params GuiLayoutOption[] options) => EditorGui.Vector2Field(GetControlRect(options), label, value);
        public static Vector3 Vector3Field(string label, Vector3 value, params GuiLayoutOption[] options) => EditorGui.Vector3Field(GetControlRect(options), label, value);
        public static Vector3 Vector3Field(GuiContent label, Vector3 value, params GuiLayoutOption[] options) => EditorGui.Vector3Field(GetControlRect(options), label, value);
        public static Vector4 Vector4Field(string label, Vector4 value, params GuiLayoutOption[] options) => EditorGui.Vector4Field(GetControlRect(options), label, value);
        public static Vector2Int Vector2IntField(string label, Vector2Int value, params GuiLayoutOption[] options) => EditorGui.Vector2IntField(GetControlRect(options), label, value);
        public static Vector3Int Vector3IntField(string label, Vector3Int value, params GuiLayoutOption[] options) => EditorGui.Vector3IntField(GetControlRect(options), label, value);
        public static Rect RectField(string label, Rect value, params GuiLayoutOption[] options) => EditorGui.RectField(GetControlRect(options), label, value);

        public static Bounds BoundsField(string label, Bounds value, params GuiLayoutOption[] options) =>
            EditorGui.BoundsField(GetControlRect(true, EditorGuiUtility.singleLineHeight * 2, options), label, value);

        public static Object ObjectField(Object obj, Type objType, bool allowSceneObjects, params GuiLayoutOption[] options) =>
            EditorGui.ObjectField(GetControlRect(false, options), obj, objType, allowSceneObjects);
        public static Object ObjectField(string label, Object obj, Type objType, bool allowSceneObjects, params GuiLayoutOption[] options) =>
            EditorGui.ObjectField(GetControlRect(options), label, obj, objType, allowSceneObjects);
        public static Object ObjectField(GuiContent label, Object obj, Type objType, bool allowSceneObjects, params GuiLayoutOption[] options) =>
            EditorGui.ObjectField(GetControlRect(options), label, obj, objType, allowSceneObjects);

        public static bool Foldout(bool foldout, string content, bool toggleOnLabelClick = true) => EditorGui.Foldout(GetControlRect(), foldout, content, toggleOnLabelClick);
        public static bool Foldout(bool foldout, GuiContent content, bool toggleOnLabelClick = true) => EditorGui.Foldout(GetControlRect(), foldout, content, toggleOnLabelClick);

        public static bool PropertyField(SerializedProperty property, params GuiLayoutOption[] options) => PropertyField(property, null, false, options);
        public static bool PropertyField(SerializedProperty property, bool includeChildren, params GuiLayoutOption[] options) => PropertyField(property, null, includeChildren, options);
        public static bool PropertyField(SerializedProperty property, GuiContent label, params GuiLayoutOption[] options) => PropertyField(property, label, false, options);

        public static bool PropertyField(SerializedProperty property, GuiContent label, bool includeChildren, params GuiLayoutOption[] options)
        {
            float h = EditorGui.GetPropertyHeight(property, label, includeChildren);
            return EditorGui.PropertyField(GetControlRect(true, h, options), property, label, includeChildren);
        }

        public static void Space() => GuiLayout.Space(6);
        public static void Space(float width) => GuiLayout.Space(width);
        public static void Space(float width, bool expand) => GuiLayout.Space(width);

        public static void Separator()
        {
            Rect r = GetControlRect(false, 6);
            EditorGui.DrawRect(new Rect(r.x, r.y + r.height / 2, r.width, 1), new Color(0.1f, 0.1f, 0.1f, 1));
        }

        public static Rect BeginHorizontal(params GuiLayoutOption[] options)
        {
            GuiLayout.BeginHorizontal(options);
            return default;
        }

        public static Rect BeginHorizontal(GuiStyle style, params GuiLayoutOption[] options)
        {
            GuiLayout.BeginHorizontal(style, options);
            return default;
        }

        public static void EndHorizontal() => GuiLayout.EndHorizontal();

        public static Rect BeginVertical(params GuiLayoutOption[] options)
        {
            GuiLayout.BeginVertical(options);
            return default;
        }

        public static Rect BeginVertical(GuiStyle style, params GuiLayoutOption[] options)
        {
            GuiLayout.BeginVertical(style, options);
            return default;
        }

        public static void EndVertical() => GuiLayout.EndVertical();
        public static Vector2 BeginScrollView(Vector2 scrollPosition, params GuiLayoutOption[] options) => GuiLayout.BeginScrollView(scrollPosition, options);
        public static void EndScrollView() => GuiLayout.EndScrollView();

        // A toggle that greys out what follows until EndToggleGroup.
        public static bool BeginToggleGroup(string label, bool toggle) => BeginToggleGroup(C(label), toggle);

        public static bool BeginToggleGroup(GuiContent label, bool toggle)
        {
            toggle = EditorGui.ToggleLeft(GetControlRect(), label, toggle);
            EditorGui.BeginDisabledGroup(!toggle);
            return toggle;
        }

        public static void EndToggleGroup() => EditorGui.EndDisabledGroup();

        // Without animation: shown while the value is above zero.
        public static bool BeginFadeGroup(float value) => value > 0;
        public static void EndFadeGroup() { }

        public class HorizontalScope : IDisposable
        {
            bool m_Done;
            public Rect rect { get; }
            public HorizontalScope(params GuiLayoutOption[] options) => GuiLayout.BeginHorizontal(options);
            public HorizontalScope(GuiStyle style, params GuiLayoutOption[] options) => GuiLayout.BeginHorizontal(style, options);

            public void Dispose()
            {
                if (!m_Done)
                    GuiLayout.EndHorizontal();
                m_Done = true;
            }
        }

        public class VerticalScope : IDisposable
        {
            bool m_Done;
            public Rect rect { get; }
            public VerticalScope(params GuiLayoutOption[] options) => GuiLayout.BeginVertical(options);
            public VerticalScope(GuiStyle style, params GuiLayoutOption[] options) => GuiLayout.BeginVertical(style, options);

            public void Dispose()
            {
                if (!m_Done)
                    GuiLayout.EndVertical();
                m_Done = true;
            }
        }

        public class ScrollViewScope : IDisposable
        {
            bool m_Done;
            public Vector2 scrollPosition { get; }
            public ScrollViewScope(Vector2 scrollPosition, params GuiLayoutOption[] options) => this.scrollPosition = GuiLayout.BeginScrollView(scrollPosition, options);

            public void Dispose()
            {
                if (!m_Done)
                    GuiLayout.EndScrollView();
                m_Done = true;
            }
        }

        public class ToggleGroupScope : IDisposable
        {
            bool m_Done;
            public bool enabled { get; }
            public ToggleGroupScope(string label, bool toggle) => enabled = BeginToggleGroup(label, toggle);
            public ToggleGroupScope(GuiContent label, bool toggle) => enabled = BeginToggleGroup(label, toggle);

            public void Dispose()
            {
                if (!m_Done)
                    EndToggleGroup();
                m_Done = true;
            }
        }

        public class FadeGroupScope : IDisposable
        {
            public bool visible { get; }
            public FadeGroupScope(float value) => visible = BeginFadeGroup(value);
            public void Dispose() => EndFadeGroup();
        }
    }
}
