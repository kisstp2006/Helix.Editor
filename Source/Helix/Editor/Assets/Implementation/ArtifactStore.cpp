#include <Helix/Editor/Assets/ArtifactStore.h>

#include <Helix/Core/Containers/HashMap.h>
#include <Helix/Core/Hashing/Hash.h>
#include <Helix/Core/Logging/Log.h>
#include <Helix/Core/Strings/Format.h>
#include <Helix/Editor/Assets/AssetImporters.h>
#include <Helix/Editor/Assets/ImportNative.h>
#include <Helix/Editor/Assets/MetaFile.h>
#include <Helix/Editor/Assets/ProjectDatabase.h>
#include <Helix/Engine/Asset/Asset.h>
#include <Helix/Engine/Asset/AssetLoader.h>
#include <Helix/Engine/Async/AsyncOperation.h>
#include <Helix/Platform/IO/FileSystem.h>
#include <Helix/Platform/Time/Time.h>

namespace helix::editor
{
    namespace
    {
        LogChannel ArtifactLog{"Artifacts"};

        // A new format of the artifacts makes every key new.
        constexpr StringView kFormat = "helix-artifact-1";

        using Kind = ArtifactStore::Dependency;

        struct DependencyEntry
        {
            // A project path, or a custom dependency's name.
            String path, hash;
            Kind kind = Kind::Source;
        };

        char LetterOf(Kind k) { return k == Kind::Artifact ? 'A' : k == Kind::Custom ? 'C' : 'S'; }

        struct Known
        {
            // The key of what the import starts from, and the artifact's (that key with its dependencies' hashes).
            String key, artifact;
            i64 size = 0, modified = 0, metaModified = 0;
            Array<DependencyEntry> dependencies;
        };

        struct Import
        {
            Guid guid;
            String path, key;
            engine::ResourceRequest request;
        };

        struct State
        {
            String root;
            HashMap<String, Known> known;
            Array<Import> pending;
            // The dependencies of imports in progress, by guid.
            HashMap<String, Array<DependencyEntry>> recorded;
            // Custom dependencies' values, and the names changed since the last refresh.
            HashMap<String, String> customs;
            Array<String> changedCustoms;
            String postprocessors;
            u32 written = 0;
            bool open = false, imports = false, sweep = false;
            // Artifacts written as imports integrated (the database saved and the catalog listed by the next tick),
            // and the assets so imported (by guid; their queued imports are done).
            bool dirty = false;
            HashMap<String, u8> integrated;
        };

        State& S()
        {
            static State* s = new State();
            return *s;
        }

        void Hex(u64 v, char* out)
        {
            for (int i = 0; i < 16; i++)
                out[i] = "0123456789abcdef"[(v >> (60 - 4 * i)) & 15];
        }

        // What a dependency is now: its file's hash, its artifact's key or the custom value; "-" when missing.
        String HashOf(StringView path, Kind kind)
        {
            if (kind == Kind::Custom)
            {
                const String* v = S().customs.Find(String(path));
                return v ? *v : String("-");
            }
            if (kind == Kind::Artifact)
            {
                const AssetRecord* r = ProjectDatabase::ByPath(path);
                return r && !r->folder ? ArtifactStore::KeyOf(*r) : String("-");
            }
            Array<u8> bytes;
            if (!ProjectDatabase::IsProjectPath(path) || !platform::ReadFile(ProjectDatabase::FullPath(path), bytes))
                return String("-");
            char hex[16];
            Hex(Hash64(bytes.Data(), bytes.Size()), hex);
            return String(StringView(hex, 16));
        }

        // Paths in the database's lines: spaces and '%' escaped.
        String Escape(StringView path)
        {
            String out;
            for (char c : path)
            {
                if (c == ' ')
                    out.Append("%20");
                else if (c == '%')
                    out.Append("%25");
                else
                    out.Append(c);
            }
            return out;
        }

