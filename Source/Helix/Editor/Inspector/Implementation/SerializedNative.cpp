#include <Helix/Editor/Inspector/SerializedNative.h>

#include <Helix/Core/Containers/HashMap.h>
#include <Helix/Core/Logging/Log.h>
#include <Helix/Core/Strings/Format.h>
#include <Helix/Editor/Application/EditorUtility.h>
#include <Helix/Editor/Scenes/EditorSceneManager.h>
#include <Helix/Engine/Asset/Asset.h>
#include <Helix/Engine/GameObject/GameObject.h>
#include <Helix/Engine/Object/Class.h>
#include <Helix/Serialization/SerializedFile/SerializedFile.h>

#include <charconv>
#include <cstring>

namespace helix::editor
{
    namespace
    {
        LogChannel SerializedLog{"Serialized"};

        namespace ser = serialization;

        constexpr i32 kReferenceKind = 100;
        constexpr i32 kArraySizeKind = 101;
        // Arrays of numbers longer than this show their size, not their items.
        constexpr u32 kMaxShownItems = 4096;

        struct Prop
        {
            String path, name, typeName;
            i32 kind = 0, depth = 0, end = 0, arraySize = -1;
            i64 l = 0;
            f64 d = 0;
            String s;
            ser::AssetReference ref;
            bool different = false;
        };

        struct Mod
        {
            String path, text;
            ser::AssetReference ref;
        };

        struct Table
        {
            Array<engine::EntityId> targets;
            Array<Prop> props;
            Array<Mod> mods;
        };

        struct Tables
        {
            HashMap<u64, Table*> open;
            u64 next = 1;
        };

        Tables& T()
        {
            static Tables* t = new Tables();
            return *t;
        }

        Table* Of(u64 handle)
        {
            Table** t = T().open.Find(handle);
            return t ? *t : nullptr;
        }

        Prop* PropOf(u64 handle, i32 index)
        {
            Table* t = Of(handle);
            return t && index >= 0 && usize(index) < t->props.Size() ? &t->props[usize(index)] : nullptr;
        }

        // References as the editor sees them: assets by guid and fileID, objects of scenes by their ids.
        class EditorReferences final : public ser::ReferenceTable
        {
        public:
            ser::AssetReference ToFile(u64 object) override
            {
                ser::AssetReference r;
                engine::Object* o = engine::EntityIdToObject(engine::EntityId::FromULong(object));
                if (!o)
                    return r;
                Guid g;
                i64 f = 0;
                i32 type = 0;
                if (engine::GetAssetReference(o, g, f, type))
                {
                    r.fileID = f;
                    r.guid = g;
                    r.type = type;
                    return r;
                }
                r.fileID = i64(object);
                return r;
            }

            u64 FromFile(const ser::AssetReference& r) override
            {
                if (r.guid.IsValid())
                {
                    engine::Object* o = engine::LoadAsset(r.guid, r.fileID);
                    return o ? engine::EntityId::ToULong(o->GetEntityId()) : 0;
                }
                return u64(r.fileID);
            }
        };

        // The binary form read by its type tree into properties.
        struct Walker
        {
            const ser::TypeTree& tree;
            Span<const u8> data;
            Array<Prop>& out;
            usize pos = 0;
            bool ok = true;

            template <class V>
            V Read()
            {
                V v{};
                if (pos + sizeof(V) <= data.Size())
                    std::memcpy(&v, data.Data() + pos, sizeof(V));
                else
                    ok = false;
                pos += sizeof(V);
                return v;
            }

            void Align() { pos = (pos + 3) & ~usize(3); }

            // A PPtr: fileID, guid and type.
            bool IsReference(u32 n) const
            {
                if (tree.nodes[n].kind != ser::ValueKind::Struct)
                    return false;
                u32 end = tree.Next(n);
                return end == n + 4 && tree.FieldName(n + 1) == "fileID" && tree.nodes[n + 1].kind == ser::ValueKind::Int64 &&
                       tree.FieldName(n + 2) == "guid" && tree.nodes[n + 2].kind == ser::ValueKind::Guid && tree.FieldName(n + 3) == "type";
            }

