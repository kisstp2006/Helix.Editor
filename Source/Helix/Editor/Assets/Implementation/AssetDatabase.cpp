#include <Helix/Editor/Assets/AssetDatabase.h>

#include <Helix/Core/Containers/HashMap.h>
#include <Helix/Core/Logging/Log.h>
#include <Helix/Core/Strings/Format.h>
#include <Helix/Editor/Application/EditorUtility.h>
#include <Helix/Editor/Assets/ArtifactStore.h>
#include <Helix/Editor/Assets/AssetModification.h>
#include <Helix/Editor/Assets/AssetImporters.h>
#include <Helix/Editor/Assets/ImportNative.h>
#include <Helix/Editor/Assets/ProjectDatabase.h>
#include <Helix/Engine/Asset/Asset.h>
#include <Helix/Engine/Asset/AssetLoader.h>
#include <Helix/Engine/Object/Class.h>
#include <Helix/Platform/IO/FileSystem.h>

namespace helix::editor
{
    namespace
    {
        LogChannel AssetLog{"AssetDatabase"};

        i32 autoRefreshBlocks = 0;

        String Lower(StringView s)
        {
            String out(s);
            for (usize i = 0; i < out.Size(); i++)
            {
                char c = out[i];
                if (c >= 'A' && c <= 'Z')
                    out[i] = char(c - 'A' + 'a');
            }
            return out;
        }

        // An asset's name: its file name without the extension.
        StringView NameOf(StringView path)
        {
            StringView name = platform::GetFileName(path);
            usize dot = name.FindLast('.');
            return dot == StringView::npos || dot == 0 ? name : name.Substr(0, dot);
        }

        StringView ParentOf(StringView path)
        {
            usize slash = path.FindLast('/');
            return slash == StringView::npos ? StringView() : path.Substr(0, slash);
        }

        String ExtensionOf(StringView path)
        {
            StringView ext = platform::GetExtension(path);
            return Lower(ext.StartsWith(".") ? ext.Substr(1) : ext);
        }

        // A path the engine's serialized loader reads (a native asset's).
        bool IsNativePath(StringView path)
        {
            engine::AssetLoader loader;
            return engine::FindAssetLoader(ExtensionOf(path).View(), loader) && loader.name == engine::SerializedAssetLoader().name;
        }

        // The project's record of an object's asset.
        const AssetRecord* RecordOf(const engine::Object* o, i64* fileID = nullptr)
        {
            Guid g;
            i64 f = 0;
            i32 t = 0;
            if (!o || !engine::GetAssetReference(o, g, f, t))
                return nullptr;
            if (fileID)
                *fileID = f;
            return ProjectDatabase::ByGuid(g);
        }

        // A native asset's objects, its main one first.
        void ObjectsOf(const Guid& guid, Array<engine::Object*>& out)
        {
            Array<engine::Object*> all;
            engine::LoadAllAssets(guid, all);
            engine::Object* main = engine::LoadAsset(guid);
            if (main)
                out.Push(main);
            for (engine::Object* o : all)
                if (o != main)
                    out.Push(o);
        }

        // A native asset's file written from its objects (their dirt cleared); no import follows.
        bool WriteNative(const AssetRecord& r)
        {
            Array<engine::Object*> objects;
            ObjectsOf(r.guid, objects);
            Array<u8> bytes;
            String error;
            if (objects.Empty() || !engine::WriteAssetFile(Span<engine::Object* const>(objects.Data(), objects.Size()), bytes, error, false) ||
                !platform::WriteFile(ProjectDatabase::FullPath(r.path.View()).View(), Span<const u8>(bytes.Data(), bytes.Size())))
            {
                HX_LOG_ERROR(AssetLog, "'{}' could not be saved: {}", r.path, error.Empty() ? StringView("the file could not be written") : error.View());
                return false;
            }
            for (engine::Object* o : objects)
                EditorUtility::ClearDirty(o);
            ProjectDatabase::NoteWritten(r.path.View());
            return true;
        }

