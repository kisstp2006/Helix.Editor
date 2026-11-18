#pragma once

#include <Helix/Core/Basics/Span.h>
#include <Helix/Core/Containers/Array.h>
#include <Helix/Core/Strings/String.h>

// The editor's host: the player core on a window with the editor's own frame, the engine's modules, the render
// pipelines, the UI and the editor's module registered, the editor's data folder (its shell, skin and scene) and its C#
// assemblies. Its own arguments:
//   -projectPath <folder>  the project (the current folder by default)
//   -pipeline <name>       the pipeline that renders (standard, physical or none; standard by default)
// Every argument of the player works too (-batchmode, -frames, -screenshot, -force-glcore, -force-device-index ...).
namespace helix::editor
{
    struct EditorOptions
    {
        String projectPath;
        String pipeline = String("standard");
        Array<String> playerArgs = Array<String>();
    };
    bool ParseEditorArgs(Span<const StringView> args, EditorOptions& out, Array<String>& errors);

    // A whole run of the editor: the modules registered, the player started, the shell's frame loop.
    i32 RunEditor(Span<const StringView> args);
}
