#include <Helix/Editor/Assets/ImportNative.h>

#include <Helix/Core/Containers/HashMap.h>
#include <Helix/Core/Logging/Log.h>
#include <Helix/Core/Strings/Format.h>
#include <Helix/Editor/Assets/ArtifactStore.h>
#include <Helix/Editor/Assets/AssetImporters.h>
#include <Helix/Editor/Assets/ProjectDatabase.h>
#include <Helix/Engine/Asset/AssetLoader.h>
#include <Helix/Engine/Object/Class.h>
#include <Helix/Platform/IO/FileSystem.h>

#include <bit>

namespace helix::editor
{
    namespace
    {
        LogChannel ImportLog{"Import"};

        // C#'s: runs the importer of the context.
        using ManagedImport = void (*)(u64 context);
        ManagedImport managedImport = nullptr;
        // C#'s postprocessing: kind 0 before an import, 1 after one, 2 after a refresh.
        using ManagedPostprocess = void (*)(i32 kind, u64 context);
        ManagedPostprocess managedPostprocess = nullptr;

        String Lower(StringView s);

        struct PostContext
        {
            StringView path;
            Array<engine::Object*> objects;
            const AssetChanges* changes = nullptr;
        };

        PostContext* PostOf(u64 context) { return reinterpret_cast<PostContext*>(uintptr_t(context)); }

        void Postprocess(StringView path, const engine::AssetObjects& out)
        {
            if (!managedPostprocess)
                return;
            PostContext c;
            c.path = path;
            for (const engine::AssetObjects::Entry& e : out.objects)
                if (engine::Object* o = engine::EntityIdToObject(e.id))
                    c.objects.Push(o);
            managedPostprocess(1, u64(uintptr_t(&c)));
        }

        // The engine's loaders wrapped: the original of each extension (copies, their names kept here), and what a
        // wrapped decode hands on.
        struct Wrapped
        {
            String name;
            engine::AssetLoader loader;
        };

        struct Wraps
        {
            Array<Wrapped*> loaders;
            HashMap<String, Wrapped*> byExtension;
        };

        Wraps& W()
        {
            static Wraps* w = new Wraps();
            return *w;
        }

        struct Boxed
        {
            void* data = nullptr;
            const Wrapped* original = nullptr;
            String path;
            bool project = false;
        };

        void* WrappedDecode(const engine::AssetDecodeInput& input, String& error)
        {
            StringView ext = platform::GetExtension(input.path);
            String key = Lower(ext.StartsWith(".") ? ext.Substr(1) : ext);
            Wrapped* const* w = W().byExtension.Find(key);
            if (!w)
            {
                error = Format("no loader takes .{}", key);
                return nullptr;
            }
            void* data = (*w)->loader.decode(input, error);
            if (!data)
                return nullptr;
            auto* b = new Boxed();
            b->data = data;
            b->original = *w;
            b->path = String(input.path);
            b->project = ProjectDatabase::IsProjectSource(input.source);
            return b;
        }

        bool WrappedIntegrate(void* decoded, engine::AssetObjects& out, Array<String>& errors)
        {
            auto* b = static_cast<Boxed*>(decoded);
            bool ok = b->original->loader.integrate(b->data, out, errors);
            if (b->project)
            {
                if (ok)
                {
                    Postprocess(b->path.View(), out);
                    ArtifactStore::Integrated(out);
                }
                ProjectDatabase::ClearMetaOverride(b->path.View());
            }
            return ok;
        }

        void WrappedRelease(void* decoded)
        {
            auto* b = static_cast<Boxed*>(decoded);
            if (b->original->loader.release)
                b->original->loader.release(b->data);
            if (b->project)
                ProjectDatabase::ClearMetaOverride(b->path.View());
            delete b;
        }

        struct Registry
        {
            // The scripted importers' classes by extension.
            HashMap<String, String> classByExtension;
            // The names the engine's loaders view (kept for as long as the loaders are).
            Array<String*> loaderNames;
        };

        Registry& R()
        {
            static Registry* r = new Registry();
            return *r;
        }

        String Lower(StringView s)
        {
            String out(s);
            for (usize i = 0; i < out.Size(); i++)
                if (out[i] >= 'A' && out[i] <= 'Z')
                    out[i] = char(out[i] - 'A' + 'a');
            return out;
        }

        struct Decoded
        {
            Guid guid;
            String path, importer;
        };

        struct Context
        {
            engine::AssetObjects* out = nullptr;
            const Decoded* asset = nullptr;
            Array<String>* errors = nullptr;
        };

        Context* ContextOf(u64 context) { return reinterpret_cast<Context*>(uintptr_t(context)); }

