#include <Helix/Editor/Application/GameViewNative.h>
#include <Helix/Engine/Rendering/Graphics.h>

namespace helix::editor
{
    void GameViewNative::SetDisplayTexture(i32 display, engine::RenderTexture* texture) { engine::Graphics::SetDisplayTexture(display, texture); }
    i32 GameViewNative::CameraCount(i32 display) { return engine::Graphics::DisplayTextureCameraCount(display); }

    i32 GameViewNative::StatsDrawCalls(i32 display) { return i32(engine::Graphics::DisplayTextureStats(display).drawCalls); }
    i32 GameViewNative::StatsSetPassCalls(i32 display) { return i32(engine::Graphics::DisplayTextureStats(display).setPassCalls); }
    i64 GameViewNative::StatsTriangles(i32 display) { return i64(engine::Graphics::DisplayTextureStats(display).triangles); }
    i32 GameViewNative::StatsCameras(i32 display) { return i32(engine::Graphics::DisplayTextureStats(display).cameras); }
    i32 GameViewNative::StatsVisible(i32 display) { return i32(engine::Graphics::DisplayTextureStats(display).visible); }
    i32 GameViewNative::StatsInstancedBatches(i32 display) { return i32(engine::Graphics::DisplayTextureStats(display).instancedBatches); }
    f64 GameViewNative::StatsDrawListTime(i32 display) { return engine::Graphics::DisplayTextureStats(display).drawListTime; }
    f64 GameViewNative::StatsSubmitTime(i32 display) { return engine::Graphics::DisplayTextureStats(display).submitTime; }
}
