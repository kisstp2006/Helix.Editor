#include <Helix/Editor/Scenes/EditorSceneManager.h>

#include <Helix/Core/Logging/Log.h>
#include <Helix/Core/Strings/Format.h>
#include <Helix/Editor/Application/EditorUtility.h>
#include <Helix/Editor/Assets/ProjectDatabase.h>
#include <Helix/Engine/Application/Application.h>
#include <Helix/Engine/GameObject/GameObject.h>
#include <Helix/Engine/Rendering/Camera.h>
#include <Helix/Engine/Rendering/Light.h>
#include <Helix/Engine/Scene/SceneFile.h>
#include <Helix/Engine/Scene/SceneManager.h>
#include <Helix/Engine/Transform/Transform.h>
#include <Helix/Platform/IO/FileSystem.h>

namespace helix::editor
{
    namespace
    {
        LogChannel SceneLog{"Scenes"};

        struct Events
        {
            engine::Event<engine::Scene, OpenSceneMode> opened;
            engine::Event<engine::Scene, NewSceneSetup, NewSceneMode> created;
            engine::Event<engine::Scene> saved, dirtied, closed;
            engine::Event<engine::Scene, bool> closing;
        };

        Events& E()
        {
            static Events* e = new Events();
            return *e;
        }

        constexpr StringView kSetupFile = "Library/LastSceneManagerSetup.txt";

        Array<engine::Scene> OpenScenes()
        {
            Array<engine::Scene> out;
            for (u32 i = 0; i < engine::SceneManager::sceneCount(); i++)
                out.Push(engine::SceneManager::GetSceneAt(i));
            return out;
        }

        // The open scenes with paths, the active one marked: a line each, "<path>" or "<path> *".
        void WriteSetup()
        {
            if (!ProjectDatabase::IsOpen())
                return;
            engine::Scene active = engine::SceneManager::GetActiveScene();
            String text;
            for (const engine::Scene& s : OpenScenes())
                if (!s.path().Empty())
                    text.Append(Format("{}{}\n", s.path(), s == active ? " *" : ""));
            String file = ProjectDatabase::FullPath(kSetupFile);
            platform::CreateDirectories(platform::GetDirectoryName(file.View()));
            platform::WriteFile(file.View(), Span<const u8>(reinterpret_cast<const u8*>(text.Data()), text.Size()));
        }

        // "Untitled", else "Untitled 1" ... (the loaded scenes' names differ).
        String FreeName()
        {
            String name("Untitled");
            for (i32 n = 1; engine::SceneManager::GetSceneByName(name.View()).IsValid(); n++)
                name = Format("Untitled {}", n);
            return name;
        }

        void AddDefaultObjects()
        {
            engine::GameObject* camera = engine::GameObject::Create("Main Camera");
            camera->AddComponent<engine::Camera>();
            camera->SetTag("MainCamera");
            camera->transform()->SetLocalPosition(Vector3(0, 1, -10));
            engine::GameObject* light = engine::GameObject::Create("Directional Light");
            engine::Light* l = light->AddComponent<engine::Light>();
            l->SetType(engine::LightType::Directional);
            light->transform()->SetLocalPosition(Vector3(0, 3, 0));
            light->transform()->SetLocalEulerAngles(Vector3(50, -30, 0));
        }

        bool IsScenePath(StringView path) { return path.EndsWith(Format(".{}", engine::kSceneFileExtension).View()); }
    }

    engine::Scene EditorSceneManager::NewScene(NewSceneSetup setup, NewSceneMode mode)
    {
        Array<engine::Scene> before = mode == NewSceneMode::Single ? OpenScenes() : Array<engine::Scene>();
        engine::Scene scene = engine::SceneManager::CreateScene(FreeName().View());
        if (!scene.IsValid())
            return scene;
        // Its objects go into it as the active scene; a single new scene replaces the open ones.
        engine::Scene previous = engine::SceneManager::GetActiveScene();
        engine::SceneManager::SetActiveScene(scene);
        if (setup == NewSceneSetup::DefaultGameObjects)
            AddDefaultObjects();
        for (const engine::Scene& s : before)
            engine::SceneManager::UnloadSceneAsync(s);
        if (mode == NewSceneMode::Additive && previous.IsValid())
            engine::SceneManager::SetActiveScene(previous);
        engine::SceneManager::ProcessSceneLoads();
        WriteSetup();
        E().created.Invoke(scene, setup, mode);
        return scene;
    }

    engine::Scene EditorSceneManager::OpenScene(StringView scenePath, OpenSceneMode mode)
    {
        const AssetRecord* r = ProjectDatabase::ByPath(scenePath);
        if (!r || r->folder || !IsScenePath(r->path.View()))
        {
            HX_LOG_ERROR(SceneLog, "OpenScene: '{}' is no scene of the project.", scenePath);
            return engine::Scene();
        }
        String path = r->path;
        // Open already: made active (opened alone, the others go).
        engine::Scene open = engine::SceneManager::GetSceneByPath(path.View());
        if (open.IsValid() && mode != OpenSceneMode::Single)
            return open;
        engine::SceneManager::LoadScene(path.View(), engine::LoadSceneParameters{mode == OpenSceneMode::Single ? engine::LoadSceneMode::Single
                                                                                                             : engine::LoadSceneMode::Additive});
        engine::SceneManager::ProcessSceneLoads();
        engine::Scene scene = engine::SceneManager::GetSceneByPath(path.View());
        if (!scene.IsValid() || !scene.isLoaded())
        {
            HX_LOG_ERROR(SceneLog, "OpenScene: '{}' could not be loaded.", path);
            return engine::Scene();
        }
        if (mode == OpenSceneMode::Single)
            engine::SceneManager::SetActiveScene(scene);
        engine::SceneManager::SetSceneDirty(scene, false);
        WriteSetup();
        E().opened.Invoke(scene, mode);
        return scene;
    }

