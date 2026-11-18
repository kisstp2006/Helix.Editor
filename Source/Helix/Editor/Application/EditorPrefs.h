#pragma once

#include <Helix/Core/Basics/Tags.h>
#include <Helix/Core/Strings/String.h>
#include <Helix/Scripting/Bindings/Interop.h>

// The editor's preferences of the user: keys with an int, a float, a string or a bool, kept in a text file of the
// user's (EditorPrefs.txt in the user's Helix folder: %APPDATA%\Helix on Windows, $XDG_CONFIG_HOME/Helix or
// ~/.config/Helix elsewhere), written when a value changes. Lines of "type<TAB>key<TAB>value", tabs, new lines and
// backslashes escaped.
namespace helix::editor
{
    HX_CLASS(Namespace = "HelixEditor", Static)
    class EditorPrefs
    {
    public:
        HX_FUNCTION() static void SetInt(StringView key, i32 value);
        HX_FUNCTION() static i32 GetInt(StringView key, i32 defaultValue = 0);
        HX_FUNCTION() static void SetFloat(StringView key, f32 value);
        HX_FUNCTION() static f32 GetFloat(StringView key, f32 defaultValue = 0);
        HX_FUNCTION() static void SetString(StringView key, StringView value);
        HX_FUNCTION() static String GetString(StringView key, StringView defaultValue = "");
        HX_FUNCTION() static void SetBool(StringView key, bool value);
        HX_FUNCTION() static bool GetBool(StringView key, bool defaultValue = false);
        HX_FUNCTION() static bool HasKey(StringView key);
        HX_FUNCTION() static void DeleteKey(StringView key);
        HX_FUNCTION() static void DeleteAll();

        // The file (tests point it elsewhere); read again from it.
        static String Path();
        static void SetPath(StringView path);
    };
}