        String Unescape(StringView text)
        {
            String out;
            for (usize i = 0; i < text.Size(); i++)
            {
                if (text[i] == '%' && i + 3 <= text.Size() && text.Substr(i, 3) == "%20")
                {
                    out.Append(' ');
                    i += 2;
                }
                else if (text[i] == '%' && i + 3 <= text.Size() && text.Substr(i, 3) == "%25")
                {
                    out.Append('%');
                    i += 2;
                }
                else
                    out.Append(text[i]);
            }
            return out;
        }

        bool SamePath(StringView a, StringView b)
        {
            if (a.Size() != b.Size())
                return false;
            for (usize i = 0; i < a.Size(); i++)
            {
                char x = a[i] >= 'A' && a[i] <= 'Z' ? char(a[i] - 'A' + 'a') : a[i];
                char y = b[i] >= 'A' && b[i] <= 'Z' ? char(b[i] - 'A' + 'a') : b[i];
                if (x != y)
                    return false;
            }
            return true;
        }

        String DbPath() { return platform::JoinPath(S().root, "Library/ArtifactDB.txt"); }
        String FileOf(StringView key) { return platform::JoinPath(S().root, Format("Library/Artifacts/{}/{}", key.Substr(0, 2), key)); }

        void LoadDb()
        {
            Array<u8> bytes;
            if (!platform::ReadFile(DbPath(), bytes))
                return;
            StringView text(reinterpret_cast<const char*>(bytes.Data()), bytes.Size());
            usize at = 0;
            while (at < text.Size())
            {
                usize end = text.Find('\n', at);
                if (end == StringView::npos)
                    end = text.Size();
                StringView line = text.Substr(at, end - at).Trimmed();
                at = end + 1;
                // #custom <name> <value>
                if (line.StartsWith("#custom "))
                {
                    StringView rest = line.Substr(8);
                    usize space = rest.Find(' ');
                    if (space != StringView::npos)
                        S().customs.Set(Unescape(rest.Substr(0, space)), String(rest.Substr(space + 1)));
                    continue;
                }
                // guid key artifact size modified metaModified, then a dependency each: S:<hash>:<path> or
                // A:<hash>:<path>
                Array<StringView> parts;
                usize p = 0;
                while (p < line.Size())
                {
                    usize q = line.Find(' ', p);
                    if (q == StringView::npos)
                        q = line.Size();
                    parts.Push(line.Substr(p, q - p));
                    p = q + 1;
                }
                if (parts.Size() < 6)
                    continue;
                Known k;
                k.key = String(parts[1]);
                k.artifact = String(parts[2]);
                i64 v[3] = {};
                bool ok = true;
                for (usize i = 0; i < 3; i++)
                {
                    i64 n = 0;
                    bool neg = parts[3 + i].StartsWith("-");
                    StringView digits = neg ? parts[3 + i].Substr(1) : parts[3 + i];
                    for (char c : digits)
                    {
                        if (c < '0' || c > '9')
                            ok = false;
                        n = n * 10 + (c - '0');
                    }
                    v[i] = neg ? -n : n;
                }
                if (!ok)
                    continue;
                k.size = v[0];
                k.modified = v[1];
                k.metaModified = v[2];
                for (usize i = 6; i < parts.Size(); i++)
                {
                    StringView d = parts[i];
                    usize colon = d.Size() > 2 ? d.Find(':', 2) : StringView::npos;
                    if (colon == StringView::npos || d[1] != ':' || (d[0] != 'S' && d[0] != 'A' && d[0] != 'C'))
                    {
                        ok = false;
                        break;
                    }
                    DependencyEntry dep;
                    dep.kind = d[0] == 'A' ? Kind::Artifact : d[0] == 'C' ? Kind::Custom : Kind::Source;
                    dep.hash = String(d.Substr(2, colon - 2));
                    dep.path = Unescape(d.Substr(colon + 1));
                    k.dependencies.Push(dep);
                }
                if (!ok)
                    continue;
                S().known.Set(String(parts[0]), k);
            }
        }

