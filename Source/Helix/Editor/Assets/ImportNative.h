#pragma once

#include <Helix/Core/Basics/Tags.h>
#include <Helix/Core/Containers/Array.h>
#include <Helix/Core/Strings/String.h>
#include <Helix/Engine/Object/Object.h>
#include <Helix/Scripting/Bindings/Interop.h>

// What the importers of C# stand on: a ScriptedImporter class registered for its extensions (its description, a
// .meta section ScriptedImporter naming the class, and an engine loader whose integration runs the class's
// OnImportAsset on the main thread), the import context's objects (an object added under an identifier, the main
// object), and an importer's settings in the .meta (AssetImporter's properties read and written by key).
namespace helix::editor
{
    HX_CLASS(Namespace = "HelixEditor.AssetImporters", Static, Internal)
    class ImportNative
    {
    public:
        // The class (its full name) imports files of the extensions (separated by ';', no dots).
        HX_FUNCTION() static void RegisterScriptedImporter(StringView className, i32 version, StringView extensions);
        // The import in progress (C#'s callback gets its context).
        HX_FUNCTION() static String ContextPath(u64 context);
        HX_FUNCTION() static String ContextImporter(u64 context);
        HX_FUNCTION() static void AddObject(u64 context, StringView identifier, engine::Object* object);
        HX_FUNCTION() static void SetMainObject(u64 context, engine::Object* object);
        HX_FUNCTION() static void LogImportMessage(u64 context, StringView message, bool error);
        // The import depends on something: kind 0 a project path's file content, 1 a project path's import, 2 a custom
        // dependency's value (by name).
        HX_FUNCTION() static void DependsOn(u64 context, StringView name, i32 kind);
        // An asset's .meta values by key path ("mipmaps.enableMipMap"), and keys with values set (key and value
        // after each other, separated by 0x1f); false when the asset has no .meta.
        HX_FUNCTION() static String MetaValue(StringView path, StringView key);
        HX_FUNCTION() static bool SetMetaValues(StringView path, StringView keysAndValues);
        HX_FUNCTION() static bool HasMeta(StringView path);

        // An AssetPostprocessor's changes to the importer, for the import about to run (key and value after each
        // other, separated by 0x1f), and the postprocessors' names and versions (in the artifacts' keys).
        HX_FUNCTION() static void SetMetaOverride(StringView path, StringView keysAndValues);
        HX_FUNCTION() static void SetPostprocessors(StringView text);
        // What C#'s postprocessing callback gets: an import's path and objects; a refresh's paths (0 imported,
        // 1 deleted, 2 moved to, 3 moved from).
        HX_FUNCTION() static String PostprocessPath(u64 context);
        HX_FUNCTION() static Array<engine::Object*> PostprocessObjects(u64 context);
        HX_FUNCTION() static Array<String> PostprocessPaths(u64 context, i32 which);

        // The postprocessing of the project's imports (C#'s AssetPostprocessor): before one (on the main thread),
        // after one (its objects, before its artifact is written), after a refresh that changed assets. The engine's
        // loaders of the importers' extensions are wrapped to call it (once the project opens).
        static void Preprocess(StringView path);
        static void PostprocessAll(const struct AssetChanges& changes);
        static void WrapLoaders();
    };
}
