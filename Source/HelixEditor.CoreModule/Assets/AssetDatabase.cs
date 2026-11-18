using System;
using System.Collections.Generic;
using System.Reflection;
using HelixEditor.AssetImporters;
using HelixEngine;
using Object = HelixEngine.Object;

namespace HelixEditor
{
    // The asset database's C# written by hand: the generic and typed loads, FindAssets' overloads and the editing scope.
    public static partial class AssetDatabase
    {
        public static T LoadAssetAtPath<T>(string assetPath) where T : Object => LoadAssetAtPath(assetPath, typeof(T)) as T;

        /// <summary>The asset's first object of a type at the path (the main object first), null for none.</summary>
        public static Object LoadAssetAtPath(string assetPath, Type type)
        {
            if (string.IsNullOrEmpty(assetPath) || type == null)
                return null;
            foreach (Object o in LoadAllAssetsAtPathInternal(assetPath))
                if (o != null && type.IsInstanceOfType(o))
                    return o;
            return null;
        }

        public static Object[] LoadAllAssetsAtPath(string assetPath) => LoadAllAssetsAtPathInternal(assetPath);

        public static string[] FindAssets(string filter) => FindAssets(filter, null);

        /// <summary>The guids of the assets the filter finds (words of their names, t:Type) in the folders (null: all).</summary>
        public static string[] FindAssets(string filter, string[] searchInFolders) =>
            FindAssetsInternal(filter ?? "", searchInFolders == null ? "" : string.Join("\n", searchInFolders));

        // ---------------------------------------------------------------------------------------------- by GUID

        public static GUID GUIDFromAssetPath(string path) => new GUID(AssetPathToGUID(path));
        public static string GUIDToAssetPath(GUID guid) => GUIDToAssetPath(guid.ToString());

        public static T LoadAssetByGUID<T>(GUID assetGUID) where T : Object => LoadAssetByGUID(assetGUID, typeof(T)) as T;
        public static Object LoadAssetByGUID(GUID assetGUID, Type type) => LoadAssetAtPath(GUIDToAssetPath(assetGUID), type);

        public static GUID[] FindAssetGUIDs(string filter) => FindAssetGUIDs(filter, null);

        public static GUID[] FindAssetGUIDs(string filter, string[] searchInFolders) =>
            Array.ConvertAll(FindAssets(filter, searchInFolders), g => new GUID(g));

        // ---------------------------------------------------------------------------------------------- objects

        public static bool TryGetGUIDAndLocalFileIdentifier(Object obj, out string guid, out long localId)
        {
            string both = obj == null ? "" : GuidAndFileIdInternal(obj);
            int space = both.IndexOf(' ');
            if (space < 0)
            {
                guid = "";
                localId = 0;
                return false;
            }
            guid = both.Substring(0, space);
            localId = long.Parse(both.Substring(space + 1), System.Globalization.CultureInfo.InvariantCulture);
            return true;
        }

        public static Type GetMainAssetTypeAtPath(string assetPath) => TypeNamed(MainAssetTypeNameInternal(assetPath ?? ""));
        public static Type GetMainAssetTypeFromGUID(GUID guid) => GetMainAssetTypeAtPath(GUIDToAssetPath(guid));

        public static Type GetTypeFromPathAndFileID(string assetPath, long localIdentifierInFile) =>
            TypeNamed(TypeNameFromPathAndFileIdInternal(assetPath ?? "", localIdentifierInFile));

        public static string GetTextMetaFilePathFromAssetPath(string path) => string.IsNullOrEmpty(path) ? "" : path.TrimEnd('/') + ".meta";

        public static string GetAssetPathFromTextMetaFilePath(string path) =>
            path != null && path.EndsWith(".meta", StringComparison.OrdinalIgnoreCase) ? path.Substring(0, path.Length - 5) : "";

        static Dictionary<string, Type> s_ObjectTypes;

        // An object class by its name (the engine's and the editor's, then the loaded assemblies').
        static Type TypeNamed(string name)
        {
            if (string.IsNullOrEmpty(name))
                return null;
            if (s_ObjectTypes == null)
            {
                s_ObjectTypes = new Dictionary<string, Type>();
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
                        if (typeof(Object).IsAssignableFrom(t) && (!s_ObjectTypes.ContainsKey(t.Name) || t.Namespace == "HelixEngine"))
                            s_ObjectTypes[t.Name] = t;
                }
            }
            return s_ObjectTypes.TryGetValue(name, out Type found) ? found : null;
        }

        // ---------------------------------------------------------------------------------------------- native assets