        bool AnyDirty(const Guid& guid)
        {
            Array<engine::Object*> objects;
            if (engine::IsAssetLoaded(guid))
                engine::LoadAllAssets(guid, objects);
            for (engine::Object* o : objects)
                if (EditorUtility::IsDirty(o))
                    return true;
            return false;
        }

        // The guids a serialized file names ("guid: <32 hex digits>").
        void GuidsIn(StringView text, Array<Guid>& out)
        {
            usize at = 0;
            while ((at = text.Find("guid: ", at)) != StringView::npos)
            {
                at += 6;
                Guid g;
                if (at + 32 <= text.Size() && Guid::TryParse(text.Substr(at, 32), g))
                    out.Push(g);
            }
        }

        // A load that imports (the asset neither loaded nor current in the artifacts, nor being imported) is
        // preprocessed first.
        void BeforeLoad(const AssetRecord& r)
        {
            if (!r.folder && !engine::IsAssetLoaded(r.guid) && ArtifactStore::Current(r).Empty() && !ArtifactStore::IsPending(r.guid))
                ImportNative::Preprocess(r.path.View());
        }

        // The type FindAssets' t: matches: the importer's main type, Folder for folders.
        StringView TypeOf(const AssetRecord& r)
        {
            if (r.folder)
                return "Folder";
            const ImporterDescription* d = AssetImporters::Find(r.importer.View());
            return d ? d->mainType.View() : StringView();
        }
    }

    void AssetDatabase::Refresh() { ProjectDatabase::Refresh(); }

    // The changes noted, then the asset imported again (loaded objects take its new data) whether or not it changed.
    void AssetDatabase::ImportAsset(StringView path)
    {
        AssetChanges changes = ProjectDatabase::Refresh();
        for (const String& p : changes.imported)
            if (p.View() == path)
                return;
        // Asked for: its artifact goes, it loads from its file and is imported anew.
        const AssetRecord* r = ProjectDatabase::ByPath(path);
        if (!r)
            return;
        ArtifactStore::Invalidate(r->guid);
        engine::RefreshAssets();
        ProjectDatabase::Reimport(path);
        if ((r = ProjectDatabase::ByPath(path)) != nullptr)
            ArtifactStore::Queue(*r);
    }

    String AssetDatabase::GUIDToAssetPath(StringView guid)
    {
        Guid g;
        if (!Guid::TryParse(guid, g))
            return String();
        const AssetRecord* r = ProjectDatabase::ByGuid(g);
        return r ? r->path : String();
    }

    String AssetDatabase::AssetPathToGUID(StringView path)
    {
        const AssetRecord* r = ProjectDatabase::ByPath(path);
        return r ? r->guid.ToString() : String();
    }

    bool AssetDatabase::AssetPathExists(StringView path) { return ProjectDatabase::ByPath(path) != nullptr; }

    bool AssetDatabase::IsValidFolder(StringView path)
    {
        const AssetRecord* r = ProjectDatabase::ByPath(path);
        return (r && r->folder) || Lower(path) == "assets";
    }

    Array<String> AssetDatabase::GetAllAssetPaths()
    {
        Array<String> out;
        for (const AssetRecord& r : ProjectDatabase::All())
            out.Push(r.path);
        return out;
    }

    Array<String> AssetDatabase::GetSubFolders(StringView path)
    {
        Array<String> out;
        String parent = Lower(path);
        while (!parent.Empty() && parent[parent.Size() - 1] == '/')
            parent = String(parent.View().Substr(0, parent.Size() - 1));
        for (const AssetRecord& r : ProjectDatabase::All())
            if (r.folder && Lower(ParentOf(r.path.View())) == parent)
                out.Push(r.path);
        return out;
    }

    bool AssetDatabase::IsMainAssetAtPathLoaded(StringView assetPath)
    {
        const AssetRecord* r = ProjectDatabase::ByPath(assetPath);
        return r && !r->folder && engine::IsAssetLoaded(r->guid) && engine::FindLoadedAsset(r->guid) != nullptr;
    }

