using System;
using System.Collections.Generic;
using System.Reflection;
using HelixEditor.AssetImporters;
using HelixEngine;
using Object = HelixEngine.Object;

namespace HelixEditor
{
    /// <summary>Takes part in the project's imports: a class of it gets the messages it has (OnPreprocessAsset,
    /// OnPreprocessTexture ... before an import, the importer's changes applying to that import; OnPostprocessTexture
    /// ... after one, on its objects; the static OnPostprocessAllAssets after a refresh that changed assets). One
    /// instance per import, in GetPostprocessOrder's order; its class's name and GetVersion are in the artifacts' keys.</summary>
    public class AssetPostprocessor
    {
        string m_AssetPath;
        AssetImporter m_Importer;

        public string assetPath { get => m_AssetPath; set => m_AssetPath = value; }
        public AssetImporter assetImporter => m_Importer;
        // Scripted imports have one; the engine's imports none yet.
        public AssetImportContext context => null;

        public virtual int GetPostprocessOrder() => 0;
        public virtual uint GetVersion() => 0;

        public void LogWarning(string warning, Object context = null) => Debug.LogWarning($"{m_AssetPath}: {warning}");
        public void LogError(string warning, Object context = null) => Debug.LogError($"{m_AssetPath}: {warning}");

        internal void BeginInternal(string path, AssetImporter importer)
        {
            m_AssetPath = path;
            m_Importer = importer;
        }
    }

    // The postprocessors of the loaded assemblies and the messages of each importer kind; the engine's imports call
    // in through one callback (before, after, all).
    internal static unsafe class AssetPostprocessors
    {
        // The messages of an importer kind: before its import, after it on objects of a type (null: none yet).
        sealed class Kind
        {
            public Type importer, objects;
            public string pre, post;
        }

        static readonly Kind[] s_Kinds =
        {
            new Kind { importer = typeof(TextureImporter), pre = "OnPreprocessTexture", post = "OnPostprocessTexture", objects = typeof(Texture2D) },
            new Kind { importer = typeof(ModelImporter), pre = "OnPreprocessModel", post = "OnPostprocessModel", objects = typeof(GameObject) },
            new Kind { importer = typeof(AudioImporter), pre = "OnPreprocessAudio" },
        };

        static readonly List<Type> s_Types = new List<Type>();
        // The instances of the imports in progress (made before, used again after).
        static readonly Dictionary<string, List<AssetPostprocessor>> s_Running = new Dictionary<string, List<AssetPostprocessor>>();
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
            void* set = Helix.Scripting.Host.FindExport("HelixEditor.SetPostprocessCallback");
            if (set != null)
            {
                delegate* unmanaged[Cdecl]<int, ulong, void> call = &Postprocess;
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
            bool added = false;
            foreach (Type t in types)
                if (!t.IsAbstract && typeof(AssetPostprocessor).IsAssignableFrom(t) && t != typeof(AssetPostprocessor) && !s_Types.Contains(t))
                {
                    s_Types.Add(t);
                    added = true;
                }
            if (!added)
                return;
            var orders = new Dictionary<Type, int>();
            var versions = new List<string>();
            foreach (Type t in s_Types)
            {
                var p = (AssetPostprocessor)Activator.CreateInstance(t, true);
                orders[t] = p.GetPostprocessOrder();
            }
            s_Types.Sort((x, y) => orders[x] != orders[y] ? orders[x].CompareTo(orders[y]) : string.CompareOrdinal(x.FullName, y.FullName));
            foreach (Type t in s_Types)
                versions.Add($"{t.FullName}:{((AssetPostprocessor)Activator.CreateInstance(t, true)).GetVersion()}");
            ImportNative.SetPostprocessors(string.Join(";", versions));
        }

        static Kind KindOf(AssetImporter importer)
        {
            if (importer == null)
                return null;
            foreach (Kind k in s_Kinds)
                if (k.importer.IsInstanceOfType(importer))
                    return k;
            return null;
        }

