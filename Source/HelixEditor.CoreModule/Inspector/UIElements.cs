using System;
using System.Collections.Generic;
using System.Globalization;
using HelixEngine;
using HelixEngine.UI;
using Object = HelixEngine.Object;

namespace HelixEditor.UIElements
{
    /// <summary>Elements bound to SerializedObjects: each bound control shows its property's value and writes its
    /// changes back (applied at once); the bound tree reads its objects again a few times a second.</summary>
    public static class BindingExtensions
    {
        sealed class Binding
        {
            public SerializedObject obj;
            public readonly List<Action> refresh = new();
            public IElementScheduledItem tick;
        }

        static readonly Dictionary<Element, Binding> s_Bound = new();

        /// <summary>The element's descendants with binding paths bound to the object's properties.</summary>
        public static void Bind(this Element element, SerializedObject obj)
        {
            if (element == null || obj == null)
                return;
            Unbind(element);
            var b = new Binding { obj = obj };
            s_Bound[element] = b;
            BindTree(element, obj, b);
            b.tick = element.schedule.Execute(() =>
            {
                if (obj.m_Handle == 0)
                    return;
                obj.Update();
                foreach (Action r in b.refresh)
                    r();
            }).Every(200);
        }

        public static void Unbind(this Element element)
        {
            if (element != null && s_Bound.Remove(element, out Binding b))
                b.tick?.Pause();
        }

        static void BindTree(Element e, SerializedObject obj, Binding b)
        {
            if (e is PropertyField pf && pf.boundPropertyInternal == null && !string.IsNullOrEmpty(pf.bindingPath))
            {
                if (obj.FindProperty(pf.bindingPath) is SerializedProperty p)
                    pf.BindInternal(p, b.refresh);
                return;
            }
            if (e is BindableElement be && !(e is PropertyField) && !string.IsNullOrEmpty(be.bindingPath) && obj.FindProperty(be.bindingPath) is SerializedProperty prop)
                BindControl(be, prop, b.refresh);
            foreach (Element c in e.Children())
                BindTree(c, obj, b);
        }

        public static void BindProperty(this BindableElement field, SerializedObject obj)
        {
            if (field != null && obj != null && obj.FindProperty(field.bindingPath ?? "") is SerializedProperty p)
                BindProperty(field, p);
        }

        public static void BindProperty(this BindableElement field, SerializedProperty property)
        {
            if (field == null || property == null)
                return;
            field.bindingPath = property.propertyPath;
            var b = new Binding { obj = property.serializedObject };
            s_Bound[field] = b;
            if (field is PropertyField pf)
                pf.BindInternal(property, b.refresh);
            else
                BindControl(field, property, b.refresh);
            b.tick = field.schedule.Execute(() =>
            {
                if (b.obj.m_Handle == 0)
                    return;
                b.obj.Update();
                foreach (Action r in b.refresh)
                    r();
            }).Every(200);
        }

        /// <summary>A callback each time the property's value changes (seen as the tree reads its object again).</summary>
        public static void TrackPropertyValue(this Element element, SerializedProperty property, Action<SerializedProperty> callback = null)
        {
            if (element == null || property == null)
                return;
            SerializedObject obj = property.serializedObject;
            string path = property.propertyPath;
            string last = Snapshot(obj.FindProperty(path));
            element.schedule.Execute(() =>
            {
                if (obj.m_Handle == 0)
                    return;
                obj.Update();
                SerializedProperty now = obj.FindProperty(path);
                string text = Snapshot(now);
                if (text != last)
                {
                    last = text;
                    callback?.Invoke(now);
                    using var e = SerializedPropertyChangeEvent.GetPooled(now);
                    e.target = element;
                    element.SendEvent(e);
                }
            }).Every(200);
        }

        public static void TrackSerializedObjectValue(this Element element, SerializedObject obj, Action<SerializedObject> callback = null)
        {
            if (element == null || obj == null)
                return;
            string last = Snapshot(obj.GetIterator());
            element.schedule.Execute(() =>
            {
                if (obj.m_Handle == 0)
                    return;
                obj.Update();
                string text = Snapshot(obj.GetIterator());
                if (text != last)
                {
                    last = text;
                    callback?.Invoke(obj);
                }
            }).Every(200);
        }

