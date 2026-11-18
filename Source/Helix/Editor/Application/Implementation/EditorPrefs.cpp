#include <Helix/Editor/Application/EditorPrefs.h>

#include <Helix/Core/Containers/Array.h>
#include <Helix/Core/Strings/Format.h>
#include <Helix/Platform/IO/FileSystem.h>

#include <cstdlib>

namespace helix::editor
{
    namespace
    {
        enum class Kind : char
        {
            Int = 'i',
            Float = 'f',
            String = 's',
            Bool = 'b',
        };

        struct Entry
        {
            Kind kind;
            String key, value;
        };

        struct Store
        {
            String path;
            Array<Entry> entries;
            bool loaded = false;
        };

        Store& S()
        {
            static Store* s = new Store();
            return *s;
        }

        String DefaultPath()
        {
#if defined(_WIN32)
            const char* base = std::getenv("APPDATA");
            String folder = base ? platform::JoinPath(base, "Helix") : platform::JoinPath(platform::TempDirectory(), "Helix");
#else
            const char* xdg = std::getenv("XDG_CONFIG_HOME");
            const char* home = std::getenv("HOME");
            String folder = xdg && *xdg ? platform::JoinPath(xdg, "Helix") : home ? platform::JoinPath(platform::JoinPath(home, ".config"), "Helix")
                                                                                   : platform::JoinPath(platform::TempDirectory(), "Helix");
#endif
            return platform::JoinPath(folder.View(), "EditorPrefs.txt");
        }

        String Escape(StringView s)
        {
            String out;
            for (char c : s)
            {
                if (c == '\\')
                    out += "\\\\";
                else if (c == '\t')
                    out += "\\t";
                else if (c == '\n')
                    out += "\\n";
                else
                    out += StringView(&c, 1);
            }
            return out;
        }

        String Unescape(StringView s)
        {
            String out;
            for (usize i = 0; i < s.Size(); i++)
            {
                char c = s[i];
                if (c == '\\' && i + 1 < s.Size())
                {
                    char n = s[++i];
                    c = n == 't' ? '\t' : n == 'n' ? '\n' : n;
                }
                out += StringView(&c, 1);
            }
            return out;
        }

        void Load()
        {
            Store& s = S();
            if (s.loaded)
                return;
            s.loaded = true;
            if (s.path.Empty())
                s.path = DefaultPath();
            s.entries.Clear();
            Array<u8> bytes;
            if (!platform::ReadFile(s.path.View(), bytes))
                return;
            StringView text(reinterpret_cast<const char*>(bytes.Data()), bytes.Size());
            usize start = 0;
            while (start < text.Size())
            {
                usize end = start;
                while (end < text.Size() && text[end] != '\n')
                    end++;
                StringView line = text.Substr(start, end - start);
                start = end + 1;
                usize tab1 = line.Size(), tab2 = line.Size();
                for (usize i = 0; i < line.Size(); i++)
                    if (line[i] == '\t')
                    {
                        if (tab1 == line.Size())
                            tab1 = i;
                        else
                        {
                            tab2 = i;
                            break;
                        }
                    }
                if (tab1 != 1 || tab2 >= line.Size())
                    continue;
                s.entries.Push({Kind(line[0]), Unescape(line.Substr(tab1 + 1, tab2 - tab1 - 1)), Unescape(line.Substr(tab2 + 1))});
            }
        }

        void Save()
        {
            Store& s = S();
            String text;
            for (const Entry& e : s.entries)
                text += Format("{}\t{}\t{}\n", char(e.kind), Escape(e.key.View()).View(), Escape(e.value.View()).View());
            platform::CreateDirectories(platform::GetDirectoryName(s.path.View()));
            platform::WriteFile(s.path.View(), Span<const u8>(reinterpret_cast<const u8*>(text.Data()), text.Size()));
        }

        Entry* Find(StringView key)
        {
            Load();
            for (Entry& e : S().entries)
                if (e.key.View() == key)
                    return &e;
            return nullptr;
        }

        void Set(StringView key, Kind kind, String value)
        {
            Entry* e = Find(key);
            if (e && e->kind == kind && e->value == value)
                return;
            if (!e)
            {
                S().entries.Push({kind, String(key), String()});
                e = &S().entries[S().entries.Size() - 1];
            }
            e->kind = kind;
            e->value = std::move(value);
            Save();
        }
    }

    void EditorPrefs::SetInt(StringView key, i32 value) { Set(key, Kind::Int, Format("{}", value)); }

    i32 EditorPrefs::GetInt(StringView key, i32 defaultValue)
    {
        Entry* e = Find(key);
        return e && e->kind == Kind::Int ? i32(std::atoi(e->value.CStr())) : defaultValue;
    }

    void EditorPrefs::SetFloat(StringView key, f32 value) { Set(key, Kind::Float, Format("{:.9g}", f64(value))); }

    f32 EditorPrefs::GetFloat(StringView key, f32 defaultValue)
    {
        Entry* e = Find(key);
        return e && e->kind == Kind::Float ? f32(std::atof(e->value.CStr())) : defaultValue;
    }

    void EditorPrefs::SetString(StringView key, StringView value) { Set(key, Kind::String, String(value)); }

    String EditorPrefs::GetString(StringView key, StringView defaultValue)
    {
        Entry* e = Find(key);
        return e && e->kind == Kind::String ? e->value : String(defaultValue);
    }

    void EditorPrefs::SetBool(StringView key, bool value) { Set(key, Kind::Bool, String(value ? "1" : "0")); }

    bool EditorPrefs::GetBool(StringView key, bool defaultValue)
    {
        Entry* e = Find(key);
        return e && e->kind == Kind::Bool ? e->value.View() == "1" : defaultValue;
    }

    bool EditorPrefs::HasKey(StringView key) { return Find(key) != nullptr; }

    void EditorPrefs::DeleteKey(StringView key)
    {
        Load();
        Array<Entry>& all = S().entries;
        for (usize i = 0; i < all.Size(); i++)
            if (all[i].key.View() == key)
            {
                all.RemoveAt(i);
                Save();
                return;
            }
    }

    void EditorPrefs::DeleteAll()
    {
        Load();
        S().entries.Clear();
        Save();
    }

    String EditorPrefs::Path()
    {
        Load();
        return S().path;
    }

    void EditorPrefs::SetPath(StringView path)
    {
        S().path = String(path);
        S().loaded = false;
    }
}
