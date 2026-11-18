#include <Helix/Editor/Assets/ProjectDatabase.h>

#include <Helix/Core/Containers/HashMap.h>
#include <Helix/Core/Logging/Log.h>
#include <Helix/Core/Strings/Format.h>
#include <Helix/Editor/Assets/ArtifactStore.h>
#include <Helix/Editor/Assets/AssetImporters.h>
#include <Helix/Editor/Assets/ImportNative.h>
#include <Helix/Platform/Threading/Thread.h>
#include <Helix/Engine/Asset/Asset.h>
#include <Helix/Platform/IO/FileSystem.h>
#include <Helix/Platform/Time/Time.h>

#include <cstring>

namespace helix::editor
{
    namespace
    {
        LogChannel AssetsLog{"Assets"};

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

        // A project path without its trailing slashes, the key of the path table.
        String Normal(StringView path)
        {
            while (!path.Empty() && (path[path.Size() - 1] == '/' || path[path.Size() - 1] == '\\'))
                path = path.Substr(0, path.Size() - 1);
            String out(path);
            for (usize i = 0; i < out.Size(); i++)
                if (out[i] == '\\')
                    out[i] = '/';
            return out;
        }

        // The engine's asset source of the project: its records' files, or their artifacts where current (read as
        // the artifacts' format, the engine's serialized assets).
        class ProjectAssetSource final : public engine::AssetSource
        {
        public:
            void List(Array<engine::AssetEntry>& out) override;
            bool Read(StringView path, Array<u8>& out, String& error) override
            {
                String artifact;
                {
                    platform::ScopedLock lock(mutex_);
                    if (const String* m = metaOverrides_.Find(String(path)))
                    {
                        out.Resize(m->Size());
                        if (m->Size())
                            std::memcpy(out.Data(), m->Data(), m->Size());
                        return true;
                    }
                    if (const String* a = artifacts_.Find(String(path)))
                        artifact = *a;
                }
                if (platform::ReadFile(artifact.Empty() ? ProjectDatabase::FullPath(path).View() : artifact.View(), out))
                    return true;
                error = Format("'{}' could not be read", path);
                return false;
            }

            platform::Mutex mutex_;
            HashMap<String, String> artifacts_;
            // By the .meta's path.
            HashMap<String, String> metaOverrides_;
        };

        struct State
        {
            String root;
            Array<AssetRecord> records;
            HashMap<String, u32> byGuid, byPath;
            ProjectAssetSource* source = nullptr;
            i32 deferred = 0;
            bool pending = false;
        };

        State& S()
        {
            static State* s = new State();
            return *s;
        }

        void Index(State& s)
        {
            s.byGuid.Clear();
            s.byPath.Clear();
            for (u32 i = 0; i < s.records.Size(); i++)
            {
                s.byGuid.Set(s.records[i].guid.ToString(), i);
                s.byPath.Set(Lower(s.records[i].path.View()), i);
            }
        }

        // The table as the last session left it (Library/SourceAssetDB.txt), so a project opened again reports only
        // what changed while it was closed. A line a record: guid folder size modified metaModified importer path.
        String SnapshotPath() { return platform::JoinPath(S().root, "Library/SourceAssetDB.txt"); }

        bool Number(StringView text, i64& out)
        {
            bool neg = text.StartsWith("-");
            StringView digits = neg ? text.Substr(1) : text;
            if (digits.Empty())
                return false;
            i64 n = 0;
            for (char c : digits)
            {
                if (c < '0' || c > '9')
                    return false;
                n = n * 10 + (c - '0');
            }
            out = neg ? -n : n;
            return true;
        }

        void LoadSnapshot(State& s)
        {
            Array<u8> bytes;
            if (!platform::ReadFile(SnapshotPath().View(), bytes))
                return;
            StringView text(reinterpret_cast<const char*>(bytes.Data()), bytes.Size());
            usize at = 0;
            while (at < text.Size())
            {
                usize end = text.Find('\n', at);
                if (end == StringView::npos)
                    end = text.Size();
                StringView line = text.Substr(at, end - at);
                at = end + 1;
                // Six fields, then the path (the rest of the line).
                StringView fields[6];
                usize p = 0;
                bool ok = true;
                for (StringView& f : fields)
                {
                    usize q = line.Find(' ', p);
                    if (q == StringView::npos)
                    {
                        ok = false;
                        break;
                    }
                    f = line.Substr(p, q - p);
                    p = q + 1;
                }
                AssetRecord r;
                if (!ok || p >= line.Size() || !Guid::TryParse(fields[0], r.guid) || !Number(fields[2], r.size) || !Number(fields[3], r.modified) ||
                    !Number(fields[4], r.metaModified))
                    continue;
                r.folder = fields[1] == "1";
                r.importer = fields[5] == "-" ? String() : String(fields[5]);
                r.path = String(line.Substr(p));
                s.records.Push(r);
            }
            Index(s);
        }