            void Number(Prop& p, ser::ValueKind k)
            {
                switch (k)
                {
                case ser::ValueKind::Bool: p.l = Read<u8>() ? 1 : 0; break;
                case ser::ValueKind::Int8: p.l = Read<i8>(); break;
                case ser::ValueKind::UInt8: p.l = Read<u8>(); break;
                case ser::ValueKind::Int16: p.l = Read<i16>(); break;
                case ser::ValueKind::UInt16: p.l = Read<u16>(); break;
                case ser::ValueKind::Int32: p.l = Read<i32>(); break;
                case ser::ValueKind::UInt32: p.l = Read<u32>(); break;
                case ser::ValueKind::Int64: p.l = Read<i64>(); break;
                case ser::ValueKind::UInt64: p.l = i64(Read<u64>()); break;
                case ser::ValueKind::Float: p.d = Read<f32>(); break;
                case ser::ValueKind::Double: p.d = Read<f64>(); break;
                default: break;
                }
                if (k == ser::ValueKind::Float || k == ser::ValueKind::Double)
                    p.l = i64(p.d);
                else
                    p.d = f64(p.l);
            }

            void Node(u32 n, StringView path, StringView name, i32 depth)
            {
                if (!ok)
                    return;
                const ser::TypeTreeNode& node = tree.nodes[n];
                usize at = out.Size();
                {
                    Prop p;
                    p.path = String(path);
                    p.name = String(name);
                    p.typeName = String(tree.TypeName(n));
                    p.kind = i32(node.kind);
                    p.depth = depth;
                    out.Push(p);
                }
                switch (node.kind)
                {
                case ser::ValueKind::String:
                {
                    u32 c = Read<u32>();
                    if (pos + c <= data.Size())
                        out[at].s = String(StringView(reinterpret_cast<const char*>(data.Data() + pos), c));
                    else
                        ok = false;
                    pos += c;
                    Align();
                    break;
                }
                case ser::ValueKind::Guid:
                {
                    Guid g;
                    if (pos + 16 <= data.Size())
                        std::memcpy(g.bytes, data.Data() + pos, 16);
                    pos += 16;
                    out[at].s = g.ToString();
                    break;
                }
                case ser::ValueKind::Array:
                case ser::ValueKind::FixedArray:
                {
                    bool fixed = node.kind == ser::ValueKind::FixedArray;
                    u32 count = fixed ? node.count : Read<u32>();
                    out[at].arraySize = i32(count);
                    if (!fixed)
                    {
                        Prop size;
                        size.path = Format("{}.Array.size", path);
                        size.name = String("size");
                        size.typeName = String("int");
                        size.kind = kArraySizeKind;
                        size.depth = depth + 1;
                        size.l = count;
                        size.d = count;
                        size.end = i32(out.Size() + 1);
                        out.Push(size);
                    }
                    // A long array of numbers (an image's bytes, a mesh's vertices) is its size only: no item each.
                    const ser::TypeTreeNode& item = tree.nodes[n + 1];
                    if (count > kMaxShownItems && item.byteSize > 0 && item.kind != ser::ValueKind::Struct && item.kind != ser::ValueKind::String)
                        pos += usize(count) * usize(item.byteSize);
                    else
                        for (u32 i = 0; i < count && ok; i++)
                            Node(n + 1, Format("{}.Array.data[{}]", path, i).View(), "data", depth + 1);
                    if (!fixed)
                        Align();
                    break;
                }
                case ser::ValueKind::Struct:
                    if (IsReference(n))
                    {
                        Prop& p = out[at];
                        p.kind = kReferenceKind;
                        p.ref.fileID = Read<i64>();
                        if (pos + 16 <= data.Size())
                            std::memcpy(p.ref.guid.bytes, data.Data() + pos, 16);
                        pos += 16;
                        p.ref.type = Read<i32>();
                        break;
                    }
                    for (u32 c = n + 1; c < tree.Next(n) && ok; c = tree.Next(c))
                        Node(c, Format("{}.{}", path, tree.FieldName(c)).View(), tree.FieldName(c), depth + 1);
                    break;
                default: Number(out[at], node.kind); break;
                }
                out[at].end = i32(out.Size());
            }
        };

