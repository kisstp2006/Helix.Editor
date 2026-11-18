using System;
using System.Collections.Generic;
using System.Globalization;
using HelixEngine;
using Object = HelixEngine.Object;

namespace HelixEditor
{
    public enum MessageType
    {
        None = 0,
        Info = 1,
        Warning = 2,
        Error = 3,
    }

    /// <summary>The editor's fields at rects (the reference's EditorGUI): a label column and the field beside it, numbers
    /// with label drag and expressions, popups and pickers through menus, properties of SerializedObjects by their types
    /// and drawers; the scopes of changes, disabled fields, indents and mixed values.</summary>
    public static partial class EditorGui
    {
        const float kIndent = 15;
        const float kSpacing = 2;

        public static int indentLevel { get; set; }
        public static bool showMixedValue { get; set; }
        public static bool actionKey => Event.current != null && (Event.current.control || Event.current.command);

        // ------------------------------------------------------------------------------------------------- scopes

        static readonly Stack<bool> s_Changed = new(), s_Enabled = new(), s_Mixed = new();

        public static void BeginChangeCheck()
        {
            s_Changed.Push(Gui.changed);
            Gui.changed = false;
        }

        public static bool EndChangeCheck()
        {
            bool changed = Gui.changed;
            Gui.changed = (s_Changed.Count > 0 && s_Changed.Pop()) || changed;
            return changed;
        }

        public static void BeginDisabledGroup(bool disabled)
        {
            s_Enabled.Push(Gui.enabled);
            Gui.enabled = Gui.enabled && !disabled;
        }

        public static void EndDisabledGroup() => Gui.enabled = s_Enabled.Count == 0 || s_Enabled.Pop();

        public class ChangeCheckScope : IDisposable
        {
            bool m_Done;
            public bool changed { get; private set; }
            public ChangeCheckScope() => BeginChangeCheck();

            public void Dispose()
            {
                if (m_Done)
                    return;
                m_Done = true;
                changed = EndChangeCheck();
            }
        }

        public struct DisabledScope : IDisposable
        {
            bool m_On;

            public DisabledScope(bool disabled)
            {
                m_On = true;
                BeginDisabledGroup(disabled);
            }

            public void Dispose()
            {
                if (!m_On)
                    return;
                m_On = false;
                EndDisabledGroup();
            }
        }

        public class DisabledGroupScope : IDisposable
        {
            bool m_Done;
            public DisabledGroupScope(bool disabled) => BeginDisabledGroup(disabled);

            public void Dispose()
            {
                if (!m_Done)
                    EndDisabledGroup();
                m_Done = true;
            }
        }

        public class IndentLevelScope : IDisposable
        {
            readonly int m_Increment;
            bool m_Done;

            public IndentLevelScope() : this(1) { }

            public IndentLevelScope(int increment)
            {
                m_Increment = increment;
                indentLevel += increment;
            }

            public void Dispose()
            {
                if (!m_Done)
                    indentLevel -= m_Increment;
                m_Done = true;
            }
        }

        public class MixedValueScope : IDisposable
        {
            readonly bool m_Was;
            public MixedValueScope() : this(true) { }

            public MixedValueScope(bool newShowMixedValue)
            {
                m_Was = showMixedValue;
                showMixedValue = newShowMixedValue;
            }

            public void Dispose() => showMixedValue = m_Was;
        }

        public class PropertyScope : IDisposable
        {
            public GuiContent content { get; }
            public PropertyScope(Rect totalPosition, GuiContent label, SerializedProperty property) => content = BeginProperty(totalPosition, label, property);
            public void Dispose() => EndProperty();
        }

        /// <summary>A property's label (its tooltip) and its mixed value shown while its fields draw.</summary>
        public static GuiContent BeginProperty(Rect totalPosition, GuiContent label, SerializedProperty property)
        {
            s_Mixed.Push(showMixedValue);
            showMixedValue = property != null && property.hasMultipleDifferentValues;
            if (property == null)
                return label;
            if (label == null)
                return new GuiContent(property.displayName) { tooltip = property.tooltip };
            return label;
        }

        public static void EndProperty() => showMixedValue = s_Mixed.Count > 0 && s_Mixed.Pop();

        // ------------------------------------------------------------------------------------------------- labels

        public static Rect IndentedRect(Rect source)
        {
            float x = indentLevel * kIndent;
            return new Rect(source.x + x, source.y, Math.Max(0, source.width - x), source.height);
        }

        static bool HasText(GuiContent label) => label != null && (!string.IsNullOrEmpty(label.text) || label.image != null);

        // The label at the left (indented) and the field's rect after the label column.
        static Rect Prefix(Rect position, GuiContent label, out Rect labelRect)
        {
            if (!HasText(label))
            {
                labelRect = new Rect(position.x, position.y, 0, position.height);
                return IndentedRect(position);
            }
            float w = EditorGuiUtility.labelWidth;
            labelRect = IndentedRect(new Rect(position.x, position.y, w, position.height));
            Gui.Label(labelRect, label, EditorStyles.label);
            return new Rect(position.x + w + kSpacing, position.y, Math.Max(0, position.width - w - kSpacing), position.height);
        }

        public static Rect PrefixLabel(Rect totalPosition, GuiContent label) => Prefix(totalPosition, label, out _);
        public static Rect PrefixLabel(Rect totalPosition, GuiContent label, GuiStyle style) => Prefix(totalPosition, label, out _);
        public static Rect PrefixLabel(Rect totalPosition, int id, GuiContent label) => Prefix(totalPosition, label, out _);