    String AssetDatabase::GetAssetPath(engine::Object* assetObject)
    {
        const AssetRecord* r = RecordOf(assetObject);
        return r ? r->path : String();
    }

    // Scenes' paths come with the scene step; an asset's path meanwhile.
    String AssetDatabase::GetAssetOrScenePath(engine::Object* assetObject) { return GetAssetPath(assetObject); }

    bool AssetDatabase::Contains(engine::Object* obj) { return RecordOf(obj) != nullptr; }

    bool AssetDatabase::IsMainAsset(engine::Object* obj)
    {
        const AssetRecord* r = RecordOf(obj);
        return r && engine::FindLoadedAsset(r->guid) == obj;
    }

    bool AssetDatabase::IsSubAsset(engine::Object* obj) { return Contains(obj) && !IsMainAsset(obj); }

    bool AssetDatabase::IsNativeAsset(engine::Object* obj)
    {
        const AssetRecord* r = RecordOf(obj);
        return r && IsNativePath(r->path.View());
    }

    bool AssetDatabase::IsForeignAsset(engine::Object* obj)
    {
        const AssetRecord* r = RecordOf(obj);
        return r && !IsNativePath(r->path.View());
    }

    String AssetDatabase::GuidAndFileIdInternal(engine::Object* obj)
    {
        i64 fileID = 0;
        const AssetRecord* r = RecordOf(obj, &fileID);
        return r ? Format("{} {}", r->guid.ToString(), fileID) : String();
    }

    String AssetDatabase::MainAssetTypeNameInternal(StringView assetPath)
    {
        const AssetRecord* r = ProjectDatabase::ByPath(assetPath);
        if (!r || r->folder)
            return String();
        // Not loaded: the importer's main type where it names one (no load to know).
        if (!engine::IsAssetLoaded(r->guid))
            if (const ImporterDescription* d = AssetImporters::Find(r->importer.View()); d && !d->mainType.Empty())
                return d->mainType;
        engine::Object* main = engine::LoadAsset(r->guid);
        return main ? String(main->Class().name) : String();
    }

    String AssetDatabase::TypeNameFromPathAndFileIdInternal(StringView assetPath, i64 localIdentifierInFile)
    {
        const AssetRecord* r = ProjectDatabase::ByPath(assetPath);
        engine::Object* o = r && !r->folder ? engine::LoadAsset(r->guid, localIdentifierInFile) : nullptr;
        return o ? String(o->Class().name) : String();
    }

    String AssetDatabase::GenerateUniqueAssetPath(StringView path)
    {
        if (path.Empty() || !ProjectDatabase::IsProjectPath(path) || !IsValidFolder(ParentOf(path)))
            return String();
        return ProjectDatabase::UniquePath(path);
    }

    void AssetDatabase::CreateAsset(engine::Object* asset, StringView path)
    {
        if (!asset)
        {
            HX_LOG_ERROR(AssetLog, "CreateAsset: the object is null.");
            return;
        }
        if (RecordOf(asset))
        {
            HX_LOG_ERROR(AssetLog, "CreateAsset: '{}' is an asset already ({}).", asset->Name(), GetAssetPath(asset));
            return;
        }
        if (!ProjectDatabase::IsProjectPath(path) || !IsValidFolder(ParentOf(path)) || !IsNativePath(path))
        {
            HX_LOG_ERROR(AssetLog, "CreateAsset: '{}' is no path of a native asset in a folder of the project.", path);
            return;
        }
        AssetModificationNative::WillCreate(Format("{}.meta", path).View());
        // One there goes first (with its .meta).
        if (ProjectDatabase::ByPath(path))
            ProjectDatabase::Delete(path);
        Array<u8> bytes;
        String error;
        engine::Object* objects[] = {asset};
        if (!engine::WriteAssetFile(Span<engine::Object* const>(objects, 1), bytes, error, false) ||
            !platform::WriteFile(ProjectDatabase::FullPath(path).View(), Span<const u8>(bytes.Data(), bytes.Size())))
        {
            HX_LOG_ERROR(AssetLog, "CreateAsset: '{}' could not be written: {}", path, error);
            return;
        }
        // The object is the asset's main object (not loaded again from the file).
        Guid guid = ProjectDatabase::MetaFor(path);
        engine::RegisterAssetObject(guid, engine::MainAssetFileId(asset->Class().classId), asset, true);
        EditorUtility::ClearDirty(asset);
        ProjectDatabase::CreatedInMemory(path);
        ProjectDatabase::Refresh();
    }

