#include <Helix/Editor/Assets/AssetImporters.h>

#include <Helix/Core/Strings/Format.h>
#include <Helix/Serialization/Json/Parser.h>

namespace helix::editor
{
    namespace
    {
        struct Registry
        {
            Array<ImporterDescription> importers;
            String defaultName = String("DefaultImporter");
            String folderName = String("DefaultImporter");
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
            {
                char c = out[i];
                if (c >= 'A' && c <= 'Z')
                    out[i] = char(c - 'A' + 'a');
            }
            return out;
        }

        // The default importer when no description names one: no settings.
        const ImporterDescription& Fallback()
        {
            static ImporterDescription* d = [] {
                auto* made = new ImporterDescription();
                made->name = String("DefaultImporter");
                made->settings.Push(String("externalObjects: {}"));
                return made;
            }();
            return *d;
        }
    }

    bool AssetImporters::Load(StringView json, String& error)
    {
        using namespace serialization::json;
        Tree tree;
        if (!Parse(json, tree, error))
            return false;
        u32 list = tree.Find(tree.root, "importers");
        if (!tree.IsArray(list))
        {
            error = "no importers array";
            return false;
        }
        for (u32 i = 0; i < tree.Count(list); i++)
        {
            u32 node = tree.Item(list, i);
            ImporterDescription d;
            u32 name = tree.Find(node, "name");
            if (!tree.IsString(name))
            {
                error = Format("importer {} has no name", i);
                return false;
            }
            d.name = String(tree.Text(name));
            i64 version = 1;
            u32 v = tree.Find(node, "version");
            if (tree.IsNumber(v) && tree.AsInt(v, version))
                d.version = i32(version);
            u32 exts = tree.Find(node, "extensions");
            if (tree.IsArray(exts))
                for (u32 k = 0; k < tree.Count(exts); k++)
                    if (tree.IsString(tree.Item(exts, k)))
                        d.extensions.Push(Lower(tree.Text(tree.Item(exts, k))));
            u32 settings = tree.Find(node, "settings");
            if (tree.IsArray(settings))
                for (u32 k = 0; k < tree.Count(settings); k++)
                    if (tree.IsString(tree.Item(settings, k)))
                        d.settings.Push(String(tree.Text(tree.Item(settings, k))));
            u32 type = tree.Find(node, "mainType");
            if (tree.IsString(type))
                d.mainType = String(tree.Text(type));
            Add(d);
        }
        u32 def = tree.Find(tree.root, "default");
        if (tree.IsString(def))
            R().defaultName = String(tree.Text(def));
        u32 folder = tree.Find(tree.root, "folder");
        if (tree.IsString(folder))
            R().folderName = String(tree.Text(folder));
        return true;
    }

    void AssetImporters::Add(const ImporterDescription& description)
    {
        for (ImporterDescription& d : R().importers)
            if (d.name == description.name)
            {
                d = description;
                return;
            }
        R().importers.Push(description);
    }

    void AssetImporters::Clear()
    {
        R().importers.Clear();
        R().defaultName = String("DefaultImporter");
        R().folderName = String("DefaultImporter");
    }

    const ImporterDescription* AssetImporters::Find(StringView name)
    {
        for (const ImporterDescription& d : R().importers)
            if (d.name.View() == name)
                return &d;
        return nullptr;
    }

    const ImporterDescription& AssetImporters::ForExtension(StringView extension)
    {
        String ext = Lower(extension.StartsWith(".") ? extension.Substr(1) : extension);
        for (const ImporterDescription& d : R().importers)
            for (const String& e : d.extensions)
                if (e == ext)
                    return d;
        const ImporterDescription* def = Find(R().defaultName.View());
        return def ? *def : Fallback();
    }

    const ImporterDescription& AssetImporters::ForFolder()
    {
        const ImporterDescription* d = Find(R().folderName.View());
        return d ? *d : Fallback();
    }

    Span<const ImporterDescription> AssetImporters::All() { return Span<const ImporterDescription>(R().importers.Data(), R().importers.Size()); }
}
