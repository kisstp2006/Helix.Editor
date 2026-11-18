using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using HelixEngine;
using HelixEditor.AssetImporters;
using Object = HelixEngine.Object;

namespace HelixEditor
{
    /// <summary>An asset's importer: its settings as its .meta keeps them (read by key, changes kept until
    /// SaveAndReimport writes them and imports the asset again). GetAtPath gives the importer class of the .meta's
    /// section (TextureImporter, ModelImporter ...; a ScriptedImporter by its class).</summary>
    public class AssetImporter : ScriptableObject
    {
        string m_Path;
        readonly Dictionary<string, string> m_Changes = new Dictionary<string, string>();

        public string assetPath => m_Path;
        public string userData { get => Value("userData"); set => Set("userData", value); }
        public string assetBundleName { get => Value("assetBundleName"); set => Set("assetBundleName", value); }
        public string assetBundleVariant { get => Value("assetBundleVariant"); set => Set("assetBundleVariant", value); }
        public bool importSettingsMissing => !ImportNative.HasMeta(m_Path);

        /// <summary>The importer of an asset (null for no asset at the path).</summary>
        public static AssetImporter GetAtPath(string path)
        {
            string section = AssetDatabase.ImporterNameInternal(path);
            if (string.IsNullOrEmpty(section))
                return null;
            Type type = ImporterTypes.ForSection(section, path);
            var importer = (AssetImporter)CreateInstance(type);
            importer.hideFlags = HideFlags.DontSave;
            importer.m_Path = path;
            return importer;
        }

        internal void SetPathInternal(string path) => m_Path = path;

        // The changes not written (key and value after each other, separated by 0x1f), dropped from the importer.
        internal string TakeChangesInternal()
        {
            if (m_Changes.Count == 0)
                return "";
            var sb = new System.Text.StringBuilder();
            foreach (var kv in m_Changes)
                sb.Append(kv.Key).Append('\x1f').Append(kv.Value ?? "").Append('\x1f');
            m_Changes.Clear();
            return sb.ToString();
        }

        /// <summary>The changed settings into the .meta, the asset imported again.</summary>
        public void SaveAndReimport()
        {
            if (m_Changes.Count > 0)
            {
                var sb = new System.Text.StringBuilder();
                foreach (var kv in m_Changes)
                    sb.Append(kv.Key).Append('\x1f').Append(kv.Value ?? "").Append('\x1f');
                ImportNative.SetMetaValues(m_Path, sb.ToString());
                m_Changes.Clear();
            }
            AssetDatabase.ImportAsset(m_Path);
        }

        // ------------------------------------------------------------------------------------- the .meta's values

        protected string Value(string key) => m_Changes.TryGetValue(key, out string v) ? v : ImportNative.MetaValue(m_Path, key);
        protected void Set(string key, string value) => m_Changes[key] = value ?? "";

        protected int GetInt(string key, int fallback = 0) =>
            int.TryParse(Value(key), NumberStyles.Integer, CultureInfo.InvariantCulture, out int v) ? v : fallback;
        protected void SetInt(string key, int value) => Set(key, value.ToString(CultureInfo.InvariantCulture));
        protected float GetFloat(string key, float fallback = 0) =>
            float.TryParse(Value(key), NumberStyles.Float, CultureInfo.InvariantCulture, out float v) ? v : fallback;
        protected void SetFloat(string key, float value) => Set(key, value.ToString("R", CultureInfo.InvariantCulture));
        protected bool GetBool(string key, bool fallback = false) => GetInt(key, fallback ? 1 : 0) != 0;
        protected void SetBool(string key, bool value) => SetInt(key, value ? 1 : 0);
    }

    // The importer class of a .meta section: AssetImporter's classes by name (any assembly's), the scripted ones by
    // their class.
    internal static class ImporterTypes
    {
        static Dictionary<string, Type> s_Types;

