#pragma once

#include <Helix/Core/Basics/Tags.h>
#include <Helix/Core/Containers/Array.h>
#include <Helix/Core/Strings/String.h>
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

        // The system's file dialogs (Helix.Platform's): the chosen path, "" when cancelled, in batch mode or where the
        // system has none. Extensions without dots, several separated by ',' or ';' ("" for any file); a folder of the
        // project ("Assets") starts in the project.
        HX_FUNCTION() static String OpenFilePanel(StringView title, StringView directory, StringView extension);
        // The filters as names and extensions after each other, separated by new lines.
        HX_FUNCTION(Internal) static String OpenFilePanelWithFiltersInternal(StringView title, StringView directory, StringView filters);
        HX_FUNCTION() static String OpenFolderPanel(StringView title, StringView folder, StringView defaultName);
        HX_FUNCTION() static String SaveFilePanel(StringView title, StringView directory, StringView defaultName, StringView extension);
        // A path of the project ("Assets/..."); "" for one outside it.
        HX_FUNCTION(Name = "SaveFilePanelInProject")
        static String SaveFilePanelInProjectApi(StringView title, StringView defaultName, StringView extension, StringView message)
        {
            return SaveFilePanelInProject(title, defaultName, extension, message, "Assets");
        }
        HX_FUNCTION() static String SaveFilePanelInProject(StringView title, StringView defaultName, StringView extension, StringView message,
                                                           StringView path);
    };
}
