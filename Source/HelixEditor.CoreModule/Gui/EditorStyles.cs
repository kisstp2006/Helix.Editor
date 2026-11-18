using HelixEngine;

namespace HelixEditor
{
    /// <summary>The editor's GUI styles, made from the GUI skin's (copies with the editor's sizes and kinds).</summary>
    public static class EditorStyles
    {
        static GuiStyle s_Label, s_BoldLabel, s_MiniLabel, s_LargeLabel, s_WordWrappedLabel, s_CenteredGreyMiniLabel, s_TextField, s_TextArea,
                        s_NumberField, s_Toggle, s_Foldout, s_FoldoutHeader, s_Popup, s_MiniButton, s_HelpBox, s_ObjectField, s_ColorField,
                        s_Toolbar, s_ToolbarButton, s_LayerMaskField, s_MiniTextField, s_InspectorTitlebar;

        static GuiStyle Copy(GuiStyle from, string name)
        {
            var s = new GuiStyle(from);
            s.name = name;
            return s;
        }

        static GuiSkin Skin => Gui.skin;

        public static GuiStyle label => s_Label ??= Copy(Skin.label, "ControlLabel");

        public static GuiStyle boldLabel => s_BoldLabel ??= Make(label, "BoldLabel", s => s.fontStyle = FontStyle.Bold);
        public static GuiStyle miniLabel => s_MiniLabel ??= Make(label, "MiniLabel", s => s.fontSize = SmallSize);
        public static GuiStyle largeLabel => s_LargeLabel ??= Make(label, "LargeLabel", s => s.fontSize = label.fontSize + 2);
        public static GuiStyle wordWrappedLabel => s_WordWrappedLabel ??= Make(label, "WordWrappedLabel", s => s.wordWrap = true);

        public static GuiStyle centeredGreyMiniLabel => s_CenteredGreyMiniLabel ??= Make(miniLabel, "CenteredGreyMiniLabel", s =>
        {
            s.alignment = TextAnchor.MiddleCenter;
            s.normal.textColor = new Color(0.6f, 0.6f, 0.6f, 1);
        });

        // A line's field: tight padding, the label's text size (the skin's own is for game UI).
        public static GuiStyle textField => s_TextField ??= Make(Skin.textField, "TextField", s =>
        {
            s.padding.top = 1;
            s.padding.bottom = 1;
            s.padding.left = 3;
            s.padding.right = 3;
            s.margin.top = 0;
            s.margin.bottom = 0;
            s.fontSize = label.fontSize;
            s.alignment = TextAnchor.MiddleLeft;
        });
        public static GuiStyle textArea => s_TextArea ??= Make(Skin.textArea, "TextArea", s =>
        {
            s.wordWrap = true;
            s.padding.top = 2;
            s.padding.bottom = 2;
            s.fontSize = label.fontSize;
        });
        public static GuiStyle numberField => s_NumberField ??= Copy(textField, "NumberField");
        public static GuiStyle miniTextField => s_MiniTextField ??= Make(textField, "MiniTextField", s => s.fontSize = SmallSize);
        public static GuiStyle toggle => s_Toggle ??= Copy(Skin.toggle, "Toggle");
        public static GuiStyle foldout => s_Foldout ??= Make(label, "Foldout", s => s.padding.left += 14);
        public static GuiStyle foldoutHeader => s_FoldoutHeader ??= Make(foldout, "FoldoutHeader", s => s.fontStyle = FontStyle.Bold);
        public static GuiStyle popup => s_Popup ??= Make(Skin.button, "MiniPopup", s => s.alignment = TextAnchor.MiddleLeft);
        public static GuiStyle miniButton => s_MiniButton ??= Make(Skin.button, "MiniButton", s => s.fontSize = SmallSize);
        public static GuiStyle helpBox => s_HelpBox ??= Make(Skin.box, "HelpBox", s =>
        {
            s.wordWrap = true;
            s.alignment = TextAnchor.MiddleLeft;
            s.fontSize = SmallSize;
        });
        public static GuiStyle objectField => s_ObjectField ??= Copy(textField, "ObjectField");
        public static GuiStyle colorField => s_ColorField ??= Copy(textField, "ColorField");
        public static GuiStyle layerMaskField => s_LayerMaskField ??= Copy(popup, "MiniPopup");
        public static GuiStyle toolbar => s_Toolbar ??= Copy(Skin.box, "Toolbar");
        // The Inspector's title bar of a component: a raised box.
        internal static GuiStyle inspectorTitlebarInternal => s_InspectorTitlebar ??= Copy(Skin.box, "IN Title");
        public static GuiStyle toolbarButton => s_ToolbarButton ??= Copy(Skin.button, "ToolbarButton");

        public static HelixEngine.Text.FontAsset standardFont => label.font;
        public static HelixEngine.Text.FontAsset boldFont => boldLabel.font;

        static int SmallSize => label.fontSize > 0 ? label.fontSize - 2 : 10;

        static GuiStyle Make(GuiStyle from, string name, System.Action<GuiStyle> change)
        {
            GuiStyle s = Copy(from, name);
            change(s);
            return s;
        }
    }
}
