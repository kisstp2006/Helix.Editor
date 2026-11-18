#pragma once

#include <Helix/Core/Basics/Span.h>
#include <Helix/Core/Containers/Array.h>
#include <Helix/Core/Strings/String.h>

// The importers a project's files go through, by extension: each a name (its .meta section), a version (a newer one
// imports its assets again), the extensions it takes, the settings a new .meta gets and the type of the main object
// it makes. The descriptions are data (the editor's Data/Settings/Importers.json); what an importer does is registered
// by its name (C++ importers, C#'s ScriptedImporter). Folders and files of no importer take the default importer.
namespace helix::editor
{
    struct ImporterDescription
    {
        String name;
        // The .meta section's key (the name when empty; every scripted importer's is ScriptedImporter).
        String section;
        i32 version = 1;
        Array<String> extensions;
        // The importer's section of a new .meta (lines without their indentation; the tail keys come after).
        Array<String> settings;
        // The main object's type (FindAssets' t: filter); empty for none.
        String mainType;
    };

    class AssetImporters
    {
    public:
        // Descriptions from JSON text ({"importers": [{"name", "version", "extensions", "settings", "mainType"}],
        // "default": name, "folder": name}); false (with the reason) when the text is not that.
        static bool Load(StringView json, String& error);
        static void Add(const ImporterDescription& description);
        static void Clear();

        // The importer of an extension (lower case, no dot), else the default one; the folders' importer.
        static const ImporterDescription& ForExtension(StringView extension);
        static const ImporterDescription& ForFolder();
        static const ImporterDescription* Find(StringView name);
        static Span<const ImporterDescription> All();
    };
}
