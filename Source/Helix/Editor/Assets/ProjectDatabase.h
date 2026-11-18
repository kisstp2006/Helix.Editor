#pragma once

#include <Helix/Core/Basics/Span.h>
#include <Helix/Core/Containers/Array.h>
#include <Helix/Core/Strings/String.h>
#include <Helix/Core/Types/Guid.h>
#include <Helix/Editor/Assets/MetaFile.h>

namespace helix::engine
{
    class AssetSource;
}

// A project's files: every file and folder under Assets/ and Packages/ (but hidden ones, names ending in '~' and the
// .meta files themselves) with its .meta: one is written for a new file (a new guid and its importer's settings), a
// duplicated or broken guid is made anew, a .meta whose asset went goes too. Refresh walks the folders and tells
// what changed since the last walk (new and changed assets, deleted ones, moved ones by their guid). The table is the
// asset database's (paths and guids both ways) and the engine's asset source of the project (its loaders read the
// files by their project paths, "Assets/Textures/A.png").
namespace helix::editor
{
    struct AssetRecord
    {
        Guid guid;
        // From the project's root, forward slashes ("Assets/Textures/A.png").
        String path;
        bool folder = false;
        i64 size = 0, modified = 0, metaModified = 0;
        String importer;
    };

    struct AssetChanges
    {
        Array<String> imported, deleted;
        // Moved assets: from where to where (the same guid).
        Array<String> movedFrom, movedTo;
        // .meta files written (new assets, guids made anew) and removed (their assets gone).
        u32 metasWritten = 0, metasRemoved = 0;
        bool Empty() const { return imported.Empty() && deleted.Empty() && movedFrom.Empty(); }
    };

    class ProjectDatabase
    {
    public:
        // The project at a folder (its Assets folder made when missing); the engine's asset source of it added.
        static bool Open(StringView root);
        static void Close();
        static bool IsOpen();
        static StringView Root();

        static AssetChanges Refresh();
        // While deferred (AssetDatabase.StartAssetEditing) a refresh waits for the end of it.
        static void Defer(bool on);
        // A loaded asset loaded again into its objects (its file or settings changed); false when it is not loaded.
        static bool Reimport(StringView path);
        // An asset whose objects are those in memory (CreateAsset wrote them): the refresh that finds its file does
        // not load it again into them.
        static void CreatedInMemory(StringView path);

        static const AssetRecord* ByGuid(const Guid& guid);
        // A project path (case does not matter; trailing slashes neither).
        static const AssetRecord* ByPath(StringView path);
        static Span<const AssetRecord> All();
        // The .meta of an asset, read now (false when it has none).
        static bool ReadMeta(StringView path, MetaFile& out);
        static bool WriteMeta(StringView path, const MetaFile& meta);

        // File operations that keep the .meta files with their assets; "" on success, else the reason.
        static String CreateFolder(StringView parent, StringView name, String& made);
        static String Move(StringView from, StringView to);
        static String Copy(StringView from, StringView to);
        static String Delete(StringView path);
        // The guid of a file's .meta, the .meta written first when it has none (an asset made by the editor).
        static Guid MetaFor(StringView path);
        // A file the editor wrote itself (a native asset saved): its record takes its new size and time, no import.
        static void NoteWritten(StringView path);
        // A path like the one given that no file or folder has (" 1", " 2" ... before the extension).
        static String UniquePath(StringView path);
        // The .meta an import reads in place of the file's (an AssetPostprocessor's changes to that import), until
        // cleared.
        static void SetMetaOverride(StringView path, StringView text);
        static void ClearMetaOverride(StringView path);
        // Whether the engine's asset source is the project's.
        static bool IsProjectSource(const engine::AssetSource* source);
        // A path of the project as a file system path.
        static String FullPath(StringView path);
        // Whether a path is one the database keeps (Assets/..., Packages/<package>/...).
        static bool IsProjectPath(StringView path);
    };
}