        // The values below a property as text (to see a change).
        static string Snapshot(SerializedProperty p)
        {
            if (p == null)
                return "";
            var sb = new System.Text.StringBuilder();
            SerializedProperty it = p.Copy();
            SerializedProperty end = p.indexInternal < 0 ? null : p.GetEndProperty(true);
            if (p.indexInternal < 0)
                it.Next(true);
            do
            {
                sb.Append(it.propertyPath).Append('=').Append(it.longValue).Append(',').Append(it.doubleValue.ToString(CultureInfo.InvariantCulture))
                  .Append(',').Append(it.stringValue).Append(',').Append(it.objectReferenceValue?.GetEntityId().value ?? 0).Append(';');
            } while (it.Next(true) && (end == null || it.indexInternal < end.indexInternal));
            return sb.ToString();
        }

        // A control of the property's value: shown now and again as the object changes, its changes applied.
        internal static void BindControl(Element control, SerializedProperty property, List<Action> refresh)
        {
            SerializedObject obj = property.serializedObject;
            string path = property.propertyPath;
            SerializedProperty P() => obj.FindProperty(path);
            void Hook<T>(INotifyValueChanged<T> c, Func<SerializedProperty, T> read, Action<SerializedProperty, T> write)
            {
                c.SetValueWithoutNotify(read(property));
                c.RegisterValueChangedCallback(e =>
                {
                    SerializedProperty p = P();
                    if (p == null)
                        return;
                    write(p, e.newValue);
                    obj.ApplyModifiedProperties();
                });
                refresh.Add(() =>
                {
                    SerializedProperty p = P();
                    if (p != null && !EqualityComparer<T>.Default.Equals(c.value, read(p)))
                        c.SetValueWithoutNotify(read(p));
                });
            }
            switch (control)
            {
            case IntegerField f: Hook(f, p => p.intValue, (p, v) => p.intValue = v); break;
            case LongField f: Hook(f, p => p.longValue, (p, v) => p.longValue = v); break;
            case FloatField f: Hook(f, p => p.floatValue, (p, v) => p.floatValue = v); break;
            case DoubleField f: Hook(f, p => p.doubleValue, (p, v) => p.doubleValue = v); break;
            case TextField f: Hook(f, p => p.stringValue, (p, v) => p.stringValue = v); break;
            case Toggle f: Hook(f, p => p.boolValue, (p, v) => p.boolValue = v); break;
            case Slider f: Hook(f, p => p.floatValue, (p, v) => p.floatValue = v); break;
            case SliderInt f: Hook(f, p => p.intValue, (p, v) => p.intValue = v); break;
            case Vector2Field f: Hook(f, p => p.vector2Value, (p, v) => p.vector2Value = v); break;
            case Vector3Field f: Hook(f, p => p.vector3Value, (p, v) => p.vector3Value = v); break;
            case Vector4Field f: Hook(f, p => p.vector4Value, (p, v) => p.vector4Value = v); break;
            case Vector2IntField f: Hook(f, p => p.vector2IntValue, (p, v) => p.vector2IntValue = v); break;
            case Vector3IntField f: Hook(f, p => p.vector3IntValue, (p, v) => p.vector3IntValue = v); break;
            case RectField f: Hook(f, p => p.rectValue, (p, v) => p.rectValue = v); break;
            case BoundsField f: Hook(f, p => p.boundsValue, (p, v) => p.boundsValue = v); break;
            case ColorField f: Hook(f, p => p.colorValue, (p, v) => p.colorValue = v); break;
            case ObjectField f: Hook(f, p => p.objectReferenceValue, (p, v) => p.objectReferenceValue = v); break;
            case LayerField f: Hook(f, p => p.intValue, (p, v) => p.intValue = v); break;
            case TagField f: Hook(f, p => p.stringValue, (p, v) => p.stringValue = v); break;
            case EnumField f:
                if (EnumTypeOf(property) is Type t)
                {
                    f.Init((Enum)Enum.ToObject(t, property.longValue));
                    Hook(f, p => (Enum)Enum.ToObject(t, p.longValue),
                         (p, v) => p.longValue = Convert.ToInt64(v, CultureInfo.InvariantCulture));
                }
                break;
            case DropdownField f:
                f.choices = new List<string>(property.enumDisplayNames);
                Hook(f, p => p.enumValueIndex >= 0 && p.enumValueIndex < f.choices.Count ? f.choices[p.enumValueIndex] : "",
                     (p, v) => p.enumValueIndex = f.choices.IndexOf(v));
                break;
            }
        }

        internal static Type EnumTypeOf(SerializedProperty p)
        {
            Editors.FieldOf(p.serializedObject.targetObject.GetType(), p.propertyPath, out Type t);
            return t != null && t.IsEnum ? t : null;
        }
    }

