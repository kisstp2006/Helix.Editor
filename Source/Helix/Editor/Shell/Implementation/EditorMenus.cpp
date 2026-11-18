#include <Helix/Editor/Shell/EditorMenus.h>

#include <Helix/Core/Strings/Format.h>

namespace helix::editor
{
    namespace
    {
        struct Item
        {
            String path;
            i32 priority = 0;
            String shortcut;
            MenuItemFunctions functions;
            bool managed = false;
            bool managedValidates = false;
            bool checkedState = false;
            u32 sequence = 0;
        };

        struct Title
        {
            String name;
            i32 order = 0;
        };

        struct Registry
        {
            Array<Item> items;
            Array<Title> titles;
            u32 version = 1;
            u32 sequence = 0;
            bool validated = true;
        };

        Registry& R()
        {
            static Registry* r = new Registry();
            return *r;
        }

        Item* Find(StringView path)
        {
            for (Item& i : R().items)
                if (i.path.View() == path)
                    return &i;
            return nullptr;
        }

        // The first part of a path ("File" of "File/Save").
        StringView Head(StringView path)
        {
            for (usize i = 0; i < path.Size(); i++)
                if (path[i] == '/')
                    return path.Substr(0, i);
            return path;
        }

        bool StartsWithFolder(StringView path, StringView folder)
        {
            return path.Size() > folder.Size() && path.Substr(0, folder.Size()) == folder && path[folder.Size()] == '/';
        }

        void Changed() { R().version++; }
    }

    void EditorMenus::AddTitle(StringView name, i32 order)
    {
        for (Title& t : R().titles)
            if (t.name.View() == name)
            {
                t.order = order;
                Changed();
                return;
            }
        R().titles.Push({String(name), order});
        Changed();
    }

    void EditorMenus::Add(StringView path, i32 priority, StringView shortcut, const MenuItemFunctions& functions)
    {
        Item* item = Find(path);
        if (!item)
        {
            R().items.Push(Item());
            item = &R().items[R().items.Size() - 1];
            item->path = String(path);
            item->sequence = R().sequence++;
        }
        item->priority = priority;
        item->shortcut = String(shortcut);
        item->functions = functions;
        item->managed = false;
        // A title not registered comes after the registered ones, in the order its first item came.
        StringView head = Head(path);
        bool known = false;
        for (const Title& t : R().titles)
            known = known || t.name.View() == head;
        if (!known)
            R().titles.Push({String(head), 1000 + i32(R().titles.Size())});
        Changed();
    }

    void EditorMenus::AddManagedInternal(StringView path, i32 priority, StringView shortcut, bool validates)
    {
        Add(path, priority, shortcut, {});
        Item* item = Find(path);
        item->managed = true;
        item->managedValidates = validates;
    }

    bool EditorMenus::Remove(StringView path)
    {
        Array<Item>& items = R().items;
        for (usize i = 0; i < items.Size(); i++)
            if (items[i].path.View() == path)
            {
                items.RemoveAt(i);
                Changed();
                return true;
            }
        return false;
    }

    engine::Event<String>& EditorMenus::managedExecute()
    {
        static engine::Event<String>* e = new engine::Event<String>();
        return *e;
    }

    engine::Event<String>& EditorMenus::managedValidate()
    {
        static engine::Event<String>* e = new engine::Event<String>();
        return *e;
    }

    void EditorMenus::SetValidatedInternal(bool value) { R().validated = value; }

    bool EditorMenus::Validate(StringView path)
    {
        Item* item = Find(path);
        if (!item)
            return false;
        if (item->managed)
        {
            if (!item->managedValidates)
                return true;
            R().validated = true;
            managedValidate().Invoke(item->path);
            return R().validated;
        }
        return !item->functions.validate || item->functions.validate(item->functions.user);
    }

