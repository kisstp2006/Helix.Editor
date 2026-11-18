#include <Helix/Editor/Host/EditorHost.h>

#include <Helix/Editor/Application/EditorApplication.h>

#include <Helix/Core/Logging/Log.h>
#include <Helix/Core/Strings/Format.h>
#include <Helix/Engine/Asset/Asset.h>
#include <Helix/Engine/Rendering/Pipeline/RenderPipeline.h>
#include <Helix/Engine/Script/ScriptRuntime.h>
#include <Helix/Engine/Script/ScriptableObject.h>
#include <Helix/Platform/IO/FileSystem.h>
#include <Helix/Platform/Window/Window.h>
#include <Helix/Player/Host/Player.h>
#include <Helix/RenderPipelines/Physical/Pipeline/PhysicalRenderPipelineAsset.h>
#include <Helix/RenderPipelines/Standard/Pipeline/UniversalRenderPipelineAsset.h>
#include <Helix/Scripting/Bindings/Interop.h>
#include <Helix/UI/Core/Atoms.h>
#include <Helix/UI/Engine/Panel.h>
#include <Helix/UI/Engine/UIView.h>

extern "C" void HxBindings_HelixRenderPipelinesCore_Publish();
extern "C" void HxBindings_HelixRenderPipelinesStandard_Publish();
extern "C" void HxBindings_HelixRenderPipelinesPhysical_Publish();
extern "C" void HxBindings_HelixUI_Publish();
extern "C" void HxBindings_HelixCanvasUI_Publish();
HX_BINDINGS_EXPORT void HxBindings_HelixEditor_Publish();

namespace helix::editor
{
    namespace
    {
        LogChannel EditorLog{"Editor"};

        struct ModuleEntry
        {
            StringView name;
            void (*publish)();
        };
        // The engine's modules the editor has (the pipelines among them: the editor renders with either).
        constexpr ModuleEntry kModules[] = {
            {"HelixRenderPipelinesCore", &HxBindings_HelixRenderPipelinesCore_Publish},
            {"HelixRenderPipelinesStandard", &HxBindings_HelixRenderPipelinesStandard_Publish},
            {"HelixRenderPipelinesPhysical", &HxBindings_HelixRenderPipelinesPhysical_Publish},
            {"HelixUI", &HxBindings_HelixUI_Publish},
            {"HelixCanvasUI", &HxBindings_HelixCanvasUI_Publish},
            {"HelixEditor", &HxBindings_HelixEditor_Publish},
        };

        struct PipelineEntry
        {
            StringView name;
            engine::ScriptableObject* (*create)();
        };
        engine::ScriptableObject* CreateStandard() { return rendering::universal::UniversalRenderPipelineAsset::Create(); }
        engine::ScriptableObject* CreatePhysical() { return rendering::physical::PhysicalRenderPipelineAsset::Create(); }
        constexpr PipelineEntry kPipelines[] = {{"standard", &CreateStandard}, {"physical", &CreatePhysical}, {"none", nullptr}};

        bool Register(StringView name, void (*publish)())
        {
            publish();
            String error;
            if (engine::RegisterLinkedGameModule(name, error))
                return true;
            HX_LOG_ERROR(EditorLog, "The module {} did not register: {}", name, error);
            return false;
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

        // The window's frame: an element of the shell with the class editor-caption under the point is the caption.
        platform::WindowHitTest HitTest(void*, i32 x, i32 y)
        {
            static const ui::Atom caption = ui::Intern("editor-caption");
            for (ui::UIView* view : ui::UIView::Views())
            {
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
                {
                    bool known = false;
                    for (const PipelineEntry& p : kPipelines)
                        known = known || p.name == v;
                    if (known)
                        out.pipeline = String(v);
                    else
                        errors.Push(Format("-pipeline takes standard, physical or none, not '{}'", v));
                }
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

        bool registered = true;
        for (const ModuleEntry& m : kModules)
            registered = Register(m.name, m.publish) && registered;
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
            window->SetHitTest(&HitTest, nullptr);
        EditorApplication::SetHost(window, editor.projectPath.View(), platform::ExecutablePath().View());
        EditorApplication::SetDataPath(options.dataFolder.View());
        EditorApplication::SetExecuteMethod(editor.executeMethod.View());
        engine::FolderAssetSource* shaders = LoadShaders();
        engine::ScriptableObject* asset = nullptr;
        for (const PipelineEntry& p : kPipelines)
            if (p.name == editor.pipeline.View() && p.create)
                asset = p.create();
        if (asset)
            engine::GraphicsSettings::SetDefaultRenderPipeline(asset);
        HX_LOG_INFO(EditorLog, "Helix Editor on '{}' ({} pipeline).", editor.projectPath, editor.pipeline);
        platform::RunMainLoop(&RunFrame, &run);
        i32 code = run.code ? run.code : run.player.ExitCode();
        engine::GraphicsSettings::SetDefaultRenderPipeline(nullptr);
        EditorApplication::SetHost(nullptr, editor.projectPath.View(), platform::ExecutablePath().View());
        run.player.Shutdown();
        delete shaders;
        return code;
    }
}