    /// <summary>Raised on an element tracking a property when its value changed.</summary>
    public class SerializedPropertyChangeEvent : EventBase<SerializedPropertyChangeEvent>
    {
        public SerializedProperty changedProperty { get; private set; }

        public static SerializedPropertyChangeEvent GetPooled(SerializedProperty value)
        {
            SerializedPropertyChangeEvent e = GetPooled();
            e.changedProperty = value;
            return e;
        }
    }

    /// <summary>A property's field as the retained UI shows it: its drawer's own GUI, else the control of its type (a
    /// foldout of its children for structs and arrays), else its IMGUI drawer in an ImguiContainer.</summary>
    public class PropertyField : BindableElement
    {
        public static readonly string hssClassName = "helix-property-field";
        public static readonly string labelHssClassName = hssClassName + "__label";
        public static readonly string inputHssClassName = hssClassName + "__input";

        SerializedProperty m_Property;

        public string label { get; set; }

        public PropertyField() => AddToClassList(hssClassName);
        public PropertyField(SerializedProperty property) : this(property, null) { }

        public PropertyField(SerializedProperty property, string label) : this()
        {
            this.label = label;
            if (property != null)
            {
                bindingPath = property.propertyPath;
                m_Property = property.Copy();
            }
        }

        internal SerializedProperty boundPropertyInternal => m_Property != null && childCount > 0 ? m_Property : null;

        /// <summary>The field's controls made for its property.</summary>
        internal void BindInternal(SerializedProperty property, List<Action> refresh)
        {
            m_Property = property.Copy();
            Clear();
            Add(Make(m_Property, label ?? m_Property.displayName, refresh));
        }

        // Built at once when made with a property (the inspector binds it after).
        public void BindProperty(SerializedProperty property) => BindingExtensions.BindProperty(this, property);

        static Element Make(SerializedProperty p, string label, List<Action> refresh)
        {
            Editors.Handler h = Editors.HandlerOf(p);
            if (h.drawer != null)
            {
                Element custom = h.drawer.CreatePropertyGui(p.Copy());
                if (custom != null)
                {
                    custom.Bind(p.serializedObject);
                    return custom;
                }
                // The IMGUI drawer in a container of its height.
                SerializedObject obj = p.serializedObject;
                string path = p.propertyPath;
                var gui = new ImguiContainer();
                gui.onGuiHandler = () =>
                {
                    SerializedProperty now = obj.FindProperty(path);
                    if (now == null)
                        return;
                    obj.Update();
                    float height = EditorGui.GetPropertyHeight(now, new GuiContent(label), true);
                    gui.style.height = height;
                    EditorGui.PropertyField(new Rect(0, 0, gui.layout.width, height), now, new GuiContent(label), true);
                    obj.ApplyModifiedProperties();
                };
                return gui;
            }
            Element control = p.propertyType switch
            {
                SerializedPropertyType.Integer => p.numericType == SerializedPropertyNumericType.Int64 || p.numericType == SerializedPropertyNumericType.UInt64
                                                      ? new LongField(label) : new IntegerField(label),
                SerializedPropertyType.Boolean => new Toggle(label),
                SerializedPropertyType.Float => p.numericType == SerializedPropertyNumericType.Double ? new DoubleField(label) : new FloatField(label),
                SerializedPropertyType.String => new TextField(label),
                SerializedPropertyType.Color => new ColorField(label),
                SerializedPropertyType.ObjectReference => new ObjectField(label) { objectType = ObjectTypeOf(p), allowSceneObjects = !AssetDatabase.Contains(p.serializedObject.targetObject) },
                SerializedPropertyType.Enum => BindingExtensions.EnumTypeOf(p) != null ? new EnumField(label) : new DropdownField(label),
                SerializedPropertyType.Vector2 => new Vector2Field(label),
                SerializedPropertyType.Vector3 => new Vector3Field(label),
                SerializedPropertyType.Vector4 => new Vector4Field(label),
                SerializedPropertyType.Vector2Int => new Vector2IntField(label),
                SerializedPropertyType.Vector3Int => new Vector3IntField(label),
                SerializedPropertyType.Rect => new RectField(label),
                SerializedPropertyType.Bounds => new BoundsField(label),
                SerializedPropertyType.ArraySize => new IntegerField(label) { isDelayed = true },
                _ => null,
            };
            if (control != null)
            {
                BindingExtensions.BindControl(control, p, refresh);
                return control;
            }
            // A struct or an array: a foldout of its children's fields.
            var foldout = new Foldout { text = label, value = p.isExpanded };
            SerializedProperty keep = p.Copy();
            foldout.RegisterValueChangedCallback(e => keep.isExpanded = e.newValue);
            SerializedProperty end = p.GetEndProperty(true);
            SerializedProperty child = p.Copy();
            for (bool enter = true; child.NextVisible(enter) && (end == null || child.indexInternal < end.indexInternal); enter = false)
            {
                var field = new PropertyField(child);
                field.BindInternal(child, refresh);
                foldout.Add(field);
            }
            return foldout;
        }

