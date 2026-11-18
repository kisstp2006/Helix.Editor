#pragma once

#include <Helix/Core/Basics/Tags.h>
#include <Helix/Core/Containers/Array.h>
#include <Helix/Core/Strings/String.h>
#include <Helix/Engine/Event/Event.h>
#include <Helix/Scripting/Bindings/Interop.h>

// The editor's menus: one registry of items by their path ("File/Save Scene", the last part the item's name), ordered
// by priority (a gap of 11 or more from the item above starts a group, shown with a separator, as the reference's
// MenuItem), with a shortcut's text, a check mark and a validation. C++ registers functions here; C#'s MenuItem
// attributes come through the managed hooks. The menu bar's titles are registered with their own order. Plugins add
// and remove items at run time; whatever draws menus reads the registry.
namespace helix::editor
{
    struct MenuItemFunctions
    {
        void (*execute)(void* user) = nullptr;
        // False greys the item out; null: always enabled.
        bool (*validate)(void* user) = nullptr;
        void* user = nullptr;
    };

    // An entry of a menu: an item, a submenu or a separator.
    struct MenuEntryData
    {
        String name;
        String path;
        String shortcut;
        bool submenu = false;
        bool separator = false;
        bool enabled = true;
        bool checkedState = false;
    };

    HX_CLASS(Namespace = "HelixEditor", Static)
    class EditorMenus
    {
    public:
        // A title of the menu bar and its order (File 0, Edit 10, ... as the reference's).
        HX_FUNCTION() static void AddTitle(StringView name, i32 order);
        static void Add(StringView path, i32 priority, StringView shortcut, const MenuItemFunctions& functions);
        HX_FUNCTION() static bool Remove(StringView path);
        // An item's run (false when there is none, or its validation fails).
        HX_FUNCTION() static bool Execute(StringView path);
        HX_FUNCTION() static bool Validate(StringView path);
        HX_FUNCTION() static bool Exists(StringView path);
        HX_FUNCTION() static void SetChecked(StringView path, bool value);
        HX_FUNCTION() static bool GetChecked(StringView path);
        // The bar's titles in order.
        HX_FUNCTION(Internal) static Array<String> TitlesInternal();
        // The entries under a path (a title or a submenu), in order, with separators between groups.
        static Array<MenuEntryData> Entries(StringView path);
        // The same across the bindings, each entry one text of fields split by the unit separator (0x1f): its kind
        // (i an item, m a submenu, s a separator), enabled and checked (0 or 1), the name, the path, the shortcut.
        HX_FUNCTION(Internal) static Array<String> EntriesInternal(StringView path);
        // A change of the items or titles (the menu bar builds itself again).
        HX_FUNCTION(Internal) static u32 VersionInternal();
        // Every item and title gone (tests).
        HX_FUNCTION(Internal) static void ClearInternal();

        // C#'s items: registered here, run and validated by C# through the hooks.
        HX_FUNCTION(Internal) static void AddManagedInternal(StringView path, i32 priority, StringView shortcut, bool validates);
        HX_EVENT(Internal) static engine::Event<String>& managedExecute();
        HX_EVENT(Internal) static engine::Event<String>& managedValidate();
        HX_FUNCTION(Internal) static void SetValidatedInternal(bool value);
    };
}