    void AssetDatabase::AddObjectToAssetInternal(engine::Object* objectToAdd, StringView path)
    {
        const AssetRecord* r = ProjectDatabase::ByPath(path);
        if (!objectToAdd || !r || r->folder || !IsNativePath(r->path.View()))
        {
            HX_LOG_ERROR(AssetLog, "AddObjectToAsset: '{}' is no native asset.", path);
            return;
        }
        if (RecordOf(objectToAdd))
        {
            HX_LOG_ERROR(AssetLog, "AddObjectToAsset: '{}' is an asset's object already.", objectToAdd->Name());
            return;
        }
        Array<engine::Object*> objects;
        ObjectsOf(r->guid, objects);
        // Its fileID as the asset's file gives it (by class and name; another taken: with its index).
        HashMap<i64, u8> used;
        for (engine::Object* o : objects)
        {
            i64 f = 0;
            RecordOf(o, &f);
            used.Set(f, u8(1));
        }
        i32 classId = objectToAdd->Class().classId;
        i64 fileID = engine::AssetObjectFileId(classId, objectToAdd->Name());
        while (used.Find(fileID))
            fileID = engine::AssetObjectFileId(classId, String(objectToAdd->Name()).Append(Format("#{}", objects.Size())));
        engine::RegisterAssetObject(r->guid, fileID, objectToAdd, objects.Empty());
        // Written by the next save.
        EditorUtility::SetDirty(objects.Empty() ? objectToAdd : objects[0]);
    }

    void AssetDatabase::RemoveObjectFromAsset(engine::Object* objectToRemove)
    {
        const AssetRecord* r = RecordOf(objectToRemove);
        if (!r)
            return;
        Guid guid = r->guid;
        engine::UnregisterAssetObject(objectToRemove);
        if (engine::Object* main = engine::FindLoadedAsset(guid))
            EditorUtility::SetDirty(main);
    }

    void AssetDatabase::SetMainObject(engine::Object* mainObject, StringView assetPath)
    {
        i64 fileID = 0;
        const AssetRecord* r = RecordOf(mainObject, &fileID);
        if (!r || r->path.View() != ProjectDatabase::ByPath(assetPath)->path.View())
        {
            HX_LOG_ERROR(AssetLog, "SetMainObject: the object is no object of '{}'.", assetPath);
            return;
        }
        engine::RegisterAssetObject(r->guid, fileID, mainObject, true);
        EditorUtility::SetDirty(mainObject);
    }

    void AssetDatabase::SaveAssetIfDirty(engine::Object* obj)
    {
        const AssetRecord* r = RecordOf(obj);
        if (r && IsNativePath(r->path.View()) && AnyDirty(r->guid))
            WriteNative(*r);
    }

    void AssetDatabase::SaveAssetIfDirtyByGuidInternal(StringView guid)
    {
        Guid g;
        const AssetRecord* r = Guid::TryParse(guid, g) ? ProjectDatabase::ByGuid(g) : nullptr;
        if (r && IsNativePath(r->path.View()) && AnyDirty(r->guid))
            WriteNative(*r);
    }

    Array<String> AssetDatabase::LabelsInternal(StringView path)
    {
        MetaFile meta;
        return ProjectDatabase::ReadMeta(path, meta) ? meta.labels : Array<String>();
    }

