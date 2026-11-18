using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text.Json;
using HelixEditor.SceneManagement;
using HelixEngine;
using Object = HelixEngine.Object;

namespace HelixEditor
{
    public enum SerializedPropertyType
    {
        Generic = -1,
        Integer = 0,
        Boolean = 1,
        Float = 2,
        String = 3,
        Color = 4,
        ObjectReference = 5,
        LayerMask = 6,
        Enum = 7,
        Vector2 = 8,
        Vector3 = 9,
        Vector4 = 10,
        Rect = 11,
        ArraySize = 12,
        Character = 13,
        AnimationCurve = 14,
        Bounds = 15,
        Gradient = 16,
        Quaternion = 17,
        ExposedReference = 18,
        FixedBufferSize = 19,
        Vector2Int = 20,
        Vector3Int = 21,
        RectInt = 22,
        BoundsInt = 23,
        ManagedReference = 24,
        Hash128 = 25,
        RenderingLayerMask = 26,
        EntityId = 27,
    }

    public enum SerializedPropertyNumericType
    {
        Unknown = 0,
        Int8 = 1,
        UInt8 = 2,
        Int16 = 3,
        UInt16 = 4,
        Int32 = 5,
        UInt32 = 6,
        Int64 = 7,
        UInt64 = 8,
        Float = 100,
        Double = 101,
    }

    // The data of the serialized fields' view (Data/Settings/SerializedFields.json): the fields left out, and the struct
    // types that are kinds of SerializedPropertyType with the fields they are made of.
    internal static class SerializedFields
    {
        static HashSet<string> s_Hidden;
        static Dictionary<string, (SerializedPropertyType type, string[] fields)> s_Types;
        static Dictionary<string, string> s_Members;

        static void Load()
        {
            if (s_Types != null)
                return;
            s_Hidden = new HashSet<string>();
            s_Types = new Dictionary<string, (SerializedPropertyType, string[])>();
            s_Members = new Dictionary<string, string>();
            try
            {
                string file = System.IO.Path.Combine(EditorApplication.editorDataPathInternal, "Settings/SerializedFields.json");
                using JsonDocument doc = JsonDocument.Parse(System.IO.File.ReadAllText(file));
                foreach (JsonElement h in doc.RootElement.GetProperty("hidden").EnumerateArray())
                    s_Hidden.Add(h.GetString());
                foreach (JsonProperty t in doc.RootElement.GetProperty("types").EnumerateObject())
                {
                    var parts = new List<string>();
                    foreach (JsonElement p in t.Value.EnumerateArray())
                        parts.Add(p.GetString());
                    if (parts.Count > 0 && Enum.TryParse(parts[0], out SerializedPropertyType kind))
                        s_Types[t.Name] = (kind, parts.GetRange(1, parts.Count - 1).ToArray());
                }
                if (doc.RootElement.TryGetProperty("members", out JsonElement members))
                    foreach (JsonProperty m in members.EnumerateObject())
                        s_Members[m.Name] = m.Value.GetString();
            }
            catch (Exception e)
            {
                Debug.LogError($"Settings/SerializedFields.json could not be read: {e.Message}");
            }
        }

        /// <summary>The C# member of a native class that a serialized field is (its type tells an enum): the
        /// "members" table by "Class.path", else the field's name without m_ in lower camel case at the top.</summary>
        internal static MemberInfo MemberOf(Type type, string path, int depth)
        {
            Load();
            string name = null;
            for (Type t = type; t != null && name == null; t = t.BaseType)
                s_Members.TryGetValue(t.Name + "." + path, out name);
            if (name == null && depth == 0 && path.StartsWith("m_", StringComparison.Ordinal) && path.Length > 2)
                name = char.ToLowerInvariant(path[2]) + path.Substring(3);
            if (name == null)
                return null;
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public;
            return (MemberInfo)type.GetProperty(name, flags) ?? type.GetField(name, flags);
        }

        internal static bool Hidden(string name)
        {
            Load();
            return s_Hidden.Contains(name);
        }

