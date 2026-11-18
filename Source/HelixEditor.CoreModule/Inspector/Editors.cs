using System;
using System.Collections.Generic;
using System.Reflection;
using HelixEngine;
using HelixEngine.UI;
using Object = HelixEngine.Object;

namespace HelixEditor
{
    /// <summary>The editor class of an inspected type (and its subclasses, when asked; a fallback gives way to others).</summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
    public sealed class CustomEditor : Attribute
    {
        public CustomEditor(Type inspectedType) => this.inspectedType = inspectedType;

        public CustomEditor(Type inspectedType, bool editorForChildClasses)
        {
            this.inspectedType = inspectedType;
            this.editorForChildClasses = editorForChildClasses;
        }

        public Type inspectedType { get; }
        public bool editorForChildClasses { get; }
        public bool isFallback { get; set; }
    }

    /// <summary>The editor edits several selected objects at once.</summary>
    [AttributeUsage(AttributeTargets.Class, Inherited = false)]
    public sealed class CanEditMultipleObjects : Attribute { }

    /// <summary>The drawer of a field type (and its subclasses, when asked) or of a PropertyAttribute.</summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
    public sealed class CustomPropertyDrawer : Attribute
    {
        public CustomPropertyDrawer(Type type) => this.type = type;

        public CustomPropertyDrawer(Type type, bool useForChildren)
        {
            this.type = type;
            this.useForChildren = useForChildren;
        }

        internal Type type { get; }
        internal bool useForChildren { get; }
    }

    /// <summary>An inspector of objects: its target(s), their SerializedObject, its GUI (OnInspectorGui, or the
    /// retained CreateInspectorGui); the default one draws every visible property.</summary>
    public class Editor : ScriptableObject
    {
        [ThreadStatic] static Object[] s_Pending;
        [ThreadStatic] static Object s_PendingContext;

        // Set before OnEnable runs (as the editor is made).
        Object[] m_Targets = s_Pending;
        Object m_Context = s_PendingContext;
        SerializedObject m_Serialized;

        public Object target
        {
            get => m_Targets != null && m_Targets.Length > 0 ? m_Targets[0] : null;
            set
            {
                m_Targets = value == null ? Array.Empty<Object>() : new[] { value };
                m_Serialized = null;
            }
        }

        public Object[] targets => m_Targets ?? Array.Empty<Object>();

        public SerializedObject serializedObject
        {
            get
            {
                if (m_Serialized == null && targets.Length > 0)
                    m_Serialized = new SerializedObject(targets, m_Context);
                return m_Serialized;
            }
        }

        /// <summary>Raised by Repaint: the inspectors of the editor's targets draw it again.</summary>
        internal static event Action<Editor> repaintRequested;

        public void Repaint()
        {
            repaintRequested?.Invoke(this);
            EditorWindow.drawingInternal?.Repaint();
        }

        public virtual void OnInspectorGui() => DrawDefaultInspector();

        /// <summary>Every visible property of the targets (the script's reference greyed), changes applied.</summary>
        public bool DrawDefaultInspector()
        {
            SerializedObject so = serializedObject;
            if (so == null)
                return false;
            EditorGui.BeginChangeCheck();
            so.UpdateIfRequiredOrScript();
            SerializedProperty it = so.GetIterator();
            for (bool enter = true; it.NextVisible(enter); enter = false)
                using (new EditorGui.DisabledScope(it.propertyPath == "m_Script"))
                    EditorGuiLayout.PropertyField(it, true);
            so.ApplyModifiedProperties();
            return EditorGui.EndChangeCheck();
        }

        public virtual Element CreateInspectorGui() => null;
        public virtual bool RequiresConstantRepaint() => false;
        public virtual bool UseDefaultMargins() => true;
        public virtual bool HasPreviewGui() => false;
        public virtual GuiContent GetPreviewTitle() => new GuiContent(target != null ? target.name : "");
        public virtual void OnPreviewGui(Rect r, GuiStyle background) { }
        public virtual string GetInfoString() => "";

