#pragma once

#include <Helix/Core/Basics/Tags.h>
#include <Helix/Core/Containers/Array.h>
#include <Helix/Core/Strings/String.h>
#include <Helix/Engine/Object/Object.h>
#include <Helix/Scripting/Bindings/Interop.h>

// What SerializedObject and SerializedProperty stand on: an object's serialized fields as its class writes them (its
// binary form with the type tree, read back into one flat table of properties in depth first order: paths, field
// names, type names, depths, values; arrays as "path.Array.size" and "path.Array.data[i]", object references as
// leaves), for one or several objects of one class (each property's value from the first, and whether the others
// differ). Changes wait as text by path (the form of prefab modifications) until applied through each class's
// applyProperty; the table is read again after. Several tables at once, by handle.
namespace helix::editor
{
    HX_CLASS(Namespace = "HelixEditor", Static, Internal)
    class SerializedNative
    {
    public:
        // A table of the objects (0 when they are none or of different classes).
        HX_FUNCTION() static u64 Open(Span<engine::Object* const> targets);
        HX_FUNCTION() static void Close(u64 handle);
        // Read again from the objects (waiting changes dropped).
        HX_FUNCTION() static void Update(u64 handle);
        // The waiting changes written into the objects (true when there were any); the table read again.
        HX_FUNCTION() static bool Apply(u64 handle);
        HX_FUNCTION() static bool HasModified(u64 handle);
        HX_FUNCTION() static i32 TargetCount(u64 handle);
        HX_FUNCTION() static engine::Object* Target(u64 handle, i32 index);

        // The properties: their count and, by index, path, name, type name, kind (ValueKind, 100 for an object
        // reference, 101 for an array's size), depth, the index after the property's subtree, array size.
        HX_FUNCTION() static i32 Count(u64 handle);
        HX_FUNCTION() static i32 Find(u64 handle, StringView path);
        HX_FUNCTION() static String Path(u64 handle, i32 index);
        HX_FUNCTION() static String Name(u64 handle, i32 index);
        HX_FUNCTION() static String TypeName(u64 handle, i32 index);
        HX_FUNCTION() static i32 Kind(u64 handle, i32 index);
        HX_FUNCTION() static i32 Depth(u64 handle, i32 index);
        HX_FUNCTION() static i32 End(u64 handle, i32 index);
        HX_FUNCTION() static i32 ArraySize(u64 handle, i32 index);
        // Whether the objects' values differ (anywhere below the property).
        HX_FUNCTION() static bool Different(u64 handle, i32 index);

        HX_FUNCTION() static i64 LongValue(u64 handle, i32 index);
        HX_FUNCTION() static f64 DoubleValue(u64 handle, i32 index);
        HX_FUNCTION() static String StringValue(u64 handle, i32 index);
        HX_FUNCTION() static engine::Object* ObjectValue(u64 handle, i32 index);
        // An object reference's fileID and guid (32 hex digits, "" for an object of a scene).
        HX_FUNCTION() static i64 ReferenceFileId(u64 handle, i32 index);
        HX_FUNCTION() static String ReferenceGuid(u64 handle, i32 index);

        // A change of a leaf (its text as a scene file writes it); the table shows it at once.
        HX_FUNCTION() static bool SetText(u64 handle, i32 index, StringView text);
        HX_FUNCTION() static bool SetObject(u64 handle, i32 index, engine::Object* value);
        // An array's size: the waiting changes and the size applied at once (the new items are in the table).
        HX_FUNCTION() static bool SetArraySize(u64 handle, i32 index, i32 size);
    };
}