        public static void AddObjectToAsset(Object objectToAdd, string path) => AddObjectToAssetInternal(objectToAdd, path);
        public static void AddObjectToAsset(Object objectToAdd, Object assetObject) => AddObjectToAssetInternal(objectToAdd, GetAssetPath(assetObject));
        public static void SaveAssetIfDirty(GUID guid) => SaveAssetIfDirtyByGuidInternal(guid.ToString());

        public static string[] GetLabels(Object obj) => obj == null ? Array.Empty<string>() : LabelsInternal(GetAssetPath(obj));
        public static string[] GetLabels(GUID guid) => LabelsInternal(GUIDToAssetPath(guid));

        public static void SetLabels(Object obj, string[] labels)
        {
            if (obj != null)
                SetLabelsInternal(GetAssetPath(obj), labels == null ? "" : string.Join("\n", labels));
        }

        public static void ClearLabels(Object obj) => SetLabels(obj, null);

        // ---------------------------------------------------------------------------------------------- dependencies

        public static string[] GetDependencies(string pathName) => GetDependencies(pathName, true);
        public static string[] GetDependencies(string pathName, bool recursive) => GetDependenciesInternal(pathName ?? "", recursive);
        public static string[] GetDependencies(string[] pathNames) => GetDependencies(pathNames, true);

        public static string[] GetDependencies(string[] pathNames, bool recursive) =>
            GetDependenciesInternal(pathNames == null ? "" : string.Join("\n", pathNames), recursive);

        public static Hash128 GetAssetDependencyHash(string path)
        {
            string hash = AssetDependencyHashInternal(path ?? "");
            return string.IsNullOrEmpty(hash) ? default : Hash128.Parse(hash);
        }

        public static Hash128 GetAssetDependencyHash(GUID guid) => GetAssetDependencyHash(GUIDToAssetPath(guid));

        public static void RegisterCustomDependency(string dependency, Hash128 hashOfValue)
        {
            if (string.IsNullOrEmpty(dependency))
                throw new ArgumentException("A custom dependency needs a name.", nameof(dependency));
            RegisterCustomDependencyInternal(dependency, hashOfValue.ToString());
        }

        // ---------------------------------------------------------------------------------------------- batches

        /// <summary>The copies made in one refresh; false when any failed (or the lists differ in length).</summary>
        public static bool CopyAssets(string[] paths, string[] newPaths)
        {
            if (paths == null || newPaths == null || paths.Length != newPaths.Length)
                return false;
            bool all = true;
            using (new AssetEditingScope())
                for (int i = 0; i < paths.Length; i++)
                    all &= CopyAsset(paths[i], newPaths[i]);
            return all;
        }

        /// <summary>The deletions made in one refresh; the paths that failed listed.</summary>
        public static bool DeleteAssets(string[] paths, List<string> outFailedPaths)
        {
            outFailedPaths?.Clear();
            if (paths == null)
                return false;
            bool all = true;
            using (new AssetEditingScope())
                foreach (string p in paths)
                    if (!DeleteAsset(p))
                    {
                        all = false;
                        outFailedPaths?.Add(p);
                    }
            return all;
        }

        // ---------------------------------------------------------------------------------------------- importers

        public static Type GetImporterType(string assetPath)
        {
            string section = ImporterNameInternal(assetPath ?? "");
            return string.IsNullOrEmpty(section) ? null : ImporterTypes.ForSection(section, assetPath);
        }

        public static Type GetImporterType(GUID guid) => GetImporterType(GUIDToAssetPath(guid));
        public static Type[] GetImporterTypes(string[] paths) => paths == null ? Array.Empty<Type>() : Array.ConvertAll(paths, GetImporterType);

        public static Type[] GetImporterTypes(ReadOnlySpan<GUID> guids)
        {
            var types = new Type[guids.Length];
            for (int i = 0; i < guids.Length; i++)
                types[i] = GetImporterType(guids[i]);
            return types;
        }

        public static Type[] GetAvailableImporters(string path)
        {
            var types = new List<Type>();
            foreach (string name in AvailableImportersInternal(path ?? ""))
                if (ImporterTypes.ByName(name, path) is Type t && !types.Contains(t))
                    types.Add(t);
            return types.ToArray();
        }

        public static Type GetDefaultImporter(string path) => ImporterTypes.ByName(DefaultImporterInternal(path ?? ""), path);

        /// <summary>StartAssetEditing in a using block: the changes refreshed once as it ends.</summary>
        public sealed class AssetEditingScope : IDisposable
        {
            bool m_Disposed;

            public AssetEditingScope() => StartAssetEditing();

            public void Dispose()
            {
                if (m_Disposed)
                    return;
                m_Disposed = true;
                StopAssetEditing();
            }
        }
    }
}