        // Whether OnInspectorGui is the class's own (not the default inspector's).
        internal bool hasOwnInspectorGuiInternal =>
            GetType().GetMethod(nameof(OnInspectorGui), BindingFlags.Instance | BindingFlags.Public)?.DeclaringType != typeof(Editor);

        void OnDestroy()
        {
            m_Serialized?.Dispose();
            m_Serialized = null;
        }

        // ------------------------------------------------------------------------------------------- making editors

        public static Editor CreateEditor(Object targetObject) => CreateEditor(new[] { targetObject }, null);
        public static Editor CreateEditor(Object targetObject, Type editorType) => CreateEditor(new[] { targetObject }, editorType);
        public static Editor CreateEditor(Object[] targetObjects) => CreateEditor(targetObjects, null);
        public static Editor CreateEditor(Object[] targetObjects, Type editorType) => CreateEditorWithContext(targetObjects, null, editorType);

        /// <summary>The editor of the objects: the given type, else the registered editor of their type (the default
        /// inspector without one); null for no objects.</summary>
        public static Editor CreateEditorWithContext(Object[] targetObjects, Object context, Type editorType = null)
        {
            if (targetObjects == null || targetObjects.Length == 0 || Array.Exists(targetObjects, o => o == null))
                return null;
            editorType ??= Editors.For(targetObjects[0].GetType());
            if (editorType == null || !typeof(Editor).IsAssignableFrom(editorType))
                return null;
            s_Pending = (Object[])targetObjects.Clone();
            s_PendingContext = context;
            try
            {
                var e = (Editor)CreateInstance(editorType);
                e.hideFlags = HideFlags.HideAndDontSave;
                return e;
            }
            finally
            {
                s_Pending = null;
                s_PendingContext = null;
            }
        }

        public static void CreateCachedEditor(Object targetObject, Type editorType, ref Editor previousEditor) =>
            CreateCachedEditor(new[] { targetObject }, editorType, ref previousEditor);

        /// <summary>The previous editor kept when it is of the type and edits the same objects; else made anew.</summary>
        public static void CreateCachedEditor(Object[] targetObjects, Type editorType, ref Editor previousEditor)
        {
            Type wanted = editorType ?? (targetObjects != null && targetObjects.Length > 0 && targetObjects[0] != null ? Editors.For(targetObjects[0].GetType()) : null);
            if (previousEditor != null && previousEditor.GetType() == wanted && SameTargets(previousEditor.targets, targetObjects))
                return;
            if (previousEditor != null)
                DestroyImmediate(previousEditor);
            previousEditor = CreateEditor(targetObjects, editorType);
        }

        public static void CreateCachedEditorWithContext(Object targetObject, Object context, Type editorType, ref Editor previousEditor)
        {
            if (previousEditor != null)
                DestroyImmediate(previousEditor);
            previousEditor = CreateEditorWithContext(new[] { targetObject }, context, editorType);
        }

        static bool SameTargets(Object[] a, Object[] b)
        {
            if (a == null || b == null || a.Length != b.Length)
                return false;
            for (int i = 0; i < a.Length; i++)
                if (a[i] != b[i])
                    return false;
            return true;
        }
    }

    // The editor every type without one of its own gets.
    internal sealed class GenericInspector : Editor { }

    /// <summary>A field's drawer: its GUI at a rect, its height, or its retained GUI.</summary>
    public abstract class PropertyDrawer
    {
        public PropertyAttribute attribute { get; internal set; }
        public FieldInfo fieldInfo { get; internal set; }
        public string preferredLabel { get; internal set; }

        public virtual void OnGui(Rect position, SerializedProperty property, GuiContent label) =>
            EditorGui.LabelField(position, label, new GuiContent("No GUI implemented"));

