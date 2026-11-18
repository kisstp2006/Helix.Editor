#include <Helix/Editor/Host/EditorHost.h>

#include <Helix/Editor/Application/EditorApplication.h>
#include <Helix/Editor/Assets/ArtifactStore.h>
#include <Helix/Editor/Assets/AssetDatabase.h>
#include <Helix/Editor/Assets/AssetImporters.h>
#include <Helix/Editor/Assets/ImportNative.h>
#include <Helix/Editor/Assets/ProjectDatabase.h>
#include <Helix/Engine/Application/Application.h>
#include <Helix/Editor/Host/EditorModules.h>

#include <Helix/Core/Logging/Log.h>
#include <Helix/Core/Strings/Format.h>
#include <Helix/Engine/Asset/Asset.h>
#include <Helix/Engine/Rendering/Graphics.h>
#include <Helix/Engine/Rendering/Pipeline/RenderPipeline.h>
#include <Helix/Engine/Script/ScriptRuntime.h>
#include <Helix/Engine/Script/ScriptableObject.h>
#include <Helix/Platform/IO/FileSystem.h>
#include <Helix/Platform/Window/Window.h>
#include <Helix/Player/Host/Player.h>
#include <Helix/Player/Scripts/PlayerScripts.h>
#include <Helix/Scripting/Host/DotnetHost.h>
#include <Helix/Serialization/Json/Parser.h>
#include <Helix/Scripting/Bindings/Interop.h>
#include <Helix/UI/Core/Atoms.h>
#include <Helix/UI/Engine/Panel.h>
#include <Helix/UI/Engine/UIPanelSettings.h>
#include <Helix/UI/Engine/UIView.h>

#include <bit>

HX_BINDINGS_EXPORT void HxBindings_HelixEditor_Publish();

namespace helix::editor
{
    namespace
    {
        LogChannel EditorLog{"Editor"};

        bool Register(StringView name, void (*publish)())
        {
            publish();
            String error;
            if (engine::RegisterLinkedGameModule(name, error))
                return true;
            HX_LOG_ERROR(EditorLog, "The module {} did not register: {}", name, error);
            return false;
        }

        // The pipeline a name stands for: a short name of the data folder's Settings/Pipelines.json (its default when
        // none is given), else an asset class name. Answers false for an unknown name; an empty class is no pipeline.
        bool PipelineClass(StringView dataFolder, StringView name, String& shortName, String& className)
        {
            String path = platform::JoinPath(dataFolder, "Settings/Pipelines.json");
            Array<u8> bytes;
            serialization::json::Tree tree;
            String error;
            if (!platform::ReadFile(path, bytes) ||
                !serialization::json::Parse(StringView(reinterpret_cast<const char*>(bytes.Data()), bytes.Size()), tree, error))
            {
                HX_LOG_ERROR(EditorLog, "The pipelines could not be read from '{}' {}", path, error);
                return false;
            }
            u32 table = tree.Find(tree.root, "pipelines");
            shortName = String(name);
            if (shortName.Empty())
            {
                u32 d = tree.Find(tree.root, "default");
                shortName = tree.IsString(d) ? String(tree.Text(d)) : String();
            }
            u32 entry = tree.IsObject(table) ? tree.Find(table, shortName.View()) : serialization::json::kNoNode;
            if (tree.IsString(entry))
            {
                className = String(tree.Text(entry));
                return true;
            }
            // Not a short name: an asset class by name.
            className = shortName;
            return !className.Empty();
        }

        // The project's asset database: the importers' descriptions of the data folder, the project's files with their
        // .meta files, refreshed as the editor starts and when it comes back to the front.
        // The project's script assemblies (Library/ScriptAssemblies, where the script compilation puts them): loaded
        // before its first refresh, so their importers and postprocessors take part in it.
        void LoadProjectAssemblies(StringView projectPath)
        {
            String folder = platform::JoinPath(projectPath, "Library/ScriptAssemblies");
            Array<String> files;
            platform::ListDirectory(folder.View(), [](const platform::DirectoryEntry& e, void* user) {
                if (!e.isDirectory && platform::GetExtension(e.name) == ".dll")
                    static_cast<Array<String>*>(user)->Push(String(e.name));
            }, &files);
            if (files.Empty() || !scripting::DotnetHost::IsRunning())
                return;
            String error;
            String core = platform::JoinPath(platform::JoinPath(platform::GetDirectoryName(platform::ExecutablePath()), "Managed"), player::kCoreModuleAssembly);
            scripting::AssemblyHandle assembly = scripting::DotnetHost::LoadAssembly(core.View(), error);
            auto load = assembly.IsValid()
                            ? std::bit_cast<i32 (*)(const char*, i32)>(scripting::DotnetHost::GetFunction(assembly, "HelixEngine.Internal.ScriptBridge", "LoadScripts", error))
                            : nullptr;
            if (!load)
            {
                HX_LOG_ERROR(EditorLog, "The project's script assemblies could not be loaded: {}", error);
                return;
            }
            for (const String& f : files)
            {
                String path = platform::JoinPath(folder, f);
                i32 classes = load(path.Data(), i32(path.Size()));
                if (classes < 0)
                    HX_LOG_ERROR(EditorLog, "The project's assembly {} did not load.", f);
                else
                    HX_LOG_INFO(EditorLog, "The project's assembly {} loaded: {} script classes.", f, classes);
            }
        }