        internal static bool TypeOf(string typeName, out SerializedPropertyType type, out string[] fields)
        {
            Load();
            if (typeName != null && s_Types.TryGetValue(typeName, out var t))
            {
                type = t.type;
                fields = t.fields;
                return true;
            }
            type = SerializedPropertyType.Generic;
            fields = Array.Empty<string>();
            return false;
        }

        /// <summary>"m_LocalPosition" as "Local Position", "_speed" as "Speed", "kMax" as "Max".</summary>
        internal static string Nicify(string name)
        {
            if (string.IsNullOrEmpty(name))
                return name ?? "";
            string s = name;
            if (s.StartsWith("m_", StringComparison.Ordinal) && s.Length > 2)
                s = s.Substring(2);
            else if (s.StartsWith("_", StringComparison.Ordinal) && s.Length > 1)
                s = s.Substring(1);
            else if (s.Length > 1 && s[0] == 'k' && char.IsUpper(s[1]))
                s = s.Substring(1);
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                bool boundary = i > 0 && (char.IsUpper(c) && (char.IsLower(s[i - 1]) || (i + 1 < s.Length && char.IsLower(s[i + 1]) && char.IsUpper(s[i - 1]))) ||
                                          char.IsDigit(c) && !char.IsDigit(s[i - 1]));
                if (boundary && sb.Length > 0 && sb[sb.Length - 1] != ' ')
                    sb.Append(' ');
                sb.Append(i == 0 ? char.ToUpperInvariant(c) : c);
            }
            return sb.ToString();
        }
    }

    /// <summary>An object's serialized fields to read and change (several objects of one class together): its
    /// properties as its class writes them; changes wait until ApplyModifiedProperties writes them into the objects
    /// (their scenes then changed, assets marked dirty).</summary>
    public class SerializedObject : IDisposable
    {
        internal ulong m_Handle;
        readonly Object[] m_Targets;
        readonly Object m_Context;

        public SerializedObject(Object obj) : this(new[] { obj }, null) { }
        public SerializedObject(Object obj, Object context) : this(new[] { obj }, context) { }
        public SerializedObject(Object[] objs) : this(objs, null) { }

        public SerializedObject(Object[] objs, Object context)
        {
            if (objs == null || objs.Length == 0 || Array.Exists(objs, o => o == null))
                throw new ArgumentException("A SerializedObject needs objects.", nameof(objs));
            m_Targets = (Object[])objs.Clone();
            m_Context = context;
            m_Handle = SerializedNative.Open(objs);
            if (m_Handle == 0)
                throw new ArgumentException("The objects of a SerializedObject are of one class.", nameof(objs));
        }

        ~SerializedObject()
        {
            ulong h = m_Handle;
            m_Handle = 0;
            if (h != 0)
                EditorApplication.delayCall += () => SerializedNative.Close(h);
        }

        public void Dispose()
        {
            if (m_Handle != 0)
                SerializedNative.Close(m_Handle);
            m_Handle = 0;
            GC.SuppressFinalize(this);
        }

        public Object targetObject => m_Targets[0];
        public Object[] targetObjects => (Object[])m_Targets.Clone();
        public Object context => m_Context;
        public bool isEditingMultipleObjects => m_Targets.Length > 1;
        public int maxArraySizeForMultiEditing { get; set; } = 32;
        public bool forceChildVisibility { get; set; }
        // The Inspector's Debug mode: the fields an inspector leaves out are shown too.
        internal bool debugModeInternal { get; set; }
        public bool hasModifiedProperties => SerializedNative.HasModified(m_Handle);

        public SerializedProperty GetIterator() => new SerializedProperty(this, -1);

        /// <summary>The property at a path ("m_LocalPosition.x", "list.Array.data[2]"), null for none.</summary>
        public SerializedProperty FindProperty(string propertyPath)
        {
            int i = SerializedNative.Find(m_Handle, propertyPath ?? "");
            return i < 0 ? null : new SerializedProperty(this, i);
        }

        public void Update() => SerializedNative.Update(m_Handle);

        public bool UpdateIfRequiredOrScript()
        {
            Update();
            return true;
        }

        public bool ApplyModifiedProperties()
        {
            bool applied = SerializedNative.Apply(m_Handle);
            if (applied)
                foreach (Object t in m_Targets)
                    OnValidate(t);
            return applied;
        }

        // Undo comes with the undo step: both apply the same way now.
        public bool ApplyModifiedPropertiesWithoutUndo() => ApplyModifiedProperties();

        static void OnValidate(Object target)
        {
            MethodInfo m = target?.GetType().GetMethod("OnValidate", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null,
                                                        Type.EmptyTypes, null);
            try
            {
                m?.Invoke(target, null);
            }
            catch (TargetInvocationException e)
            {
                Debug.LogException(e.InnerException ?? e);
            }
        }

        /// <summary>The values of another object's property (and what is below it) into the same path here.</summary>
        public void CopyFromSerializedProperty(SerializedProperty prop) => CopyFromSerializedPropertyIfDifferent(prop);

        public bool CopyFromSerializedPropertyIfDifferent(SerializedProperty prop)
        {
            if (prop == null)
                return false;
            SerializedProperty here = FindProperty(prop.propertyPath);
            return here != null && SerializedProperty.CopyValues(prop, here);
        }

        public void SetIsDifferentCacheDirty() { }

        internal ulong handleInternal => m_Handle;
    }

    /// <summary>A position in a SerializedObject's properties: one property's name, type and value, and the way on to
    /// the next (depth first).</summary>
    public class SerializedProperty : IEnumerable
    {
        const int kReference = 100, kArraySize = 101;
        const int kBool = 0, kFloat = 9, kDouble = 10, kString = 11, kGuid = 12, kArray = 13, kFixedArray = 14, kStruct = 15;

        static readonly Dictionary<(ulong, string), bool> s_Expanded = new();

        readonly SerializedObject m_Object;
        int m_Index;

        internal SerializedProperty(SerializedObject obj, int index)
        {
            m_Object = obj;
            m_Index = index;
        }

        ulong H => m_Object.m_Handle;
        // Its place in the object's properties (depth first).
        internal int indexInternal => m_Index;
        int Kind => m_Index < 0 ? kStruct : SerializedNative.Kind(H, m_Index);
        int EndIndex => m_Index < 0 ? SerializedNative.Count(H) : SerializedNative.End(H, m_Index);

        public SerializedObject serializedObject => m_Object;
        public string name => m_Index < 0 ? "Base" : SerializedNative.Name(H, m_Index);
        public string propertyPath => m_Index < 0 ? "" : SerializedNative.Path(H, m_Index);
        public string type => m_Index < 0 ? "" : SerializedNative.TypeName(H, m_Index);
        public int depth => m_Index < 0 ? -1 : SerializedNative.Depth(H, m_Index);
        public string tooltip => FieldOf()?.GetCustomAttribute<TooltipAttribute>()?.tooltip ?? "";

        public string displayName
        {
            get
            {
                string n = name;
                if (Kind == kArraySize)
                    return "Size";
                if (n == "data" && propertyPath.EndsWith("]", StringComparison.Ordinal))
                {
                    int open = propertyPath.LastIndexOf('[');
                    return "Element " + propertyPath.Substring(open + 1, propertyPath.Length - open - 2);
                }
                return SerializedFields.Nicify(n);
            }
        }

        public bool editable
        {
            get
            {
                foreach (Object t in m_Object.targetObjects)
                    if ((t.hideFlags & HideFlags.NotEditable) != 0)
                        return false;
                return true;
            }
        }

        public SerializedPropertyType propertyType
        {
            get
            {
                int k = Kind;
                switch (k)
                {
                case kReference: return SerializedPropertyType.ObjectReference;
                case kArraySize: return SerializedPropertyType.ArraySize;
                case kBool: return SerializedPropertyType.Boolean;
                case kFloat:
                case kDouble: return SerializedPropertyType.Float;
                case kString: return SerializedPropertyType.String;
                case kStruct: return SerializedFields.TypeOf(type, out SerializedPropertyType t, out _) ? t : SerializedPropertyType.Generic;
                case kGuid:
                case kArray:
                case kFixedArray: return SerializedPropertyType.Generic;
                default: return EnumType() != null ? SerializedPropertyType.Enum : SerializedPropertyType.Integer;
                }
            }
        }

        public SerializedPropertyNumericType numericType
        {
            get
            {
                int k = Kind;
                if (k >= 1 && k <= 8)
                    return (SerializedPropertyNumericType)k;
                return k == kFloat ? SerializedPropertyNumericType.Float : k == kDouble ? SerializedPropertyNumericType.Double : SerializedPropertyNumericType.Unknown;
            }
        }

        public bool isArray => Kind == kArray;
        public bool isFixedBuffer => Kind == kFixedArray;
        public bool hasChildren => m_Index < 0 ? SerializedNative.Count(H) > 0 : EndIndex > m_Index + 1 && Kind != kReference;
        public bool hasVisibleChildren
        {
            get
            {
                // Composite values (vectors, colors ...) show as one field.
                if (!hasChildren || propertyType != SerializedPropertyType.Generic && propertyType != SerializedPropertyType.Bounds &&
                                    propertyType != SerializedPropertyType.BoundsInt)
                    return false;
                for (int i = Math.Max(m_Index + 1, 0); i < EndIndex; i++)
                    if (Visible(i))
                        return true;
                return false;
            }
        }

        public bool hasMultipleDifferentValues => m_Index >= 0 && SerializedNative.Different(H, m_Index);

        public bool isExpanded
        {
            get => s_Expanded.TryGetValue(Key(), out bool v) && v;
            set => s_Expanded[Key()] = value;
        }

        (ulong, string) Key() => (m_Object.targetObject.GetEntityId().value, propertyPath);

        // ---------------------------------------------------------------------------------------------- values

        static string Text(double v, int kind) =>
            kind == kFloat ? ((float)v).ToString("R", CultureInfo.InvariantCulture) : v.ToString("R", CultureInfo.InvariantCulture);

        void SetNumber(double v) => SerializedNative.SetText(H, m_Index, Text(v, Kind));
        void SetInteger(long v) => SerializedNative.SetText(H, m_Index, v.ToString(CultureInfo.InvariantCulture));

        public long longValue
        {
            get => m_Index < 0 ? 0 : SerializedNative.LongValue(H, m_Index);
            set
            {
                if (Kind == kArraySize)
                    SerializedNative.SetText(H, m_Index, value.ToString(CultureInfo.InvariantCulture));
                else
                    SetInteger(value);
            }
        }

        public int intValue { get => (int)longValue; set => longValue = value; }
        public uint uintValue { get => (uint)longValue; set => longValue = value; }
        public ulong ulongValue { get => (ulong)longValue; set => longValue = (long)value; }
        public bool boolValue { get => longValue != 0; set => SetInteger(value ? 1 : 0); }

        public double doubleValue
        {
            get => m_Index < 0 ? 0 : SerializedNative.DoubleValue(H, m_Index);
            set => SetNumber(value);
        }

        public float floatValue { get => (float)doubleValue; set => doubleValue = value; }

        public string stringValue
        {
            get => m_Index < 0 ? "" : SerializedNative.StringValue(H, m_Index);
            set => SerializedNative.SetText(H, m_Index, value ?? "");
        }

        public Object objectReferenceValue
        {
            get => Kind == kReference ? SerializedNative.ObjectValue(H, m_Index) : null;
            set
            {
                if (Kind == kReference)
                    SerializedNative.SetObject(H, m_Index, value);
            }
        }

        public EntityId objectReferenceEntityIdValue
        {
            get => objectReferenceValue != null ? objectReferenceValue.GetEntityId() : default;
            set => objectReferenceValue = Resources.EntityIdToObject(value);
        }

        // A struct's fields by name (the type's fields from the data).
        double Part(string field) => FindPropertyRelative(field)?.doubleValue ?? 0;

        void SetPart(string field, double v) => FindPropertyRelative(field)?.SetNumber(v);

        public Vector2 vector2Value
        {
            get => new Vector2((float)Part("x"), (float)Part("y"));
            set { SetPart("x", value.x); SetPart("y", value.y); }
        }

        public Vector3 vector3Value
        {
            get => new Vector3((float)Part("x"), (float)Part("y"), (float)Part("z"));
            set { SetPart("x", value.x); SetPart("y", value.y); SetPart("z", value.z); }
        }

        public Vector4 vector4Value
        {
            get => new Vector4((float)Part("x"), (float)Part("y"), (float)Part("z"), (float)Part("w"));
            set { SetPart("x", value.x); SetPart("y", value.y); SetPart("z", value.z); SetPart("w", value.w); }
        }

        public Quaternion quaternionValue
        {
            get => new Quaternion((float)Part("x"), (float)Part("y"), (float)Part("z"), (float)Part("w"));
            set { SetPart("x", value.x); SetPart("y", value.y); SetPart("z", value.z); SetPart("w", value.w); }
        }

        public Color colorValue
        {
            get => new Color((float)Part("r"), (float)Part("g"), (float)Part("b"), (float)Part("a"));
            set { SetPart("r", value.r); SetPart("g", value.g); SetPart("b", value.b); SetPart("a", value.a); }
        }

        public Rect rectValue
        {
            get => new Rect((float)Part("x"), (float)Part("y"), (float)Part("width"), (float)Part("height"));
            set { SetPart("x", value.x); SetPart("y", value.y); SetPart("width", value.width); SetPart("height", value.height); }
        }

        public Vector2Int vector2IntValue
        {
            get => new Vector2Int((int)Part("x"), (int)Part("y"));
            set { SetPart("x", value.x); SetPart("y", value.y); }
        }

        public Vector3Int vector3IntValue
        {
            get => new Vector3Int((int)Part("x"), (int)Part("y"), (int)Part("z"));
            set { SetPart("x", value.x); SetPart("y", value.y); SetPart("z", value.z); }
        }

        public RectInt rectIntValue
        {
            get => new RectInt((int)Part("x"), (int)Part("y"), (int)Part("width"), (int)Part("height"));
            set { SetPart("x", value.x); SetPart("y", value.y); SetPart("width", value.width); SetPart("height", value.height); }
        }

        public Bounds boundsValue
        {
            get => new Bounds(FindPropertyRelative("m_Center")?.vector3Value ?? Vector3.zero, 2 * (FindPropertyRelative("m_Extent")?.vector3Value ?? Vector3.zero));
            set
            {
                if (FindPropertyRelative("m_Center") is SerializedProperty c)
                    c.vector3Value = value.center;
                if (FindPropertyRelative("m_Extent") is SerializedProperty e)
                    e.vector3Value = value.extents;
            }
        }

        // ---------------------------------------------------------------------------------------------- enums

        // A field's member of a C# script (its enum type, its attributes).
        FieldInfo FieldOf()
        {
            if (m_Index < 0 || depth != 0)
                return null;
            return m_Object.targetObject.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        }

        internal Type EnumTypeInternal() => EnumType();

        Type EnumType()
        {
            Type t = FieldOf()?.FieldType;
            // A native class's field: the type of its C# member (Light's m_Type is Light.type, a LightType).
            if (t == null && m_Index >= 0 && m_Object.targetObject != null)
                t = SerializedFields.MemberOf(m_Object.targetObject.GetType(), propertyPath, depth) switch
                {
                    PropertyInfo p => p.PropertyType,
                    FieldInfo f => f.FieldType,
                    _ => null,
                };
            return t != null && t.IsEnum ? t : null;
        }

        public string[] enumNames => EnumType() is Type t ? Enum.GetNames(t) : Array.Empty<string>();

        public string[] enumDisplayNames => Array.ConvertAll(enumNames, SerializedFields.Nicify);

        public int enumValueIndex
        {
            get
            {
                if (!(EnumType() is Type t))
                    return -1;
                Array values = Enum.GetValues(t);
                for (int i = 0; i < values.Length; i++)
                    if (Convert.ToInt64(values.GetValue(i), CultureInfo.InvariantCulture) == longValue)
                        return i;
                return -1;
            }
            set
            {
                if (EnumType() is Type t && value >= 0 && value < Enum.GetValues(t).Length)
                    longValue = Convert.ToInt64(Enum.GetValues(t).GetValue(value), CultureInfo.InvariantCulture);
            }
        }

        public int enumValueFlag { get => intValue; set => intValue = value; }

        // ---------------------------------------------------------------------------------------------- arrays

        public int arraySize
        {
            get => Kind == kArray || Kind == kFixedArray ? SerializedNative.ArraySize(H, m_Index) : 0;
            set
            {
                if (Kind == kArray)
                    SerializedNative.SetArraySize(H, m_Index, Math.Max(0, value));
            }
        }

        public int minArraySize => arraySize;
        public int fixedBufferSize => Kind == kFixedArray ? arraySize : 0;

        public SerializedProperty GetArrayElementAtIndex(int index) => m_Object.FindProperty($"{propertyPath}.Array.data[{index}]");
        public SerializedProperty GetFixedBufferElementAtIndex(int index) => m_Object.FindProperty($"{propertyPath}.Array.data[{index}]");

        /// <summary>A copy of the element at the index put after it (the array one longer).</summary>
        public void InsertArrayElementAtIndex(int index)
        {
            string path = propertyPath;
            int size = arraySize;
            index = Math.Clamp(index, 0, size);
            arraySize = size + 1;
            SerializedProperty array = m_Object.FindProperty(path);
            int from = size == 0 ? -1 : Math.Min(index, size - 1);
            for (int i = size; i > index; i--)
                CopyValues(array.GetArrayElementAtIndex(i - 1), array.GetArrayElementAtIndex(i));
            if (from >= 0 && index > from)
                CopyValues(array.GetArrayElementAtIndex(from), array.GetArrayElementAtIndex(index));
            Reposition(path);
        }

        public void DeleteArrayElementAtIndex(int index)
        {
            string path = propertyPath;
            int size = arraySize;
            if (index < 0 || index >= size)
                return;
            for (int i = index; i < size - 1; i++)
                CopyValues(GetArrayElementAtIndex(i + 1), GetArrayElementAtIndex(i));
            arraySize = size - 1;
            Reposition(path);
        }

        public bool MoveArrayElement(int srcIndex, int dstIndex)
        {
            int size = arraySize;
            if (srcIndex < 0 || srcIndex >= size || dstIndex < 0 || dstIndex >= size || srcIndex == dstIndex)
                return false;
            // The source's values kept aside, the elements between shifted, the source put at the destination.
            var kept = Leaves(GetArrayElementAtIndex(srcIndex));
            int step = srcIndex < dstIndex ? 1 : -1;
            for (int i = srcIndex; i != dstIndex; i += step)
                CopyValues(GetArrayElementAtIndex(i + step), GetArrayElementAtIndex(i));
            Restore(kept, GetArrayElementAtIndex(dstIndex));
            return true;
        }

        public void ClearArray() => arraySize = 0;

        // After the array's size changed the table was read again: this property found again by its path.
        void Reposition(string path)
        {
            int i = SerializedNative.Find(H, path);
            if (i >= 0)
                m_Index = i;
        }

        // A property's leaves (relative path, kind, value) to put back.
        List<(string path, int kind, string text, Object obj)> Leaves(SerializedProperty p)
        {
            var list = new List<(string, int, string, Object)>();
            string root = p.propertyPath;
            for (int i = p.m_Index; i < p.EndIndex; i++)
            {
                int k = SerializedNative.Kind(H, i);
                if (k == kStruct || k == kArray || k == kFixedArray)
                    continue;
                string rel = SerializedNative.Path(H, i).Substring(root.Length);
                var leaf = new SerializedProperty(m_Object, i);
                list.Add((rel, k, LeafText(leaf), k == kReference ? leaf.objectReferenceValue : null));
            }
            return list;
        }

        void Restore(List<(string path, int kind, string text, Object obj)> leaves, SerializedProperty to)
        {
            string root = to.propertyPath;
            foreach (var l in leaves)
            {
                int i = SerializedNative.Find(H, root + l.path);
                if (i < 0)
                    continue;
                if (l.kind == kReference)
                    SerializedNative.SetObject(H, i, l.obj);
                else if (l.kind != kArraySize)
                    SerializedNative.SetText(H, i, l.text);
            }
        }

        /// <summary>Every value of the object (paths, kinds, values), its bookkeeping left out.</summary>
        internal static List<(string path, int kind, string text, Object obj)> SnapshotInternal(SerializedObject so)
        {
            var all = new List<(string, int, string, Object)>();
            SerializedProperty it = so.GetIterator();
            for (bool enter = true; it.Next(enter); enter = false)
            {
                if (SerializedFields.Hidden(it.name) || it.name == "m_Script")
                    continue;
                string root = it.propertyPath;
                foreach (var l in it.Leaves(it))
                    all.Add((root + l.path, l.kind, l.text, l.obj));
            }
            return all;
        }

        /// <summary>The values into another object of the same class (paths it lacks left out); arrays sized first.</summary>
        internal static void RestoreInternal(SerializedObject so, List<(string path, int kind, string text, Object obj)> values)
        {
            foreach (var v in values)
                if (v.kind == kArraySize && so.FindProperty(v.path) is SerializedProperty size)
                    size.longValue = long.Parse(v.text, CultureInfo.InvariantCulture);
            foreach (var v in values)
            {
                int i = SerializedNative.Find(so.m_Handle, v.path);
                if (i < 0 || v.kind == kArraySize)
                    continue;
                if (v.kind == kReference)
                    SerializedNative.SetObject(so.m_Handle, i, v.obj);
                else
                    SerializedNative.SetText(so.m_Handle, i, v.text);
            }
        }

        static string LeafText(SerializedProperty p)
        {
            int k = p.Kind;
            if (k == kString || k == kGuid)
                return p.stringValue;
            if (k == kFloat || k == kDouble)
                return Text(p.doubleValue, k);
            return p.longValue.ToString(CultureInfo.InvariantCulture);
        }

        // The values below one property into another of the same layout; true when anything differed.
        internal static bool CopyValues(SerializedProperty from, SerializedProperty to)
        {
            if (from == null || to == null)
                return false;
            bool changed = false;
            string fromRoot = from.propertyPath, toRoot = to.propertyPath;
            ulong fh = from.H, th = to.H;
            for (int i = from.m_Index; i < from.EndIndex; i++)
            {
                int k = SerializedNative.Kind(fh, i);
                if (k == kStruct || k == kArray || k == kFixedArray || k == kArraySize)
                    continue;
                int j = SerializedNative.Find(th, toRoot + SerializedNative.Path(fh, i).Substring(fromRoot.Length));
                if (j < 0)
                    continue;
                var a = new SerializedProperty(from.m_Object, i);
                var b = new SerializedProperty(to.m_Object, j);
                if (k == kReference)
                {
                    if (a.objectReferenceValue != b.objectReferenceValue)
                    {
                        SerializedNative.SetObject(th, j, a.objectReferenceValue);
                        changed = true;
                    }
                }
                else if (LeafText(a) != LeafText(b) || b.hasMultipleDifferentValues)
                {
                    SerializedNative.SetText(th, j, LeafText(a));
                    changed = true;
                }
            }
            return changed;
        }

        public static bool DataEquals(SerializedProperty x, SerializedProperty y) => EqualContents(x, y);

        public static bool EqualContents(SerializedProperty x, SerializedProperty y)
        {
            if (x == null || y == null)
                return x == y;
            var a = x.Leaves(x);
            var b = y.Leaves(y);
            if (a.Count != b.Count)
                return false;
            for (int i = 0; i < a.Count; i++)
                if (a[i].path != b[i].path || a[i].text != b[i].text || a[i].obj != b[i].obj)
                    return false;
            return true;
        }

        // ---------------------------------------------------------------------------------------------- moving on

        bool Visible(int i)
        {
            if (m_Object.debugModeInternal)
                return true;
            string n = SerializedNative.Name(H, i);
            if (SerializedFields.Hidden(n))
                return false;
            if (SerializedNative.Depth(H, i) == 0)
            {
                FieldInfo f = m_Object.targetObject.GetType().GetField(n, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (f != null && f.GetCustomAttribute<HideInInspector>() != null)
                    return false;
            }
            // Below a hidden one: hidden too.
            for (int p = i - 1; p >= 0; p--)
                if (SerializedNative.End(H, p) > i && SerializedFields.Hidden(SerializedNative.Name(H, p)))
                    return false;
            return true;
        }

        /// <summary>To the next property (into this one's children when asked and it has any); false at the end.</summary>
        public bool Next(bool enterChildren)
        {
            int count = SerializedNative.Count(H);
            int next = m_Index < 0 ? 0 : enterChildren && hasChildren ? m_Index + 1 : EndIndex;
            if (next >= count)
            {
                m_Index = count;
                return false;
            }
            m_Index = next;
            return true;
        }

        /// <summary>Next, past the properties an inspector does not show (Base's own bookkeeping, HideInInspector), and
        /// into the children of composite values (vectors, colors ...) only through their own drawers.</summary>
        public bool NextVisible(bool enterChildren)
        {
            bool enter = enterChildren && (m_Index < 0 || propertyType == SerializedPropertyType.Generic && Kind != kReference);
            while (Next(enter))
            {
                if (Visible(m_Index))
                    return true;
                enter = false;
            }
            return false;
        }

        public SerializedProperty Copy() => new SerializedProperty(m_Object, m_Index);
        public void Reset() => m_Index = -1;

        /// <summary>The property after this one's children (null at the end).</summary>
        public SerializedProperty GetEndProperty(bool includeInvisible = false)
        {
            var end = new SerializedProperty(m_Object, m_Index);
            bool more = includeInvisible ? end.Next(false) : end.NextVisible(false);
            return more ? end : null;
        }

        public int CountRemaining()
        {
            var p = Copy();
            int n = 0;
            while (p.NextVisible(false))
                n++;
            return n;
        }

        public int CountInProperty()
        {
            int n = 0;
            for (int i = Math.Max(m_Index, 0); i < EndIndex; i++)
                if (Visible(i))
                    n++;
            return n;
        }

        public SerializedProperty FindPropertyRelative(string relativePropertyPath) =>
            m_Index < 0 ? m_Object.FindProperty(relativePropertyPath) : m_Object.FindProperty($"{propertyPath}.{relativePropertyPath}");

        /// <summary>The property this one is in (null at the top).</summary>
        public SerializedProperty Parent()
        {
            for (int p = m_Index - 1; p >= 0; p--)
                if (SerializedNative.End(H, p) > m_Index && SerializedNative.Depth(H, p) == depth - 1)
                    return new SerializedProperty(m_Object, p);
            return null;
        }

        public bool DuplicateCommand()
        {
            SerializedProperty parent = Parent();
            if (parent == null || !parent.isArray)
                return false;
            string p = propertyPath;
            int open = p.LastIndexOf('['), index = int.Parse(p.Substring(open + 1, p.Length - open - 2), CultureInfo.InvariantCulture);
            parent.InsertArrayElementAtIndex(index);
            return true;
        }

        public bool DeleteCommand()
        {
            SerializedProperty parent = Parent();
            if (parent == null || !parent.isArray)
                return false;
            string p = propertyPath;
            int open = p.LastIndexOf('['), index = int.Parse(p.Substring(open + 1, p.Length - open - 2), CultureInfo.InvariantCulture);
            parent.DeleteArrayElementAtIndex(index);
            return true;
        }

        /// <summary>The visible children, one by one (a copy for each).</summary>
        public IEnumerator GetEnumerator()
        {
            int end = EndIndex;
            var p = Copy();
            if (!p.NextVisible(true))
                yield break;
            while (p.m_Index < end)
            {
                yield return p.Copy();
                if (!p.NextVisible(false))
                    yield break;
            }
        }
    }
}
