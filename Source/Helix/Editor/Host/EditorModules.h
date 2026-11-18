#pragma once

#include <Helix/Core/Basics/Span.h>
#include <Helix/Core/Strings/StringView.h>

// The engine's modules linked into the host: their names and the functions publishing their C# bindings. The table is
// written by the build (CMakeLists.txt, from its list of the host's modules).
namespace helix::editor
{
    struct LinkedModule
    {
        const char* name;
        void (*publish)();
    };

    Span<const LinkedModule> LinkedModules();
}
