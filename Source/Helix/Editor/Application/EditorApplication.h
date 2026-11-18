#pragma once

#include <Helix/Core/Basics/Tags.h>
#include <Helix/Core/Strings/String.h>
#include <Helix/Engine/Event/Event.h>
#include <Helix/Math/Geometry.h>
#include <Helix/Scripting/Bindings/Interop.h>

namespace helix::platform
{
    class Window;
}

// The editor's application: its update each frame, the play mode's flags (the play mode itself comes with step 8),
// the project, the main window's frame (minimize, maximize, close, its title) and the time since the editor started.
namespace helix::editor
{
    HX_CLASS(Namespace = "HelixEditor", Static)
    class EditorApplication
    {
    public:
        HX_PROPERTY() static bool isPlaying();
        static void SetIsPlaying(bool value);
        HX_PROPERTY() static bool isPaused();
        static void SetIsPaused(bool value);
        HX_PROPERTY(ReadOnly) static bool isPlayingOrWillChangePlaymode();
        HX_PROPERTY(ReadOnly) static f64 timeSinceStartup();
        HX_PROPERTY(ReadOnly) static String applicationPath();
        HX_PROPERTY(ReadOnly) static String applicationContentsPath();
        HX_FUNCTION() static void EnterPlaymode() { SetIsPlaying(true); }
        HX_FUNCTION() static void ExitPlaymode() { SetIsPlaying(false); }
        HX_FUNCTION() static void Step();
        HX_FUNCTION() static void Exit(i32 returnValue);
        HX_FUNCTION() static bool ExecuteMenuItem(StringView menuItemPath);
        // Once a frame of the editor (C#'s update and delayCall hang on it).
        HX_EVENT() static engine::Event<>& update();
        HX_EVENT(Internal) static engine::Event<>& playModeChangedNative();
        HX_EVENT(Internal) static engine::Event<>& pauseChangedNative();

        // The project: its folder and its name (the folder's).
        HX_PROPERTY(ReadOnly, Internal) static String projectPathInternal();
        HX_PROPERTY(ReadOnly, Internal) static String projectNameInternal();
        // The editor's data folder (its shell, skin, layouts).
        HX_PROPERTY(ReadOnly, Internal) static String editorDataPathInternal();
        static void SetDataPath(StringView path);
        // -executeMethod's method (empty without one); the host has the process's arguments on every platform.
        HX_PROPERTY(ReadOnly, Internal) static String executeMethodInternal();
        static void SetExecuteMethod(StringView method);

        // The main window's frame (the shell's caption buttons and title).
        HX_FUNCTION(Internal) static void MinimizeWindowInternal();
        HX_FUNCTION(Internal) static void ToggleMaximizeWindowInternal();
        HX_FUNCTION(Internal) static bool IsWindowMaximizedInternal();
        HX_FUNCTION(Internal) static void CloseWindowInternal();
        HX_FUNCTION(Internal) static void SetWindowTitleInternal(StringView title);

        // Windows of the editor's own beside the main one (floating windows): each a display (Displays) of the
        // player's; its client area's rectangle on the desktop. -1 where the host has none (batch mode).
        HX_PROPERTY(ReadOnly, Internal) static bool toolWindowsInternal();
        HX_FUNCTION(Internal) static i32 OpenToolWindowInternal(StringView title, Rect rect);
        // A window of no frame (a popup menu over the others).
        HX_FUNCTION(Internal) static i32 OpenPopupWindowInternal(Rect rect);
        HX_FUNCTION(Internal) static bool ToolWindowFocusedInternal(i32 display);
        HX_FUNCTION(Internal) static void CloseToolWindowInternal(i32 display);
        HX_FUNCTION(Internal) static bool ToolWindowCloseRequestedInternal(i32 display);
        HX_FUNCTION(Internal) static Rect DisplayRectInternal(i32 display);
        HX_FUNCTION(Internal) static void SetDisplayRectInternal(i32 display, Rect rect);

        // The host's tool windows.
        struct ToolWindowHost
        {
            i32 (*open)(void* user, StringView title, Rect rect, bool decorated) = nullptr;
            bool (*focused)(void* user, i32 display) = nullptr;
            void (*close)(void* user, i32 display) = nullptr;
            bool (*closeRequested)(void* user, i32 display) = nullptr;
            void* user = nullptr;
        };
        static void SetToolWindowHost(const ToolWindowHost& host);

        // The host's: the window, the project, the frame's tick, a quit asked for.
        static void SetHost(platform::Window* window, StringView projectPath, StringView applicationPath);
        static void Tick();
        static bool QuitRequested(i32& code);
    };
}