        internal static Type ForSection(string section, string path)
        {
            if (s_Types == null)
            {
                s_Types = new Dictionary<string, Type>();
                foreach (Assembly a in AppDomain.CurrentDomain.GetAssemblies())
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
                        if (!t.IsAbstract && typeof(AssetImporter).IsAssignableFrom(t))
                            s_Types[t.FullName] = t;
                }
            }
            if (section == "ScriptedImporter")
            {
                string script = ImportNative.MetaValue(path, "script");
                if (!string.IsNullOrEmpty(script) && s_Types.TryGetValue(script, out Type scripted))
                    return scripted;
            }
            return s_Types.TryGetValue("HelixEditor." + section, out Type t2) ? t2 : typeof(AssetImporter);
        }

        // An importer by its description's name ("ScriptedImporter:<class>" for the scripted ones).
        internal static Type ByName(string name, string path)
        {
            if (string.IsNullOrEmpty(name))
                return null;
            ForSection("", path);
            const string scripted = "ScriptedImporter:";
            if (name.StartsWith(scripted, StringComparison.Ordinal))
                return s_Types.TryGetValue(name.Substring(scripted.Length), out Type s) ? s : null;
            return s_Types.TryGetValue("HelixEditor." + name, out Type t) ? t : typeof(AssetImporter);
        }

        internal static void Forget() => s_Types = null;
    }

    public class DefaultImporter : AssetImporter { }

    public enum TextureImporterType
    {
        Default = 0,
        NormalMap = 1,
        GUI = 2,
        Cookie = 4,
        Lightmap = 6,
        Cursor = 7,
        Sprite = 8,
        SingleChannel = 10,
        Shadowmask = 11,
        DirectionalLightmap = 12,
    }

    /// <summary>A texture's import settings (the keys of its .meta's TextureImporter section).</summary>
    public class TextureImporter : AssetImporter
    {
        public bool isReadable { get => GetBool("isReadable"); set => SetBool("isReadable", value); }
        public bool mipmapEnabled { get => GetBool("mipmaps.enableMipMap", true); set => SetBool("mipmaps.enableMipMap", value); }
        public bool sRGBTexture { get => GetBool("mipmaps.sRGBTexture", true); set => SetBool("mipmaps.sRGBTexture", value); }
        public int maxTextureSize { get => GetInt("maxTextureSize", 2048); set => SetInt("maxTextureSize", value); }
        public TextureImporterType textureType { get => (TextureImporterType)GetInt("textureType"); set => SetInt("textureType", (int)value); }
        public FilterMode filterMode { get => (FilterMode)GetInt("textureSettings.filterMode", 1); set => SetInt("textureSettings.filterMode", (int)value); }
        public int anisoLevel { get => GetInt("textureSettings.aniso", 1); set => SetInt("textureSettings.aniso", value); }
        public float mipMapBias { get => GetFloat("textureSettings.mipBias"); set => SetFloat("textureSettings.mipBias", value); }
        public TextureWrapMode wrapModeU { get => (TextureWrapMode)GetInt("textureSettings.wrapU"); set => SetInt("textureSettings.wrapU", (int)value); }
        public TextureWrapMode wrapModeV { get => (TextureWrapMode)GetInt("textureSettings.wrapV"); set => SetInt("textureSettings.wrapV", (int)value); }
        public TextureWrapMode wrapModeW { get => (TextureWrapMode)GetInt("textureSettings.wrapW"); set => SetInt("textureSettings.wrapW", (int)value); }
        public TextureWrapMode wrapMode
        {
            get => wrapModeU;
            set
            {
                wrapModeU = value;
                wrapModeV = value;
                wrapModeW = value;
            }
        }
        public bool alphaIsTransparency { get => GetBool("alphaIsTransparency"); set => SetBool("alphaIsTransparency", value); }
        public float spritePixelsPerUnit { get => GetFloat("spritePixelsToUnits", 100); set => SetFloat("spritePixelsToUnits", value); }
    }

    /// <summary>A model's import settings (the keys of its .meta's ModelImporter section).</summary>
    public class ModelImporter : AssetImporter
    {
        public float globalScale { get => GetFloat("meshes.globalScale", 1); set => SetFloat("meshes.globalScale", value); }
        public bool useFileScale { get => GetBool("meshes.useFileScale", true); set => SetBool("meshes.useFileScale", value); }
        public bool isReadable { get => GetBool("meshes.isReadable"); set => SetBool("meshes.isReadable", value); }
        public bool importBlendShapes { get => GetBool("meshes.importBlendShapes", true); set => SetBool("meshes.importBlendShapes", value); }
        public bool importAnimation { get => GetBool("importAnimation", true); set => SetBool("importAnimation", value); }
    }

    /// <summary>An audio clip's import settings (the keys of its .meta's AudioImporter section).</summary>
    public class AudioImporter : AssetImporter
    {
        public bool forceToMono { get => GetBool("forceToMono"); set => SetBool("forceToMono", value); }
        public bool loadInBackground { get => GetBool("loadInBackground"); set => SetBool("loadInBackground", value); }
    }

    /// <summary>A font's import settings (the keys of its .meta's TrueTypeFontImporter section).</summary>
    public class TrueTypeFontImporter : AssetImporter
    {
        public int fontSize { get => GetInt("fontSize", 16); set => SetInt("fontSize", value); }
        public int characterSpacing { get => GetInt("characterSpacing"); set => SetInt("characterSpacing", value); }
        public int characterPadding { get => GetInt("characterPadding", 1); set => SetInt("characterPadding", value); }
        public bool includeFontData { get => GetBool("includeFontData", true); set => SetBool("includeFontData", value); }
    }

    public class MonoImporter : AssetImporter
    {
        public int executionOrderInternal { get => GetInt("executionOrder"); set => SetInt("executionOrder", value); }
    }
}

