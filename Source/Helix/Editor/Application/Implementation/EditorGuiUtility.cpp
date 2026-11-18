#include <Helix/Editor/Application/EditorGuiUtility.h>

#include <Helix/Engine/GameObject/Tags.h>

#include <Helix/Engine/Asset/Asset.h>

namespace helix::editor
{
    engine::Object* EditorGuiUtility::Load(StringView path)
    {
        const engine::AssetEntry* entry = engine::FindAssetAtPath(path);
        return entry ? engine::LoadAsset(entry->guid) : nullptr;
    }

    Array<String> EditorGuiUtility::TagsInternal()
    {
        Array<String> out;
        for (u32 i = 0; i < engine::TagCount(); i++)
            out.Push(String(engine::TagName(engine::TagHandle{u16(i)})));
        return out;
    }
}
