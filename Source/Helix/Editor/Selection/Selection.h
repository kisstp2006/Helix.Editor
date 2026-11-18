#pragma once

#include <Helix/Core/Basics/Tags.h>
#include <Helix/Core/Containers/Array.h>
#include <Helix/Core/Strings/String.h>
#include <Helix/Engine/Event/Event.h>
#include <Helix/Engine/GameObject/GameObject.h>
#include <Helix/Engine/Object/Object.h>
#include <Helix/Engine/Transform/Transform.h>
#include <Helix/Scripting/Bindings/Interop.h>

// The editor's selection (the reference's Selection): objects of scenes and assets by entity id, in the order they were
// selected, the active one among them and its context; one registry for C++ and C# (windows of both read and change it,
// and hear of its changes). Objects destroyed since drop out as it is read.
namespace helix::editor
{
    HX_ENUM(Namespace = "HelixEditor", Attributes = "[System.Flags]")
    enum class SelectionMode : i32
    {
        Unfiltered = 0,
        TopLevel = 1,
        Deep = 2,
        ExcludePrefab = 4,
        Editable = 8,
        Assets = 16,
        DeepAssets = 32,
        OnlyUserModifiable = 8
    };

    HX_CLASS(Namespace = "HelixEditor", Static)
    class Selection
    {
    public:
        HX_PROPERTY(Setter = "SetActiveObject") static engine::Object* activeObject();
        static void SetActiveObject(engine::Object* value);
        HX_PROPERTY(Setter = "SetActiveGameObject") static engine::GameObject* activeGameObject();
        static void SetActiveGameObject(engine::GameObject* value);
        // The active GameObject's Transform when it is an object of a scene.
        HX_PROPERTY(Setter = "SetActiveTransform") static engine::Transform* activeTransform();
        static void SetActiveTransform(engine::Transform* value);
        HX_PROPERTY(Setter = "SetActiveEntityId") static engine::EntityId activeEntityId();
        static void SetActiveEntityId(engine::EntityId value);
        HX_PROPERTY(ReadOnly) static engine::Object* activeContext();
        HX_PROPERTY(ReadOnly) static i32 count();

        HX_FUNCTION() static bool Contains(engine::Object* obj);
        HX_FUNCTION(Name = "Contains") static bool ContainsId(engine::EntityId entityId);
        HX_FUNCTION() static void SetActiveObjectWithContext(engine::Object* obj, engine::Object* context);

        // The selected objects in the order they were selected, as C#'s properties read them; setting them keeps the
        // active one when it is among them, else the first becomes active.
        HX_FUNCTION(Internal) static Array<engine::Object*> ObjectsInternal();
        HX_FUNCTION(Internal) static void SetObjectsInternal(Span<engine::Object* const> objects);
        HX_FUNCTION(Internal) static Array<engine::EntityId> EntityIdsInternal();
        HX_FUNCTION(Internal) static void SetEntityIdsInternal(Span<const engine::EntityId> ids);
        HX_FUNCTION(Internal) static Array<engine::GameObject*> GameObjectsInternal();
        HX_FUNCTION(Internal) static Array<String> AssetGuidsInternal();
        // The selection filtered by a mode's flags (C# filters by type after), and its Transforms so filtered.
        HX_FUNCTION(Internal) static Array<engine::Object*> FilteredInternal(SelectionMode mode);
        HX_FUNCTION(Internal) static Array<engine::Transform*> TransformsInternal(SelectionMode mode);

        // Raised once a change of the selection is done (C#'s Selection.selectionChanged).
        HX_EVENT(Delegate = "global::HelixEngine.Events.HelixAction")
        static engine::Event<>& changedInternal();

        // For C++: the selection at once, and an empty selection.
        static void Set(Span<engine::Object* const> objects, engine::Object* active);
        static void Clear();
    };
}
