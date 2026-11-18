using System;
using System.Collections.Generic;
using System.Reflection;
using HelixEngine;
using Object = HelixEngine.Object;

namespace HelixEditor
{
    public enum AssetDeleteResult
    {
        DidNotDelete = 0,
        FailedDelete = 1,
        DidDelete = 2,
    }

    public enum AssetMoveResult
    {
        DidNotMove = 0,
        FailedMove = 1,
        DidMove = 2,
    }

    public enum RemoveAssetOptions
    {
        MoveAssetToTrash = 0,
        DeleteAssets = 2,
    }

    public enum StatusQueryOptions
    {
        ForceUpdate = 0,
        UseCachedIfPossible = 1,
        UseCachedAsync = 2,
    }

    /// <summary>Its classes' static messages hear of AssetDatabase's operations before they run: OnWillCreateAsset (the
    /// new asset's .meta path), OnWillDeleteAsset and OnWillMoveAsset (done by the processor, failed, or left to the
    /// database), OnWillSaveAssets (the paths saved), and IsOpenForEdit, CanOpenForEdit and MakeEditable (version
    /// control's word on files).</summary>
    public class AssetModificationProcessor
    {
    }

    // The processors of the loaded assemblies, asked through one callback by the native operations.
    internal static unsafe class AssetModificationProcessors
    {
        static readonly List<Type> s_Types = new List<Type>();
        static bool s_Started;

        const BindingFlags kStatic = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

#pragma warning disable CA2255
        [System.Runtime.CompilerServices.ModuleInitializer]
#pragma warning restore CA2255
        internal static void Init() => HelixEngine.Internal.ScriptBridge.WhenBindingsLoaded(Start);