        static Type ObjectTypeOf(SerializedProperty p)
        {
            Editors.FieldOf(p.serializedObject.targetObject.GetType(), p.propertyPath, out Type t);
            return t != null && typeof(Object).IsAssignableFrom(t) ? t : typeof(Object);
        }
    }

    /// <summary>An object's inspector as the retained UI shows it: its editor's CreateInspectorGui, else the editor's
    /// own OnInspectorGui in an ImguiContainer, else a PropertyField for each visible property.</summary>
    public class InspectorElement : BindableElement
    {
        public static readonly string hssClassName = "helix-inspector-element";
        public static readonly string noInspectorFoundVariantHssClassName = hssClassName + "__no-inspector-found";
        public static readonly string customInspectorVariantHssClassName = hssClassName + "__custom-inspector";
        public static readonly string imguiInspectorVariantHssClassName = hssClassName + "__imgui-inspector";

        Editor m_Editor;
        bool m_OwnEditor;

        public InspectorElement() => AddToClassList(hssClassName);
        public InspectorElement(Object obj) : this(Editor.CreateEditor(obj), true) { }
        public InspectorElement(SerializedObject obj) : this(Editor.CreateEditor(obj.targetObjects), true) { }
        public InspectorElement(Editor editor) : this(editor, false) { }

        InspectorElement(Editor editor, bool own) : this()
        {
            m_Editor = editor;
            m_OwnEditor = own;
            Build();
            RegisterCallback<DetachFromPanelEvent>(e => Release());
        }

        public Editor editorInternal => m_Editor;

        void Release()
        {
            this.Unbind();
            if (m_OwnEditor && m_Editor != null)
                Object.DestroyImmediate(m_Editor);
            m_Editor = null;
        }

        void Build()
        {
            Clear();
            if (m_Editor == null)
            {
                AddToClassList(noInspectorFoundVariantHssClassName);
                return;
            }
            Element custom = m_Editor.CreateInspectorGui();
            if (custom != null)
            {
                AddToClassList(customInspectorVariantHssClassName);
                Add(custom);
                custom.Bind(m_Editor.serializedObject);
                return;
            }
            if (m_Editor.hasOwnInspectorGuiInternal)
            {
                AddToClassList(imguiInspectorVariantHssClassName);
                Editor editor = m_Editor;
                var gui = new ImguiContainer();
                gui.style.minHeight = EditorGuiUtility.singleLineHeight;
                // The container as tall as what the editor laid out (measured as it paints).
                gui.onGuiHandler = () =>
                {
                    if (editor == null)
                        return;
                    EditorGuiUtility.currentViewWidth = gui.layout.width;
                    GuiLayout.BeginVertical();
                    editor.OnInspectorGui();
                    GuiLayout.EndVertical();
                    if (Event.current != null && Event.current.type == EventType.Repaint)
                    {
                        float height = GuiLayoutUtility.GetLastRect().yMax + 2;
                        if (height > 2 && Math.Abs(height - gui.layout.height) > 0.5f)
                            gui.style.height = height;
                    }
                };
                Add(gui);
                return;
            }
            FillDefaultInspector(this, m_Editor.serializedObject, m_Editor);
            this.Bind(m_Editor.serializedObject);
        }

        /// <summary>A PropertyField for each visible property of the object (the script's reference greyed).</summary>
        public static void FillDefaultInspector(Element container, SerializedObject serializedObject, Editor editor)
        {
            if (container == null || serializedObject == null)
                return;
            SerializedProperty it = serializedObject.GetIterator();
            for (bool enter = true; it.NextVisible(enter); enter = false)
            {
                var field = new PropertyField(it.Copy()) { name = "PropertyField:" + it.propertyPath };
                if (it.propertyPath == "m_Script")
                    field.SetEnabled(false);
                container.Add(field);
            }
        }
    }