    bool EditorMenus::Execute(StringView path)
    {
        Item* item = Find(path);
        if (!item || !Validate(path))
            return false;
        // The item may go while it runs: its path is kept.
        String p = item->path;
        if (item->managed)
            managedExecute().Invoke(p);
        else if (item->functions.execute)
            item->functions.execute(item->functions.user);
        return true;
    }

    bool EditorMenus::Exists(StringView path) { return Find(path) != nullptr; }

    void EditorMenus::SetChecked(StringView path, bool value)
    {
        if (Item* item = Find(path))
            item->checkedState = value;
    }

    bool EditorMenus::GetChecked(StringView path)
    {
        Item* item = Find(path);
        return item && item->checkedState;
    }

    Array<String> EditorMenus::TitlesInternal()
    {
        Array<Title> titles = R().titles;
        // By order, then as registered (a stable insertion sort).
        for (usize i = 1; i < titles.Size(); i++)
            for (usize j = i; j > 0 && titles[j - 1].order > titles[j].order; j--)
            {
                Title t = titles[j - 1];
                titles[j - 1] = titles[j];
                titles[j] = t;
            }
        Array<String> out;
        for (const Title& t : titles)
        {
            // A title shows when it has items.
            bool used = false;
            for (const Item& item : R().items)
                used = used || StartsWithFolder(item.path.View(), t.name.View());
            if (used)
                out.Push(t.name);
        }
        return out;
    }

    Array<String> EditorMenus::EntriesInternal(StringView path)
    {
        Array<String> out;
        for (const MenuEntryData& e : Entries(path))
            out.Push(Format("{}\x1f{}\x1f{}\x1f{}\x1f{}\x1f{}", e.separator ? 's' : e.submenu ? 'm' : 'i', e.enabled ? 1 : 0, e.checkedState ? 1 : 0,
                            e.name.View(), e.path.View(), e.shortcut.View()));
        return out;
    }

    Array<MenuEntryData> EditorMenus::Entries(StringView path)
    {
        // The direct children: items, and submenus at the priority of their first item.
        struct Child
        {
            String name;
            i32 priority;
            u32 sequence;
            const Item* item;
        };
        Array<Child> children;
        for (const Item& item : R().items)
        {
            if (!StartsWithFolder(item.path.View(), path))
                continue;
            StringView rest = item.path.View().Substr(path.Size() + 1);
            StringView name = Head(rest);
            bool isSubmenu = name.Size() < rest.Size();
            bool merged = false;
            for (Child& c : children)
                if (c.name.View() == name && isSubmenu && !c.item)
                {
                    if (item.priority < c.priority)
                        c.priority = item.priority;
                    merged = true;
                }
            if (!merged)
                children.Push({String(name), item.priority, item.sequence, isSubmenu ? nullptr : &item});
        }
        for (usize i = 1; i < children.Size(); i++)
            for (usize j = i; j > 0 && (children[j - 1].priority > children[j].priority ||
                                        (children[j - 1].priority == children[j].priority && children[j - 1].sequence > children[j].sequence));
                 j--)
            {
                Child t = children[j - 1];
                children[j - 1] = children[j];
                children[j] = t;
            }
        Array<MenuEntryData> out;
        for (usize i = 0; i < children.Size(); i++)
        {
            if (i > 0 && children[i].priority - children[i - 1].priority >= 11)
            {
                MenuEntryData sep;
                sep.separator = true;
                out.Push(sep);
            }
            MenuEntryData e;
            e.name = children[i].name;
            e.path = Format("{}/{}", path, children[i].name.View());
            e.submenu = children[i].item == nullptr;
            if (const Item* item = children[i].item)
            {
                e.shortcut = item->shortcut;
                e.checkedState = item->checkedState;
                e.enabled = Validate(item->path.View());
            }
            out.Push(e);
        }
        return out;
    }

    u32 EditorMenus::VersionInternal() { return R().version; }

    void EditorMenus::ClearInternal()
    {
        R().items.Clear();
        R().titles.Clear();
        Changed();
    }
}
