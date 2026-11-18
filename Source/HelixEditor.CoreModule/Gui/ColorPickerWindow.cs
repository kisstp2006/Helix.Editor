using System;
using System.Globalization;
using HelixEngine;

namespace HelixEditor
{
    // The editor's color picker: a window of the color's channels and its hex code, the field told of each change.
    internal sealed class ColorPickerWindow : EditorWindow
    {
        Color m_Color;
        Action<Color> m_Changed;

        internal static ColorPickerWindow Open(Color color, Action<Color> changed)
        {
            var w = GetWindow<ColorPickerWindow>("Color");
            w.m_Color = color;
            w.m_Changed = changed;
            w.Focus();
            return w;
        }

        internal Color colorInternal => m_Color;

        internal void SetColorInternal(Color c)
        {
            m_Color = c;
            m_Changed?.Invoke(c);
            Repaint();
        }

        void OnGui()
        {
            Rect swatch = GuiLayoutUtility.GetRect(10, 40, GuiLayout.ExpandWidth(true));
            EditorGui.DrawRect(swatch, new Color(m_Color.r, m_Color.g, m_Color.b, 1));
            EditorGui.BeginChangeCheck();
            float r = EditorGuiLayout.Slider("R", m_Color.r, 0, 1);
            float g = EditorGuiLayout.Slider("G", m_Color.g, 0, 1);
            float b = EditorGuiLayout.Slider("B", m_Color.b, 0, 1);
            float a = EditorGuiLayout.Slider("A", m_Color.a, 0, 1);
            string hex = EditorGuiLayout.DelayedTextField("Hexadecimal", Hex(m_Color));
            if (EditorGui.EndChangeCheck())
            {
                var c = new Color(r, g, b, a);
                if (hex != Hex(m_Color) && TryHex(hex, out Color parsed))
                    c = parsed;
                SetColorInternal(c);
            }
        }

        static string Hex(Color c) =>
            $"{(int)Math.Round(c.r * 255):X2}{(int)Math.Round(c.g * 255):X2}{(int)Math.Round(c.b * 255):X2}{(int)Math.Round(c.a * 255):X2}";

        static bool TryHex(string text, out Color c)
        {
            c = default;
            text = text?.Trim().TrimStart('#') ?? "";
            if (text.Length != 6 && text.Length != 8 || !uint.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint v))
                return false;
            if (text.Length == 6)
                v = (v << 8) | 0xFF;
            c = new Color(((v >> 24) & 255) / 255f, ((v >> 16) & 255) / 255f, ((v >> 8) & 255) / 255f, (v & 255) / 255f);
            return true;
        }
    }
}