        void OpenProject(StringView dataFolder, StringView projectPath)
        {
            // Application.dataPath: the project's Assets folder (as the reference's editor has it).
            engine::Application::SetDataPath(platform::JoinPath(projectPath, "Assets").View());
            LoadProjectAssemblies(projectPath);
            Array<u8> bytes;
            String error;
            String importers = platform::JoinPath(dataFolder, "Settings/Importers.json");
            if (!platform::ReadFile(importers, bytes) ||
                !AssetImporters::Load(StringView(reinterpret_cast<const char*>(bytes.Data()), bytes.Size()), error))
                HX_LOG_ERROR(EditorLog, "The importers could not be read from '{}' {}", importers, error);
            ProjectDatabase::Open(projectPath);
            // The project's imports run its asset postprocessors.
            ImportNative::WrapLoaders();
            ArtifactStore::SetImports(true);
            AssetChanges changes = ProjectDatabase::Refresh();
            // What the last session left unimported (or whose artifacts went) is imported now.
            for (const AssetRecord& r : ProjectDatabase::All())
                ArtifactStore::Queue(r);
            HX_LOG_INFO(EditorLog, "The project has {} assets ({} .meta files written).", ProjectDatabase::All().Size(), changes.metasWritten);
            engine::Application::focusChanged().Add([](void*, bool focused) {
                if (focused && AssetDatabase::AutoRefreshAllowed())
                    ProjectDatabase::Refresh();
            });
        }

        // The shader assemblies beside the executable, loaded as assets.
        engine::FolderAssetSource* LoadShaders()
        {
            String folder = platform::JoinPath(platform::GetDirectoryName(platform::ExecutablePath()), "Shaders");
            if (!platform::DirectoryExists(folder))
            {
                HX_LOG_ERROR(EditorLog, "No shader folder at '{}'.", folder);
                return nullptr;
            }
            auto* source = new engine::FolderAssetSource(folder);
            engine::AddAssetSource(source);
            Array<engine::AssetEntry> entries;
            source->List(entries);
            for (const engine::AssetEntry& e : entries)
                if (platform::GetExtension(e.path.View()) == ".dll")
                    engine::LoadAsset(e.guid);
            return source;
        }

        // A window's frame (the main window's, a floating window's: its display as the user): an element of a view of
        // that display with the class editor-caption under the point is the caption.
        platform::WindowHitTest HitTest(void* user, i32 x, i32 y)
        {
            static const ui::Atom caption = ui::Intern("editor-caption");
            i32 display = i32(intptr_t(user));
            for (ui::UIView* view : ui::UIView::Views())
            {
                ui::UIPanelSettings* settings = view->panelSettings();
                if ((settings ? settings->targetDisplay() : 0) != display)
                    continue;
                ui::ElementHandle e = view->Pick(Vector2(f32(x), f32(y)));
                if (!e.IsValid())
                    continue;
                ui::Panel* panel = view->panel();
                return panel && panel->tree().HasClass(e, caption) ? platform::WindowHitTest::Caption : platform::WindowHitTest::Client;
            }
            return platform::WindowHitTest::Client;
        }

        struct Run
        {
            player::Player player;
            i32 code = 0;
        };

        bool RunFrame(void* user)
        {
            auto* run = static_cast<Run*>(user);
            EditorApplication::Tick();
            ArtifactStore::Tick();
            if (EditorApplication::QuitRequested(run->code))
                return false;
            return run->player.Frame();
        }
    }

    bool ParseEditorArgs(Span<const StringView> args, EditorOptions& out, Array<String>& errors)
    {
        usize before = errors.Size();
        for (usize i = 0; i < args.Size(); i++)
        {
            StringView a = args[i];
            if (a == "-projectPath" || a == "-pipeline" || a == "-executeMethod")
            {
                if (i + 1 >= args.Size())
                {
                    errors.Push(Format("{} needs a value", a));
                    continue;
                }
                StringView v = args[++i];
                if (a == "-projectPath")
                    out.projectPath = String(v);
                else if (a == "-executeMethod")
                    out.executeMethod = String(v);
                else
                    out.pipeline = String(v);
                continue;
            }
            out.playerArgs.Push(String(a));
        }
        if (out.projectPath.Empty())
            out.projectPath = platform::CurrentDirectory();
        return errors.Size() == before;
    }

