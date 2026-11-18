#include <Helix/Editor/Selection/Selection.h>

#include <Helix/Core/Containers/HashMap.h>
#include <Helix/Editor/Assets/ProjectDatabase.h>
#include <Helix/Engine/Asset/Asset.h>
#include <Helix/Engine/GameObject/GameObject.h>
#include <Helix/Engine/Transform/Transform.h>

namespace helix::editor
{
    namespace
    {
        struct State
        {
            Array<engine::EntityId> ids;
            engine::EntityId active, context;
            engine::Event<> changed;
        };

        State& S()
        {
            static State* s = new State();
            return *s;
        }

        // The ids of objects destroyed since go.
        void Prune()
        {
            State& s = S();
            for (usize i = s.ids.Size(); i-- > 0;)
                if (!engine::EntityIdToObject(s.ids[i]))
                    s.ids.RemoveAt(i);
            if (s.active.IsValid() && !engine::EntityIdToObject(s.active))
                s.active = s.ids.Empty() ? engine::EntityId() : s.ids[0];
            if (s.context.IsValid() && !engine::EntityIdToObject(s.context))
                s.context = engine::EntityId();
        }

        // A new selection (the event only when it differs).
        void Change(Array<engine::EntityId> ids, engine::EntityId active, engine::EntityId context)
        {
            State& s = S();
            // Each object once, in order.
            Array<engine::EntityId> unique;
            for (engine::EntityId id : ids)
            {
                bool seen = !id.IsValid() || !engine::EntityIdToObject(id);
                for (engine::EntityId u : unique)
                    seen = seen || u == id;
                if (!seen)
                    unique.Push(id);
            }
            bool found = false;
            for (engine::EntityId u : unique)
                found = found || u == active;
            if (!found)
                active = unique.Empty() ? engine::EntityId() : unique[0];
            bool same = unique.Size() == s.ids.Size() && active == s.active && context == s.context;
            for (usize i = 0; same && i < unique.Size(); i++)
                same = unique[i] == s.ids[i];
            if (same)
                return;
            s.ids = helix::Move(unique);
            s.active = active;
            s.context = context;
            s.changed.Invoke();
        }

        bool IsAsset(const engine::Object* o)
        {
            Guid g;
            i64 f = 0;
            i32 t = 0;
            return o && engine::GetAssetReference(o, g, f, t);
        }

        // An object's GameObject (itself, or a component's).
        engine::GameObject* GameObjectOf(engine::Object* o)
        {
            if (!o)
                return nullptr;
            if (o->Is<engine::GameObject>())
                return static_cast<engine::GameObject*>(o);
            if (o->Is<engine::Component>())
                return static_cast<engine::Component*>(o)->gameObject();
            return nullptr;
        }

        // A GameObject of a scene (not of an asset).
        bool InScene(engine::GameObject* go) { return go && !IsAsset(go) && go->scene().IsValid(); }

        bool Selected(engine::EntityId id)
        {
            for (engine::EntityId s : S().ids)
                if (s == id)
                    return true;
            return false;
        }

        // A selected ancestor (TopLevel leaves the descendants of selected objects out).
        bool AncestorSelected(engine::Transform* t)
        {
            for (engine::Transform* p = t ? t->parent() : nullptr; p; p = p->parent())
                if (Selected(p->gameObject()->GetEntityId()) || Selected(p->GetEntityId()))
                    return true;
            return false;
        }

        void AddDeep(engine::Transform* t, Array<engine::Object*>& out, HashMap<engine::EntityId, u8>& seen)
        {
            for (u32 i = 0; i < t->childCount(); i++)
            {
                engine::Transform* c = t->GetChild(i);
                engine::GameObject* go = c->gameObject();
                if (!seen.Find(go->GetEntityId()))
                {
                    seen.Set(go->GetEntityId(), u8(1));
                    out.Push(go);
                }
                AddDeep(c, out, seen);
            }
        }
    }

    engine::Object* Selection::activeObject()
    {
        Prune();
        return engine::EntityIdToObject(S().active);
    }

    void Selection::SetActiveObject(engine::Object* value)
    {
        Array<engine::EntityId> ids;
        if (value)
            ids.Push(value->GetEntityId());
        Change(helix::Move(ids), value ? value->GetEntityId() : engine::EntityId(), engine::EntityId());
    }

    engine::GameObject* Selection::activeGameObject()
    {
        engine::Object* o = activeObject();
        return o && o->Is<engine::GameObject>() ? static_cast<engine::GameObject*>(o) : nullptr;
    }

    void Selection::SetActiveGameObject(engine::GameObject* value) { SetActiveObject(value); }

    engine::Transform* Selection::activeTransform()
    {
        engine::GameObject* go = activeGameObject();
        return InScene(go) ? go->transform() : nullptr;
    }

    void Selection::SetActiveTransform(engine::Transform* value) { SetActiveObject(value ? value->gameObject() : nullptr); }