        public virtual float GetPropertyHeight(SerializedProperty property, GuiContent label) => EditorGuiUtility.singleLineHeight;
        public virtual Element CreatePropertyGui(SerializedProperty property) => null;
        public virtual bool CanCacheInspectorGui(SerializedProperty property) => true;
    }

    /// <summary>What a PropertyAttribute draws above its field (a header, a space).</summary>
    public abstract class DecoratorDrawer
    {
        public PropertyAttribute attribute { get; internal set; }
        public virtual void OnGui(Rect position) { }
        public virtual float GetHeight() => EditorGuiUtility.singleLineHeight;
        public virtual Element CreatePropertyGui() => null;
        public virtual bool CanCacheInspectorGui() => true;
    }

    // The registries of editors and drawers (from the loaded assemblies' attributes), and a property's handler: its
    // decorators and its drawer.
    internal static class Editors
    {
        sealed class Entry
        {
            public Type type;
            public bool children, fallback;
        }

        static readonly Dictionary<Type, List<Entry>> s_Editors = new(), s_Drawers = new();
        static bool s_Started;

#pragma warning disable CA2255
        [System.Runtime.CompilerServices.ModuleInitializer]
#pragma warning restore CA2255
        internal static void Init() => HelixEngine.Internal.ScriptBridge.WhenBindingsLoaded(Start);

        static void Start()
        {
            if (s_Started)
                return;
            s_Started = true;
            HelixEngine.Internal.ScriptBridge.OnAssembly(Register);
        }

        static void Register(Assembly a)
        {
            Type[] types;
            try
            {
                types = a.GetTypes();
            }
            catch (ReflectionTypeLoadException e)
            {
                types = Array.FindAll(e.Types, t => t != null);
            }
            foreach (Type t in types)
            {
                if (t.IsAbstract)
                    continue;
                if (typeof(Editor).IsAssignableFrom(t))
                    foreach (CustomEditor c in t.GetCustomAttributes<CustomEditor>(false))
                        if (c.inspectedType != null)
                            Add(s_Editors, c.inspectedType, new Entry { type = t, children = c.editorForChildClasses, fallback = c.isFallback });
                if (typeof(PropertyDrawer).IsAssignableFrom(t) || typeof(DecoratorDrawer).IsAssignableFrom(t))
                    foreach (CustomPropertyDrawer c in t.GetCustomAttributes<CustomPropertyDrawer>(false))
                        if (c.type != null)
                            Add(s_Drawers, c.type, new Entry { type = t, children = c.useForChildren });
            }
        }

        static void Add(Dictionary<Type, List<Entry>> into, Type key, Entry e)
        {
            if (!into.TryGetValue(key, out List<Entry> list))
                into[key] = list = new List<Entry>();
            // The newest first (a project's own over the editor's); fallbacks last.
            list.Insert(0, e);
            list.Sort((x, y) => x.fallback.CompareTo(y.fallback));
        }

        // The registered class of a type: its own, else the nearest base's that takes children.
        static Type Find(Dictionary<Type, List<Entry>> registry, Type type, Func<Type, bool> accept = null)
        {
            for (Type t = type; t != null; t = t.BaseType)
                if (registry.TryGetValue(t, out List<Entry> list))
                    foreach (Entry e in list)
                        if ((t == type || e.children) && (accept == null || accept(e.type)))
                            return e.type;
            return null;
        }

        internal static Type For(Type inspected) => Find(s_Editors, inspected) ?? typeof(GenericInspector);
        internal static Type DrawerFor(Type type) => type == null ? null : Find(s_Drawers, type);

        // ------------------------------------------------------------------------------------------- handlers

        internal sealed class Handler
        {
            public readonly List<DecoratorDrawer> decorators = new();
            public PropertyDrawer drawer;
            public FieldInfo field;
            public Type fieldType;
        }

        static readonly Dictionary<(Type, string), Handler> s_Handlers = new();