        static void Call(object target, Type type, string name, object[] args, Type[] parameters)
        {
            MethodInfo m = type.GetMethod(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, parameters, null);
            if (m == null)
                return;
            try
            {
                m.Invoke(target, args);
            }
            catch (TargetInvocationException e)
            {
                Debug.LogException(e.InnerException ?? e);
            }
        }

        static List<AssetPostprocessor> Instances(string path, AssetImporter importer)
        {
            var list = new List<AssetPostprocessor>();
            foreach (Type t in s_Types)
            {
                var p = (AssetPostprocessor)Activator.CreateInstance(t, true);
                p.BeginInternal(path, importer);
                list.Add(p);
            }
            return list;
        }

        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvCdecl) })]
        static void Postprocess(int kind, ulong context)
        {
            try
            {
                if (s_Types.Count == 0)
                    return;
                if (kind == 0)
                    Before(ImportNative.PostprocessPath(context));
                else if (kind == 1)
                    After(ImportNative.PostprocessPath(context), ImportNative.PostprocessObjects(context));
                else
                    All(ImportNative.PostprocessPaths(context, 0), ImportNative.PostprocessPaths(context, 1), ImportNative.PostprocessPaths(context, 2),
                        ImportNative.PostprocessPaths(context, 3));
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }

        static void Before(string path)
        {
            AssetImporter importer = AssetImporter.GetAtPath(path);
            List<AssetPostprocessor> list = Instances(path, importer);
            s_Running[path] = list;
            Kind k = KindOf(importer);
            foreach (AssetPostprocessor p in list)
            {
                Call(p, p.GetType(), "OnPreprocessAsset", null, Type.EmptyTypes);
                if (k?.pre != null)
                    Call(p, p.GetType(), k.pre, null, Type.EmptyTypes);
            }
            // The changes apply to this import only (the .meta stays as it is).
            string changes = importer?.TakeChangesInternal();
            if (!string.IsNullOrEmpty(changes))
                ImportNative.SetMetaOverride(path, changes);
            if (importer != null)
                Object.DestroyImmediate(importer);
        }

        static void After(string path, Object[] objects)
        {
            if (!s_Running.Remove(path, out List<AssetPostprocessor> list))
                list = Instances(path, null);
            AssetImporter importer = list.Count > 0 && list[0].assetImporter != null ? list[0].assetImporter : null;
            Kind k = KindOf(importer ?? AssetImporter.GetAtPath(path));
            foreach (AssetPostprocessor p in list)
            {
                if (k?.post != null && k.objects != null)
                    foreach (Object o in objects)
                        if (k.objects.IsInstanceOfType(o) && (k.objects != typeof(GameObject) || ((GameObject)o).transform.parent == null))
                            Call(p, p.GetType(), k.post, new object[] { o }, new[] { k.objects });
                foreach (Object o in objects)
                    if (o is Material m)
                        Call(p, p.GetType(), "OnPostprocessMaterial", new object[] { m }, new[] { typeof(Material) });
            }
        }

        static void All(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
        {
            foreach (Type t in s_Types)
            {
                const BindingFlags flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
                Type s = typeof(string[]);
                MethodInfo withReload = t.GetMethod("OnPostprocessAllAssets", flags, null, new[] { s, s, s, s, typeof(bool) }, null);
                MethodInfo plain = t.GetMethod("OnPostprocessAllAssets", flags, null, new[] { s, s, s, s }, null);
                try
                {
                    if (withReload != null)
                        withReload.Invoke(null, new object[] { imported, deleted, moved, movedFrom, false });
                    else
                        plain?.Invoke(null, new object[] { imported, deleted, moved, movedFrom });
                }
                catch (TargetInvocationException e)
                {
                    Debug.LogException(e.InnerException ?? e);
                }
            }
        }
    }
}