        // An artifact's file goes once no entry names it (its folder too when empty).
        void RemoveArtifact(StringView artifact)
        {
            if (artifact.Empty())
                return;
            for (auto e : S().known)
                if (e.value.artifact == artifact)
                    return;
            String file = FileOf(artifact);
            platform::RemoveFile(file.View());
            platform::RemoveEmptyDirectory(platform::GetDirectoryName(file.View()));
        }

        void SaveDb()
        {
            String text;
            for (auto e : S().known)
            {
                text.Append(Format("{} {} {} {} {} {}", e.key, e.value.key, e.value.artifact, e.value.size, e.value.modified,
                                   e.value.metaModified));
                for (const DependencyEntry& d : e.value.dependencies)
                    text.Append(Format(" {}:{}:{}", LetterOf(d.kind), d.hash, Escape(d.path.View())));
                text.Append('\n');
            }
            for (auto c : S().customs)
                text.Append(Format("#custom {} {}\n", Escape(c.key.View()), c.value));
            platform::CreateDirectories(platform::JoinPath(S().root, "Library"));
            platform::WriteFile(DbPath(), Span<const u8>(reinterpret_cast<const u8*>(text.Data()), text.Size()));
        }

        // The artifact's key: the import's, and with dependencies their hashes too.
        String ArtifactKey(StringView key, const Array<DependencyEntry>& dependencies)
        {
            if (dependencies.Empty())
                return String(key);
            u64 h = Hash64(key.Data(), key.Size());
            for (const DependencyEntry& d : dependencies)
            {
                String part = Format("{}|{}|{}", LetterOf(d.kind), d.hash, d.path);
                h = Hash64(part.Data(), part.Size(), h);
            }
            char hex[32];
            for (usize i = 0; i < 16; i++)
                hex[i] = key[i];
            Hex(h, hex + 16);
            return String(StringView(hex, 32));
        }

        // The asset's artifact under the key, with its dependencies (null: those it had, the same artifact).
        void Remember(const AssetRecord& r, StringView key, Array<DependencyEntry>* dependencies)
        {
            Known k;
            if (dependencies)
            {
                k.dependencies = helix::Move(*dependencies);
                k.artifact = ArtifactKey(key, k.dependencies);
            }
            else if (Known* was = S().known.Find(r.guid.ToString()))
            {
                k.dependencies = was->dependencies;
                k.artifact = was->artifact;
            }
            k.key = String(key);
            k.size = r.size;
            k.modified = r.modified;
            k.metaModified = r.metaModified;
            S().known.Set(r.guid.ToString(), k);
        }
    }

    void ArtifactStore::Open(StringView projectRoot)
    {
        Close();
        State& s = S();
        s.root = String(projectRoot);
        s.open = true;
        s.sweep = true;
        LoadDb();
    }

    void ArtifactStore::SetImports(bool on)
    {
        bool was = S().imports;
        S().imports = on;
        // The engine's catalog anew: its entries load from current artifacts (or no longer).
        if (was != on && S().open)
            engine::RefreshAssets();
    }

    void ArtifactStore::Invalidate(const Guid& guid)
    {
        Known* k = S().known.Find(guid.ToString());
        if (!k)
            return;
        String artifact = k->artifact;
        S().known.Remove(guid.ToString());
        RemoveArtifact(artifact.View());
        SaveDb();
    }