        /// <summary>The handler of a property: its decorators (not on an array's items) and its drawer (by an attribute,
        /// not on an array itself; else by the field's type).</summary>
        internal static Handler HandlerOf(SerializedProperty property)
        {
            Type target = property.serializedObject.targetObject.GetType();
            string key = Indexless(property.propertyPath);
            if (s_Handlers.TryGetValue((target, key), out Handler cached))
                return cached;
            var h = new Handler();
            h.field = FieldOf(target, property.propertyPath, out h.fieldType);
            bool element = property.propertyPath.EndsWith("]", StringComparison.Ordinal);
            if (h.field != null)
            {
                var attrs = new List<PropertyAttribute>(h.field.GetCustomAttributes<PropertyAttribute>(true));
                attrs.Sort((a, b) => a.order.CompareTo(b.order));
                foreach (PropertyAttribute a in attrs)
                {
                    Type drawer = DrawerFor(a.GetType());
                    if (drawer == null)
                        continue;
                    if (typeof(DecoratorDrawer).IsAssignableFrom(drawer))
                    {
                        if (!element)
                        {
                            var d = (DecoratorDrawer)Activator.CreateInstance(drawer, true);
                            d.attribute = a;
                            h.decorators.Add(d);
                        }
                    }
                    else if (h.drawer == null && !property.isArray)
                    {
                        h.drawer = (PropertyDrawer)Activator.CreateInstance(drawer, true);
                        h.drawer.attribute = a;
                        h.drawer.fieldInfo = h.field;
                    }
                }
            }
            if (h.drawer == null && !property.isArray && h.fieldType != null && DrawerFor(h.fieldType) is Type byType &&
                typeof(PropertyDrawer).IsAssignableFrom(byType))
            {
                h.drawer = (PropertyDrawer)Activator.CreateInstance(byType, true);
                h.drawer.fieldInfo = h.field;
            }
            s_Handlers[(target, key)] = h;
            return h;
        }

        static string Indexless(string path)
        {
            var sb = new System.Text.StringBuilder(path.Length);
            bool skip = false;
            foreach (char c in path)
            {
                if (c == '[')
                    skip = true;
                else if (c == ']')
                    skip = false;
                else if (!skip)
                    sb.Append(c);
            }
            return sb.ToString();
        }

        /// <summary>The C# field a property path names on a type (null for fields only native code has), and the
        /// field's type (an array's item type for its items).</summary>
        internal static FieldInfo FieldOf(Type type, string path, out Type fieldType)
        {
            fieldType = null;
            FieldInfo field = null;
            Type current = type;
            string[] parts = path.Split('.');
            for (int i = 0; i < parts.Length; i++)
            {
                string p = parts[i];
                if (p == "Array" && i + 1 < parts.Length && parts[i + 1].StartsWith("data[", StringComparison.Ordinal))
                {
                    current = ItemType(current);
                    fieldType = current;
                    i++;
                    continue;
                }
                if (p == "Array" && i + 1 < parts.Length && parts[i + 1] == "size")
                {
                    fieldType = typeof(int);
                    return field;
                }
                FieldInfo f = null;
                for (Type t = current; t != null && f == null; t = t.BaseType)
                    f = t.GetField(p, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                if (f == null)
                {
                    fieldType = null;
                    return null;
                }
                field = f;
                current = f.FieldType;
                fieldType = current;
            }
            return field;
        }

        static Type ItemType(Type t)
        {
            if (t == null)
                return null;
            if (t.IsArray)
                return t.GetElementType();
            return t.IsGenericType && t.GetGenericArguments().Length == 1 ? t.GetGenericArguments()[0] : null;
        }
    }

    // ------------------------------------------------------------------------------------------- built in drawers

