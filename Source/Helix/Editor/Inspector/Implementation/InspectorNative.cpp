#include <Helix/Editor/Inspector/InspectorNative.h>

#include <Helix/Core/Strings/Format.h>
#include <Helix/Engine/Object/Class.h>
#include <Helix/Engine/Script/GameBehaviour.h>
#include <Helix/Engine/Script/ScriptClass.h>
#include <Helix/Engine/Script/ScriptRuntime.h>
#include <Helix/Engine/Transform/Transform.h>
#include <Helix/Scripting/Types/Type.h>

#include <algorithm>

namespace helix::editor
{
    namespace
    {
        constexpr StringView kScripts = "Scripts/";

        bool Abstract(scripting::TypeId type)
        {
            const scripting::TypeInfo* t = type.IsValid() ? scripting::GetType(type) : nullptr;
            return t && (t->flags & scripting::TypeAbstract);
        }

        // A C# script made as a GameBehaviour (not a class of its own): the script registry holds it, not the classes.
        const engine::ScriptClass* ManagedScript(StringView name)
        {
            for (u32 i = 0; i < engine::ScriptClassCount(); i++)
                if (const engine::ScriptClass* s = engine::GetScriptClass(i); s && s->alive && !s->OwnClass() && s->name.View() == name)
                    return s;
            return nullptr;
        }
    }

    Array<String> InspectorNative::ComponentClasses()
    {
        Array<String> out;
        u32 component = engine::ClassOf<engine::Component>(), transform = engine::ClassOf<engine::Transform>(),
            behaviour = engine::ClassOf<engine::GameBehaviour>();
        // The engine's classes and the scripts of classes of their own (C++ scripts, C# below them).
        for (u32 i = 0; i < engine::ClassCount(); i++)
        {
            const engine::ClassInfo& c = engine::GetClass(i);
            if (!engine::IsDerivedFrom(i, component) || engine::IsDerivedFrom(i, transform) || i == behaviour || (c.flags & engine::ClassAbstract) ||
                (!c.create && !c.createScript) || c.name.Empty() || Abstract(c.type))
                continue;
            out.Push((c.flags & engine::ClassScript) ? Format("{}{}", kScripts, c.name) : String(c.name));
        }
        // C# scripts made as GameBehaviours.
        for (u32 i = 0; i < engine::ScriptClassCount(); i++)
        {
            const engine::ScriptClass* s = engine::GetScriptClass(i);
            if (s && s->alive && !s->OwnClass() && s->engineClass == behaviour && !Abstract(s->type) && !s->name.Empty())
                out.Push(Format("{}{}", kScripts, s->name));
        }
        std::sort(out.begin(), out.end(), [](const String& a, const String& b) { return a.View() < b.View(); });
        // A name once (a class registered again as its module came back).
        Array<String> unique;
        for (const String& s : out)
            if (unique.Empty() || unique.Back().View() != s.View())
                unique.Push(s);
        return unique;
    }

    engine::Component* InspectorNative::AddComponent(engine::GameObject* gameObject, StringView className)
    {
        if (!gameObject)
            return nullptr;
        bool script = className.StartsWith(kScripts);
        StringView name = script ? className.Substr(kScripts.Size()) : className;
        if (script)
            if (const engine::ScriptClass* s = ManagedScript(name))
                return engine::AddScript(gameObject, *s);
        u32 klass = engine::FindClassByName(name);
        return klass == engine::kNoClass ? nullptr : gameObject->AddComponent(klass);
    }

    bool InspectorNative::MoveComponent(engine::Component* component, i32 index)
    {
        engine::GameObject* g = component ? component->gameObject() : nullptr;
        return g && index > 0 && g->MoveComponent(component, u32(index));
    }
}