    void AssetDatabase::SetLabelsInternal(StringView path, StringView labels)
    {
        MetaFile meta;
        if (!ProjectDatabase::ReadMeta(path, meta))
            return;
        meta.labels.Clear();
        usize at = 0;
        while (at < labels.Size())
        {
            usize end = labels.Find('\n', at);
            if (end == StringView::npos)
                end = labels.Size();
            StringView l = labels.Substr(at, end - at).Trimmed();
            bool seen = false;
            for (const String& e : meta.labels)
                seen = seen || e.View() == l;
            if (!l.Empty() && !seen)
                meta.labels.Push(String(l));
            at = end + 1;
        }
        ProjectDatabase::WriteMeta(path, meta);
        ProjectDatabase::Refresh();
    }

    Array<String> AssetDatabase::GetDependenciesInternal(StringView paths, bool recursive)
    {
        Array<String> out, queue;
        HashMap<String, u8> seen;
        auto add = [&](StringView p) {
            String key = Lower(p);
            if (seen.Find(key))
                return;
            seen.Set(key, u8(1));
            out.Push(String(p));
            queue.Push(String(p));
        };
        usize at = 0;
        while (at < paths.Size())
        {
            usize end = paths.Find('\n', at);
            if (end == StringView::npos)
                end = paths.Size();
            StringView p = paths.Substr(at, end - at).Trimmed();
            if (const AssetRecord* r = p.Empty() ? nullptr : ProjectDatabase::ByPath(p))
                add(r->path.View());
            at = end + 1;
        }
        // Breadth first; one level unless recursive.
        usize first = 0, levelEnd = queue.Size();
        for (u32 level = 0; first < queue.Size(); level++)
        {
            if (level > 0 && !recursive)
                break;
            levelEnd = queue.Size();
            for (; first < levelEnd; first++)
            {
                const AssetRecord* r = ProjectDatabase::ByPath(queue[first].View());
                if (!r || r->folder)
                    continue;
                Array<Guid> guids;
                if (IsNativePath(r->path.View()))
                {
                    Array<u8> bytes;
                    if (platform::ReadFile(ProjectDatabase::FullPath(r->path.View()).View(), bytes))
                        GuidsIn(StringView(reinterpret_cast<const char*>(bytes.Data()), bytes.Size()), guids);
                }
                for (const Guid& g : guids)
                    if (const AssetRecord* d = ProjectDatabase::ByGuid(g); d && d->guid != r->guid)
                        add(d->path.View());
                Array<String> recorded;
                ArtifactStore::DependenciesOf(r->guid, recorded);
                for (const String& p : recorded)
                    if (const AssetRecord* d = ProjectDatabase::ByPath(p.View()))
                        add(d->path.View());
            }
        }
        return out;
    }

    String AssetDatabase::AssetDependencyHashInternal(StringView path)
    {
        const AssetRecord* r = ProjectDatabase::ByPath(path);
        return r ? ArtifactStore::DependencyHash(*r) : String();
    }

    void AssetDatabase::RegisterCustomDependencyInternal(StringView dependency, StringView hashOfValue)
    {
        if (!dependency.Empty())
            ArtifactStore::RegisterCustom(dependency, hashOfValue);
    }

    u32 AssetDatabase::UnregisterCustomDependencyPrefixFilter(StringView prefixFilter) { return ArtifactStore::UnregisterCustomPrefix(prefixFilter); }

    void AssetDatabase::AllowAutoRefresh()
    {
        if (autoRefreshBlocks > 0)
            autoRefreshBlocks--;
    }

    void AssetDatabase::DisallowAutoRefresh() { autoRefreshBlocks++; }
    bool AssetDatabase::AutoRefreshAllowed() { return autoRefreshBlocks == 0; }