    void ArtifactStore::Prune()
    {
        State& s = S();
        if (!s.open)
            return;
        Array<String> gone, artifacts;
        for (auto e : s.known)
        {
            Guid g;
            if (!Guid::TryParse(e.key.View(), g) || !ProjectDatabase::ByGuid(g))
            {
                gone.Push(e.key);
                artifacts.Push(e.value.artifact);
            }
        }
        for (const String& g : gone)
            s.known.Remove(g);
        for (const String& a : artifacts)
            RemoveArtifact(a.View());
        if (s.sweep)
        {
            s.sweep = false;
            // Library/Artifacts/<kk>/<key>: the keys no entry names (nor an import in progress).
            String top = platform::JoinPath(s.root, "Library/Artifacts");
            Array<String> folders;
            platform::ListDirectory(top.View(), [](const platform::DirectoryEntry& e, void* user) {
                if (e.isDirectory)
                    static_cast<Array<String>*>(user)->Push(String(e.name));
            }, &folders);
            HashMap<String, bool> named;
            for (auto e : s.known)
                named.Set(e.value.artifact, true);
            for (const String& f : folders)
            {
                Array<String> files;
                String folder = platform::JoinPath(top, f);
                platform::ListDirectory(folder.View(), [](const platform::DirectoryEntry& e, void* user) {
                    if (!e.isDirectory)
                        static_cast<Array<String>*>(user)->Push(String(e.name));
                }, &files);
                for (const String& name : files)
                    if (!named.Find(name))
                        platform::RemoveFile(platform::JoinPath(folder, name).View());
                platform::RemoveEmptyDirectory(folder.View());
            }
        }
        if (!gone.Empty())
            SaveDb();
    }

    void ArtifactStore::Close()
    {
        State& s = S();
        s.known.Clear();
        s.pending.Clear();
        s.recorded.Clear();
        s.integrated.Clear();
        s.written = 0;
        s.open = false;
    }

    String ArtifactStore::KeyOf(const AssetRecord& r)
    {
        Array<u8> file, meta;
        platform::ReadFile(ProjectDatabase::FullPath(r.path.View()), file);
        platform::ReadFile(Format("{}.meta", ProjectDatabase::FullPath(r.path.View())).View(), meta);
        const ImporterDescription& d = AssetImporters::ForExtension(platform::GetExtension(r.path.View()));
        String head = Format("{}|{}|{}|{}|{}|{}", kFormat, r.guid.ToString(), r.importer, d.name, d.version, S().postprocessors);
        u64 h = Hash64(head.Data(), head.Size());
        // The .meta without its labels (they change no import).
        MetaFile parsed;
        String ignored;
        if (MetaFile::Parse(StringView(reinterpret_cast<const char*>(meta.Data()), meta.Size()), parsed, ignored) && !parsed.labels.Empty())
        {
            parsed.labels.Clear();
            String text = parsed.Write();
            h = Hash64(text.Data(), text.Size(), h);
        }
        else
            h = Hash64(meta.Data(), meta.Size(), h);
        h = Hash64(file.Data(), file.Size(), h);
        // Two halves of the guid's hash beside it: 32 hex digits.
        u64 g = Hash64(r.guid.ToString().Data(), 32, h);
        char hex[32];
        Hex(h, hex);
        Hex(g, hex + 16);
        return String(StringView(hex, 32));
    }

    String ArtifactStore::Current(const AssetRecord& r)
    {
        State& s = S();
        if (!s.open || !s.imports || r.folder)
            return String();
        Known* k = s.known.Find(r.guid.ToString());
        if (!k)
            return String();
        // The times as they were: the key holds; else it is computed again (a .meta rewritten the same, a file touched).
        if (k->size != r.size || k->modified != r.modified || k->metaModified != r.metaModified)
        {
            String key = KeyOf(r);
            if (key != k->key)
                return String();
            Remember(r, key.View(), nullptr);
            SaveDb();
            k = s.known.Find(r.guid.ToString());
        }
        for (const DependencyEntry& d : k->dependencies)
            if (HashOf(d.path.View(), d.kind) != d.hash)
                return String();
        String file = FileOf(k->artifact.View());
        return platform::FileExists(file.View()) ? file : String();
    }