        // An object's properties as its class writes them now.
        bool Read(engine::Object* o, Array<Prop>& out)
        {
            out.Clear();
            const engine::ClassInfo& c = o->Class();
            if (!c.writeBinary)
                return false;
            EditorReferences refs;
            ser::SerializedFileWriter w(true);
            c.writeBinary(*o, w, 1, &refs);
            Array<u8> bytes;
            String error;
            if (!w.Finish(bytes, error))
            {
                HX_LOG_ERROR(SerializedLog, "'{}' could not be written: {}", o->Name(), error);
                return false;
            }
            ser::SerializedFile file;
            if (!file.Open(Span<const u8>(bytes.Data(), bytes.Size()), error) || file.Objects().Empty() || !file.HasTypeTrees())
            {
                HX_LOG_ERROR(SerializedLog, "'{}' could not be read back: {}", o->Name(), error);
                return false;
            }
            const ser::TypeTree& tree = file.Types()[file.Objects()[0].type].tree;
            if (tree.nodes.Empty())
                return false;
            Walker w2{tree, file.Data(0), out};
            for (u32 n = 1; n < tree.nodes.Size() && w2.ok; n = tree.Next(n))
                w2.Node(n, tree.FieldName(n), tree.FieldName(n), 0);
            return w2.ok;
        }

        bool SameValue(const Prop& a, const Prop& b)
        {
            return a.path == b.path && a.kind == b.kind && a.l == b.l && a.d == b.d && a.s == b.s && a.ref == b.ref && a.arraySize == b.arraySize;
        }

        void Snapshot(Table& t)
        {
            t.props.Clear();
            engine::Object* first = t.targets.Empty() ? nullptr : engine::EntityIdToObject(t.targets[0]);
            if (!first || !Read(first, t.props))
                return;
            Array<Prop> other;
            for (usize i = 1; i < t.targets.Size(); i++)
            {
                engine::Object* o = engine::EntityIdToObject(t.targets[i]);
                if (!o || !Read(o, other))
                    continue;
                // By path: a property another object lacks or holds otherwise differs.
                HashMap<String, u32> byPath;
                for (u32 k = 0; k < other.Size(); k++)
                    byPath.Set(other[k].path, k);
                for (Prop& p : t.props)
                {
                    const u32* k = byPath.Find(p.path);
                    if (!k || !SameValue(p, other[*k]))
                        p.different = true;
                }
            }
            // A property differs where anything below it does.
            for (usize i = t.props.Size(); i-- > 0;)
                for (i32 c = i32(i) + 1; c < t.props[i].end; c++)
                    if (t.props[usize(c)].different)
                    {
                        t.props[i].different = true;
                        break;
                    }
        }

        // The objects changed: their scenes, or the assets marked dirty.
        void Changed(engine::Object* o)
        {
            engine::GameObject* go = o->Is<engine::GameObject>() ? static_cast<engine::GameObject*>(o)
                                     : o->Is<engine::Component>() ? static_cast<engine::Component*>(o)->gameObject()
                                                                   : nullptr;
            Guid g;
            i64 f = 0;
            i32 type = 0;
            if (go && !engine::GetAssetReference(go, g, f, type) && go->scene().IsValid())
                EditorSceneManager::MarkSceneDirty(go->scene());
            else
                EditorUtility::SetDirty(o);
        }

        bool ApplyMods(Table& t)
        {
            if (t.mods.Empty())
                return false;
            EditorReferences refs;
            for (engine::EntityId id : t.targets)
            {
                engine::Object* o = engine::EntityIdToObject(id);
                if (!o || !o->Class().applyProperty)
                    continue;
                for (const Mod& m : t.mods)
                {
                    String error;
                    if (!o->Class().applyProperty(*o, m.path.View(), m.text.View(), m.ref, &refs, error))
                        HX_LOG_ERROR(SerializedLog, "'{}' of '{}' could not be set: {}", m.path, o->Name(), error);
                }
                Changed(o);
            }
            t.mods.Clear();
            Snapshot(t);
            return true;
        }