    [CustomPropertyDrawer(typeof(RangeAttribute))]
    internal sealed class RangeDrawer : PropertyDrawer
    {
        public override void OnGui(Rect position, SerializedProperty property, GuiContent label)
        {
            var range = (RangeAttribute)attribute;
            using var scope = new EditorGui.PropertyScope(position, label, property);
            EditorGui.BeginChangeCheck();
            if (property.propertyType == SerializedPropertyType.Float)
            {
                float v = EditorGui.Slider(position, scope.content, property.floatValue, range.min, range.max);
                if (EditorGui.EndChangeCheck())
                    property.floatValue = v;
            }
            else if (property.propertyType == SerializedPropertyType.Integer)
            {
                int v = EditorGui.IntSlider(position, scope.content, property.intValue, (int)range.min, (int)range.max);
                if (EditorGui.EndChangeCheck())
                    property.intValue = v;
            }
            else
            {
                EditorGui.EndChangeCheck();
                EditorGui.LabelField(position, scope.content, new GuiContent("Use Range with float or int."));
            }
        }
    }

    [CustomPropertyDrawer(typeof(MinAttribute))]
    internal sealed class MinDrawer : PropertyDrawer
    {
        public override void OnGui(Rect position, SerializedProperty property, GuiContent label)
        {
            float min = ((MinAttribute)attribute).min;
            EditorGui.DefaultPropertyFieldInternal(position, property, label);
            if (property.propertyType == SerializedPropertyType.Float && property.floatValue < min)
                property.floatValue = min;
            else if (property.propertyType == SerializedPropertyType.Integer && property.intValue < (int)min)
                property.intValue = (int)min;
        }
    }

    [CustomPropertyDrawer(typeof(DelayedAttribute))]
    internal sealed class DelayedDrawer : PropertyDrawer
    {
        public override void OnGui(Rect position, SerializedProperty property, GuiContent label)
        {
            using var scope = new EditorGui.PropertyScope(position, label, property);
            EditorGui.BeginChangeCheck();
            switch (property.propertyType)
            {
            case SerializedPropertyType.Float:
                float f = EditorGui.DelayedFloatField(position, scope.content, property.floatValue);
                if (EditorGui.EndChangeCheck())
                    property.floatValue = f;
                break;
            case SerializedPropertyType.Integer:
                int i = EditorGui.DelayedIntField(position, scope.content, property.intValue);
                if (EditorGui.EndChangeCheck())
                    property.intValue = i;
                break;
            case SerializedPropertyType.String:
                string s = EditorGui.DelayedTextField(position, scope.content, property.stringValue);
                if (EditorGui.EndChangeCheck())
                    property.stringValue = s;
                break;
            default:
                EditorGui.EndChangeCheck();
                EditorGui.DefaultPropertyFieldInternal(position, property, label);
                break;
            }
        }
    }

    [CustomPropertyDrawer(typeof(MultilineAttribute))]
    internal sealed class MultilineDrawer : PropertyDrawer
    {
        int Lines => Math.Max(1, ((MultilineAttribute)attribute).lines);

        public override float GetPropertyHeight(SerializedProperty property, GuiContent label) =>
            property.propertyType == SerializedPropertyType.String ? Lines * 13 + 6 : EditorGuiUtility.singleLineHeight;

        public override void OnGui(Rect position, SerializedProperty property, GuiContent label)
        {
            if (property.propertyType != SerializedPropertyType.String)
            {
                EditorGui.DefaultPropertyFieldInternal(position, property, label);
                return;
            }
            using var scope = new EditorGui.PropertyScope(position, label, property);
            Rect field = EditorGui.PrefixLabel(position, scope.content);
            EditorGui.BeginChangeCheck();
            int was = EditorGui.indentLevel;
            EditorGui.indentLevel = 0;
            string s = EditorGui.TextArea(field, property.stringValue);
            EditorGui.indentLevel = was;
            if (EditorGui.EndChangeCheck())
                property.stringValue = s;
        }
    }

