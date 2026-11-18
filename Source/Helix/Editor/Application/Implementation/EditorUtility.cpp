#include <Helix/Editor/Application/EditorUtility.h>

#include <Helix/Core/Containers/HashMap.h>

namespace helix::editor
{
    namespace
    {
        struct Dirt
        {
            HashMap<engine::EntityId, i32> counts;
            HashMap<engine::EntityId, bool> dirty;
        };

        Dirt& D()
        {
            static Dirt* d = new Dirt();
            return *d;
        }
    }

    void EditorUtility::SetDirty(engine::Object* target)
    {
        if (!target)
            return;
        engine::EntityId id = target->GetEntityId();
        D().counts[id]++;
        D().dirty.Set(id, true);
    }

    bool EditorUtility::IsDirty(engine::Object* target) { return target && D().dirty.Find(target->GetEntityId()) != nullptr; }

    void EditorUtility::ClearDirty(engine::Object* target)
    {
        if (target)
            D().dirty.Remove(target->GetEntityId());
    }

    i32 EditorUtility::GetDirtyCount(engine::Object* target)
    {
        const i32* n = target ? D().counts.Find(target->GetEntityId()) : nullptr;
        return n ? *n : 0;
    }

    void EditorUtility::DirtyObjects(Array<engine::Object*>& out)
    {
        Array<engine::EntityId> gone;
        for (auto e : D().dirty)
        {
            if (engine::Object* o = engine::EntityIdToObject(e.key))
                out.Push(o);
            else
                gone.Push(e.key);
        }
        for (engine::EntityId id : gone)
            D().dirty.Remove(id);
    }
}