        static GuiContent C(string text) => text == null ? null : new GuiContent(text);

        public static void LabelField(Rect position, string label) => LabelField(position, C(label), EditorStyles.label);
        public static void LabelField(Rect position, string label, GuiStyle style) => LabelField(position, C(label), style);
        public static void LabelField(Rect position, GuiContent label) => LabelField(position, label, EditorStyles.label);
        public static void LabelField(Rect position, GuiContent label, GuiStyle style) => Gui.Label(IndentedRect(position), label ?? new GuiContent(""), style ?? EditorStyles.label);
        public static void LabelField(Rect position, string label, string label2) => LabelField(position, C(label), C(label2), EditorStyles.label);
        public static void LabelField(Rect position, GuiContent label, GuiContent label2) => LabelField(position, label, label2, EditorStyles.label);

        public static void LabelField(Rect position, GuiContent label, GuiContent label2, GuiStyle style)
        {
            Rect field = Prefix(position, label, out _);
            Gui.Label(field, label2 ?? new GuiContent(""), style ?? EditorStyles.label);
        }

        public static void HelpBox(Rect position, string message, MessageType type) => HelpBox(position, new GuiContent(message ?? ""), type);

        public static void HelpBox(Rect position, GuiContent message, MessageType type)
        {
            Texture2D icon = type switch
            {
                MessageType.Info => EditorGuiUtility.FindTexture("Info"),
                MessageType.Warning => EditorGuiUtility.FindTexture("Warning"),
                MessageType.Error => EditorGuiUtility.FindTexture("Error"),
                _ => null,
            };
            Rect r = IndentedRect(position);
            Gui.Box(r, new GuiContent("", message?.tooltip ?? ""), EditorStyles.helpBox);
            float textX = r.x + 6;
            if (icon != null)
            {
                float s = Math.Min(24, r.height - 6);
                Gui.DrawTexture(new Rect(r.x + 6, r.y + (r.height - s) / 2, s, s), icon, ScaleMode.ScaleToFit);
                textX += s + 6;
            }
            Gui.Label(new Rect(textX, r.y, r.xMax - textX - 4, r.height), message?.text ?? "", EditorStyles.helpBox);
        }

        public static void DrawRect(Rect rect, Color color)
        {
            if (Event.current == null || Event.current.type != EventType.Repaint)
                return;
            Color was = Gui.color;
            Gui.color = color * was;
            Gui.DrawTexture(rect, Texture2D.whiteTexture);
            Gui.color = was;
        }

        public static void FocusTextInControl(string name) => Gui.FocusControl(name ?? "");

        // ------------------------------------------------------------------------------------------------- text

        // A field's text being edited (its control's name): what the user typed, kept until it is taken.
        static string s_EditName, s_EditText;
        static readonly int s_TextHint = "EditorGuiText".GetHashCode(), s_DragHint = "EditorGuiDrag".GetHashCode();

        // A text field of the editor: named so the edit can be followed; delayed fields give their text on Enter or as
        // they lose the focus.
        static string Text(Rect field, string text, bool delayed, GuiStyle style, out bool committed)
        {
            committed = false;
            int id = GuiUtility.GetControlID(s_TextHint, FocusType.Passive, field);
            string name = "EditorGui" + id.ToString(CultureInfo.InvariantCulture);
            bool focused = Gui.GetNameOfFocusedControl() == name;
            Event e = Event.current;
            // Left while editing: the typed text is taken.
            if (!focused && s_EditName == name)
            {
                committed = true;
                string typed = s_EditText;
                s_EditName = null;
                return typed;
            }
            if (focused && e != null && e.type == EventType.KeyDown && (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter))
            {
                if (s_EditName == name)
                {
                    committed = true;
                    string typed = s_EditText;
                    s_EditName = null;
                    e.Use();
                    return typed;
                }
            }
            string shown = focused && s_EditName == name ? s_EditText : (showMixedValue ? "-" : text ?? "");
            Gui.SetNextControlName(name);
            string result = Gui.TextField(field, shown, style ?? EditorStyles.textField);
            if (result != shown)
            {
                s_EditName = name;
                s_EditText = result;
                if (!delayed)
                {
                    committed = true;
                    return result;
                }
            }
            return text;
        }

        public static string TextField(Rect position, string text) => TextField(position, (GuiContent)null, text, EditorStyles.textField);
        public static string TextField(Rect position, string label, string text) => TextField(position, C(label), text, EditorStyles.textField);
        public static string TextField(Rect position, GuiContent label, string text) => TextField(position, label, text, EditorStyles.textField);

        public static string TextField(Rect position, GuiContent label, string text, GuiStyle style)
        {
            Rect field = Prefix(position, label, out _);
            string result = Text(field, text, false, style, out bool committed);
            if (committed && result != text)
                Gui.changed = true;
            return committed ? result : text;
        }

        public static string DelayedTextField(Rect position, string text) => DelayedTextField(position, (GuiContent)null, text);
        public static string DelayedTextField(Rect position, string label, string text) => DelayedTextField(position, C(label), text);

        public static string DelayedTextField(Rect position, GuiContent label, string text)
        {
            Rect field = Prefix(position, label, out _);
            string result = Text(field, text, true, EditorStyles.textField, out bool committed);
            if (committed && result != text)
            {
                Gui.changed = true;
                return result;
            }
            return text;
        }

