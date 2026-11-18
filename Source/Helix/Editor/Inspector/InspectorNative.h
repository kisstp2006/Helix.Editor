#pragma once

#include <Helix/Core/Basics/Tags.h>
#include <Helix/Core/Containers/Array.h>
#include <Helix/Core/Strings/String.h>
#include <Helix/Engine/GameObject/GameObject.h>
#include <Helix/Scripting/Bindings/Interop.h>

// What the Inspector window does to components that C# reaches only through the engine's class registry: the classes
// a GameObject may get (C++ and C# alike, by name), adding one by its name, moving one in the list.
namespace helix::editor
{
    HX_CLASS(Namespace = "HelixEditor", Static, Internal)
    class InspectorNative
    {
    public:
        // The component classes that can be added (not abstract, not the Transform), as "name" or "Scripts/name" for
        // script classes, sorted.
        HX_FUNCTION() static Array<String> ComponentClasses();
        // The component of the class added (its required ones first); null when the class is none.
        HX_FUNCTION() static engine::Component* AddComponent(engine::GameObject* gameObject, StringView className);
        HX_FUNCTION() static bool MoveComponent(engine::Component* component, i32 index);
    };
}