        void SaveSnapshot(const State& s)
        {
            String text;
            for (const AssetRecord& r : s.records)
                text.Append(Format("{} {} {} {} {} {} {}\n", r.guid.ToString(), r.folder ? 1 : 0, r.size, r.modified, r.metaModified,
                                   r.importer.Empty() ? StringView("-") : r.importer.View(), r.path));
            platform::CreateDirectories(platform::JoinPath(s.root, "Library").View());
            platform::WriteFile(SnapshotPath().View(), Span<const u8>(reinterpret_cast<const u8*>(text.Data()), text.Size()));
        }

        // The Resources path of a project path: lower case, no extension, below the last folder named Resources.
        String ResourcePath(StringView path)
        {
            usize at = StringView::npos;
            for (usize i = 0; i + 11 <= path.Size(); i++)
                if (path.Substr(i, 11) == "/Resources/" || (i == 0 && path.StartsWith("Resources/")))
                    at = i;
            if (at == StringView::npos)
                return String();
            StringView rest = path.Substr(path.Substr(at, 11) == "/Resources/" ? at + 11 : at + 10);
            usize dot = rest.FindLast('.');
            usize slash = rest.FindLast('/');
            if (dot != StringView::npos && (slash == StringView::npos || dot > slash))
                rest = rest.Substr(0, dot);
            return Lower(rest);
        }

        void ProjectAssetSource::List(Array<engine::AssetEntry>& out)
        {
            HashMap<String, String> artifacts;
            for (const AssetRecord& r : S().records)
            {
                if (r.folder)
                    continue;
                engine::AssetEntry e;
                e.guid = r.guid;
                e.path = r.path;
                e.resourcePath = ResourcePath(r.path.View());
                e.source = this;
                String artifact = ArtifactStore::Current(r);
                if (!artifact.Empty())
                {
                    e.loadAs = String("asset");
                    artifacts.Set(r.path, artifact);
                }
                out.Push(e);
            }
            platform::ScopedLock lock(mutex_);
            artifacts_ = Move(artifacts);
        }

        bool Hidden(StringView name)
        {
            return name.Empty() || name[0] == '.' || name[name.Size() - 1] == '~' || name == "cvs" || name == "CVS";
        }

        // The time of a .meta written by the walk itself: the next walk takes the file's time without a change.
        constexpr i64 kWrittenNow = -1;

        bool IsMeta(StringView name) { return name.Size() > 5 && name.Substr(name.Size() - 5) == ".meta"; }

        // One walk of the project's folders: the records as of now, the .meta files written and removed.
        struct Walk
        {
            Array<AssetRecord> records;
            HashMap<String, u32> seen;
            u32 written = 0, removed = 0;
        };

        struct Entry
        {
            String name;
            bool directory = false;
            i64 size = 0, modified = 0;
        };

        void ListEntries(StringView full, Array<Entry>& out)
        {
            platform::ListDirectory(full, [](const platform::DirectoryEntry& e, void* user) {
                Entry entry;
                entry.name = String(e.name);
                entry.directory = e.isDirectory;
                entry.size = e.size;
                entry.modified = e.modified;
                static_cast<Array<Entry>*>(user)->Push(entry);
            }, &out);
        }

        MetaFile NewMeta(bool folder, StringView name)
        {
            MetaFile m;
            m.guid = Guid::Generate();
            m.folder = folder;
            const ImporterDescription& d = folder ? AssetImporters::ForFolder() : AssetImporters::ForExtension(platform::GetExtension(name));
            m.importer = d.section.Empty() ? d.name : d.section;
            m.settings = d.settings;
            m.SetValue("userData", "");
            m.SetValue("assetBundleName", "");
            m.SetValue("assetBundleVariant", "");
            return m;
        }

        bool Write(StringView metaFull, const MetaFile& m)
        {
            String text = m.Write();
            return platform::WriteFile(metaFull, Span<const u8>(reinterpret_cast<const u8*>(text.Data()), text.Size()));
        }

