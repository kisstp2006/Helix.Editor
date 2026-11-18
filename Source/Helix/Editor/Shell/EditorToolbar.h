#pragma once

#include <Helix/Core/Basics/Tags.h>
#include <Helix/Core/Containers/Array.h>
#include <Helix/Core/Strings/String.h>
#include <Helix/Engine/Event/Event.h>
#include <Helix/Scripting/Bindings/Interop.h>

// The main toolbar's elements: one registry by id ("Editor/Play"), each in a group of the bar (left, middle, right)
// at an order, with its kind (a button, a toggle, a dropdown), icon, text and tooltip. A strip names the elements drawn
// joined (the tools). The places can move: a preset of the bar (group and order of each id) is saved and applied as
// data. C++ elements run functions; C#'s run through the managed hook. The bar is drawn from the registry.
namespace helix::editor
{
    HX_ENUM(Namespace = "HelixEditor")
    enum class ToolbarGroup : i32
    {
        Left = 0,
        Middle = 1,
        Right = 2,
    };

    HX_ENUM(Namespace = "HelixEditor")
    enum class ToolbarElementKind : i32
    {
        Button = 0,
        Toggle = 1,
        Dropdown = 2,
    };

    struct ToolbarElementFunctions
    {
        // A click (a toggle's value flips first); a dropdown opens its menu here.
        void (*click)(void* user) = nullptr;
        void* user = nullptr;
    };

    struct ToolbarElementData
    {
        String id;
        ToolbarGroup group = ToolbarGroup::Left;
        i32 order = 0;
        ToolbarElementKind kind = ToolbarElementKind::Button;
        String strip;
        String icon;
        String text;
        String tooltip;
        bool on = false;
        bool enabled = true;
    };

    HX_CLASS(Namespace = "HelixEditor", Static)
    class EditorToolbar
    {
    public:
        static void Add(const ToolbarElementData& element, const ToolbarElementFunctions& functions);
        HX_FUNCTION(Internal) static void AddManagedInternal(StringView id, ToolbarGroup group, i32 order, ToolbarElementKind kind, StringView strip,
                                                             StringView icon, StringView text, StringView tooltip, bool on, bool enabled);
        HX_FUNCTION() static bool Remove(StringView id);
        HX_FUNCTION() static bool Exists(StringView id);
        HX_FUNCTION() static bool Click(StringView id);
        HX_FUNCTION() static void SetOn(StringView id, bool value);
        HX_FUNCTION() static bool GetOn(StringView id);
        HX_FUNCTION() static void SetEnabled(StringView id, bool value);
        HX_FUNCTION() static void SetText(StringView id, StringView text);
        // An element's place: group and order (the user moves it; presets keep it).
        HX_FUNCTION() static void Move(StringView id, ToolbarGroup group, i32 order);
        // The bar's elements of a group in order.
        static Array<ToolbarElementData> Elements(ToolbarGroup group);
        // The same across the bindings: each element one text of fields split by the unit separator (0x1f): id, group,
        // order, kind, strip, icon, text, tooltip, on and enabled (0 or 1).
        HX_FUNCTION(Internal) static Array<String> ElementsInternal(ToolbarGroup group);
        HX_FUNCTION(Internal) static u32 VersionInternal();
        HX_FUNCTION(Internal) static void ClearInternal();
        // A preset: "id=group,order" lines of every element; applied to those that exist.
        HX_FUNCTION() static String SavePreset();
        HX_FUNCTION() static void ApplyPreset(StringView preset);

        HX_EVENT(Internal) static engine::Event<String>& managedClick();
    };
}
