#pragma once

#include <Helix/Core/Basics/Tags.h>
#include <Helix/Core/Containers/Array.h>
#include <Helix/Core/Strings/String.h>
#include <Helix/Engine/Object/Object.h>
#include <Helix/Scripting/Bindings/Interop.h>

// The asset database of the editor's project (the reference's AssetDatabase): paths and guids both ways, loading by
// path, the project's files moved, copied and deleted with their .meta files, folders, FindAssets, the refresh that
// takes the files' changes. Over ProjectDatabase (its table) and the engine's loaders (by the files' extensions).
namespace helix::editor
{
    HX_CLASS(Namespace = "HelixEditor", Static)
    class AssetDatabase
    {
    public:
        HX_FUNCTION() static void Refresh();
        HX_FUNCTION() static void ImportAsset(StringView path);
        HX_FUNCTION() static String GUIDToAssetPath(StringView guid);
        HX_FUNCTION() static String AssetPathToGUID(StringView path);
        HX_FUNCTION() static bool AssetPathExists(StringView path);
        HX_FUNCTION() static bool IsValidFolder(StringView path);
        HX_FUNCTION() static Array<String> GetAllAssetPaths();
        HX_FUNCTION() static Array<String> GetSubFolders(StringView path);

        HX_FUNCTION() static engine::Object* LoadMainAssetAtPath(StringView assetPath);
        HX_FUNCTION(Internal) static Array<engine::Object*> LoadAllAssetsAtPathInternal(StringView assetPath);
        HX_FUNCTION() static bool IsMainAssetAtPathLoaded(StringView assetPath);

        // Objects and their assets: the path of an object's asset ("" for none), whether it is one, its main or a
        // sub object, of a native asset (the engine's serialized file) or a foreign one (imported from another
        // format); its guid and fileID ("<guid> <fileID>", "" for none).
        HX_FUNCTION() static String GetAssetPath(engine::Object* assetObject);
        HX_FUNCTION() static String GetAssetOrScenePath(engine::Object* assetObject);
        HX_FUNCTION() static bool Contains(engine::Object* obj);
        HX_FUNCTION() static bool IsMainAsset(engine::Object* obj);
        HX_FUNCTION() static bool IsSubAsset(engine::Object* obj);
        HX_FUNCTION() static bool IsNativeAsset(engine::Object* obj);
        HX_FUNCTION() static bool IsForeignAsset(engine::Object* obj);
        HX_FUNCTION(Internal) static String GuidAndFileIdInternal(engine::Object* obj);
        // The class names of an asset's main object and of an object of it ("" for none).
        HX_FUNCTION(Internal) static String MainAssetTypeNameInternal(StringView assetPath);
        HX_FUNCTION(Internal) static String TypeNameFromPathAndFileIdInternal(StringView assetPath, i64 localIdentifierInFile);
        HX_FUNCTION() static String GenerateUniqueAssetPath(StringView path);

        // Native assets: an object made an asset at a path (the engine's serialized file; one there is replaced),
        // objects added to and removed from one, its main object; the dirty ones written.
        HX_FUNCTION() static void CreateAsset(engine::Object* asset, StringView path);
        HX_FUNCTION(Internal) static void AddObjectToAssetInternal(engine::Object* objectToAdd, StringView path);
        HX_FUNCTION() static void RemoveObjectFromAsset(engine::Object* objectToRemove);
        HX_FUNCTION() static void SetMainObject(engine::Object* mainObject, StringView assetPath);
        HX_FUNCTION() static void SaveAssetIfDirty(engine::Object* obj);
        HX_FUNCTION(Internal) static void SaveAssetIfDirtyByGuidInternal(StringView guid);

        // Labels (in the .meta; FindAssets' l:), separated by new lines when set.
        HX_FUNCTION(Internal) static Array<String> LabelsInternal(StringView path);
        HX_FUNCTION(Internal) static void SetLabelsInternal(StringView path, StringView labels);

        // The paths of the assets the assets depend on (themselves first; paths separated by new lines): the guids a
        // native asset's file names, the dependencies its import recorded; recursive: theirs too.
        HX_FUNCTION(Internal) static Array<String> GetDependenciesInternal(StringView paths, bool recursive);
        // 32 hex digits ("" for no asset).
        HX_FUNCTION(Internal) static String AssetDependencyHashInternal(StringView path);
        HX_FUNCTION(Internal) static void RegisterCustomDependencyInternal(StringView dependency, StringView hashOfValue);
        HX_FUNCTION() static u32 UnregisterCustomDependencyPrefixFilter(StringView prefixFilter);

        // The refresh as the editor comes to the front (counted: every Disallow needs its Allow).
        HX_FUNCTION() static void AllowAutoRefresh();
        HX_FUNCTION() static void DisallowAutoRefresh();
        static bool AutoRefreshAllowed();

        // Importers: the one an asset has (its .meta's section), those that take its extension (the native one
        // first, then the scripted ones as "ScriptedImporter:<class>"), the native one.
        HX_FUNCTION(Internal) static Array<String> AvailableImportersInternal(StringView path);
        HX_FUNCTION(Internal) static String DefaultImporterInternal(StringView path);

        // The new folder's guid ("" when it could not be made).
        HX_FUNCTION() static String CreateFolder(StringView parentFolder, StringView newFolderName);
        // "" on success, else the reason.
        HX_FUNCTION() static String MoveAsset(StringView oldPath, StringView newPath);
        HX_FUNCTION() static String RenameAsset(StringView pathName, StringView newName);
        HX_FUNCTION() static String ValidateMoveAsset(StringView oldPath, StringView newPath);
        HX_FUNCTION() static bool CopyAsset(StringView path, StringView newPath);
        HX_FUNCTION() static bool DeleteAsset(StringView path);

        // The guids of the assets a filter finds: words in their names, t:<type> (the importer's main type, a folder's
        // "Folder"), in folders (separated by new lines; none: all).
        HX_FUNCTION(Internal) static Array<String> FindAssetsInternal(StringView filter, StringView folders);
        // The importer of an asset (its .meta's section).
        HX_FUNCTION(Internal) static String ImporterNameInternal(StringView path);
        // The asset's current artifact file ("" for none), the imports in progress, the artifacts written since start.
        HX_FUNCTION(Internal) static String ArtifactPathInternal(StringView path);
        HX_FUNCTION(Internal) static i32 ImportsPendingInternal();
        HX_FUNCTION(Internal) static i32 ArtifactsWrittenInternal();

        // Changes made between the two are refreshed once, at the end.
        HX_FUNCTION() static void StartAssetEditing();
        HX_FUNCTION() static void StopAssetEditing();
        HX_FUNCTION() static void SaveAssets();
    };
}
