#include <Helix/Editor/Assets/MetaFile.h>

#include <Helix/Core/Strings/Format.h>

namespace helix::editor
{
    namespace
    {
        // A line's key (before its colon) and value (after it, trimmed); false for a line of no key.
        bool KeyValue(StringView line, StringView& key, StringView& value)
        {
            usize colon = line.Find(':');
            if (colon == StringView::npos || colon == 0)
                return false;
            key = line.Substr(0, colon).Trimmed();
            value = line.Substr(colon + 1).Trimmed();
            return !key.Empty();
        }

        // The keys every importer's section ends with.
        constexpr StringView kTail[] = {"userData", "assetBundleName", "assetBundleVariant"};
    }

    bool MetaFile::Parse(StringView text, MetaFile& out, String& error)
    {
        out = MetaFile();
        bool inSection = false, inLabels = false;
        usize at = 0;
        while (at < text.Size())
        {
            usize end = text.Find('\n', at);
            if (end == StringView::npos)
                end = text.Size();
            StringView raw = text.Substr(at, end - at);
            at = end + 1;
            if (!raw.Empty() && raw[raw.Size() - 1] == '\r')
                raw = raw.Substr(0, raw.Size() - 1);
            if (raw.Trimmed().Empty())
                continue;
            bool indented = raw[0] == ' ' || raw[0] == '\t';
            // The labels' items ("- name", at the top or indented).
            if (inLabels && raw.Trimmed().StartsWith("- "))
            {
                out.labels.Push(String(raw.Trimmed().Substr(2).Trimmed()));
                continue;
            }
            inLabels = false;
            if (indented && inSection)
            {
                // Two spaces in: the section's own lines (deeper ones keep the rest of their indentation).
                usize cut = 0;
                while (cut < 2 && cut < raw.Size() && raw[cut] == ' ')
                    cut++;
                out.settings.Push(String(raw.Substr(cut)));
                continue;
            }
            inSection = false;
            StringView key, value;
            if (!KeyValue(raw, key, value))
                continue;
            if (key == "fileFormatVersion")
                continue;
            if (key == "guid")
            {
                if (!Guid::TryParse(value, out.guid))
                {
                    error = Format("the guid '{}' is not 32 hex digits", value);
                    return false;
                }
                continue;
            }
            if (key == "labels")
            {
                inLabels = true;
                continue;
            }
            if (key == "folderAsset")
            {
                out.folder = value == "yes";
                continue;
            }
            if (key == "timeCreated" || key == "licenseType")
                continue;
            // A key with no value of its own starts the importer's section.
            if (value.Empty() && out.importer.Empty())
            {
                out.importer = String(key);
                inSection = true;
            }
        }
        if (!out.guid.IsValid())
        {
            error = "no guid";
            return false;
        }
        return true;
    }

    String MetaFile::Write() const
    {
        String s = Format("fileFormatVersion: 2\nguid: {}\n", guid.ToString());
        if (!labels.Empty())
        {
            s.Append("labels:\n");
            for (const String& l : labels)
                s.Append(Format("- {}\n", l));
        }
        if (folder)
            s.Append("folderAsset: yes\n");
        if (!importer.Empty())
        {
            s.Append(importer);
            s.Append(":\n");
            for (const String& line : settings)
            {
                s.Append("  ");
                s.Append(line);
                s.Append("\n");
            }
        }
        return s;
    }

    namespace
    {
        usize Indent(StringView line)
        {
            usize n = 0;
            while (n < line.Size() && line[n] == ' ')
                n++;
            return n;
        }

        // The line of a key path ("mipmaps.enableMipMap": a key two spaces in under "mipmaps:") among lines [from, to)
        // at an indentation; the index, or npos. `end` is where the lines of that level end.
        usize FindKey(const Array<String>& lines, usize from, usize to, usize indent, StringView key, usize& end)
        {
            end = to;
            for (usize i = from; i < to; i++)
            {
                StringView line = lines[i].View();
                usize in = Indent(line);
                if (in < indent)
                {
                    end = i;
                    return StringView::npos;
                }
                if (in != indent)
                    continue;
                StringView k, v;
                if (KeyValue(line.Substr(in), k, v) && k == key)
                    return i;
            }
            return StringView::npos;
        }

        // The end of a key's children (the first line after it at its indentation or less).
        usize ChildrenEnd(const Array<String>& lines, usize at)
        {
            usize indent = Indent(lines[at].View());
            usize i = at + 1;
            while (i < lines.Size() && Indent(lines[i].View()) > indent)
                i++;
            return i;
        }
    }

    String MetaFile::Value(StringView key) const
    {
        usize from = 0, to = settings.Size(), indent = 0;
        while (true)
        {
            usize dot = key.Find('.');
            StringView head = dot == StringView::npos ? key : key.Substr(0, dot);
            usize end;
            usize at = FindKey(settings, from, to, indent, head, end);
            if (at == StringView::npos)
                return String();
            if (dot == StringView::npos)
            {
                StringView k, v;
                KeyValue(settings[at].View().Substr(indent), k, v);
                return String(v);
            }
            from = at + 1;
            to = ChildrenEnd(settings, at);
            indent += 2;
            key = key.Substr(dot + 1);
        }
    }

    void MetaFile::SetValue(StringView key, StringView value)
    {
        usize from = 0, to = settings.Size(), indent = 0;
        while (true)
        {
            usize dot = key.Find('.');
            StringView head = dot == StringView::npos ? key : key.Substr(0, dot);
            String pad;
            for (usize n = 0; n < indent; n++)
                pad.Append(" ");
            usize end;
            usize at = FindKey(settings, from, to, indent, head, end);
            if (at == StringView::npos)
            {
                // Added: at the top level before the tail keys (a tail key last, in their order); in a parent after
                // its children.
                usize insert = to;
                bool tail = false;
                for (StringView t : kTail)
                    tail = tail || t == head;
                if (indent == 0 && !tail)
                    for (usize i = 0; i < settings.Size(); i++)
                    {
                        StringView k, v;
                        if (Indent(settings[i].View()) == 0 && KeyValue(settings[i].View(), k, v))
                            for (StringView t : kTail)
                                if (k == t && i < insert)
                                    insert = i;
                    }
                if (dot == StringView::npos)
                {
                    settings.Insert(insert, value.Empty() ? Format("{}{}: ", pad, head) : Format("{}{}: {}", pad, head, value));
                    return;
                }
                settings.Insert(insert, Format("{}{}:", pad, head));
                at = insert;
            }
            if (dot == StringView::npos)
            {
                settings[at] = value.Empty() ? Format("{}{}: ", pad, head) : Format("{}{}: {}", pad, head, value);
                return;
            }
            from = at + 1;
            to = ChildrenEnd(settings, at);
            indent += 2;
            key = key.Substr(dot + 1);
        }
    }
}
