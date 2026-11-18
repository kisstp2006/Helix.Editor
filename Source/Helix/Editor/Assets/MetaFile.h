#pragma once

#include <Helix/Core/Containers/Array.h>
#include <Helix/Core/Strings/String.h>
#include <Helix/Core/Types/Guid.h>

// The .meta file next to every asset and folder of a project (the reference's layout):
//   fileFormatVersion: 2
//   guid: <32 hex digits>
//   labels:                           (when it has some)
//   - <label>
//   folderAsset: yes                  (folders)
//   <Importer>:                       (the importer's section)
//     serializedVersion: ...          (its settings, two spaces in)
//     userData:
//     assetBundleName:
//     assetBundleVariant:
// The section's lines are kept as they are (an importer reads and writes its own keys); unknown top lines are kept.
namespace helix::editor
{
    struct MetaFile
    {
        Guid guid;
        bool folder = false;
        Array<String> labels;
        // The importer's name (its section's key) and the section's lines without their indentation.
        String importer;
        Array<String> settings;

        // False (with the reason) when the text has no guid.
        static bool Parse(StringView text, MetaFile& out, String& error);
        String Write() const;

        // A key of the importer's section, a path for nested ones ("mipmaps.enableMipMap"); "" when it has none. Set
        // adds a missing one (its parents too) before the tail keys, or after its parent's children.
        String Value(StringView key) const;
        void SetValue(StringView key, StringView value);
    };
}