        void WalkFolder(Walk& w, StringView relative)
        {
            String full = ProjectDatabase::FullPath(relative);
            Array<Entry> entries;
            ListEntries(full.View(), entries);
            // The .meta files of the folder by their asset's name.
            HashMap<String, u32> metas;
            for (u32 i = 0; i < entries.Size(); i++)
                if (!entries[i].directory && IsMeta(entries[i].name.View()))
                    metas.Set(String(entries[i].name.View().Substr(0, entries[i].name.Size() - 5)), i);
            HashMap<String, bool> assets;
            for (const Entry& e : entries)
            {
                if (IsMeta(e.name.View()) || Hidden(e.name.View()))
                    continue;
                assets.Set(e.name, true);
                String path = Format("{}/{}", relative, e.name);
                String metaFull = Format("{}/{}.meta", full, e.name);
                MetaFile meta;
                bool good = false;
                i64 metaModified = 0;
                if (const u32* at = metas.Find(e.name))
                {
                    Array<u8> bytes;
                    String error;
                    metaModified = entries[*at].modified;
                    good = platform::ReadFile(metaFull, bytes) &&
                           MetaFile::Parse(StringView(reinterpret_cast<const char*>(bytes.Data()), bytes.Size()), meta, error);
                    if (!good)
                        HX_LOG_WARNING(AssetsLog, "The .meta of '{}' is broken ({}): a new one is written.", path, error);
                }
                // A guid another asset has already (a file copied with its .meta): a new one.
                bool duplicate = good && w.seen.Find(meta.guid.ToString()) != nullptr;
                if (duplicate)
                {
                    HX_LOG_WARNING(AssetsLog, "'{}' has the guid of '{}': it gets a new one.", path, w.records[*w.seen.Find(meta.guid.ToString())].path);
                    meta.guid = Guid::Generate();
                    good = Write(metaFull.View(), meta);
                    w.written++;
                    metaModified = kWrittenNow;
                }
                if (!good)
                {
                    meta = NewMeta(e.directory, e.name.View());
                    if (Write(metaFull.View(), meta))
                        w.written++;
                    metaModified = kWrittenNow;
                }
                AssetRecord r;
                r.guid = meta.guid;
                r.path = path;
                r.folder = e.directory;
                r.size = e.size;
                r.modified = e.modified;
                r.metaModified = metaModified;
                r.importer = meta.importer;
                w.seen.Set(r.guid.ToString(), u32(w.records.Size()));
                w.records.Push(r);
                if (e.directory)
                    WalkFolder(w, path.View());
            }
            // .meta files whose assets went.
            for (const Entry& e : entries)
                if (!e.directory && IsMeta(e.name.View()) && !assets.Find(String(e.name.View().Substr(0, e.name.Size() - 5))))
                {
                    if (platform::RemoveFile(Format("{}/{}", full, e.name).View()))
                        w.removed++;
                }
        }

        bool CopyTree(StringView from, StringView to)
        {
            String src = ProjectDatabase::FullPath(from), dst = ProjectDatabase::FullPath(to);
            if (platform::DirectoryExists(src.View()))
            {
                if (!platform::CreateDirectories(dst.View()))
                    return false;
                Array<Entry> entries;
                ListEntries(src.View(), entries);
                for (const Entry& e : entries)
                    if (!IsMeta(e.name.View()) && !Hidden(e.name.View()) &&
                        !CopyTree(Format("{}/{}", from, e.name).View(), Format("{}/{}", to, e.name).View()))
                        return false;
                return true;
            }
            Array<u8> bytes;
            if (!platform::ReadFile(src.View(), bytes) || !platform::WriteFile(dst.View(), Span<const u8>(bytes.Data(), bytes.Size())))
                return false;
            // The copy's .meta: the original's settings with a new guid.
            MetaFile meta;
            if (ProjectDatabase::ReadMeta(from, meta))
            {
                meta.guid = Guid::Generate();
                ProjectDatabase::WriteMeta(to, meta);
            }
            return true;
        }

        // A file or folder the system holds for a moment (a virus scanner or the indexer reading a new file on
        // Windows) is tried again a few times.
        bool RemoveOne(StringView full, bool folder)
        {
            for (i32 attempt = 0; attempt < 10; attempt++)
            {
                if (folder ? platform::RemoveEmptyDirectory(full) : platform::RemoveFile(full))
                    return true;
                platform::SleepSeconds(0.025);
            }
            return false;
        }