        // The file's import runs on the main thread (C#): decode only notes what it is.
        void* Decode(const engine::AssetDecodeInput& input, String& error)
        {
            StringView ext = platform::GetExtension(input.path);
            String key = Lower(ext.StartsWith(".") ? ext.Substr(1) : ext);
            const String* importer = R().classByExtension.Find(key);
            if (!importer)
            {
                error = Format("no scripted importer takes .{}", key);
                return nullptr;
            }
            auto* d = new Decoded();
            d->guid = input.guid;
            d->path = String(input.path);
            d->importer = *importer;
            return d;
        }

        bool Integrate(void* decoded, engine::AssetObjects& out, Array<String>& errors)
        {
            auto* d = static_cast<Decoded*>(decoded);
            if (!managedImport)
            {
                errors.Push(Format("the scripted importer {} has no C# side", d->importer));
                return false;
            }
            Context c{&out, d, &errors};
            ArtifactStore::BeginImport(d->guid);
            managedImport(u64(uintptr_t(&c)));
            bool ok = errors.Empty() || !out.objects.Empty();
            if (ok)
            {
                Postprocess(d->path.View(), out);
                ArtifactStore::Integrated(out);
            }
            ProjectDatabase::ClearMetaOverride(d->path.View());
            return ok;
        }

        void Release(void* decoded) { delete static_cast<Decoded*>(decoded); }

        void SetImportCallback(void* function) { managedImport = std::bit_cast<ManagedImport>(function); }
        void SetPostprocessCallback(void* function) { managedPostprocess = std::bit_cast<ManagedPostprocess>(function); }

        struct ExportRegistrar
        {
            ExportRegistrar()
            {
                scripting::RegisterNativeExport("HelixEditor.SetImportCallback", std::bit_cast<const void*>(&SetImportCallback));
                scripting::RegisterNativeExport("HelixEditor.SetPostprocessCallback", std::bit_cast<const void*>(&SetPostprocessCallback));
            }
        } registrar;
    }

    void ImportNative::RegisterScriptedImporter(StringView className, i32 version, StringView extensions)
    {
        ImporterDescription d;
        d.name = Format("ScriptedImporter:{}", className);
        d.section = String("ScriptedImporter");
        d.version = version;
        d.settings.Push(String("internalIDToNameTable: []"));
        d.settings.Push(String("externalObjects: {}"));
        d.settings.Push(String("serializedVersion: 2"));
        d.settings.Push(Format("script: {}", className));
        engine::AssetLoader loader;
        R().loaderNames.Push(new String(d.name));
        loader.name = R().loaderNames[R().loaderNames.Size() - 1]->View();
        usize at = 0;
        while (at <= extensions.Size())
        {
            usize end = extensions.Find(';', at);
            if (end == StringView::npos)
                end = extensions.Size();
            StringView e = extensions.Substr(at, end - at).Trimmed();
            if (e.StartsWith("."))
                e = e.Substr(1);
            if (!e.Empty())
            {
                d.extensions.Push(Lower(e));
                loader.extensions.Push(Lower(e));
                R().classByExtension.Set(Lower(e), String(className));
            }
            at = end + 1;
        }
        AssetImporters::Add(d);
        loader.decode = &Decode;
        loader.integrate = &Integrate;
        loader.release = &Release;
        engine::RegisterAssetLoader(loader);
    }

    String ImportNative::ContextPath(u64 context)
    {
        Context* c = ContextOf(context);
        return c ? c->asset->path : String();
    }

    String ImportNative::ContextImporter(u64 context)
    {
        Context* c = ContextOf(context);
        return c ? c->asset->importer : String();
    }

    void ImportNative::AddObject(u64 context, StringView identifier, engine::Object* object)
    {
        Context* c = ContextOf(context);
        if (!c || !object)
            return;
        i32 classId = engine::GetClass(object->ClassIndex()).classId;
        c->out->Add(engine::AssetObjectFileId(classId, identifier), object);
    }

    void ImportNative::SetMainObject(u64 context, engine::Object* object)
    {
        Context* c = ContextOf(context);
        if (!c || !object)
            return;
        for (const engine::AssetObjects::Entry& e : c->out->objects)
            if (e.id == object->GetEntityId())
            {
                c->out->mainFileID = e.fileID;
                return;
            }
        // Not added yet: added as the main object under its name.
        i32 classId = engine::GetClass(object->ClassIndex()).classId;
        c->out->Add(engine::AssetObjectFileId(classId, "main"), object, true);
    }

    void ImportNative::LogImportMessage(u64 context, StringView message, bool error)
    {
        Context* c = ContextOf(context);
        StringView path = c ? c->asset->path.View() : StringView("?");
        if (error)
        {
            if (c)
                c->errors->Push(String(message));
            else
                HX_LOG_ERROR(ImportLog, "{}: {}", path, message);
        }
        else
            HX_LOG_WARNING(ImportLog, "{}: {}", path, message);
    }