    [CustomPropertyDrawer(typeof(TextAreaAttribute))]
    internal sealed class TextAreaDrawer : PropertyDrawer
    {
        public override float GetPropertyHeight(SerializedProperty property, GuiContent label)
        {
            if (property.propertyType != SerializedPropertyType.String)
                return EditorGuiUtility.singleLineHeight;
            var a = (TextAreaAttribute)attribute;
            int lines = Math.Clamp((property.stringValue ?? "").Split('\n').Length, Math.Max(1, a.minLines), Math.Max(1, a.maxLines));
            return EditorGuiUtility.singleLineHeight + lines * 13 + 6;
        }

        public override void OnGui(Rect position, SerializedProperty property, GuiContent label)
        {
            if (property.propertyType != SerializedPropertyType.String)
            {
                EditorGui.DefaultPropertyFieldInternal(position, property, label);
                return;
            }
            using var scope = new EditorGui.PropertyScope(position, label, property);
            float line = EditorGuiUtility.singleLineHeight;
            EditorGui.LabelField(new Rect(position.x, position.y, position.width, line), scope.content);
            EditorGui.BeginChangeCheck();
            string s = EditorGui.TextArea(new Rect(position.x, position.y + line, position.width, position.height - line), property.stringValue);
            if (EditorGui.EndChangeCheck())
                property.stringValue = s;
        }
    }

    [CustomPropertyDrawer(typeof(HeaderAttribute))]
    internal sealed class HeaderDecorator : DecoratorDrawer
    {
        public override float GetHeight() => EditorGuiUtility.singleLineHeight * 1.5f;

        public override void OnGui(Rect position)
        {
            var r = new Rect(position.x, position.y + position.height - EditorGuiUtility.singleLineHeight, position.width, EditorGuiUtility.singleLineHeight);
            EditorGui.LabelField(r, ((HeaderAttribute)attribute).header, EditorStyles.boldLabel);
        }
    }

    [CustomPropertyDrawer(typeof(SpaceAttribute))]
    internal sealed class SpaceDecorator : DecoratorDrawer
    {
        public override float GetHeight() => ((SpaceAttribute)attribute).height;
    }

    // ------------------------------------------------------------------------------------------- properties

    public static partial class EditorGui
    {
        const float kLine = 18;

        public static bool PropertyField(Rect position, SerializedProperty property) => PropertyField(position, property, null, false);
        public static bool PropertyField(Rect position, SerializedProperty property, bool includeChildren) => PropertyField(position, property, null, includeChildren);
        public static bool PropertyField(Rect position, SerializedProperty property, GuiContent label) => PropertyField(position, property, label, false);

        /// <summary>The property's field by its drawer or its type (its decorators above it); with includeChildren an
        /// expanded struct's or array's children below it. True when it has children, is expanded and they were not
        /// drawn.</summary>
        public static bool PropertyField(Rect position, SerializedProperty property, GuiContent label, bool includeChildren)
        {
            if (property == null)
                return false;
            Editors.Handler h = Editors.HandlerOf(property);
            float y = position.y;
            foreach (DecoratorDrawer d in h.decorators)
            {
                float dh = d.GetHeight();
                d.OnGui(new Rect(position.x, y, position.width, dh));
                y += dh;
            }
            position = new Rect(position.x, y, position.width, Math.Max(0, position.yMax - y));
            if (h.drawer != null)
            {
                GuiContent l = label ?? new GuiContent(property.displayName) { tooltip = property.tooltip };
                h.drawer.OnGui(position, property, l);
                return false;
            }
            return DefaultPropertyField(position, property, label, includeChildren);
        }

        internal static bool DefaultPropertyFieldInternal(Rect position, SerializedProperty property, GuiContent label) =>
            DefaultPropertyField(position, property, label, false);

        static string[] LayerNames()
        {
            var names = new string[32];
            for (int i = 0; i < 32; i++)
            {
                string n = LayerMask.LayerToName(i);
                names[i] = string.IsNullOrEmpty(n) ? $"Layer {i}" : n;
            }
            return names;
        }