        public static string TextArea(Rect position, string text) => TextArea(position, text, EditorStyles.textArea);

        public static string TextArea(Rect position, string text, GuiStyle style)
        {
            string result = Gui.TextArea(IndentedRect(position), text ?? "", style ?? EditorStyles.textArea);
            return result;
        }

        // ------------------------------------------------------------------------------------------------- numbers

        static double s_DragStart, s_DragSum;

        // A number field: the label drags the value; the text takes numbers and expressions (2*3, (1+2)/4).
        static double Number(Rect position, GuiContent label, double value, bool integer, bool delayed)
        {
            Rect field = Prefix(position, label, out Rect labelRect);
            Event e = Event.current;
            int drag = GuiUtility.GetControlID(s_DragHint, FocusType.Passive, labelRect);
            if (e != null && labelRect.width > 0 && Gui.enabled)
            {
                switch (e.GetTypeForControl(drag))
                {
                case EventType.MouseDown:
                    if (e.button == 0 && labelRect.Contains(e.mousePosition))
                    {
                        GuiUtility.hotControl = drag;
                        s_DragStart = value;
                        s_DragSum = 0;
                        e.Use();
                    }
                    break;
                case EventType.MouseDrag:
                    if (GuiUtility.hotControl == drag)
                    {
                        s_DragSum += e.delta.x;
                        double sensitivity = integer ? Math.Max(1, Math.Abs(s_DragStart) * 0.03) : Math.Max(0.01, Math.Abs(s_DragStart) * 0.03);
                        double v = s_DragStart + s_DragSum * sensitivity * (e.shift ? 4 : e.alt ? 0.25 : 1);
                        v = integer ? Math.Round(v) : Math.Round(v, 3);
                        if (v != value)
                        {
                            value = v;
                            Gui.changed = true;
                        }
                        e.Use();
                    }
                    break;
                case EventType.MouseUp:
                    if (GuiUtility.hotControl == drag)
                    {
                        GuiUtility.hotControl = 0;
                        e.Use();
                    }
                    break;
                }
            }
            string text = integer ? ((long)value).ToString(CultureInfo.InvariantCulture) : value.ToString("0.#######", CultureInfo.InvariantCulture);
            string typed = Text(field, text, delayed, EditorStyles.numberField, out bool committed);
            if (committed && Expression.TryEvaluate(typed, out double result))
            {
                if (integer)
                    result = Math.Round(result);
                if (result != value)
                {
                    value = result;
                    Gui.changed = true;
                }
            }
            return value;
        }

        public static int IntField(Rect position, int value) => IntField(position, (GuiContent)null, value);
        public static int IntField(Rect position, string label, int value) => IntField(position, C(label), value);
        public static int IntField(Rect position, GuiContent label, int value) => (int)Math.Clamp(Number(position, label, value, true, false), int.MinValue, int.MaxValue);
        public static int IntField(Rect position, GuiContent label, int value, GuiStyle style) => IntField(position, label, value);
        public static long LongField(Rect position, long value) => LongField(position, (GuiContent)null, value);
        public static long LongField(Rect position, string label, long value) => LongField(position, C(label), value);
        public static long LongField(Rect position, GuiContent label, long value) => (long)Number(position, label, value, true, false);
        public static float FloatField(Rect position, float value) => FloatField(position, (GuiContent)null, value);
        public static float FloatField(Rect position, string label, float value) => FloatField(position, C(label), value);
        public static float FloatField(Rect position, GuiContent label, float value) => (float)Number(position, label, value, false, false);
        public static float FloatField(Rect position, GuiContent label, float value, GuiStyle style) => FloatField(position, label, value);
        public static double DoubleField(Rect position, double value) => DoubleField(position, (GuiContent)null, value);
        public static double DoubleField(Rect position, string label, double value) => DoubleField(position, C(label), value);
        public static double DoubleField(Rect position, GuiContent label, double value) => Number(position, label, value, false, false);
        public static int DelayedIntField(Rect position, int value) => DelayedIntField(position, (GuiContent)null, value);
        public static int DelayedIntField(Rect position, string label, int value) => DelayedIntField(position, C(label), value);
        public static int DelayedIntField(Rect position, GuiContent label, int value) => (int)Math.Clamp(Number(position, label, value, true, true), int.MinValue, int.MaxValue);
        public static float DelayedFloatField(Rect position, float value) => DelayedFloatField(position, (GuiContent)null, value);
        public static float DelayedFloatField(Rect position, string label, float value) => DelayedFloatField(position, C(label), value);
        public static float DelayedFloatField(Rect position, GuiContent label, float value) => (float)Number(position, label, value, false, true);
        public static double DelayedDoubleField(Rect position, GuiContent label, double value) => Number(position, label, value, false, true);

        // The arithmetic a number field takes: + - * / and parentheses, numbers as the invariant culture writes them.
        internal static class Expression
        {
            internal static bool TryEvaluate(string text, out double value)
            {
                value = 0;
                if (string.IsNullOrWhiteSpace(text))
                    return false;
                int at = 0;
                try
                {
                    value = Sum(text, ref at);
                    Skip(text, ref at);
                    return at == text.Length && !double.IsNaN(value) && !double.IsInfinity(value);
                }
                catch (FormatException)
                {
                    return false;
                }
            }

            static void Skip(string s, ref int at)
            {
                while (at < s.Length && char.IsWhiteSpace(s[at]))
                    at++;
            }