    /// <summary>A field of an object reference: its name and type, a picker of the objects it may take.</summary>
    public class ObjectField : BaseField<Object>
    {
        public static new readonly string hssClassName = "helix-object-field";
        readonly Label m_Display;

        public Type objectType { get; set; } = typeof(Object);
        public bool allowSceneObjects { get; set; } = true;

        public ObjectField() : this(null) { }

        public ObjectField(string label) : base(label, null)
        {
            AddToClassList(hssClassName);
            var input = new Element();
            input.AddToClassList(hssClassName + "__input");
            input.style.flexDirection = FlexDirection.Row;
            m_Display = new Label();
            m_Display.AddToClassList(hssClassName + "__display");
            m_Display.style.flexGrow = 1;
            var picker = new Button(Pick) { text = "o" };
            picker.AddToClassList(hssClassName + "__selector");
            input.Add(m_Display);
            input.Add(picker);
            visualInput = input;
            Show(null);
            RegisterCallback<ChangeEvent<Object>>(e => Show(e.newValue));
        }

        public override void SetValueWithoutNotify(Object newValue)
        {
            base.SetValueWithoutNotify(newValue);
            Show(newValue);
        }

        void Show(Object v) => m_Display.text = EditorGuiUtility.ObjectContent(v, objectType).text;

        void Pick()
        {
            GenericMenu menu = EditorGui.ObjectMenu(objectType ?? typeof(Object), allowSceneObjects, value, o => value = o);
            menu.DropDownInternal(this);
        }
    }

    /// <summary>A color's swatch: a click opens the editor's color picker.</summary>
    public class ColorField : BaseField<Color>
    {
        public static new readonly string hssClassName = "helix-color-field";
        readonly Element m_Swatch;

        public bool showAlpha { get; set; } = true;
        public bool hdr { get; set; }

        public ColorField() : this(null) { }

        public ColorField(string label) : base(label, null)
        {
            AddToClassList(hssClassName);
            m_Swatch = new Element();
            m_Swatch.AddToClassList(hssClassName + "__color");
            m_Swatch.style.flexGrow = 1;
            m_Swatch.style.minHeight = 14;
            visualInput = m_Swatch;
            m_Swatch.RegisterCallback<PointerDownEvent>(e => ColorPickerWindow.Open(value, c => value = c));
            RegisterCallback<ChangeEvent<Color>>(e => Show(e.newValue));
            Show(value);
        }

        public override void SetValueWithoutNotify(Color newValue)
        {
            base.SetValueWithoutNotify(newValue);
            Show(newValue);
        }

        void Show(Color c) => m_Swatch.style.backgroundColor = new Color(c.r, c.g, c.b, 1);
    }

    /// <summary>A layer by its name.</summary>
    public class LayerField : BaseField<int>
    {
        readonly DropdownField m_Menu = new DropdownField();

        public LayerField() : this(null) { }

        public LayerField(string label) : base(label, null)
        {
            AddToClassList("helix-layer-field");
            var names = new List<string>();
            for (int i = 0; i < 32; i++)
                names.Add(string.IsNullOrEmpty(LayerMask.LayerToName(i)) ? $"Layer {i}" : $"{i}: {LayerMask.LayerToName(i)}");
            m_Menu.choices = names;
            m_Menu.RegisterValueChangedCallback(e => value = Math.Max(0, m_Menu.choices.IndexOf(e.newValue)));
            visualInput = m_Menu;
            SetValueWithoutNotify(0);
        }

        public override void SetValueWithoutNotify(int newValue)
        {
            base.SetValueWithoutNotify(newValue);
            if (newValue >= 0 && newValue < m_Menu.choices.Count)
                m_Menu.SetValueWithoutNotify(m_Menu.choices[newValue]);
        }
    }

    /// <summary>A tag of the tag manager.</summary>
    public class TagField : BaseField<string>
    {
        readonly DropdownField m_Menu = new DropdownField();

        public TagField() : this(null) { }

        public TagField(string label) : base(label, null)
        {
            AddToClassList("helix-tag-field");
            m_Menu.choices = new List<string>(EditorGuiUtility.tagsInternal);
            m_Menu.RegisterValueChangedCallback(e => value = e.newValue);
            visualInput = m_Menu;
            SetValueWithoutNotify("Untagged");
        }

        public override void SetValueWithoutNotify(string newValue)
        {
            base.SetValueWithoutNotify(newValue);
            m_Menu.SetValueWithoutNotify(newValue ?? "");
        }
    }
}
