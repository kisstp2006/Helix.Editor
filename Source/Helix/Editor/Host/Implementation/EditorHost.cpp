#include <Helix/Editor/Host/EditorHost.h>

#include <Helix/Editor/Application/EditorApplication.h>
#include <Helix/Editor/Host/EditorModules.h>

#include <Helix/Core/Logging/Log.h>
#include <Helix/Core/Strings/Format.h>
#include <Helix/Engine/Asset/Asset.h>
#include <Helix/Engine/Rendering/Pipeline/RenderPipeline.h>
#include <Helix/Engine/Script/ScriptRuntime.h>
#include <Helix/Engine/Script/ScriptableObject.h>
#include <Helix/Platform/IO/FileSystem.h>
#include <Helix/Platform/Window/Window.h>
#include <Helix/Player/Host/Player.h>
#include <Helix/Serialization/Json/Parser.h>
#include <Helix/Scripting/Bindings/Interop.h>
#include <Helix/UI/Core/Atoms.h>
#include <Helix/UI/Engine/Panel.h>
#include <Helix/UI/Engine/UIPanelSettings.h>
#include <Helix/UI/Engine/UIView.h>

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
            tools.open = [](void* user, StringView title, Rect rect) {
                auto* player = static_cast<player::Player*>(user);
                i32 display = player->OpenToolWindow({.title = title, .x = i32(rect.x), .y = i32(rect.y), .width = i32(rect.width), .height = i32(rect.height)});
                if (platform::Window* w = player->DisplayWindow(display))
                    w->SetHitTest(&HitTest, reinterpret_cast<void*>(intptr_t(display)));
                return display;
            };
            tools.close = [](void* user, i32 display) { static_cast<player::Player*>(user)->CloseToolWindow(display); };
            tools.closeRequested = [](void* user, i32 display) {
                platform::Window* w = static_cast<player::Player*>(user)->DisplayWindow(display);
                return w && w->CloseRequested();
            };
            EditorApplication::SetToolWindowHost(tools);
        }
        EditorApplication::SetHost(window, editor.projectPath.View(), platform::ExecutablePath().View());
        EditorApplication::SetDataPath(options.dataFolder.View());
        EditorApplication::SetExecuteMethod(editor.executeMethod.View());
        engine::FolderAssetSource* shaders = LoadShaders();
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
        run.player.Shutdown();
        delete shaders;
        return code;
    }
}
