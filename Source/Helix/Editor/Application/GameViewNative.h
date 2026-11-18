#pragma once

#include <Helix/Core/Basics/Tags.h>
#include <Helix/Engine/Texture/RenderTexture.h>
#include <Helix/Scripting/Bindings/Interop.h>

// The Game view's side of the renderer: the game's content of a display drawn into the view's texture (the editor
// keeps its own content apart from the game's), the cameras that drew there, the frame's statistics.
namespace helix::editor
{
    HX_CLASS(Namespace = "HelixEditor", Static, Internal)
    class GameViewNative
    {
    public:
        // The game's cameras, canvases and screen panels of a display into a texture (null: none).
        HX_FUNCTION(Internal) static void SetDisplayTexture(i32 display, engine::RenderTexture* texture);
        // The game's cameras that drew into the display's texture in the last frame.
        HX_FUNCTION(Internal) static i32 CameraCount(i32 display);

        // The last frame's statistics of the display's texture.
        HX_FUNCTION(Internal) static i32 StatsDrawCalls(i32 display);
        HX_FUNCTION(Internal) static i32 StatsSetPassCalls(i32 display);
        HX_FUNCTION(Internal) static i64 StatsTriangles(i32 display);
        HX_FUNCTION(Internal) static i32 StatsCameras(i32 display);
        HX_FUNCTION(Internal) static i32 StatsVisible(i32 display);
        HX_FUNCTION(Internal) static i32 StatsInstancedBatches(i32 display);
        HX_FUNCTION(Internal) static f64 StatsDrawListTime(i32 display);
        HX_FUNCTION(Internal) static f64 StatsSubmitTime(i32 display);
    };
}