        bool RemoveTree(StringView full)
        {
            if (platform::DirectoryExists(full))
            {
                Array<Entry> entries;
                ListEntries(full, entries);
                for (const Entry& e : entries)
                    if (!RemoveTree(Format("{}/{}", full, e.name).View()))
                        return false;
                return RemoveOne(full, true);
            }
            return RemoveOne(full, false);
        }

        String ParentOf(StringView path)
        {
            usize slash = path.FindLast('/');
            return slash == StringView::npos ? String() : String(path.Substr(0, slash));
        }
    }

    bool ProjectDatabase::Open(StringView root)
    {
        Close();
        State& s = S();
        s.root = Normal(root);
        platform::CreateDirectories(FullPath("Assets").View());
        LoadSnapshot(s);
        ArtifactStore::Open(s.root.View());
        s.source = new ProjectAssetSource();
        engine::AddAssetSource(s.source);
        return true;
    }

    void ProjectDatabase::Close()
    {
        State& s = S();
        if (s.source)
        {
            SaveSnapshot(s);
            engine::RemoveAssetSource(s.source);
            delete s.source;
            s.source = nullptr;
        }
        s.records.Clear();
        s.byGuid.Clear();
        s.byPath.Clear();
        s.root = String();
        ArtifactStore::Close();
    }

    bool ProjectDatabase::IsOpen() { return S().source != nullptr; }
    StringView ProjectDatabase::Root() { return S().root.View(); }

    String ProjectDatabase::FullPath(StringView path) { return path.Empty() ? S().root : Format("{}/{}", S().root, Normal(path)); }

    bool ProjectDatabase::IsProjectPath(StringView path)
    {
        String p = Normal(path);
        return p == "Assets" || p.View().StartsWith("Assets/") || p.View().StartsWith("Packages/");
    }

    AssetChanges ProjectDatabase::Refresh()
    {
        State& s = S();
        AssetChanges changes;
        if (!IsOpen())
            return changes;
        if (s.deferred > 0)
        {
            s.pending = true;
            return changes;
        }
        s.pending = false;
        Walk w;
        WalkFolder(w, "Assets");
        // Packages/<package>/...: the packages' folders only (Packages/ holds the manifest too).
        Array<Entry> packages;
        ListEntries(FullPath("Packages").View(), packages);
        for (const Entry& p : packages)
            if (p.directory && !Hidden(p.name.View()))
                WalkFolder(w, Format("Packages/{}", p.name).View());
        changes.metasWritten = w.written;
        changes.metasRemoved = w.removed;
        // What changed against the last walk, by guid.
        for (const AssetRecord& r : w.records)
        {
            const u32* was = s.byGuid.Find(r.guid.ToString());
            if (!was)
            {
                changes.imported.Push(r.path);
                continue;
            }
            const AssetRecord& old = s.records[*was];
            if (old.path != r.path)
            {
                changes.movedFrom.Push(old.path);
                changes.movedTo.Push(r.path);
            }
            bool metaChanged = old.metaModified != kWrittenNow && old.metaModified != r.metaModified;
            bool fileChanged = !r.folder && (old.size != r.size || old.modified != r.modified);
            if (metaChanged || fileChanged || (r.metaModified == kWrittenNow && old.metaModified != kWrittenNow))
                changes.imported.Push(r.path);
        }
        for (const AssetRecord& old : s.records)
            if (!w.seen.Find(old.guid.ToString()))
                changes.deleted.Push(old.path);
        s.records = helix::Move(w.records);
        Index(s);
        SaveSnapshot(s);
        // The artifacts of assets gone go with them.
        ArtifactStore::Prune();
        // Assets whose imports depend on a changed path are imported again with them.
        Array<String> touched;
        for (const Array<String>* list : {&changes.imported, &changes.deleted, &changes.movedFrom, &changes.movedTo})
            for (const String& p : *list)
                touched.Push(p);
        Array<Guid> dependents;
        Array<String> customs;
        ArtifactStore::TakeChangedCustoms(customs);
        if (!touched.Empty() || !customs.Empty())
            ArtifactStore::DependentsOf(Span<const String>(touched.Data(), touched.Size()), Span<const String>(customs.Data(), customs.Size()),
                                        dependents);
        for (const Guid& g : dependents)
        {
            const AssetRecord* r = ByGuid(g);
            bool listed = false;
            for (const String& p : changes.imported)
                listed |= r && p == r->path;
            if (r && !listed)
                changes.imported.Push(r->path);
        }
        // The engine's catalog anew (assets whose artifacts are no longer current load from their files).
        if (!changes.Empty())
            engine::RefreshAssets();
        // Loaded assets whose file or settings changed load again into their objects.
        for (const String& path : changes.imported)
            Reimport(path.View());
        // New and changed assets are imported (their artifacts written as their loads finish).
        for (const String& path : changes.imported)
            if (const AssetRecord* r = ByPath(path.View()))
                ArtifactStore::Queue(*r);
        if (!changes.Empty())
            ImportNative::PostprocessAll(changes);
        return changes;
    }