        static bool DefaultPropertyField(Rect position, SerializedProperty property, GuiContent label, bool includeChildren)
        {
            GuiContent l = BeginProperty(position, label, property);
            var line = new Rect(position.x, position.y, position.width, Math.Min(kLine, position.height > 0 ? position.height : kLine));
            bool hidden = false;
            Type fieldType = Editors.HandlerOf(property).fieldType;
            BeginChangeCheck();
            switch (property.propertyType)
            {
            case SerializedPropertyType.Integer:
            {
                long v = LongField(line, l, property.longValue);
                if (EndChangeCheck())
                    property.longValue = v;
                break;
            }
            case SerializedPropertyType.Boolean:
            {
                bool v = Toggle(line, l, property.boolValue);
                if (EndChangeCheck())
                    property.boolValue = v;
                break;
            }
            case SerializedPropertyType.Float:
            {
                double v = property.numericType == SerializedPropertyNumericType.Double ? DoubleField(line, l, property.doubleValue)
                                                                                        : FloatField(line, l, property.floatValue);
                if (EndChangeCheck())
                    property.doubleValue = v;
                break;
            }
            case SerializedPropertyType.String:
            {
                string v = TextField(line, l, property.stringValue);
                if (EndChangeCheck())
                    property.stringValue = v;
                break;
            }
            case SerializedPropertyType.Color:
            {
                Color v = ColorField(line, l, property.colorValue);
                if (EndChangeCheck())
                    property.colorValue = v;
                break;
            }
            case SerializedPropertyType.ObjectReference:
            {
                Type t = fieldType != null && typeof(Object).IsAssignableFrom(fieldType) ? fieldType : typeof(Object);
                bool scene = !AssetDatabase.Contains(property.serializedObject.targetObject);
                Object v = ObjectField(line, l, property.objectReferenceValue, t, scene);
                if (EndChangeCheck())
                    property.objectReferenceValue = v;
                break;
            }
            case SerializedPropertyType.LayerMask:
            {
                SerializedProperty bits = property.FindPropertyRelative("m_Bits") ?? property;
                int v = MaskField(line, l, bits.intValue, LayerNames());
                if (EndChangeCheck())
                    bits.intValue = v;
                break;
            }
            case SerializedPropertyType.Enum:
            {
                Type t = fieldType != null && fieldType.IsEnum ? fieldType : null;
                if (t != null && t.GetCustomAttribute<FlagsAttribute>() != null)
                {
                    Enum v = EnumFlagsField(line, l, (Enum)Enum.ToObject(t, property.longValue));
                    if (EndChangeCheck())
                        property.longValue = Convert.ToInt64(v, System.Globalization.CultureInfo.InvariantCulture);
                }
                else
                {
                    int v = Popup(line, l, property.enumValueIndex, property.enumDisplayNames);
                    if (EndChangeCheck())
                        property.enumValueIndex = v;
                }
                break;
            }
            case SerializedPropertyType.Vector2:
            {
                Vector2 v = Vector2Field(line, l, property.vector2Value);
                if (EndChangeCheck())
                    property.vector2Value = v;
                break;
            }
            case SerializedPropertyType.Vector3:
            {
                Vector3 v = Vector3Field(line, l, property.vector3Value);
                if (EndChangeCheck())
                    property.vector3Value = v;
                break;
            }
            case SerializedPropertyType.Vector4:
            case SerializedPropertyType.Quaternion:
            {
                Vector4 now = property.propertyType == SerializedPropertyType.Vector4 ? property.vector4Value
                                                                                     : new Vector4(property.quaternionValue.x, property.quaternionValue.y,
                                                                                                   property.quaternionValue.z, property.quaternionValue.w);
                Vector4 v = Vector4Field(line, l, now);
                if (EndChangeCheck())
                {
                    if (property.propertyType == SerializedPropertyType.Vector4)
                        property.vector4Value = v;
                    else
                        property.quaternionValue = new Quaternion(v.x, v.y, v.z, v.w);
                }
                break;
            }
            case SerializedPropertyType.Vector2Int:
            {
                Vector2Int v = Vector2IntField(line, l, property.vector2IntValue);
                if (EndChangeCheck())
                    property.vector2IntValue = v;
                break;
            }
            case SerializedPropertyType.Vector3Int:
            {
                Vector3Int v = Vector3IntField(line, l, property.vector3IntValue);
                if (EndChangeCheck())
                    property.vector3IntValue = v;
                break;
            }
            case SerializedPropertyType.Rect:
            {
                Rect v = RectField(line, l, property.rectValue);
                if (EndChangeCheck())
                    property.rectValue = v;
                break;
            }
            case SerializedPropertyType.Bounds:
            {
                Bounds v = BoundsField(new Rect(position.x, position.y, position.width, kLine * 2), l, property.boundsValue);
                if (EndChangeCheck())
                    property.boundsValue = v;
                break;
            }
            case SerializedPropertyType.ArraySize:
            {
                int v = DelayedIntField(line, l, property.intValue);
                if (EndChangeCheck())
                    property.intValue = Math.Max(0, v);
                break;
            }
            default:
            {
                EndChangeCheck();
                if (property.hasVisibleChildren)
                {
                    property.isExpanded = Foldout(line, property.isExpanded, l, true);
                    if (property.isExpanded && includeChildren)
                        DrawChildren(position, property, line.yMax);
                    else
                        hidden = property.isExpanded;
                }
                else
                    LabelField(line, l, new GuiContent(property.type));
                break;
            }
            }
            EndProperty();
            return hidden;
        }