namespace HelixEditor.AssetImporters
{
    /// <summary>Makes a ScriptedImporter class the importer of files of its extensions (a newer version imports
    /// them again).</summary>
    [AttributeUsage(AttributeTargets.Class, Inherited = false)]
    public sealed class ScriptedImporterAttribute : Attribute
    {
        public int version { get; private set; }
        public string[] fileExtensions { get; private set; }
        public string[] overrideFileExtensions { get; private set; }
        public int importQueuePriority { get; private set; }
        public bool AllowCaching { get; set; }

        public ScriptedImporterAttribute(int version, string ext) : this(version, new[] { ext }) { }
        public ScriptedImporterAttribute(int version, string[] exts) : this(version, exts, 0) { }

        public ScriptedImporterAttribute(int version, string[] exts, int importQueueOffset)
        {
            this.version = version;
            fileExtensions = exts ?? Array.Empty<string>();
            overrideFileExtensions = Array.Empty<string>();
            importQueuePriority = importQueueOffset;
        }

        public ScriptedImporterAttribute(int version, string[] exts, string[] overrideExts) : this(version, exts, 0) =>
            overrideFileExtensions = overrideExts ?? Array.Empty<string>();

        public ScriptedImporterAttribute(int version, string[] exts, string[] overrideExts, int importQueueOffset) : this(version, exts, importQueueOffset) =>
            overrideFileExtensions = overrideExts ?? Array.Empty<string>();
    }

    /// <summary>An importer written in C#: OnImportAsset makes the asset's objects from its file.</summary>
    public abstract class ScriptedImporter : AssetImporter
    {
        public abstract void OnImportAsset(AssetImportContext ctx);

        public virtual bool SupportsRemappedAssetType(Type type) => false;
    }

    /// <summary>What an import makes: the asset's objects under identifiers, its main object, its messages.</summary>
    public class AssetImportContext
    {
        readonly ulong m_Context;
        readonly List<Object> m_Objects = new List<Object>();
        Object m_Main;

        internal AssetImportContext(ulong context, string path)
        {
            m_Context = context;
            assetPath = path;
        }

        public string assetPath { get; }
        public Object mainObject => m_Main;

        public void AddObjectToAsset(string identifier, Object obj) => AddObjectToAsset(identifier, obj, null);

        public void AddObjectToAsset(string identifier, Object obj, Texture2D thumbnail)
        {
            if (obj == null)
                throw new ArgumentNullException(nameof(obj));
            if (string.IsNullOrEmpty(identifier))
                throw new ArgumentException("An object of an asset needs an identifier.", nameof(identifier));
            m_Objects.Add(obj);
            ImportNative.AddObject(m_Context, identifier, obj);
        }