    void ArtifactStore::Queue(const AssetRecord& r)
    {
        State& s = S();
        if (!s.open || !s.imports || r.folder)
            return;
        for (const Import& i : s.pending)
            if (i.guid == r.guid)
                return;
        if (!Current(r).Empty())
            return;
        // No loader makes the asset's objects (a kind the engine does not load yet): it stays as its file.
        String ext(platform::GetExtension(r.path.View()));
        if (ext.View().StartsWith("."))
            ext = String(ext.View().Substr(1));
        for (usize n = 0; n < ext.Size(); n++)
            if (ext[n] >= 'A' && ext[n] <= 'Z')
                ext[n] = char(ext[n] - 'A' + 'a');
        engine::AssetLoader loader;
        if (!engine::FindAssetLoader(ext.View(), loader))
            return;
        // The engine's serialized files (native assets) are in the artifacts' format already: they load as they are.
        if (loader.name == engine::SerializedAssetLoader().name)
            return;
        Import i;
        i.guid = r.guid;
        i.path = r.path;
        i.key = KeyOf(r);
        s.integrated.Remove(r.guid.ToString());
        // A loaded asset is imported again by its reimport (preprocessed there).
        if (!engine::IsAssetLoaded(r.guid))
            ImportNative::Preprocess(r.path.View());
        i.request = engine::LoadAssetAsync(r.guid);
        s.pending.Push(i);
    }

    void ArtifactStore::Integrated(const engine::AssetObjects& objects)
    {
        State& s = S();
        const AssetRecord* r = ProjectDatabase::ByGuid(objects.guid());
        if (!s.open || !s.imports || !r || r->folder || objects.objects.Empty())
            return;
        Array<u8> bytes;
        String error;
        if (!engine::WriteAssetObjects(objects, bytes, error, true))
        {
            HX_LOG_WARNING(ArtifactLog, "'{}' was imported but its artifact could not be written: {}", r->path, error);
            return;
        }
        Array<DependencyEntry> dependencies;
        if (Array<DependencyEntry>* d = s.recorded.Find(r->guid.ToString()))
            dependencies = helix::Move(*d);
        s.recorded.Remove(r->guid.ToString());
        String key = KeyOf(*r);
        String file = FileOf(ArtifactKey(key.View(), dependencies).View());
        platform::CreateDirectories(platform::GetDirectoryName(file.View()));
        if (!platform::WriteFile(file.View(), Span<const u8>(bytes.Data(), bytes.Size())))
            return;
        String before;
        if (const Known* k = s.known.Find(r->guid.ToString()))
            before = k->artifact;
        Remember(*r, key.View(), &dependencies);
        if (before != s.known.Find(r->guid.ToString())->artifact)
            RemoveArtifact(before.View());
        s.written++;
        s.dirty = true;
        s.integrated.Set(r->guid.ToString(), u8(1));
    }

    u32 ArtifactStore::Tick()
    {
        State& s = S();
        u32 done = 0;
        for (usize n = 0; n < s.pending.Size();)
        {
            Import& i = s.pending[n];
            if (i.request.IsValid() && !i.request.isDone())
            {
                n++;
                continue;
            }
            // Written as it integrated (the usual way); else from its loaded objects.
            if (s.integrated.Remove(i.guid.ToString()))
            {
                s.pending.RemoveAt(n);
                done++;
                continue;
            }
            Array<engine::Object*> objects;
            engine::LoadAllAssets(i.guid, objects);
            Array<u8> bytes;
            String error;
            const AssetRecord* r = ProjectDatabase::ByGuid(i.guid);
            if (!objects.Empty() && r && engine::WriteAssetFile(Span<engine::Object* const>(objects.Data(), objects.Size()), bytes, error, true))
            {
                Array<DependencyEntry> dependencies;
                if (Array<DependencyEntry>* d = s.recorded.Find(i.guid.ToString()))
                    dependencies = helix::Move(*d);
                String file = FileOf(ArtifactKey(i.key.View(), dependencies).View());
                platform::CreateDirectories(platform::GetDirectoryName(file.View()));
                if (platform::WriteFile(file.View(), Span<const u8>(bytes.Data(), bytes.Size())))
                {
                    String before;
                    if (const Known* was = s.known.Find(i.guid.ToString()))
                        before = was->artifact;
                    Remember(*r, i.key.View(), &dependencies);
                    // The artifact it replaces (another key) is no longer needed.
                    if (before != s.known.Find(i.guid.ToString())->artifact)
                        RemoveArtifact(before.View());
                    s.written++;
                }
            }
            else if (!objects.Empty() && r)
                HX_LOG_WARNING(ArtifactLog, "'{}' was imported but its artifact could not be written: {}", i.path, error);
            s.recorded.Remove(i.guid.ToString());
            s.pending.RemoveAt(n);
            done++;
        }
        if (done || s.dirty)
        {
            s.dirty = false;
            SaveDb();
            // The imported assets load from their artifacts from now on.
            engine::RefreshAssets();
        }
        return done;
    }