    engine::EntityId Selection::activeEntityId()
    {
        Prune();
        return S().active;
    }

    void Selection::SetActiveEntityId(engine::EntityId value) { SetActiveObject(engine::EntityIdToObject(value)); }

    engine::Object* Selection::activeContext()
    {
        Prune();
        return engine::EntityIdToObject(S().context);
    }

    i32 Selection::count()
    {
        Prune();
        return i32(S().ids.Size());
    }

    bool Selection::Contains(engine::Object* obj) { return obj && ContainsId(obj->GetEntityId()); }

    bool Selection::ContainsId(engine::EntityId entityId)
    {
        Prune();
        return entityId.IsValid() && Selected(entityId);
    }

    void Selection::SetActiveObjectWithContext(engine::Object* obj, engine::Object* context)
    {
        Array<engine::EntityId> ids;
        if (obj)
            ids.Push(obj->GetEntityId());
        Change(helix::Move(ids), obj ? obj->GetEntityId() : engine::EntityId(), context ? context->GetEntityId() : engine::EntityId());
    }

    Array<engine::Object*> Selection::ObjectsInternal()
    {
        Prune();
        Array<engine::Object*> out;
        for (engine::EntityId id : S().ids)
            out.Push(engine::EntityIdToObject(id));
        return out;
    }

    void Selection::SetObjectsInternal(Span<engine::Object* const> objects)
    {
        Array<engine::EntityId> ids;
        for (engine::Object* o : objects)
            if (o)
                ids.Push(o->GetEntityId());
        Change(helix::Move(ids), S().active, engine::EntityId());
    }

    Array<engine::EntityId> Selection::EntityIdsInternal()
    {
        Prune();
        return S().ids;
    }

    void Selection::SetEntityIdsInternal(Span<const engine::EntityId> ids)
    {
        Array<engine::EntityId> copy;
        for (engine::EntityId id : ids)
            copy.Push(id);
        Change(helix::Move(copy), S().active, engine::EntityId());
    }

    Array<engine::GameObject*> Selection::GameObjectsInternal()
    {
        Array<engine::GameObject*> out;
        for (engine::Object* o : ObjectsInternal())
            if (o->Is<engine::GameObject>())
                out.Push(static_cast<engine::GameObject*>(o));
        return out;
    }

    Array<String> Selection::AssetGuidsInternal()
    {
        Array<String> out;
        for (engine::Object* o : ObjectsInternal())
        {
            Guid g;
            i64 f = 0;
            i32 t = 0;
            if (engine::GetAssetReference(o, g, f, t) && ProjectDatabase::ByGuid(g))
            {
                String guid = g.ToString();
                bool seen = false;
                for (const String& s : out)
                    seen = seen || s == guid;
                if (!seen)
                    out.Push(guid);
            }
        }
        return out;
    }

    Array<engine::Object*> Selection::FilteredInternal(SelectionMode mode)
    {
        u32 m = u32(mode);
        Array<engine::Object*> out;
        HashMap<engine::EntityId, u8> seen;
        for (engine::Object* o : ObjectsInternal())
        {
            bool asset = IsAsset(o);
            engine::GameObject* go = GameObjectOf(o);
            if ((m & (u32(SelectionMode::Assets) | u32(SelectionMode::DeepAssets))) && !asset)
                continue;
            if ((m & u32(SelectionMode::ExcludePrefab)) && asset && go)
                continue;
            if ((m & u32(SelectionMode::Editable)) && engine::Has(o->hideFlags, engine::HideFlags::NotEditable))
                continue;
            if ((m & u32(SelectionMode::TopLevel)) && InScene(go) && AncestorSelected(go->transform()))
                continue;
            if (!seen.Find(o->GetEntityId()))
            {
                seen.Set(o->GetEntityId(), u8(1));
                out.Push(o);
            }
            if ((m & u32(SelectionMode::Deep)) && go)
                AddDeep(go->transform(), out, seen);
        }
        return out;
    }

    Array<engine::Transform*> Selection::TransformsInternal(SelectionMode mode)
    {
        Array<engine::Transform*> out;
        for (engine::Object* o : FilteredInternal(mode))
        {
            engine::GameObject* go = GameObjectOf(o);
            if (!InScene(go) || !o->Is<engine::GameObject>())
                continue;
            bool seen = false;
            for (engine::Transform* t : out)
                seen = seen || t == go->transform();
            if (!seen)
                out.Push(go->transform());
        }
        return out;
    }

    engine::Event<>& Selection::changedInternal() { return S().changed; }

    void Selection::Set(Span<engine::Object* const> objects, engine::Object* active)
    {
        Array<engine::EntityId> ids;
        for (engine::Object* o : objects)
            if (o)
                ids.Push(o->GetEntityId());
        Change(helix::Move(ids), active ? active->GetEntityId() : engine::EntityId(), engine::EntityId());
    }

    void Selection::Clear() { Change(Array<engine::EntityId>(), engine::EntityId(), engine::EntityId()); }
}
