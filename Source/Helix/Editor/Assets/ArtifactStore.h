#pragma once

#include <Helix/Core/Basics/Span.h>
#include <Helix/Core/Containers/Array.h>
#include <Helix/Core/Strings/String.h>
#include <Helix/Core/Types/Guid.h>

// The project's import artifacts (Library/): an asset's objects as its importer made them, written as a binary asset
// file under a key of what made them (the guid, the importer and its version, the .meta's text, the file's content,
// the store's format), so an asset whose key is the same loads from its artifact, not from its file. Library/
// ArtifactDB.txt keeps each asset's key with the times of its file and .meta (a quick check; the key is computed again
// when they differ) and the dependencies its import recorded (another asset's file, or its artifact's key, with the
// hash they had: a change of one makes the artifact no longer current). An import loads the asset from its file
// (decoded on jobs, several at once) and writes the artifact once it is loaded.
namespace helix::editor
{
    struct AssetRecord;
}

namespace helix::engine
{
    class AssetObjects;
}

namespace helix::editor
{

    class ArtifactStore
    {
    public:
        static void Open(StringView projectRoot);
        static void Close();
        // Imports run (the editor's host turns them on; off, assets load from their files and no artifact is written).
        static void SetImports(bool on);
        // The asset's artifact is no longer current (imported again whatever its times say); its file is removed.
        static void Invalidate(const Guid& guid);
        // Entries of assets the project no longer has go with their files; after the store opened, also the files
        // no entry names (left by an editor that stopped before saving its database).
        static void Prune();

        // The artifact file an asset loads from now ("" when none is current for its file, settings and importer).
        static String Current(const AssetRecord& record);
        static String KeyOf(const AssetRecord& record);

        // An import of the asset (unless its artifact is current); Tick finishes those loaded, ImportAll waits.
        static void Queue(const AssetRecord& record);
        // An import of a project asset integrated its objects: its artifact is written now (before the renderer
        // drops the CPU copies of what is not readable).
        static void Integrated(const engine::AssetObjects& objects);
        // Imports finished in this call (their artifacts written); the asset table is listed again when any did.
        static u32 Tick();
        static void ImportAll();
        static u32 Pending();
        static bool IsPending(const Guid& guid);
        // Artifacts written and imports made since the store opened (for tests and the status bar).
        static u32 Written();

        // What an import depends on: a project path's file content, a project path's import (its artifact's key), or
        // a custom dependency's value (by name, RegisterCustom).
        enum class Dependency : u8
        {
            Source,
            Artifact,
            Custom
        };
        // The import of an asset starts (the dependencies recorded before are dropped), and depends on something (a
        // path with no asset yet, or a custom name with no value, counts as missing).
        static void BeginImport(const Guid& guid);
        static void AddDependency(const Guid& guid, StringView name, Dependency kind);
        // The assets whose artifacts depend on one of the paths, or on one of the custom dependencies.
        static void DependentsOf(Span<const String> paths, Span<const String> customs, Array<Guid>& out);
        // The paths an asset's artifact depends on (its sources and artifacts).
        static void DependenciesOf(const Guid& guid, Array<String>& out);
        // The hash of what an asset's import depends on: its artifact's key, or its import's key without one.
        static String DependencyHash(const AssetRecord& record);

        // Custom dependencies (AssetDatabase.RegisterCustomDependency): their values (32 hex digits), kept in the
        // database; a changed value's names are taken by the next refresh.
        static void RegisterCustom(StringView name, StringView value);
        static u32 UnregisterCustomPrefix(StringView prefix);
        static void TakeChangedCustoms(Array<String>& out);

        // What else goes into every key: the asset postprocessors' names and versions (a change imports again).
        static void SetPostprocessors(StringView text);
    };
}