    bool ProjectDatabase::Reimport(StringView path)
    {
        const AssetRecord* r = ByPath(path);
        if (!r || r->folder || !engine::IsAssetLoaded(r->guid))
            return false;
        Guid guid = r->guid;
        Array<engine::Object*> loaded;
        engine::LoadAllAssets(guid, loaded);
        for (engine::Object* o : loaded)
            engine::UnloadAssetData(o);
        if (ArtifactStore::Current(*r).Empty())
            ImportNative::Preprocess(r->path.View());
        engine::LoadAsset(guid);
        return true;
    }

    void ProjectDatabase::Defer(bool on)
    {
        State& s = S();
        s.deferred += on ? 1 : (s.deferred > 0 ? -1 : 0);
        if (s.deferred == 0 && s.pending)
            Refresh();
    }

    const AssetRecord* ProjectDatabase::ByGuid(const Guid& guid)
    {
        const u32* at = S().byGuid.Find(guid.ToString());
        return at ? &S().records[*at] : nullptr;
    }

    const AssetRecord* ProjectDatabase::ByPath(StringView path)
    {
        const u32* at = S().byPath.Find(Lower(Normal(path).View()));
        return at ? &S().records[*at] : nullptr;
    }

    Span<const AssetRecord> ProjectDatabase::All() { return Span<const AssetRecord>(S().records.Data(), S().records.Size()); }

    bool ProjectDatabase::ReadMeta(StringView path, MetaFile& out)
    {
        Array<u8> bytes;
        String error;
        return platform::ReadFile(Format("{}.meta", FullPath(path)).View(), bytes) &&
               MetaFile::Parse(StringView(reinterpret_cast<const char*>(bytes.Data()), bytes.Size()), out, error);
    }

    bool ProjectDatabase::WriteMeta(StringView path, const MetaFile& meta) { return Write(Format("{}.meta", FullPath(path)).View(), meta); }

    void ProjectDatabase::SetMetaOverride(StringView path, StringView text)
    {
        if (!S().source)
            return;
        platform::ScopedLock lock(S().source->mutex_);
        S().source->metaOverrides_.Set(Format("{}.meta", Normal(path)), String(text));
    }

    void ProjectDatabase::ClearMetaOverride(StringView path)
    {
        if (!S().source)
            return;
        platform::ScopedLock lock(S().source->mutex_);
        S().source->metaOverrides_.Remove(Format("{}.meta", Normal(path)));
    }

    bool ProjectDatabase::IsProjectSource(const engine::AssetSource* source) { return source && source == S().source; }

    Guid ProjectDatabase::MetaFor(StringView path)
    {
        String p = Normal(path);
        MetaFile meta;
        if (ReadMeta(p.View(), meta))
            return meta.guid;
        meta = NewMeta(false, platform::GetFileName(p.View()));
        return WriteMeta(p.View(), meta) ? meta.guid : Guid();
    }

    void ProjectDatabase::NoteWritten(StringView path)
    {
        State& s = S();
        const u32* at = s.byPath.Find(Lower(Normal(path).View()));
        if (!at)
            return;
        AssetRecord& r = s.records[*at];
        String full = FullPath(r.path.View());
        struct Find
        {
            StringView name;
            AssetRecord* record;
        } f{platform::GetFileName(full.View()), &r};
        platform::ListDirectory(platform::GetDirectoryName(full.View()), [](const platform::DirectoryEntry& e, void* user) {
            auto* f = static_cast<Find*>(user);
            if (!e.isDirectory && e.name == f->name)
            {
                f->record->size = e.size;
                f->record->modified = e.modified;
            }
        }, &f);
    }