            static double Sum(string s, ref int at)
            {
                double v = Product(s, ref at);
                for (Skip(s, ref at); at < s.Length && (s[at] == '+' || s[at] == '-'); Skip(s, ref at))
                {
                    char op = s[at++];
                    double r = Product(s, ref at);
                    v = op == '+' ? v + r : v - r;
                }
                return v;
            }

            static double Product(string s, ref int at)
            {
                double v = Unary(s, ref at);
                for (Skip(s, ref at); at < s.Length && (s[at] == '*' || s[at] == '/'); Skip(s, ref at))
                {
                    char op = s[at++];
                    double r = Unary(s, ref at);
                    v = op == '*' ? v * r : v / r;
                }
                return v;
            }

            static double Unary(string s, ref int at)
            {
                Skip(s, ref at);
                if (at < s.Length && s[at] == '-')
                {
                    at++;
                    return -Unary(s, ref at);
                }
                if (at < s.Length && s[at] == '+')
                {
                    at++;
                    return Unary(s, ref at);
                }
                if (at < s.Length && s[at] == '(')
                {
                    at++;
                    double v = Sum(s, ref at);
                    Skip(s, ref at);
                    if (at >= s.Length || s[at] != ')')
                        throw new FormatException();
                    at++;
                    return v;
                }
                int start = at;
                while (at < s.Length && (char.IsDigit(s[at]) || s[at] == '.' || s[at] == 'e' || s[at] == 'E' ||
                                         (at > start && (s[at] == '-' || s[at] == '+') && (s[at - 1] == 'e' || s[at - 1] == 'E'))))
                    at++;
                if (at == start || !double.TryParse(s.Substring(start, at - start), NumberStyles.Float, CultureInfo.InvariantCulture, out double n))
                    throw new FormatException();
                return n;
            }
        }

        // ------------------------------------------------------------------------------------------------- toggles, sliders

        public static bool Toggle(Rect position, bool value) => Toggle(position, (GuiContent)null, value);
        public static bool Toggle(Rect position, string label, bool value) => Toggle(position, C(label), value);

        public static bool Toggle(Rect position, GuiContent label, bool value)
        {
            Rect field = Prefix(position, label, out _);
            bool result = Gui.Toggle(new Rect(field.x, field.y, Math.Min(field.width, field.height), field.height), showMixedValue ? false : value, "",
                                     EditorStyles.toggle);
            if (result != (showMixedValue ? false : value))
            {
                Gui.changed = true;
                return showMixedValue ? true : result;
            }
            return value;
        }

        public static bool ToggleLeft(Rect position, string label, bool value) => ToggleLeft(position, C(label), value);

        public static bool ToggleLeft(Rect position, GuiContent label, bool value)
        {
            bool result = Gui.Toggle(IndentedRect(position), value, label ?? new GuiContent(""), EditorStyles.toggle);
            if (result != value)
                Gui.changed = true;
            return result;
        }

        public static float Slider(Rect position, float value, float leftValue, float rightValue) => Slider(position, (GuiContent)null, value, leftValue, rightValue);
        public static float Slider(Rect position, string label, float value, float leftValue, float rightValue) => Slider(position, C(label), value, leftValue, rightValue);

        public static float Slider(Rect position, GuiContent label, float value, float leftValue, float rightValue)
        {
            Rect field = Prefix(position, label, out _);
            float numberWidth = EditorGuiUtility.fieldWidth;
            var bar = new Rect(field.x, field.y, Math.Max(0, field.width - numberWidth - 5), field.height);
            var number = new Rect(field.xMax - numberWidth, field.y, numberWidth, field.height);
            float slid = Gui.HorizontalSlider(bar, value, leftValue, rightValue);
            if (slid != value)
            {
                value = slid;
                Gui.changed = true;
            }
            int was = indentLevel;
            indentLevel = 0;
            float typed = FloatField(number, value);
            indentLevel = was;
            return Math.Clamp(typed, Math.Min(leftValue, rightValue), Math.Max(leftValue, rightValue));
        }

        public static int IntSlider(Rect position, int value, int leftValue, int rightValue) => IntSlider(position, (GuiContent)null, value, leftValue, rightValue);
        public static int IntSlider(Rect position, string label, int value, int leftValue, int rightValue) => IntSlider(position, C(label), value, leftValue, rightValue);

        public static int IntSlider(Rect position, GuiContent label, int value, int leftValue, int rightValue) =>
            (int)Math.Round(Slider(position, label, value, leftValue, rightValue));

        // ------------------------------------------------------------------------------------------------- popups

        // Menus' answers by the control that opened them, taken by its next pass.
        static readonly Dictionary<int, object> s_Answers = new();
        static readonly int s_PopupHint = "EditorGuiPopup".GetHashCode();

        static bool Answer<T>(int id, out T value)
        {
            if (s_Answers.TryGetValue(id, out object a) && a is T t)
            {
                s_Answers.Remove(id);
                value = t;
                Gui.changed = true;
                return true;
            }
            value = default;
            return false;
        }

        internal static void AnswerInternal(int id, object value)
        {
            s_Answers[id] = value;
            EditorWindow.drawingInternal?.Repaint();
        }

