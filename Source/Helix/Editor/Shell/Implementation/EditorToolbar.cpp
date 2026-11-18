#include <Helix/Editor/Shell/EditorToolbar.h>

#include <Helix/Core/Strings/Format.h>

#include <cstdlib>

namespace helix::editor
{
    namespace
    {
        struct Element
        {
            ToolbarElementData data;
            ToolbarElementFunctions functions;
            bool managed = false;
            u32 sequence = 0;
        };

        struct Registry
        {
            Array<Element> elements;
            u32 version = 1;
            u32 sequence = 0;
        };

        Registry& R()
        {
            static Registry* r = new Registry();
            return *r;
        }

        Element* Find(StringView id)
        {
            for (Element& e : R().elements)
                if (e.data.id.View() == id)
                    return &e;
            return nullptr;
        }

        void Changed() { R().version++; }
    }

    void EditorToolbar::Add(const ToolbarElementData& element, const ToolbarElementFunctions& functions)
    {
        Element* e = Find(element.id.View());
        if (!e)
        {
            R().elements.Push(Element());
            e = &R().elements[R().elements.Size() - 1];
            e->sequence = R().sequence++;
        }
        e->data = element;
        e->functions = functions;
        e->managed = false;
        Changed();
    }

    void EditorToolbar::AddManagedInternal(StringView id, ToolbarGroup group, i32 order, ToolbarElementKind kind, StringView strip, StringView icon,
                                           StringView text, StringView tooltip, bool on, bool enabled)
    {
        ToolbarElementData d;
        d.id = String(id);
        d.group = group;
        d.order = order;
        d.kind = kind;
        d.strip = String(strip);
        d.icon = String(icon);
        d.text = String(text);
        d.tooltip = String(tooltip);
        d.on = on;
        d.enabled = enabled;
        Add(d, {});
        Find(id)->managed = true;
    }

    bool EditorToolbar::Remove(StringView id)
    {
        Array<Element>& all = R().elements;
        for (usize i = 0; i < all.Size(); i++)
            if (all[i].data.id.View() == id)
            {
                all.RemoveAt(i);
                Changed();
                return true;
            }
        return false;
    }

    bool EditorToolbar::Exists(StringView id) { return Find(id) != nullptr; }

    engine::Event<String>& EditorToolbar::managedClick()
    {
        static engine::Event<String>* e = new engine::Event<String>();
        return *e;
    }

    bool EditorToolbar::Click(StringView id)
    {
        Element* e = Find(id);
        if (!e || !e->data.enabled)
            return false;
        if (e->data.kind == ToolbarElementKind::Toggle)
        {
            e->data.on = !e->data.on;
            Changed();
        }
        String key = e->data.id;
        if (e->managed)
            managedClick().Invoke(key);
        else if (e->functions.click)
            e->functions.click(e->functions.user);
        return true;
    }

    void EditorToolbar::SetOn(StringView id, bool value)
    {
        if (Element* e = Find(id); e && e->data.on != value)
        {
            e->data.on = value;
            Changed();
        }
    }

    bool EditorToolbar::GetOn(StringView id)
    {
        Element* e = Find(id);
        return e && e->data.on;
    }

    void EditorToolbar::SetEnabled(StringView id, bool value)
    {
        if (Element* e = Find(id); e && e->data.enabled != value)
        {
            e->data.enabled = value;
            Changed();
        }
    }

    void EditorToolbar::SetText(StringView id, StringView text)
    {
        if (Element* e = Find(id); e && e->data.text.View() != text)
        {
            e->data.text = String(text);
            Changed();
        }
    }

    void EditorToolbar::Move(StringView id, ToolbarGroup group, i32 order)
    {
        if (Element* e = Find(id))
        {
            e->data.group = group;
            e->data.order = order;
            Changed();
        }
    }

    Array<String> EditorToolbar::ElementsInternal(ToolbarGroup group)
    {
        Array<String> out;
        for (const ToolbarElementData& d : Elements(group))
            out.Push(Format("{}\x1f{}\x1f{}\x1f{}\x1f{}\x1f{}\x1f{}\x1f{}\x1f{}\x1f{}", d.id.View(), i32(d.group), d.order, i32(d.kind), d.strip.View(),
                            d.icon.View(), d.text.View(), d.tooltip.View(), d.on ? 1 : 0, d.enabled ? 1 : 0));
        return out;
    }

    Array<ToolbarElementData> EditorToolbar::Elements(ToolbarGroup group)
    {
        Array<const Element*> list;
        for (const Element& e : R().elements)
            if (e.data.group == group)
                list.Push(&e);
        for (usize i = 1; i < list.Size(); i++)
            for (usize j = i; j > 0 && (list[j - 1]->data.order > list[j]->data.order ||
                                        (list[j - 1]->data.order == list[j]->data.order && list[j - 1]->sequence > list[j]->sequence));
                 j--)
            {
                const Element* t = list[j - 1];
                list[j - 1] = list[j];
                list[j] = t;
            }
        Array<ToolbarElementData> out;
        for (const Element* e : list)
            out.Push(e->data);
        return out;
    }

    u32 EditorToolbar::VersionInternal() { return R().version; }

    void EditorToolbar::ClearInternal()
    {
        R().elements.Clear();
        Changed();
    }

    String EditorToolbar::SavePreset()
    {
        String out;
        for (const Element& e : R().elements)
            out += Format("{}={},{}\n", e.data.id.View(), i32(e.data.group), e.data.order);
        return out;
    }

    void EditorToolbar::ApplyPreset(StringView preset)
    {
        usize start = 0;
        while (start < preset.Size())
        {
            usize end = start;
            while (end < preset.Size() && preset[end] != '\n')
                end++;
            StringView line = preset.Substr(start, end - start);
            start = end + 1;
            usize eq = 0, comma = 0;
            for (usize i = 0; i < line.Size(); i++)
            {
                if (line[i] == '=' && !eq)
                    eq = i;
                else if (line[i] == ',' && eq)
                    comma = i;
            }
            if (!eq || !comma)
                continue;
            String group(line.Substr(eq + 1, comma - eq - 1));
            String order(line.Substr(comma + 1));
            Move(line.Substr(0, eq), ToolbarGroup(std::atoi(group.CStr())), std::atoi(order.CStr()));
        }
    }
}