    i32 RunEditor(Span<const StringView> args)
    {
        EditorOptions editor;
        Array<String> errors;
        if (!ParseEditorArgs(args, editor, errors))
            for (const String& e : errors)
                HX_LOG_ERROR(EditorLog, "Command line: {}", e);

        // The player's arguments with the editor's data folder (its shell, skin and scene) unless one is given.
        Array<StringView> playerArgs;
        bool dataFolder = false;
        for (const String& a : editor.playerArgs)
        {
            playerArgs.Push(a.View());
            dataFolder = dataFolder || a == "-dataFolder";
        }
        if (!dataFolder)
        {
            playerArgs.Push("-dataFolder");
            playerArgs.Push(HELIX_EDITOR_DATA_DIR);
        }
        player::PlayerOptions options;
        errors.Clear();
        if (!player::ParseCommandLine(Span<const StringView>(playerArgs.Data(), playerArgs.Size()), options, errors))
            for (const String& e : errors)
                HX_LOG_ERROR(EditorLog, "Command line: {}", e);
        // The editor's C# assemblies are built beside it.
        String managed = platform::JoinPath(platform::GetDirectoryName(platform::ExecutablePath()), "Managed");
        if (platform::DirectoryExists(managed))
            options.managedFolder = managed;
        options.customFrame = !options.batchMode;

        // The linked engine modules (the build's list), then the editor's own.
        bool registered = true;
        for (const LinkedModule& m : LinkedModules())
            registered = Register(m.name, m.publish) && registered;
        registered = Register("HelixEditor", &HxBindings_HelixEditor_Publish) && registered;
        if (!registered)
            return 1;

        Run run;
        if (!run.player.Start(options))
        {
            i32 code = run.player.Running() ? 1 : 0;
            run.player.Shutdown();
            return code;
        }
        platform::Window* window = run.player.Window();
        if (window)
        {
            window->SetHitTest(&HitTest, nullptr);
            // Floating windows: tool windows of the player, framed by the editor too.
            EditorApplication::ToolWindowHost tools;
            tools.user = &run.player;
            tools.open = [](void* user, StringView title, Rect rect, bool decorated) {
                auto* player = static_cast<player::Player*>(user);
                i32 display = player->OpenToolWindow({.title = title, .x = i32(rect.x), .y = i32(rect.y), .width = i32(rect.width), .height = i32(rect.height),
                                                      .decorated = decorated});
                if (platform::Window* w = player->DisplayWindow(display))
                    w->SetHitTest(&HitTest, reinterpret_cast<void*>(intptr_t(display)));
                return display;
            };
            tools.close = [](void* user, i32 display) { static_cast<player::Player*>(user)->CloseToolWindow(display); };
            tools.focused = [](void* user, i32 display) {
                platform::Window* w = static_cast<player::Player*>(user)->DisplayWindow(display);
                return w && w->HasFocus();
            };
            tools.closeRequested = [](void* user, i32 display) {
                platform::Window* w = static_cast<player::Player*>(user)->DisplayWindow(display);
                return w && w->CloseRequested();
            };
            EditorApplication::SetToolWindowHost(tools);
        }
        EditorApplication::SetHost(window, editor.projectPath.View(), platform::ExecutablePath().View());
        EditorApplication::SetDataPath(options.dataFolder.View());
        // The editor's own content (its DontDestroyOnLoad scene) apart from the game's: the windows draw the editor's,
        // the Game view gets the game's displays.
        engine::Graphics::SetSeparateEditorContent(true);
        EditorApplication::SetExecuteMethod(editor.executeMethod.View());
        engine::FolderAssetSource* shaders = LoadShaders();
        OpenProject(options.dataFolder.View(), editor.projectPath.View());
        String pipelineName, pipelineClass;
        engine::ScriptableObject* asset = nullptr;
        if (PipelineClass(options.dataFolder.View(), editor.pipeline.View(), pipelineName, pipelineClass) && !pipelineClass.Empty())
        {
            asset = engine::ScriptableObject::CreateInstanceByName(pipelineClass.View());
            if (!asset)
                HX_LOG_ERROR(EditorLog, "-pipeline: no pipeline asset class '{}' among the linked modules.", pipelineClass);
        }
        if (asset)
            engine::GraphicsSettings::SetDefaultRenderPipeline(asset);
        HX_LOG_INFO(EditorLog, "Helix Editor on '{}' ({} pipeline).", editor.projectPath, pipelineName.Empty() ? StringView("no") : pipelineName.View());
        platform::RunMainLoop(&RunFrame, &run);
        i32 code = run.code ? run.code : run.player.ExitCode();
        engine::GraphicsSettings::SetDefaultRenderPipeline(nullptr);
        EditorApplication::SetHost(nullptr, editor.projectPath.View(), platform::ExecutablePath().View());
        EditorApplication::SetToolWindowHost({});
        ProjectDatabase::Close();
        run.player.Shutdown();
        delete shaders;
        return code;
    }
}