        public void SetMainObject(Object obj)
        {
            m_Main = obj;
            ImportNative.SetMainObject(m_Context, obj);
        }

        public void GetObjects(List<Object> objects)
        {
            objects.Clear();
            objects.AddRange(m_Objects);
        }

        /// <summary>The import is made again when the file at the path changes (it may not exist yet).</summary>
        public void DependsOnSourceAsset(string path)
        {
            if (string.IsNullOrEmpty(path))
                throw new ArgumentException("A dependency needs a path.", nameof(path));
            ImportNative.DependsOn(m_Context, path, 0);
        }

        public void DependsOnSourceAsset(GUID guid) => DependsOnSourceAsset(AssetDatabase.GUIDToAssetPath(guid));

        /// <summary>The import is made again when the import of the asset at the path changes.</summary>
        public void DependsOnArtifact(string path)
        {
            if (string.IsNullOrEmpty(path))
                throw new ArgumentException("A dependency needs a path.", nameof(path));
            ImportNative.DependsOn(m_Context, path, 1);
        }

        public void DependsOnArtifact(GUID guid) => DependsOnArtifact(AssetDatabase.GUIDToAssetPath(guid));

        /// <summary>The import is made again when the custom dependency's value changes (RegisterCustomDependency).</summary>
        public void DependsOnCustomDependency(string dependency)
        {
            if (string.IsNullOrEmpty(dependency))
                throw new ArgumentException("A custom dependency needs a name.", nameof(dependency));
            ImportNative.DependsOn(m_Context, dependency, 2);
        }

        /// <summary>The main object of another asset, the import depending on its import.</summary>
        public Object GetReferenceToAssetMainObject(string path)
        {
            DependsOnArtifact(path);
            return AssetDatabase.LoadMainAssetAtPath(path);
        }

        public void LogImportError(string msg, Object obj = null) => ImportNative.LogImportMessage(m_Context, msg, true);
        public void LogImportWarning(string msg, Object obj = null) => ImportNative.LogImportMessage(m_Context, msg, false);
    }

    // The scripted importers: registered from the loaded assemblies once the bindings are there; the engine's loader
    // calls back into C# to run one.
    internal static unsafe class ScriptedImporters
    {
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
            void* set = Helix.Scripting.Host.FindExport("HelixEditor.SetImportCallback");
            if (set != null)
            {
                delegate* unmanaged[Cdecl]<ulong, void> call = &Import;
                ((delegate* unmanaged[Cdecl]<void*, void>)set)(call);
            }
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
                if (t.IsAbstract || !typeof(ScriptedImporter).IsAssignableFrom(t))
                    continue;
                var attr = t.GetCustomAttribute<ScriptedImporterAttribute>();
                if (attr == null)
                    continue;
                ImportNative.RegisterScriptedImporter(t.FullName, attr.version, string.Join(";", attr.fileExtensions));
                ImporterTypes.Forget();
            }
        }

        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvCdecl) })]
        static void Import(ulong context)
        {
            try
            {
                string path = ImportNative.ContextPath(context);
                string className = ImportNative.ContextImporter(context);
                Type type = ImporterTypes.ForSection("ScriptedImporter", path);
                if (type == null || !typeof(ScriptedImporter).IsAssignableFrom(type))
                    foreach (Assembly a in AppDomain.CurrentDomain.GetAssemblies())
                        type = a.GetType(className, false) ?? type;
                if (type == null || !typeof(ScriptedImporter).IsAssignableFrom(type))
                {
                    ImportNative.LogImportMessage(context, $"no scripted importer {className}", true);
                    return;
                }
                var importer = (ScriptedImporter)ScriptableObject.CreateInstance(type);
                importer.hideFlags = HideFlags.DontSave;
                importer.SetPathInternal(path);
                try
                {
                    importer.OnImportAsset(new AssetImportContext(context, path));
                }
                finally
                {
                    Object.DestroyImmediate(importer);
                }
            }
            catch (Exception e)
            {
                ImportNative.LogImportMessage(context, e.ToString(), true);
            }
        }
    }
}