    bool EditorSceneManager::SaveScene(engine::Scene scene, StringView dstScenePath, bool saveAsCopy)
    {
        if (!scene.IsValid() || !scene.isLoaded())
            return false;
        String path(dstScenePath.Empty() ? scene.path() : dstScenePath);
        // An untitled scene: where to, asked outside batch mode.
        if (path.Empty() && !engine::Application::isBatchMode())
            path = EditorUtility::SaveFilePanelInProject("Save Scene", scene.name(), engine::kSceneFileExtension, "Save the scene in the project.", "Assets");
        if (path.Empty())
            return false;
        if (!ProjectDatabase::IsProjectPath(path.View()) || !IsScenePath(path.View()))
        {
            HX_LOG_ERROR(SceneLog, "SaveScene: '{}' is no scene path of the project.", path);
            return false;
        }
        String text, error;
        if (!engine::WriteSceneYaml(scene, text, error))
        {
            HX_LOG_ERROR(SceneLog, "SaveScene: '{}' could not be written: {}", path, error);
            return false;
        }
        String full = ProjectDatabase::FullPath(path.View());
        platform::CreateDirectories(platform::GetDirectoryName(full.View()));
        if (!platform::WriteFile(full.View(), Span<const u8>(reinterpret_cast<const u8*>(text.Data()), text.Size())))
        {
            HX_LOG_ERROR(SceneLog, "SaveScene: '{}' could not be written.", full);
            return false;
        }
        if (!saveAsCopy)
        {
            if (path.View() != scene.path())
                engine::SceneManager::SetScenePath(scene, path.View());
            engine::SceneManager::SetSceneDirty(scene, false);
        }
        ProjectDatabase::Refresh();
        WriteSetup();
        if (!saveAsCopy)
            E().saved.Invoke(scene);
        return true;
    }

    bool EditorSceneManager::SaveOpenScenes()
    {
        bool all = true;
        for (const engine::Scene& s : OpenScenes())
            if (s.isDirty())
                all = SaveScene(s, StringView(), false) && all;
        return all;
    }

    bool EditorSceneManager::CloseScene(engine::Scene scene, bool removeScene)
    {
        if (!scene.IsValid() || engine::SceneManager::sceneCount() <= 1)
            return false;
        E().closing.Invoke(scene, removeScene);
        if (engine::SceneManager::GetActiveScene() == scene)
            for (const engine::Scene& s : OpenScenes())
                if (!(s == scene))
                {
                    engine::SceneManager::SetActiveScene(s);
                    break;
                }
        engine::SceneManager::UnloadSceneAsync(scene);
        engine::SceneManager::ProcessSceneLoads();
        WriteSetup();
        E().closed.Invoke(scene);
        return true;
    }

    bool EditorSceneManager::MarkSceneDirty(engine::Scene scene)
    {
        if (!scene.IsValid() || !scene.isLoaded())
            return false;
        if (!scene.isDirty())
        {
            engine::SceneManager::SetSceneDirty(scene, true);
            E().dirtied.Invoke(scene);
        }
        return true;
    }

    void EditorSceneManager::MarkAllScenesDirty()
    {
        for (const engine::Scene& s : OpenScenes())
            MarkSceneDirty(s);
    }

    engine::Scene EditorSceneManager::GetActiveScene() { return engine::SceneManager::GetActiveScene(); }

    bool EditorSceneManager::SetActiveScene(engine::Scene scene)
    {
        bool ok = engine::SceneManager::SetActiveScene(scene);
        if (ok)
            WriteSetup();
        return ok;
    }

    void EditorSceneManager::RestoreSetupInternal()
    {
        Array<u8> bytes;
        Array<String> paths;
        String active;
        if (ProjectDatabase::IsOpen() && platform::ReadFile(ProjectDatabase::FullPath(kSetupFile).View(), bytes))
        {
            StringView text(reinterpret_cast<const char*>(bytes.Data()), bytes.Size());
            usize at = 0;
            while (at < text.Size())
            {
                usize end = text.Find('\n', at);
                if (end == StringView::npos)
                    end = text.Size();
                StringView line = text.Substr(at, end - at).Trimmed();
                at = end + 1;
                bool isActive = line.EndsWith(" *");
                if (isActive)
                    line = line.Substr(0, line.Size() - 2);
                if (line.Empty() || !ProjectDatabase::ByPath(line))
                    continue;
                paths.Push(String(line));
                if (isActive)
                    active = String(line);
            }
        }
        engine::Scene first;
        for (const String& p : paths)
        {
            engine::Scene s = OpenScene(p.View(), first.IsValid() ? OpenSceneMode::Additive : OpenSceneMode::Single);
            if (!first.IsValid())
                first = s;
        }
        if (!first.IsValid())
        {
            NewScene(NewSceneSetup::DefaultGameObjects, NewSceneMode::Single);
            return;
        }
        if (!active.Empty())
            SetActiveScene(engine::SceneManager::GetSceneByPath(active.View()));
    }

    engine::Event<engine::Scene, OpenSceneMode>& EditorSceneManager::sceneOpened() { return E().opened; }
    engine::Event<engine::Scene, NewSceneSetup, NewSceneMode>& EditorSceneManager::newSceneCreated() { return E().created; }
    engine::Event<engine::Scene>& EditorSceneManager::sceneSaved() { return E().saved; }
    engine::Event<engine::Scene>& EditorSceneManager::sceneDirtied() { return E().dirtied; }
    engine::Event<engine::Scene, bool>& EditorSceneManager::sceneClosing() { return E().closing; }
    engine::Event<engine::Scene>& EditorSceneManager::sceneClosed() { return E().closed; }
}