    String ProjectDatabase::UniquePath(StringView path)
    {
        String p = Normal(path);
        if (!platform::FileExists(FullPath(p.View()).View()) && !platform::DirectoryExists(FullPath(p.View()).View()))
            return p;
        StringView name = platform::GetFileName(p.View());
        StringView folder = p.View().Substr(0, p.Size() - name.Size());
        usize dot = name.FindLast('.');
        StringView stem = dot == StringView::npos || dot == 0 ? name : name.Substr(0, dot);
        StringView ext = dot == StringView::npos || dot == 0 ? StringView() : name.Substr(dot);
        // A name ending in " <n>" goes on from n.
        i32 n = 1;
        usize space = stem.FindLast(' ');
        if (space != StringView::npos && space + 1 < stem.Size())
        {
            i32 v = 0;
            bool digits = true;
            for (char c : stem.Substr(space + 1))
            {
                digits = digits && c >= '0' && c <= '9';
                v = v * 10 + (c - '0');
            }
            if (digits)
            {
                stem = stem.Substr(0, space);
                n = v + 1;
            }
        }
        for (;; n++)
        {
            String candidate = Format("{}{} {}{}", folder, stem, n, ext);
            String full = FullPath(candidate.View());
            if (!platform::FileExists(full.View()) && !platform::DirectoryExists(full.View()))
                return candidate;
        }
    }

    String ProjectDatabase::CreateFolder(StringView parent, StringView name, String& made)
    {
        String p = Normal(parent);
        if (!IsProjectPath(p.View()) || !platform::DirectoryExists(FullPath(p.View()).View()))
            return Format("'{}' is no folder of the project", parent);
        if (name.Empty() || name.Find('/') != StringView::npos || name.Find('\\') != StringView::npos)
            return Format("'{}' is no folder name", name);
        // A free name (" 1", " 2" ... after a taken one).
        String path = Format("{}/{}", p, name);
        for (i32 n = 1; platform::DirectoryExists(FullPath(path.View()).View()) || platform::FileExists(FullPath(path.View()).View()); n++)
            path = Format("{}/{} {}", p, name, n);
        if (!platform::CreateDirectories(FullPath(path.View()).View()))
            return Format("'{}' could not be made", path);
        MetaFile meta = NewMeta(true, name);
        WriteMeta(path.View(), meta);
        made = path;
        Refresh();
        return String();
    }

    String ProjectDatabase::Move(StringView from, StringView to)
    {
        String a = Normal(from), b = Normal(to);
        if (!IsProjectPath(a.View()) || !IsProjectPath(b.View()))
            return "only paths under Assets/ and Packages/ move";
        String fa = FullPath(a.View()), fb = FullPath(b.View());
        bool folder = platform::DirectoryExists(fa.View());
        if (!folder && !platform::FileExists(fa.View()))
            return Format("'{}' does not exist", a);
        if (platform::DirectoryExists(fb.View()) || platform::FileExists(fb.View()))
            return Format("'{}' exists already", b);
        if (!platform::DirectoryExists(FullPath(ParentOf(b.View()).View()).View()))
            return Format("the folder of '{}' does not exist", b);
        if (folder && b.View().StartsWith(Format("{}/", a).View()))
            return "a folder does not move into itself";
        if (!platform::RenameFile(fa.View(), fb.View()))
            return Format("'{}' could not be moved", a);
        platform::RenameFile(Format("{}.meta", fa).View(), Format("{}.meta", fb).View());
        Refresh();
        return String();
    }

    String ProjectDatabase::Copy(StringView from, StringView to)
    {
        String a = Normal(from), b = Normal(to);
        if (!IsProjectPath(a.View()) || !IsProjectPath(b.View()))
            return "only paths under Assets/ and Packages/ copy";
        String fa = FullPath(a.View()), fb = FullPath(b.View());
        if (!platform::DirectoryExists(fa.View()) && !platform::FileExists(fa.View()))
            return Format("'{}' does not exist", a);
        if (platform::DirectoryExists(fb.View()) || platform::FileExists(fb.View()))
            return Format("'{}' exists already", b);
        if (!CopyTree(a.View(), b.View()))
            return Format("'{}' could not be copied", a);
        Refresh();
        return String();
    }

    String ProjectDatabase::Delete(StringView path)
    {
        String p = Normal(path);
        if (!IsProjectPath(p.View()) || p == "Assets")
            return Format("'{}' is no asset to delete", path);
        String full = FullPath(p.View());
        if (!platform::DirectoryExists(full.View()) && !platform::FileExists(full.View()))
            return Format("'{}' does not exist", p);
        if (!RemoveTree(full.View()))
            return Format("'{}' could not be deleted", p);
        platform::RemoveFile(Format("{}.meta", full).View());
        Refresh();
        return String();
    }
}