        void AddMod(Table& t, StringView path, StringView text, const ser::AssetReference& ref)
        {
            for (Mod& m : t.mods)
                if (m.path.View() == path)
                {
                    m.text = String(text);
                    m.ref = ref;
                    return;
                }
            Mod m;
            m.path = String(path);
            m.text = String(text);
            m.ref = ref;
            t.mods.Push(m);
        }
    }

    u64 SerializedNative::Open(Span<engine::Object* const> targets)
    {
        if (targets.Empty() || !targets[0])
            return 0;
        u32 klass = targets[0]->ClassIndex();
        auto* t = new Table();
        for (engine::Object* o : targets)
        {
            if (!o || o->ClassIndex() != klass)
            {
                delete t;
                return 0;
            }
            t->targets.Push(o->GetEntityId());
        }
        Snapshot(*t);
        u64 handle = T().next++;
        T().open.Set(handle, t);
        return handle;
    }

    void SerializedNative::Close(u64 handle)
    {
        if (Table* t = Of(handle))
        {
            delete t;
            T().open.Remove(handle);
        }
    }

    void SerializedNative::Update(u64 handle)
    {
        if (Table* t = Of(handle))
        {
            t->mods.Clear();
            Snapshot(*t);
        }
    }

    bool SerializedNative::Apply(u64 handle)
    {
        Table* t = Of(handle);
        return t && ApplyMods(*t);
    }

    bool SerializedNative::HasModified(u64 handle)
    {
        Table* t = Of(handle);
        return t && !t->mods.Empty();
    }

    i32 SerializedNative::TargetCount(u64 handle)
    {
        Table* t = Of(handle);
        return t ? i32(t->targets.Size()) : 0;
    }

    engine::Object* SerializedNative::Target(u64 handle, i32 index)
    {
        Table* t = Of(handle);
        return t && index >= 0 && usize(index) < t->targets.Size() ? engine::EntityIdToObject(t->targets[usize(index)]) : nullptr;
    }

    i32 SerializedNative::Count(u64 handle)
    {
        Table* t = Of(handle);
        return t ? i32(t->props.Size()) : 0;
    }

    i32 SerializedNative::Find(u64 handle, StringView path)
    {
        Table* t = Of(handle);
        if (!t)
            return -1;
        for (usize i = 0; i < t->props.Size(); i++)
            if (t->props[i].path.View() == path)
                return i32(i);
        return -1;
    }

    String SerializedNative::Path(u64 handle, i32 index)
    {
        Prop* p = PropOf(handle, index);
        return p ? p->path : String();
    }

    String SerializedNative::Name(u64 handle, i32 index)
    {
        Prop* p = PropOf(handle, index);
        return p ? p->name : String();
    }

    String SerializedNative::TypeName(u64 handle, i32 index)
    {
        Prop* p = PropOf(handle, index);
        return p ? p->typeName : String();
    }

    i32 SerializedNative::Kind(u64 handle, i32 index)
    {
        Prop* p = PropOf(handle, index);
        return p ? p->kind : -1;
    }

    i32 SerializedNative::Depth(u64 handle, i32 index)
    {
        Prop* p = PropOf(handle, index);
        return p ? p->depth : -1;
    }

    i32 SerializedNative::End(u64 handle, i32 index)
    {
        Prop* p = PropOf(handle, index);
        return p ? p->end : -1;
    }

    i32 SerializedNative::ArraySize(u64 handle, i32 index)
    {
        Prop* p = PropOf(handle, index);
        return p ? p->arraySize : -1;
    }

    bool SerializedNative::Different(u64 handle, i32 index)
    {
        Prop* p = PropOf(handle, index);
        return p && p->different;
    }

    i64 SerializedNative::LongValue(u64 handle, i32 index)
    {
        Prop* p = PropOf(handle, index);
        return p ? p->l : 0;
    }

