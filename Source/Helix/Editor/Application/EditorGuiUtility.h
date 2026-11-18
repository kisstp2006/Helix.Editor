#pragma once

#include <Helix/Core/Basics/Tags.h>
#include <Helix/Core/Strings/String.h>
#include <Helix/Engine/Object/Object.h>
#include <Helix/Scripting/Bindings/Interop.h>

// The editor's own resources: assets of the editor's data folder by their path in it (UI/Windows/SceneView.hxml,
// Skin/Icons/Play.png), as the editor's windows load their markup, style sheets and images.
namespace helix::editor
{
    HX_CLASS(Namespace = "HelixEditor", Static)
    class EditorGuiUtility
    {
    public:
        // The asset's main object, null when the data folder has no asset at the path.
        HX_FUNCTION() static engine::Object* Load(StringView path);
    };
}