        // A button that opens a menu below it.
        static bool DropdownField(Rect field, string text, int id)
        {
            Event e = Event.current;
            Gui.Box(field, new GuiContent(showMixedValue ? "-" : text ?? ""), EditorStyles.popup);
            if (e != null && e.type == EventType.MouseDown && e.button == 0 && field.Contains(e.mousePosition) && Gui.enabled)
            {
                e.Use();
                return true;
            }
            return false;
        }

        public static int Popup(Rect position, int selectedIndex, string[] displayedOptions) => Popup(position, (GuiContent)null, selectedIndex, displayedOptions);
        public static int Popup(Rect position, string label, int selectedIndex, string[] displayedOptions) => Popup(position, C(label), selectedIndex, displayedOptions);

        public static int Popup(Rect position, GuiContent label, int selectedIndex, string[] displayedOptions)
        {
            displayedOptions ??= Array.Empty<string>();
            Rect field = Prefix(position, label, out _);
            int id = GuiUtility.GetControlID(s_PopupHint, FocusType.Keyboard, field);
            if (Answer(id, out int chosen))
                selectedIndex = chosen;
            string shown = selectedIndex >= 0 && selectedIndex < displayedOptions.Length ? displayedOptions[selectedIndex] : "";
            if (DropdownField(field, shown, id))
            {
                var menu = new GenericMenu { allowDuplicateNames = true };
                for (int i = 0; i < displayedOptions.Length; i++)
                {
                    int index = i;
                    if (displayedOptions[i] == "")
                        menu.AddSeparator("");
                    else
                        menu.AddItem(new GuiContent(displayedOptions[i]), i == selectedIndex, () => AnswerInternal(id, index));
                }
                menu.DropDown(field);
                lastMenuInternal = menu;
            }
            return selectedIndex;
        }

        public static int Popup(Rect position, GuiContent label, int selectedIndex, GuiContent[] displayedOptions) =>
            Popup(position, label, selectedIndex, Array.ConvertAll(displayedOptions ?? Array.Empty<GuiContent>(), c => c?.text ?? ""));

        public static int IntPopup(Rect position, int selectedValue, string[] displayedOptions, int[] optionValues) =>
            IntPopup(position, (GuiContent)null, selectedValue, displayedOptions, optionValues);

        public static int IntPopup(Rect position, string label, int selectedValue, string[] displayedOptions, int[] optionValues) =>
            IntPopup(position, C(label), selectedValue, displayedOptions, optionValues);

        public static int IntPopup(Rect position, GuiContent label, int selectedValue, string[] displayedOptions, int[] optionValues)
        {
            optionValues ??= Array.Empty<int>();
            int index = Popup(position, label, Array.IndexOf(optionValues, selectedValue), displayedOptions);
            return index >= 0 && index < optionValues.Length ? optionValues[index] : selectedValue;
        }

        // An enum's names as the inspector shows them (InspectorName, else the nicified name).
        internal static string[] EnumDisplayNames(Type type)
        {
            string[] names = Enum.GetNames(type);
            var shown = new string[names.Length];
            for (int i = 0; i < names.Length; i++)
            {
                var attr = (InspectorNameAttribute)Attribute.GetCustomAttribute(type.GetField(names[i]), typeof(InspectorNameAttribute));
                shown[i] = attr != null ? attr.displayName : SerializedFields.Nicify(names[i]);
            }
            return shown;
        }

        public static Enum EnumPopup(Rect position, Enum selected) => EnumPopup(position, (GuiContent)null, selected);
        public static Enum EnumPopup(Rect position, string label, Enum selected) => EnumPopup(position, C(label), selected);

        public static Enum EnumPopup(Rect position, GuiContent label, Enum selected)
        {
            if (selected == null)
                return null;
            Type t = selected.GetType();
            Array values = Enum.GetValues(t);
            int index = Array.IndexOf(values, selected);
            int chosen = Popup(position, label, index, EnumDisplayNames(t));
            return chosen >= 0 && chosen < values.Length ? (Enum)values.GetValue(chosen) : selected;
        }

        public static int MaskField(Rect position, int mask, string[] displayedOptions) => MaskField(position, (GuiContent)null, mask, displayedOptions);
        public static int MaskField(Rect position, string label, int mask, string[] displayedOptions) => MaskField(position, C(label), mask, displayedOptions);

        /// <summary>Flags by their names (Nothing, Everything, each option toggled).</summary>
        public static int MaskField(Rect position, GuiContent label, int mask, string[] displayedOptions)
        {
            displayedOptions ??= Array.Empty<string>();
            Rect field = Prefix(position, label, out _);
            int id = GuiUtility.GetControlID(s_PopupHint, FocusType.Keyboard, field);
            if (Answer(id, out int chosen))
                mask = chosen;
            int all = displayedOptions.Length >= 32 ? -1 : (1 << displayedOptions.Length) - 1;
            string shown = mask == 0 ? "Nothing" : (mask & all) == all ? "Everything" : Count(mask) == 1 ? displayedOptions[Low(mask)] : "Mixed...";
            if (DropdownField(field, shown, id))
            {
                var menu = new GenericMenu { allowDuplicateNames = true };
                int m = mask;
                menu.AddItem(new GuiContent("Nothing"), m == 0, () => AnswerInternal(id, 0));
                menu.AddItem(new GuiContent("Everything"), (m & all) == all, () => AnswerInternal(id, all));
                for (int i = 0; i < displayedOptions.Length; i++)
                {
                    int bit = 1 << i;
                    menu.AddItem(new GuiContent(displayedOptions[i]), (m & bit) != 0, () => AnswerInternal(id, m ^ bit));
                }
                menu.DropDown(field);
                lastMenuInternal = menu;
            }
            return mask;
        }