    Array<String> AssetDatabase::AvailableImportersInternal(StringView path)
    {
        Array<String> out;
        String ext = ExtensionOf(path);
        out.Push(DefaultImporterInternal(path));
        for (const ImporterDescription& d : AssetImporters::All())
        {
            if (d.section.View() != "ScriptedImporter")
                continue;
            for (const String& e : d.extensions)
                if (e == ext)
                    out.Push(d.name);
        }
        return out;
    }

    String AssetDatabase::DefaultImporterInternal(StringView path)
    {
        String ext = ExtensionOf(path);
        for (const ImporterDescription& d : AssetImporters::All())
        {
            if (d.section.View() == "ScriptedImporter")
                continue;
            for (const String& e : d.extensions)
                if (e == ext)
                    return d.name;
        }
        const AssetRecord* r = ProjectDatabase::ByPath(path);
        return String((r && r->folder ? AssetImporters::ForFolder() : AssetImporters::ForExtension("")).name);
    }

    engine::Object* AssetDatabase::LoadMainAssetAtPath(StringView assetPath)
    {
        const AssetRecord* r = ProjectDatabase::ByPath(assetPath);
        if (!r || r->folder)
            return nullptr;
        BeforeLoad(*r);
        return engine::LoadAsset(r->guid);
    }

    Array<engine::Object*> AssetDatabase::LoadAllAssetsAtPathInternal(StringView assetPath)
    {
        Array<engine::Object*> out;
        const AssetRecord* r = ProjectDatabase::ByPath(assetPath);
        if (r && !r->folder)
        {
            BeforeLoad(*r);
            engine::LoadAllAssets(r->guid, out);
        }
        return out;
    }

    String AssetDatabase::CreateFolder(StringView parentFolder, StringView newFolderName)
    {
        String made;
        if (!ProjectDatabase::CreateFolder(parentFolder, newFolderName, made).Empty())
            return String();
        return AssetPathToGUID(made.View());
    }

    String AssetDatabase::MoveAsset(StringView oldPath, StringView newPath)
    {
        // An AssetModificationProcessor may move it itself, or refuse.
        switch (AssetModificationNative::WillMove(oldPath, newPath))
        {
        case ModificationResult::Done:
            ProjectDatabase::Refresh();
            return String();
        case ModificationResult::Failed: return Format("moving '{}' was refused", oldPath);
        default: return ProjectDatabase::Move(oldPath, newPath);
        }
    }

    String AssetDatabase::ValidateMoveAsset(StringView oldPath, StringView newPath)
    {
        if (!ProjectDatabase::ByPath(oldPath))
            return Format("'{}' is no asset", oldPath);
        if (ProjectDatabase::ByPath(newPath))
            return Format("'{}' exists already", newPath);
        if (!IsValidFolder(ParentOf(newPath)))
            return Format("the folder of '{}' does not exist", newPath);
        return String();
    }

    String AssetDatabase::RenameAsset(StringView pathName, StringView newName)
    {
        const AssetRecord* r = ProjectDatabase::ByPath(pathName);
        if (!r)
            return Format("'{}' is no asset", pathName);
        if (newName.Empty() || newName.Find('/') != StringView::npos)
            return Format("'{}' is no name", newName);
        // The extension stays (a name without one keeps the asset's).
        String name(newName);
        StringView ext = platform::GetExtension(r->path.View());
        if (!r->folder && !ext.Empty() && platform::GetExtension(newName).Empty())
            name.Append(ext);
        StringView parent = ParentOf(r->path.View());
        String from = r->path;
        return MoveAsset(from.View(), Format("{}/{}", parent, name).View());
    }

    bool AssetDatabase::CopyAsset(StringView path, StringView newPath) { return ProjectDatabase::Copy(path, newPath).Empty(); }
    bool AssetDatabase::DeleteAsset(StringView path)
    {
        switch (AssetModificationNative::WillDelete(path))
        {
        case ModificationResult::Done:
            ProjectDatabase::Refresh();
            return true;
        case ModificationResult::Failed: return false;
        default:
        {
            String error = ProjectDatabase::Delete(path);
            if (!error.Empty())
                HX_LOG_WARNING(AssetLog, "DeleteAsset: {}.", error);
            return error.Empty();
        }
        }
    }