        static void Start()
        {
            if (s_Started)
                return;
            s_Started = true;
            void* set = Helix.Scripting.Host.FindExport("HelixEditor.SetModificationCallback");
            if (set != null)
            {
                delegate* unmanaged[Cdecl]<int, ulong, int> call = &Ask;
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
                if (typeof(AssetModificationProcessor).IsAssignableFrom(t) && t != typeof(AssetModificationProcessor) && !s_Types.Contains(t))
                    s_Types.Add(t);
            s_Types.Sort((x, y) => string.CompareOrdinal(x.FullName, y.FullName));
        }

        static object Invoke(Type t, string name, Type[] parameters, object[] args)
        {
            MethodInfo m = t.GetMethod(name, kStatic, null, parameters, null);
            if (m == null)
                return null;
            try
            {
                return m.Invoke(null, args);
            }
            catch (TargetInvocationException e)
            {
                Debug.LogException(e.InnerException ?? e);
                return null;
            }
        }

        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvCdecl) })]
        static int Ask(int kind, ulong context)
        {
            try
            {
                if (s_Types.Count == 0)
                    return 0;
                string first = AssetModificationNative.RequestPath(context, 0);
                switch (kind)
                {
                case 0:
                    foreach (Type t in s_Types)
                        Invoke(t, "OnWillCreateAsset", new[] { typeof(string) }, new object[] { first });
                    return 0;
                case 1:
                    // The first answer that is not DidNotDelete decides.
                    foreach (Type t in s_Types)
                        if (Invoke(t, "OnWillDeleteAsset", new[] { typeof(string), typeof(RemoveAssetOptions) },
                                   new object[] { first, RemoveAssetOptions.DeleteAssets }) is AssetDeleteResult d && d != AssetDeleteResult.DidNotDelete)
                            return (int)d;
                    return 0;
                case 2:
                    string second = AssetModificationNative.RequestPath(context, 1);
                    foreach (Type t in s_Types)
                        if (Invoke(t, "OnWillMoveAsset", new[] { typeof(string), typeof(string) }, new object[] { first, second }) is AssetMoveResult m &&
                            m != AssetMoveResult.DidNotMove)
                            return (int)m;
                    return 0;
                case 3:
                    // Each processor picks from what the one before left.
                    string[] paths = AssetModificationNative.RequestPaths(context);
                    foreach (Type t in s_Types)
                        if (Invoke(t, "OnWillSaveAssets", new[] { typeof(string[]) }, new object[] { paths }) is string[] kept)
                            paths = kept;
                    AssetModificationNative.SetRequestPaths(context, string.Join("\n", paths));
                    return 0;
                }
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
            return 0;
        }

        // Version control's word: every processor's IsOpenForEdit / CanOpenForEdit (all must agree), MakeEditable.
        internal static bool Query(string name, string[] paths, List<string> outNotEditablePaths, StatusQueryOptions options)
        {
            outNotEditablePaths?.Clear();
            bool all = true;
            foreach (Type t in s_Types)
            {
                var notEditable = new List<string>();
                if (Invoke(t, name, new[] { typeof(string[]), typeof(List<string>), typeof(StatusQueryOptions) },
                           new object[] { paths, notEditable, options }) is bool ok && !ok)
                {
                    all = false;
                    foreach (string p in notEditable)
                        if (outNotEditablePaths != null && !outNotEditablePaths.Contains(p))
                            outNotEditablePaths.Add(p);
                }
            }
            return all;
        }

        internal static bool MakeEditable(string[] paths, string prompt, List<string> outNotEditablePaths)
        {
            outNotEditablePaths?.Clear();
            bool all = true;
            foreach (Type t in s_Types)
            {
                var notEditable = new List<string>();
                if (Invoke(t, "MakeEditable", new[] { typeof(string[]), typeof(string), typeof(List<string>) },
                           new object[] { paths, prompt, notEditable }) is bool ok && !ok)
                {
                    all = false;
                    outNotEditablePaths?.AddRange(notEditable);
                }
            }
            return all;
        }
    }

    public static partial class AssetDatabase
    {
        public static bool IsOpenForEdit(Object assetObject) => IsOpenForEdit(GetAssetPath(assetObject));
        public static bool IsOpenForEdit(Object assetObject, StatusQueryOptions statusOptions) => IsOpenForEdit(GetAssetPath(assetObject), statusOptions);
        public static bool IsOpenForEdit(Object assetObject, out string message) => IsOpenForEdit(GetAssetPath(assetObject), out message);
        public static bool IsOpenForEdit(string assetOrMetaFilePath) => IsOpenForEdit(assetOrMetaFilePath, StatusQueryOptions.UseCachedIfPossible);
        public static bool IsOpenForEdit(string assetOrMetaFilePath, StatusQueryOptions statusOptions) => IsOpenForEdit(assetOrMetaFilePath, out _, statusOptions);

        public static bool IsOpenForEdit(string assetOrMetaFilePath, out string message, StatusQueryOptions statusOptions = StatusQueryOptions.UseCachedIfPossible)
        {
            bool ok = AssetModificationProcessors.Query("IsOpenForEdit", new[] { assetOrMetaFilePath }, null, statusOptions);
            message = ok ? "" : $"'{assetOrMetaFilePath}' is not open for edit";
            return ok;
        }

        public static void IsOpenForEdit(string[] assetOrMetaFilePaths, List<string> outNotEditablePaths,
                                         StatusQueryOptions statusQueryOptions = StatusQueryOptions.UseCachedIfPossible) =>
            AssetModificationProcessors.Query("IsOpenForEdit", assetOrMetaFilePaths ?? Array.Empty<string>(), outNotEditablePaths, statusQueryOptions);

        public static bool IsMetaFileOpenForEdit(Object assetObject) => IsOpenForEdit(GetTextMetaFilePathFromAssetPath(GetAssetPath(assetObject)));
        public static bool IsMetaFileOpenForEdit(Object assetObject, StatusQueryOptions statusOptions) =>
            IsOpenForEdit(GetTextMetaFilePathFromAssetPath(GetAssetPath(assetObject)), statusOptions);
        public static bool IsMetaFileOpenForEdit(Object assetObject, out string message, StatusQueryOptions statusOptions = StatusQueryOptions.UseCachedIfPossible) =>
            IsOpenForEdit(GetTextMetaFilePathFromAssetPath(GetAssetPath(assetObject)), out message, statusOptions);

        public static bool CanOpenForEdit(Object assetObject) => CanOpenForEdit(GetAssetPath(assetObject));
        public static bool CanOpenForEdit(string assetOrMetaFilePath) => CanOpenForEdit(assetOrMetaFilePath, out _);

        public static bool CanOpenForEdit(string assetOrMetaFilePath, out string message, StatusQueryOptions statusOptions = StatusQueryOptions.UseCachedIfPossible)
        {
            bool ok = AssetModificationProcessors.Query("CanOpenForEdit", new[] { assetOrMetaFilePath }, null, statusOptions);
            message = ok ? "" : $"'{assetOrMetaFilePath}' cannot be opened for edit";
            return ok;
        }

        public static void CanOpenForEdit(string[] assetOrMetaFilePaths, List<string> outNotEditablePaths,
                                          StatusQueryOptions statusQueryOptions = StatusQueryOptions.UseCachedIfPossible) =>
            AssetModificationProcessors.Query("CanOpenForEdit", assetOrMetaFilePaths ?? Array.Empty<string>(), outNotEditablePaths, statusQueryOptions);

        public static bool MakeEditable(string path) => MakeEditable(new[] { path });

        public static bool MakeEditable(string[] paths, string prompt = null, List<string> outNotEditablePaths = null) =>
            AssetModificationProcessors.MakeEditable(paths ?? Array.Empty<string>(), prompt, outNotEditablePaths);
    }
}
