#include <Helix/Editor/Application/EditorApplication.h>

#include <Helix/Editor/Shell/EditorMenus.h>
#include <Helix/Engine/Screen/Display.h>
#include <Helix/Platform/IO/FileSystem.h>
#include <Helix/Platform/Window/Window.h>

#include <chrono>

namespace helix::editor
{
    namespace
    {
        struct State
        {
            platform::Window* window = nullptr;
            String projectPath, applicationPath, dataPath, executeMethod;
            EditorApplication::ToolWindowHost tools;
            bool playing = false, paused = false, stepAsked = false;
            bool quit = false;
            i32 quitCode = 0;
            std::chrono::steady_clock::time_point start = std::chrono::steady_clock::now();
        };

        State& S()
        {
            static State* s = new State();
            return *s;
        }
    }

    bool EditorApplication::isPlaying() { return S().playing; }

    void EditorApplication::SetIsPlaying(bool value)
    {
        if (S().playing == value)
            return;
        S().playing = value;
        if (!value)
            S().paused = false;
        playModeChangedNative().Invoke();
    }

    bool EditorApplication::isPaused() { return S().paused; }

    void EditorApplication::SetIsPaused(bool value)
    {
        if (S().paused == value)
            return;
        S().paused = value;
        pauseChangedNative().Invoke();
    }

    bool EditorApplication::isPlayingOrWillChangePlaymode() { return S().playing; }

    f64 EditorApplication::timeSinceStartup()
    {
        return std::chrono::duration<f64>(std::chrono::steady_clock::now() - S().start).count();
    }

    String EditorApplication::applicationPath() { return S().applicationPath; }
    String EditorApplication::applicationContentsPath() { return String(platform::GetDirectoryName(S().applicationPath.View())); }

    void EditorApplication::Step()
    {
        // A frame of the game while paused (the play mode runs it with step 8).
        S().stepAsked = true;
    }

    void EditorApplication::Exit(i32 returnValue)
    {
        S().quit = true;
        S().quitCode = returnValue;
    }

    bool EditorApplication::ExecuteMenuItem(StringView menuItemPath) { return EditorMenus::Execute(menuItemPath); }

    engine::Event<>& EditorApplication::update()
    {
        static engine::Event<>* e = new engine::Event<>();
        return *e;
    }

    engine::Event<>& EditorApplication::playModeChangedNative()
    {
        static engine::Event<>* e = new engine::Event<>();
        return *e;
    }

    engine::Event<>& EditorApplication::pauseChangedNative()
    {
        static engine::Event<>* e = new engine::Event<>();
        return *e;
    }

    String EditorApplication::projectPathInternal() { return S().projectPath; }
    String EditorApplication::editorDataPathInternal() { return S().dataPath; }
    void EditorApplication::SetDataPath(StringView path) { S().dataPath = String(path); }
    String EditorApplication::executeMethodInternal() { return S().executeMethod; }
    void EditorApplication::SetExecuteMethod(StringView method) { S().executeMethod = String(method); }

    String EditorApplication::projectNameInternal()
    {
        StringView p = S().projectPath.View();
        while (!p.Empty() && (p[p.Size() - 1] == '/' || p[p.Size() - 1] == '\\'))
            p = p.Substr(0, p.Size() - 1);
        usize slash = 0;
        bool found = false;
        for (usize i = 0; i < p.Size(); i++)
            if (p[i] == '/' || p[i] == '\\')
                slash = i, found = true;
        return String(found ? p.Substr(slash + 1) : p);
    }

    void EditorApplication::MinimizeWindowInternal()
    {
        if (S().window)
            S().window->Minimize();
    }

    void EditorApplication::ToggleMaximizeWindowInternal()
    {
        if (platform::Window* w = S().window)
            w->SetMode(w->GetMode() == platform::WindowMode::Maximized ? platform::WindowMode::Windowed : platform::WindowMode::Maximized);
    }

    bool EditorApplication::IsWindowMaximizedInternal() { return S().window && S().window->GetMode() == platform::WindowMode::Maximized; }

    void EditorApplication::CloseWindowInternal() { Exit(0); }

    void EditorApplication::SetWindowTitleInternal(StringView title)
    {
        if (S().window)
            S().window->SetTitle(title);
    }

    void EditorApplication::SetHost(platform::Window* window, StringView projectPath, StringView applicationPath)
    {
        S().window = window;
        S().projectPath = String(projectPath);
        S().applicationPath = String(applicationPath);
    }

    void EditorApplication::Tick() { update().Invoke(); }

    void EditorApplication::SetToolWindowHost(const ToolWindowHost& host) { S().tools = host; }
    bool EditorApplication::toolWindowsInternal() { return S().tools.open != nullptr; }

    i32 EditorApplication::OpenToolWindowInternal(StringView title, Rect rect)
    {
        const ToolWindowHost& t = S().tools;
        return t.open ? t.open(t.user, title, rect) : -1;
    }

    void EditorApplication::CloseToolWindowInternal(i32 display)
    {
        const ToolWindowHost& t = S().tools;
        if (t.close)
            t.close(t.user, display);
    }

    bool EditorApplication::ToolWindowCloseRequestedInternal(i32 display)
    {
        const ToolWindowHost& t = S().tools;
        return t.closeRequested && t.closeRequested(t.user, display);
    }

    Rect EditorApplication::DisplayRectInternal(i32 display)
    {
        const engine::DisplayState& d = engine::Displays::Get(display);
        return Rect(f32(d.x), f32(d.y), f32(d.systemWidth), f32(d.systemHeight));
    }

    void EditorApplication::SetDisplayRectInternal(i32 display, Rect rect)
    {
        engine::Displays::Place(display, i32(rect.width), i32(rect.height), i32(rect.x), i32(rect.y));
    }

    bool EditorApplication::QuitRequested(i32& code)
    {
        code = S().quitCode;
        return S().quit;
    }
}