        static int Count(int m)
        {
            int n = 0;
            for (; m != 0; m &= m - 1)
                n++;
            return n;
        }

        static int Low(int m)
        {
            for (int i = 0; i < 32; i++)
                if ((m & (1 << i)) != 0)
                    return i;
            return 0;
        }

        public static Enum EnumFlagsField(Rect position, Enum enumValue) => EnumFlagsField(position, (GuiContent)null, enumValue);
        public static Enum EnumFlagsField(Rect position, string label, Enum enumValue) => EnumFlagsField(position, C(label), enumValue);

        public static Enum EnumFlagsField(Rect position, GuiContent label, Enum enumValue)
        {
            if (enumValue == null)
                return null;
            Type t = enumValue.GetType();
            // The single bit values as the mask's options.
            var names = new List<string>();
            var bits = new List<long>();
            string[] shown = EnumDisplayNames(t);
            Array values = Enum.GetValues(t);
            for (int i = 0; i < values.Length; i++)
            {
                long v = Convert.ToInt64(values.GetValue(i), CultureInfo.InvariantCulture);
                if (v != 0 && (v & (v - 1)) == 0)
                {
                    names.Add(shown[i]);
                    bits.Add(v);
                }
            }
            long current = Convert.ToInt64(enumValue, CultureInfo.InvariantCulture);
            int mask = 0;
            for (int i = 0; i < bits.Count; i++)
                if ((current & bits[i]) != 0)
                    mask |= 1 << i;
            int result = MaskField(position, label, mask, names.ToArray());
            if (result == mask)
                return enumValue;
            long flags = 0;
            for (int i = 0; i < bits.Count; i++)
                if ((result & (1 << i)) != 0)
                    flags |= bits[i];
            return (Enum)Enum.ToObject(t, flags);
        }

        public static int LayerField(Rect position, int layer) => LayerField(position, (GuiContent)null, layer);
        public static int LayerField(Rect position, string label, int layer) => LayerField(position, C(label), layer);

        public static int LayerField(Rect position, GuiContent label, int layer)
        {
            var names = new List<string>();
            var values = new List<int>();
            for (int i = 0; i < 32; i++)
            {
                string n = LayerMask.LayerToName(i);
                if (!string.IsNullOrEmpty(n))
                {
                    names.Add($"{i}: {n}");
                    values.Add(i);
                }
            }
            return IntPopup(position, label, layer, names.ToArray(), values.ToArray());
        }

        public static string TagField(Rect position, string tag) => TagField(position, (GuiContent)null, tag);
        public static string TagField(Rect position, string label, string tag) => TagField(position, C(label), tag);

        public static string TagField(Rect position, GuiContent label, string tag)
        {
            string[] tags = EditorGuiUtility.tagsInternal;
            int index = Popup(position, label, Array.IndexOf(tags, tag), tags);
            return index >= 0 && index < tags.Length ? tags[index] : tag;
        }

        // ------------------------------------------------------------------------------------------------- composites

        static readonly string[] s_Xyzw = { "X", "Y", "Z", "W" };

        // Several numbers side by side, each with its letter.
        static double[] Numbers(Rect position, GuiContent label, double[] values, string[] letters, bool integer)
        {
            Rect field = Prefix(position, label, out _);
            int n = values.Length;
            float each = (field.width - (n - 1) * kSpacing) / n;
            int wasIndent = indentLevel;
            float wasLabel = EditorGuiUtility.labelWidth;
            indentLevel = 0;
            EditorGuiUtility.labelWidth = 13;
            var result = (double[])values.Clone();
            for (int i = 0; i < n; i++)
            {
                var r = new Rect(field.x + i * (each + kSpacing), field.y, each, field.height);
                result[i] = Number(r, new GuiContent(letters[i]), values[i], integer, false);
            }
            EditorGuiUtility.labelWidth = wasLabel == 13 ? 0 : wasLabel;
            indentLevel = wasIndent;
            return result;
        }

        static Vector4 Floats(Rect position, GuiContent label, Vector4 v, int count, string[] letters = null)
        {
            double[] r = Numbers(position, label, new double[] { v.x, v.y, v.z, v.w }[..count], letters ?? s_Xyzw, false);
            return new Vector4((float)r[0], count > 1 ? (float)r[1] : 0, count > 2 ? (float)r[2] : 0, count > 3 ? (float)r[3] : 0);
        }

        public static Vector2 Vector2Field(Rect position, string label, Vector2 value) => Vector2Field(position, C(label), value);

        public static Vector2 Vector2Field(Rect position, GuiContent label, Vector2 value)
        {
            Vector4 r = Floats(position, label, new Vector4(value.x, value.y, 0, 0), 2);
            return new Vector2(r.x, r.y);
        }

        public static Vector3 Vector3Field(Rect position, string label, Vector3 value) => Vector3Field(position, C(label), value);

        public static Vector3 Vector3Field(Rect position, GuiContent label, Vector3 value)
        {
            Vector4 r = Floats(position, label, new Vector4(value.x, value.y, value.z, 0), 3);
            return new Vector3(r.x, r.y, r.z);
        }

        public static Vector4 Vector4Field(Rect position, string label, Vector4 value) => Vector4Field(position, C(label), value);
        public static Vector4 Vector4Field(Rect position, GuiContent label, Vector4 value) => Floats(position, label, value, 4);

