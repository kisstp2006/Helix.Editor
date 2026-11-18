#pragma once

#include <Helix/Core/Basics/Tags.h>
#include <Helix/Core/Containers/Array.h>
#include <Helix/Core/Strings/String.h>
#include <Helix/Scripting/Bindings/Interop.h>

// The AssetModificationProcessor messages of AssetDatabase's operations (C#'s processors): an asset about to be made,
// deleted, moved; the native assets about to be saved (the processors pick which). The operations ask through one C#
// callback; without one (no processors) they go on.
namespace helix::editor
{
    // What a processor answered: go on (the operation does it), it failed (the operation stops, failing), it was done
    // (by the processor; the operation stops, succeeding).
    enum class ModificationResult : i32
    {
        Proceed = 0,
        Failed = 1,
        Done = 2
    };

    HX_CLASS(Namespace = "HelixEditor", Static, Internal)
    class AssetModificationNative
    {
    public:
        // The request's paths (0 the first, 1 the second), the paths to save and the processors' choice of them
        // (separated by new lines).
        HX_FUNCTION() static String RequestPath(u64 context, i32 which);
        HX_FUNCTION() static Array<String> RequestPaths(u64 context);
        HX_FUNCTION() static void SetRequestPaths(u64 context, StringView paths);

        static void WillCreate(StringView path);
        static ModificationResult WillDelete(StringView path);
        static ModificationResult WillMove(StringView from, StringView to);
        // The paths left to save.
        static void WillSave(Array<String>& paths);
    };
}