        static void DrawChildren(Rect position, SerializedProperty property, float y)
        {
            SerializedProperty end = property.GetEndProperty(true);
            SerializedProperty child = property.Copy();
            indentLevel++;
            for (bool enter = true; child.NextVisible(enter) && (end == null || child.indexInternal < end.indexInternal); enter = false)
            {
                float h = GetPropertyHeight(child, null, true);
                y += EditorGuiUtility.standardVerticalSpacing;
                PropertyField(new Rect(position.x, y, position.width, h), child, null, true);
                y += h;
            }
            indentLevel--;
        }

        public static float GetPropertyHeight(SerializedProperty property, bool includeChildren) => GetPropertyHeight(property, null, includeChildren);
        public static float GetPropertyHeight(SerializedProperty property, GuiContent label = null) => GetPropertyHeight(property, label, true);

        /// <summary>The height PropertyField takes for the property (its decorators, its drawer's, its expanded children).</summary>
        public static float GetPropertyHeight(SerializedProperty property, GuiContent label, bool includeChildren)
        {
            if (property == null)
                return 0;
            Editors.Handler h = Editors.HandlerOf(property);
            float height = 0;
            foreach (DecoratorDrawer d in h.decorators)
                height += d.GetHeight();
            if (h.drawer != null)
                return height + h.drawer.GetPropertyHeight(property, label ?? new GuiContent(property.displayName));
            if (property.propertyType == SerializedPropertyType.Bounds)
                return height + kLine * 2;
            height += kLine;
            if (includeChildren && property.hasVisibleChildren && property.isExpanded &&
                (property.propertyType == SerializedPropertyType.Generic))
            {
                SerializedProperty end = property.GetEndProperty(true);
                SerializedProperty child = property.Copy();
                for (bool enter = true; child.NextVisible(enter) && (end == null || child.indexInternal < end.indexInternal); enter = false)
                    height += GetPropertyHeight(child, null, true) + EditorGuiUtility.standardVerticalSpacing;
            }
            return height;
        }

        public static float GetPropertyHeight(SerializedPropertyType type, GuiContent label) =>
            type == SerializedPropertyType.Bounds ? kLine * 2 : kLine;
    }
}