    void ImportNative::DependsOn(u64 context, StringView name, i32 kind)
    {
        Context* c = ContextOf(context);
        if (!c || kind < 0 || kind > 2)
            return;
        ArtifactStore::AddDependency(c->asset->guid, name, ArtifactStore::Dependency(kind));
    }

    void ImportNative::SetMetaOverride(StringView path, StringView keysAndValues)
    {
        MetaFile meta;
        if (!ProjectDatabase::ReadMeta(path, meta))
            return;
        usize at = 0;
        while (at < keysAndValues.Size())
        {
            usize k = keysAndValues.Find('\x1f', at);
            if (k == StringView::npos)
                break;
            usize v = keysAndValues.Find('\x1f', k + 1);
            if (v == StringView::npos)
                v = keysAndValues.Size();
            meta.SetValue(keysAndValues.Substr(at, k - at), keysAndValues.Substr(k + 1, v - k - 1));
            at = v + 1;
        }
        String text = meta.Write();
        ProjectDatabase::SetMetaOverride(path, text.View());
    }

    void ImportNative::SetPostprocessors(StringView text) { ArtifactStore::SetPostprocessors(text); }

    String ImportNative::PostprocessPath(u64 context)
    {
        PostContext* c = PostOf(context);
        return c ? String(c->path) : String();
    }

    Array<engine::Object*> ImportNative::PostprocessObjects(u64 context)
    {
        PostContext* c = PostOf(context);
        return c ? c->objects : Array<engine::Object*>();
    }

    Array<String> ImportNative::PostprocessPaths(u64 context, i32 which)
    {
        PostContext* c = PostOf(context);
        if (!c || !c->changes)
            return Array<String>();
        switch (which)
        {
        case 0: return c->changes->imported;
        case 1: return c->changes->deleted;
        case 2: return c->changes->movedTo;
        case 3: return c->changes->movedFrom;
        default: return Array<String>();
        }
    }

    void ImportNative::Preprocess(StringView path)
    {
        const AssetRecord* r = ProjectDatabase::ByPath(path);
        if (!managedPostprocess || !r || r->folder)
            return;
        PostContext c;
        c.path = r->path.View();
        managedPostprocess(0, u64(uintptr_t(&c)));
    }

    void ImportNative::PostprocessAll(const AssetChanges& changes)
    {
        if (!managedPostprocess)
            return;
        PostContext c;
        c.changes = &changes;
        managedPostprocess(2, u64(uintptr_t(&c)));
    }

    void ImportNative::WrapLoaders()
    {
        Wraps& w = W();
        String serialized(engine::SerializedAssetLoader().name);
        for (const ImporterDescription& d : AssetImporters::All())
            for (const String& ext : d.extensions)
            {
                engine::AssetLoader loader;
                if (w.byExtension.Find(ext) || !engine::FindAssetLoader(ext.View(), loader) || loader.name == serialized.View() ||
                    loader.name.StartsWith("ScriptedImporter:") || loader.decode == &WrappedDecode)
                    continue;
                // One wrap per loader: its extensions all.
                auto* wrapped = new Wrapped();
                wrapped->name = String(loader.name);
                wrapped->loader = loader;
                wrapped->loader.name = wrapped->name.View();
                w.loaders.Push(wrapped);
                for (const String& e : loader.extensions)
                    w.byExtension.Set(e, wrapped);
                engine::AssetLoader wrapper = loader;
                wrapper.name = wrapped->name.View();
                wrapper.decode = &WrappedDecode;
                wrapper.integrate = &WrappedIntegrate;
                wrapper.release = &WrappedRelease;
                engine::RegisterAssetLoader(wrapper);
            }
    }

    String ImportNative::MetaValue(StringView path, StringView key)
    {
        MetaFile meta;
        return ProjectDatabase::ReadMeta(path, meta) ? meta.Value(key) : String();
    }

    bool ImportNative::SetMetaValues(StringView path, StringView keysAndValues)
    {
        MetaFile meta;
        if (!ProjectDatabase::ReadMeta(path, meta))
            return false;
        usize at = 0;
        while (at < keysAndValues.Size())
        {
            usize k = keysAndValues.Find('\x1f', at);
            if (k == StringView::npos)
                break;
            usize v = keysAndValues.Find('\x1f', k + 1);
            if (v == StringView::npos)
                v = keysAndValues.Size();
            meta.SetValue(keysAndValues.Substr(at, k - at), keysAndValues.Substr(k + 1, v - k - 1));
            at = v + 1;
        }
        return ProjectDatabase::WriteMeta(path, meta);
    }

    bool ImportNative::HasMeta(StringView path)
    {
        MetaFile meta;
        return ProjectDatabase::ReadMeta(path, meta);
    }
}