        public static Vector2Int Vector2IntField(Rect position, string label, Vector2Int value) => Vector2IntField(position, C(label), value);

        public static Vector2Int Vector2IntField(Rect position, GuiContent label, Vector2Int value)
        {
            double[] r = Numbers(position, label, new double[] { value.x, value.y }, s_Xyzw, true);
            return new Vector2Int((int)r[0], (int)r[1]);
        }

        public static Vector3Int Vector3IntField(Rect position, string label, Vector3Int value) => Vector3IntField(position, C(label), value);

        public static Vector3Int Vector3IntField(Rect position, GuiContent label, Vector3Int value)
        {
            double[] r = Numbers(position, label, new double[] { value.x, value.y, value.z }, s_Xyzw, true);
            return new Vector3Int((int)r[0], (int)r[1], (int)r[2]);
        }

        static readonly string[] s_Rect = { "X", "Y", "W", "H" };

        public static Rect RectField(Rect position, Rect value) => RectField(position, (GuiContent)null, value);
        public static Rect RectField(Rect position, string label, Rect value) => RectField(position, C(label), value);

        public static Rect RectField(Rect position, GuiContent label, Rect value)
        {
            Vector4 r = Floats(position, label, new Vector4(value.x, value.y, value.width, value.height), 4, s_Rect);
            return new Rect(r.x, r.y, r.z, r.w);
        }

        public static Bounds BoundsField(Rect position, Bounds value) => BoundsField(position, (GuiContent)null, value);
        public static Bounds BoundsField(Rect position, string label, Bounds value) => BoundsField(position, C(label), value);

        /// <summary>Its center and extent on two lines (the rect holds both).</summary>
        public static Bounds BoundsField(Rect position, GuiContent label, Bounds value)
        {
            float h = position.height / 2;
            if (HasText(label))
                Gui.Label(IndentedRect(new Rect(position.x, position.y, EditorGuiUtility.labelWidth, h)), label, EditorStyles.label);
            Vector3 center = Vector3Field(new Rect(position.x, position.y, position.width, h), new GuiContent(HasText(label) ? " " : "Center"), value.center);
            Vector3 extent = Vector3Field(new Rect(position.x, position.y + h, position.width, h), new GuiContent(HasText(label) ? " " : "Extent"), value.extents);
            return new Bounds(center, extent * 2);
        }

        // ------------------------------------------------------------------------------------------------- colors, objects

        /// <summary>A swatch: a click opens the editor's color picker for it.</summary>
        public static Color ColorField(Rect position, Color value) => ColorField(position, (GuiContent)null, value);
        public static Color ColorField(Rect position, string label, Color value) => ColorField(position, C(label), value);

        public static Color ColorField(Rect position, GuiContent label, Color value)
        {
            Rect field = Prefix(position, label, out _);
            int id = GuiUtility.GetControlID(s_PopupHint, FocusType.Keyboard, field);
            if (Answer(id, out Color chosen))
                value = chosen;
            Event e = Event.current;
            if (e != null && e.type == EventType.Repaint)
            {
                Gui.Box(field, GuiContent.none, EditorStyles.colorField);
                var swatch = new Rect(field.x + 2, field.y + 2, field.width - 4, field.height - 6);
                DrawRect(swatch, showMixedValue ? new Color(0.5f, 0.5f, 0.5f, 1) : new Color(value.r, value.g, value.b, 1));
                // The alpha as a bar under the swatch.
                DrawRect(new Rect(swatch.x, swatch.yMax, swatch.width, 2), Color.black);
                DrawRect(new Rect(swatch.x, swatch.yMax, swatch.width * value.a, 2), Color.white);
            }
            else if (e != null && e.type == EventType.MouseDown && e.button == 0 && field.Contains(e.mousePosition) && Gui.enabled)
            {
                ColorPickerWindow.Open(value, c => AnswerInternal(id, c));
                e.Use();
            }
            return value;
        }

        public static Object ObjectField(Rect position, Object obj, Type objType, bool allowSceneObjects) =>
            ObjectField(position, (GuiContent)null, obj, objType, allowSceneObjects);

        public static Object ObjectField(Rect position, string label, Object obj, Type objType, bool allowSceneObjects) =>
            ObjectField(position, C(label), obj, objType, allowSceneObjects);

        /// <summary>The object's name and type; the picker beside it lists None, the project's assets of the type and
        /// (when allowed) the scenes' objects; a double click selects the object.</summary>
        public static Object ObjectField(Rect position, GuiContent label, Object obj, Type objType, bool allowSceneObjects)
        {
            objType ??= typeof(Object);
            Rect field = Prefix(position, label, out _);
            int id = GuiUtility.GetControlID(s_PopupHint, FocusType.Keyboard, field);
            if (Answer(id, out Picked chosen))
                obj = chosen.value;
            float pick = Math.Min(field.height, 18);
            var box = new Rect(field.x, field.y, field.width - pick, field.height);
            var picker = new Rect(field.xMax - pick, field.y, pick, field.height);
            Gui.Box(box, showMixedValue ? new GuiContent("-") : EditorGuiUtility.ObjectContent(obj, objType), EditorStyles.objectField);
            Gui.Box(picker, new GuiContent("o"), EditorStyles.miniButton);
            Event e = Event.current;
            if (e != null && e.type == EventType.MouseDown && e.button == 0 && Gui.enabled)
            {
                if (picker.Contains(e.mousePosition))
                {
                    ObjectMenu(objType, allowSceneObjects, obj, o => AnswerInternal(id, new Picked { value = o })).DropDown(field);
                    e.Use();
                }
                else if (box.Contains(e.mousePosition) && e.clickCount == 2 && obj != null)
                {
                    EditorGuiUtility.PingObject(obj);
                    e.Use();
                }
            }
            return obj;
        }