    void ArtifactStore::ImportAll()
    {
        // The loads finish on the main thread (their objects are made there).
        while (!S().pending.Empty())
        {
            engine::ProcessAssetLoads();
            if (Tick() == 0)
                platform::SleepSeconds(0.001);
        }
    }

    void ArtifactStore::BeginImport(const Guid& guid) { S().recorded.Remove(guid.ToString()); }

    void ArtifactStore::AddDependency(const Guid& guid, StringView name, Dependency kind)
    {
        Array<DependencyEntry>& list = S().recorded[guid.ToString()];
        for (const DependencyEntry& d : list)
            if (d.kind == kind && (kind == Kind::Custom ? d.path.View() == name : SamePath(d.path.View(), name)))
                return;
        DependencyEntry d;
        d.path = String(name);
        d.kind = kind;
        d.hash = HashOf(name, kind);
        list.Push(d);
    }

    void ArtifactStore::DependentsOf(Span<const String> paths, Span<const String> customs, Array<Guid>& out)
    {
        for (auto e : S().known)
        {
            bool hit = false;
            for (const DependencyEntry& d : e.value.dependencies)
            {
                if (d.kind == Kind::Custom)
                    for (const String& c : customs)
                        hit |= d.path == c;
                else
                    for (const String& p : paths)
                        hit |= SamePath(d.path.View(), p.View());
            }
            Guid g;
            if (hit && Guid::TryParse(e.key.View(), g))
                out.Push(g);
        }
    }

    void ArtifactStore::DependenciesOf(const Guid& guid, Array<String>& out)
    {
        const Known* k = S().known.Find(guid.ToString());
        if (!k)
            return;
        for (const DependencyEntry& d : k->dependencies)
            if (d.kind != Kind::Custom)
                out.Push(d.path);
    }

    String ArtifactStore::DependencyHash(const AssetRecord& r)
    {
        if (!Current(r).Empty())
            return S().known.Find(r.guid.ToString())->artifact;
        return r.folder ? String() : KeyOf(r);
    }

    void ArtifactStore::RegisterCustom(StringView name, StringView value)
    {
        State& s = S();
        String* was = s.customs.Find(String(name));
        if (was && *was == value)
            return;
        s.customs.Set(String(name), String(value));
        s.changedCustoms.Push(String(name));
        if (s.open)
            SaveDb();
    }

    u32 ArtifactStore::UnregisterCustomPrefix(StringView prefix)
    {
        State& s = S();
        Array<String> gone;
        for (auto c : s.customs)
            if (c.key.View().StartsWith(prefix))
                gone.Push(c.key);
        for (const String& g : gone)
        {
            s.customs.Remove(g);
            s.changedCustoms.Push(g);
        }
        if (!gone.Empty() && s.open)
            SaveDb();
        return u32(gone.Size());
    }

    void ArtifactStore::TakeChangedCustoms(Array<String>& out)
    {
        out = helix::Move(S().changedCustoms);
        S().changedCustoms = Array<String>();
    }

    void ArtifactStore::SetPostprocessors(StringView text) { S().postprocessors = String(text); }

    u32 ArtifactStore::Pending() { return u32(S().pending.Size()); }

    bool ArtifactStore::IsPending(const Guid& guid)
    {
        for (const Import& i : S().pending)
            if (i.guid == guid)
                return true;
        return false;
    }
    u32 ArtifactStore::Written() { return S().written; }
}