    f64 SerializedNative::DoubleValue(u64 handle, i32 index)
    {
        Prop* p = PropOf(handle, index);
        return p ? p->d : 0;
    }

    String SerializedNative::StringValue(u64 handle, i32 index)
    {
        Prop* p = PropOf(handle, index);
        return p ? p->s : String();
    }

    engine::Object* SerializedNative::ObjectValue(u64 handle, i32 index)
    {
        Prop* p = PropOf(handle, index);
        if (!p || p->kind != kReferenceKind)
            return nullptr;
        EditorReferences refs;
        return engine::EntityIdToObject(engine::EntityId::FromULong(refs.FromFile(p->ref)));
    }

    i64 SerializedNative::ReferenceFileId(u64 handle, i32 index)
    {
        Prop* p = PropOf(handle, index);
        return p ? p->ref.fileID : 0;
    }

    String SerializedNative::ReferenceGuid(u64 handle, i32 index)
    {
        Prop* p = PropOf(handle, index);
        return p && p->ref.guid.IsValid() ? p->ref.guid.ToString() : String();
    }

    bool SerializedNative::SetText(u64 handle, i32 index, StringView text)
    {
        Table* t = Of(handle);
        Prop* p = PropOf(handle, index);
        if (!t || !p || p->kind == kReferenceKind || p->kind == i32(ser::ValueKind::Struct) || p->kind == i32(ser::ValueKind::Array) ||
            p->kind == i32(ser::ValueKind::FixedArray))
            return false;
        if (p->kind == kArraySizeKind)
        {
            i32 size = 0;
            std::from_chars(text.Data(), text.Data() + text.Size(), size);
            // The array's own property is the one before its size.
            return SetArraySize(handle, index - 1, size);
        }
        bool isText = p->kind == i32(ser::ValueKind::String) || p->kind == i32(ser::ValueKind::Guid);
        // The value it has already is no change (a drawer writes its fields back each time it draws them).
        if (!p->different)
        {
            if (isText && p->s.View() == text)
                return true;
            if (!isText)
            {
                bool real = p->kind == i32(ser::ValueKind::Float) || p->kind == i32(ser::ValueKind::Double);
                f64 v = 0;
                i64 n = 0;
                if (real && std::from_chars(text.Data(), text.Data() + text.Size(), v).ec == std::errc() &&
                    (p->kind == i32(ser::ValueKind::Float) ? f32(v) == f32(p->d) : v == p->d))
                    return true;
                if (!real && std::from_chars(text.Data(), text.Data() + text.Size(), n).ec == std::errc() && n == p->l)
                    return true;
            }
        }
        // The table shows the new value.
        if (isText)
            p->s = String(text);
        else
        {
            f64 v = 0;
            std::from_chars(text.Data(), text.Data() + text.Size(), v);
            p->d = v;
            p->l = i64(v);
        }
        p->different = false;
        AddMod(*t, p->path.View(), text, ser::AssetReference());
        return true;
    }

    bool SerializedNative::SetObject(u64 handle, i32 index, engine::Object* value)
    {
        Table* t = Of(handle);
        Prop* p = PropOf(handle, index);
        if (!t || !p || p->kind != kReferenceKind)
            return false;
        EditorReferences refs;
        p->ref = value ? refs.ToFile(engine::EntityId::ToULong(value->GetEntityId())) : ser::AssetReference();
        p->different = false;
        AddMod(*t, p->path.View(), StringView(), p->ref);
        return true;
    }

    bool SerializedNative::SetArraySize(u64 handle, i32 index, i32 size)
    {
        Table* t = Of(handle);
        Prop* p = PropOf(handle, index);
        if (!t || !p || p->kind != i32(ser::ValueKind::Array) || size < 0)
            return false;
        if (p->arraySize == size && !p->different)
            return true;
        String path = Format("{}.Array.size", p->path);
        AddMod(*t, path.View(), Format("{}", size).View(), ser::AssetReference());
        ApplyMods(*t);
        return true;
    }
}
