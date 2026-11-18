using System;
using HelixEngine;
using Object = HelixEngine.Object;

namespace HelixEditor
{
    // The editor's GUI measures and contents (the reference's EditorGUIUtility): the label column's width, the line
    // height, the view's width, the editor's icons by name.
    public static partial class EditorGuiUtility
    {
        static float s_LabelWidth, s_FieldWidth;

        /// <summary>The width of the labels before fields (0 sets it back to the view's share of it).</summary>
        public static float labelWidth
        {
            get => s_LabelWidth > 0 ? s_LabelWidth : Math.Max(currentViewWidth * 0.45f - 40, 120);
            set => s_LabelWidth = value;
        }

        /// <summary>The width of a number field beside a slider and the like.</summary>
        public static float fieldWidth
        {
            get => s_FieldWidth > 0 ? s_FieldWidth : 50;
            set => s_FieldWidth = value;
        }

        public static float singleLineHeight => 18;
        public static float standardVerticalSpacing => 2;
        // The width of the view drawn now (the window's).
        public static float currentViewWidth { get; internal set; } = 400;
        // Wide enough for a vector's fields beside its label.
        public static bool wideMode { get; set; } = true;
        public static bool hierarchyMode { get; set; }
        public static bool isProSkin => true;
        public static bool editingTextField { get; set; }

        public static GuiContent TrTextContent(string text, string tooltip = null, Texture2D icon = null) =>
            new GuiContent(text ?? "", icon) { tooltip = tooltip ?? "" };

        public static GuiContent TrTempContent(string text) => new GuiContent(text ?? "");

        /// <summary>An editor icon (Skin/Icons/&lt;name&gt;.png of the editor's data folder).</summary>
        public static GuiContent IconContent(string name, string text = null) => new GuiContent(text ?? "", FindTexture(name));

        public static Texture2D FindTexture(string name) => string.IsNullOrEmpty(name) ? null : Load($"Skin/Icons/{name}.png") as Texture2D;

        /// <summary>"Name (Type)", or "None (Type)".</summary>
        public static GuiContent ObjectContent(Object obj, Type type)
        {
            string typeName = type?.Name ?? obj?.GetType().Name ?? "Object";
            return new GuiContent(obj != null ? $"{obj.name} ({typeName})" : $"None ({typeName})");
        }

        // The Project window comes with step 6: the object is selected meanwhile.
        public static void PingObject(Object obj)
        {
            if (obj != null)
                Selection.activeObject = obj;
        }

        public static string[] tagsInternal => TagsInternal();
    }
}