    Array<String> AssetDatabase::FindAssetsInternal(StringView filter, StringView folders)
    {
        // The filter's words: names (all must be in the name) and t:<type> (any of them).
        Array<String> words, types, roots, labels;
        usize at = 0;
        while (at < filter.Size())
        {
            while (at < filter.Size() && filter[at] == ' ')
                at++;
            usize end = at;
            while (end < filter.Size() && filter[end] != ' ')
                end++;
            if (end > at)
            {
                StringView w = filter.Substr(at, end - at);
                if (w.StartsWith("t:"))
                    types.Push(Lower(w.Substr(2)));
                else if (w.StartsWith("l:"))
                    labels.Push(Lower(w.Substr(2)));
                else
                    words.Push(Lower(w));
            }
            at = end;
        }
        at = 0;
        while (at < folders.Size())
        {
            usize end = folders.Find('\n', at);
            if (end == StringView::npos)
                end = folders.Size();
            StringView f = folders.Substr(at, end - at).Trimmed();
            if (!f.Empty())
                roots.Push(Lower(f));
            at = end + 1;
        }
        Array<String> out;
        for (const AssetRecord& r : ProjectDatabase::All())
        {
            String path = Lower(r.path.View());
            bool inFolder = roots.Empty();
            for (const String& root : roots)
                inFolder = inFolder || path.View().StartsWith(Format("{}/", root).View());
            if (!inFolder)
                continue;
            String name = Lower(NameOf(r.path.View()));
            bool match = true;
            for (const String& w : words)
                match = match && name.View().Find(w.View()) != StringView::npos;
            if (!types.Empty())
            {
                String type = Lower(TypeOf(r));
                bool any = false;
                for (const String& t : types)
                    any = any || type == t;
                match = match && any;
            }
            // l:<label>: any of them among the asset's labels.
            if (match && !labels.Empty())
            {
                MetaFile meta;
                bool any = false;
                if (ProjectDatabase::ReadMeta(r.path.View(), meta))
                    for (const String& l : meta.labels)
                        for (const String& want : labels)
                            any = any || Lower(l.View()) == want;
                match = any;
            }
            if (match)
                out.Push(r.guid.ToString());
        }
        return out;
    }

    String AssetDatabase::ImporterNameInternal(StringView path)
    {
        const AssetRecord* r = ProjectDatabase::ByPath(path);
        return r ? r->importer : String();
    }

    String AssetDatabase::ArtifactPathInternal(StringView path)
    {
        const AssetRecord* r = ProjectDatabase::ByPath(path);
        return r ? ArtifactStore::Current(*r) : String();
    }

    i32 AssetDatabase::ImportsPendingInternal() { return i32(ArtifactStore::Pending()); }
    i32 AssetDatabase::ArtifactsWrittenInternal() { return i32(ArtifactStore::Written()); }

    void AssetDatabase::StartAssetEditing() { ProjectDatabase::Defer(true); }
    void AssetDatabase::StopAssetEditing() { ProjectDatabase::Defer(false); }
    // The native assets with dirty objects written.
    void AssetDatabase::SaveAssets()
    {
        Array<engine::Object*> dirty;
        EditorUtility::DirtyObjects(dirty);
        HashMap<String, u8> seen;
        Array<String> paths;
        for (engine::Object* o : dirty)
        {
            const AssetRecord* r = RecordOf(o);
            if (!r || !IsNativePath(r->path.View()) || seen.Find(r->guid.ToString()))
                continue;
            seen.Set(r->guid.ToString(), u8(1));
            paths.Push(r->path);
        }
        // The AssetModificationProcessors pick the ones saved.
        AssetModificationNative::WillSave(paths);
        for (const String& p : paths)
            if (const AssetRecord* r = ProjectDatabase::ByPath(p.View()); r && IsNativePath(r->path.View()))
                WriteNative(*r);
    }
}
