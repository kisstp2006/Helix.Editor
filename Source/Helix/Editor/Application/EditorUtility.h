#pragma once

#include <Helix/Core/Basics/Tags.h>
#include <Helix/Core/Containers/Array.h>
#include <Helix/Engine/Object/Object.h>
#include <Helix/Scripting/Bindings/Interop.h>

// The editor's object utilities: dirty objects (changed, to be saved: SetDirty marks one, each mark counted; saving
// clears it).
namespace helix::editor
{
    HX_CLASS(Namespace = "HelixEditor", Static)
    class EditorUtility
    {
    public:
        HX_FUNCTION() static void SetDirty(engine::Object* target);
        HX_FUNCTION() static bool IsDirty(engine::Object* target);
        HX_FUNCTION() static void ClearDirty(engine::Object* target);
        HX_FUNCTION() static i32 GetDirtyCount(engine::Object* target);

        // The dirty objects that still exist.
        static void DirtyObjects(Array<engine::Object*>& out);
    };
}
