#include <Helix/Editor/Assets/AssetModification.h>

#include <bit>

namespace helix::editor
{
    namespace
    {
        // C#'s: kind 0 create, 1 delete, 2 move, 3 save; the result a ModificationResult.
        using ManagedModification = i32 (*)(i32 kind, u64 context);
        ManagedModification managedModification = nullptr;

        struct Request
        {
            StringView first, second;
            Array<String>* paths = nullptr;
        };

        Request* RequestOf(u64 context) { return reinterpret_cast<Request*>(uintptr_t(context)); }

        i32 Ask(i32 kind, Request& r) { return managedModification ? managedModification(kind, u64(uintptr_t(&r))) : 0; }

        void SetModificationCallback(void* function) { managedModification = std::bit_cast<ManagedModification>(function); }

        struct ExportRegistrar
        {
            ExportRegistrar()
            {
                scripting::RegisterNativeExport("HelixEditor.SetModificationCallback", std::bit_cast<const void*>(&SetModificationCallback));
            }
        } registrar;
    }

    String AssetModificationNative::RequestPath(u64 context, i32 which)
    {
        Request* r = RequestOf(context);
        return r ? String(which == 0 ? r->first : r->second) : String();
    }

    Array<String> AssetModificationNative::RequestPaths(u64 context)
    {
        Request* r = RequestOf(context);
        return r && r->paths ? *r->paths : Array<String>();
    }

    void AssetModificationNative::SetRequestPaths(u64 context, StringView paths)
    {
        Request* r = RequestOf(context);
        if (!r || !r->paths)
            return;
        r->paths->Clear();
        usize at = 0;
        while (at < paths.Size())
        {
            usize end = paths.Find('\n', at);
            if (end == StringView::npos)
                end = paths.Size();
            if (end > at)
                r->paths->Push(String(paths.Substr(at, end - at)));
            at = end + 1;
        }
    }

    void AssetModificationNative::WillCreate(StringView path)
    {
        Request r;
        r.first = path;
        Ask(0, r);
    }

    ModificationResult AssetModificationNative::WillDelete(StringView path)
    {
        Request r;
        r.first = path;
        return ModificationResult(Ask(1, r));
    }

    ModificationResult AssetModificationNative::WillMove(StringView from, StringView to)
    {
        Request r;
        r.first = from;
        r.second = to;
        return ModificationResult(Ask(2, r));
    }

    void AssetModificationNative::WillSave(Array<String>& paths)
    {
        if (!managedModification || paths.Empty())
            return;
        Request r;
        r.paths = &paths;
        Ask(3, r);
    }
}