        // A picked object as an answer (None too).
        sealed class Picked
        {
            public Object value;
        }

        // The objects a field of the type may take.
        internal static GenericMenu ObjectMenu(Type type, bool allowSceneObjects, Object current, Action<Object> picked)
        {
            var menu = new GenericMenu { allowDuplicateNames = true };
            menu.AddItem(new GuiContent("None"), current == null, () => picked(null));
            var seen = new HashSet<Object>();
            foreach (string guid in AssetDatabase.FindAssets(""))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (AssetDatabase.IsValidFolder(path))
                    continue;
                Type main = AssetDatabase.GetMainAssetTypeAtPath(path);
                if (main == null || !type.IsAssignableFrom(main) && !(typeof(Component).IsAssignableFrom(type) && main == typeof(GameObject)))
                    continue;
                Object o = AssetDatabase.LoadAssetAtPath(path, type == typeof(Object) ? main : type);
                if (o != null && seen.Add(o))
                {
                    Object target = o;
                    menu.AddItem(new GuiContent("Assets/" + path.Substring(path.IndexOf('/') + 1)), o == current, () => picked(target));
                }
            }
            if (allowSceneObjects && (typeof(GameObject).IsAssignableFrom(type) || typeof(Component).IsAssignableFrom(type) || type == typeof(Object)))
                for (int i = 0; i < SceneManagement.EditorSceneManager.sceneCount; i++)
                    foreach (GameObject root in SceneManagement.EditorSceneManager.GetSceneAt(i).GetRootGameObjects())
                        AddSceneObjects(menu, root, root.name, type, current, picked);
            return menu;
        }

        static void AddSceneObjects(GenericMenu menu, GameObject go, string path, Type type, Object current, Action<Object> picked)
        {
            Object o = type == typeof(GameObject) || type == typeof(Object) ? go : go.GetComponent(type);
            if (o != null)
            {
                Object target = o;
                menu.AddItem(new GuiContent("Scene/" + path), o == current, () => picked(target));
            }
            for (int i = 0; i < go.transform.childCount; i++)
            {
                GameObject c = go.transform.GetChild(i).gameObject;
                AddSceneObjects(menu, c, path + " > " + c.name, type, current, picked);
            }
        }

        // ------------------------------------------------------------------------------------------------- foldouts

        static readonly int s_FoldoutHint = "EditorGuiFoldout".GetHashCode();

        public static bool Foldout(Rect position, bool foldout, string content) => Foldout(position, foldout, C(content), false, EditorStyles.foldout);
        public static bool Foldout(Rect position, bool foldout, GuiContent content) => Foldout(position, foldout, content, false, EditorStyles.foldout);
        public static bool Foldout(Rect position, bool foldout, string content, bool toggleOnLabelClick) => Foldout(position, foldout, C(content), toggleOnLabelClick, EditorStyles.foldout);
        public static bool Foldout(Rect position, bool foldout, GuiContent content, bool toggleOnLabelClick) => Foldout(position, foldout, content, toggleOnLabelClick, EditorStyles.foldout);

        /// <summary>An arrow (and its label): a click on the arrow, or on the label when asked, opens and closes it.</summary>
        public static bool Foldout(Rect position, bool foldout, GuiContent content, bool toggleOnLabelClick, GuiStyle style)
        {
            Rect r = IndentedRect(position);
            int id = GuiUtility.GetControlID(s_FoldoutHint, FocusType.Keyboard, r);
            var arrow = new Rect(r.x, r.y, 14, r.height);
            Event e = Event.current;
            if (e != null && e.type == EventType.Repaint)
            {
                Gui.Label(arrow, foldout ? "v" : ">", EditorStyles.miniLabel);
                Gui.Label(new Rect(r.x + 14, r.y, Math.Max(0, r.width - 14), r.height), content ?? new GuiContent(""), EditorStyles.label);
            }
            else if (e != null && e.GetTypeForControl(id) == EventType.MouseDown && e.button == 0 &&
                     (arrow.Contains(e.mousePosition) || toggleOnLabelClick && r.Contains(e.mousePosition)))
            {
                foldout = !foldout;
                Gui.changed = true;
                e.Use();
            }
            return foldout;
        }

        public static bool DropdownButton(Rect position, GuiContent content, FocusType focusType) => DropdownButton(position, content, focusType, EditorStyles.popup);

        public static bool DropdownButton(Rect position, GuiContent content, FocusType focusType, GuiStyle style)
        {
            int id = GuiUtility.GetControlID(s_PopupHint, focusType, position);
            Gui.Box(position, content ?? new GuiContent(""), style ?? EditorStyles.popup);
            Event e = Event.current;
            if (e != null && e.GetTypeForControl(id) == EventType.MouseDown && e.button == 0 && position.Contains(e.mousePosition) && Gui.enabled)
            {
                e.Use();
                return true;
            }
            return false;
        }

        // The menu a field opened last (for tests).
        internal static GenericMenu lastMenuInternal;
    }
}
