#include <Helix/Editor/Application/EditorGuiUtility.h>

#include <Helix/Engine/Asset/Asset.h>

namespace helix::editor
{
    engine::Object* EditorGuiUtility::Load(StringView path)
    {
        const engine::AssetEntry* entry = engine::FindAssetAtPath(path);
        return entry ? engine::LoadAsset(entry->guid) : nullptr;
    }
}
