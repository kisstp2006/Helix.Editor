#include <Helix/Editor/Application/EditorUtility.h>

#include <Helix/Core/Containers/HashMap.h>
#include <Helix/Core/Strings/Format.h>
#include <Helix/Editor/Assets/ProjectDatabase.h>
#include <Helix/Engine/Application/Application.h>
#include <Helix/Platform/IO/FileSystem.h>
#include <Helix/Platform/Window/FileDialog.h>

namespace helix::editor
{
    namespace
    {
        struct Dirt
        {
            HashMap<engine::EntityId, i32> counts;
            HashMap<engine::EntityId, bool> dirty;
        };

        Dirt& D()
        {
            static Dirt* d = new Dirt();
            return *d;
        }
    }

    void EditorUtility::SetDirty(engine::Object* target)
    {
        if (!target)
            return;
        engine::EntityId id = target->GetEntityId();
        D().counts[id]++;
        D().dirty.Set(id, true);
    }

    bool EditorUtility::IsDirty(engine::Object* target) { return target && D().dirty.Find(target->GetEntityId()) != nullptr; }

    void EditorUtility::ClearDirty(engine::Object* target)
    {
        if (target)
            D().dirty.Remove(target->GetEntityId());
    }

    i32 EditorUtility::GetDirtyCount(engine::Object* target)
    {
        const i32* n = target ? D().counts.Find(target->GetEntityId()) : nullptr;
        return n ? *n : 0;
    }

    namespace
    {
        // A folder of the project as a file system path; others as they are.
        String Folder(StringView folder)
        {
            if (!folder.Empty() && ProjectDatabase::IsOpen() && (ProjectDatabase::IsProjectPath(folder) || folder == "Assets"))
                return ProjectDatabase::FullPath(folder);
            return String(folder);
        }

        // "png,jpg" as one filter of the extensions.
        void AddExtensions(platform::FileDialogDesc& d, StringView extension)
        {
            if (extension.Empty())
                return;
            platform::FileDialogFilter f;
            f.extensions = String(extension);
            for (usize i = 0; i < f.extensions.Size(); i++)
                if (f.extensions[i] == ',')
                    f.extensions[i] = ';';
            f.name = Format("{} files", extension);
            d.filters.Push(f);
        }

        bool NoDialogs() { return engine::Application::isBatchMode(); }
    }

    String EditorUtility::OpenFilePanel(StringView title, StringView directory, StringView extension)
    {
        if (NoDialogs())
            return String();
        platform::FileDialogDesc d;
        d.title = String(title);
        d.directory = Folder(directory);
        AddExtensions(d, extension);
        return platform::OpenFileDialog(d);
    }

    String EditorUtility::OpenFilePanelWithFiltersInternal(StringView title, StringView directory, StringView filters)
    {
        if (NoDialogs())
            return String();
        platform::FileDialogDesc d;
        d.title = String(title);
        d.directory = Folder(directory);
        Array<StringView> parts;
        usize at = 0;
        while (at <= filters.Size() && !filters.Empty())
        {
            usize end = filters.Find('\n', at);
            if (end == StringView::npos)
                end = filters.Size();
            parts.Push(filters.Substr(at, end - at));
            at = end + 1;
        }
        for (usize i = 0; i + 1 < parts.Size(); i += 2)
        {
            platform::FileDialogFilter f;
            f.name = String(parts[i]);
            f.extensions = String(parts[i + 1]);
            for (usize c = 0; c < f.extensions.Size(); c++)
                if (f.extensions[c] == ',')
                    f.extensions[c] = ';';
            d.filters.Push(f);
        }
        return platform::OpenFileDialog(d);
    }

    String EditorUtility::OpenFolderPanel(StringView title, StringView folder, StringView defaultName)
    {
        if (NoDialogs())
            return String();
        platform::FileDialogDesc d;
        d.title = String(title);
        d.directory = Folder(folder);
        d.defaultName = String(defaultName);
        return platform::OpenFolderDialog(d);
    }

    String EditorUtility::SaveFilePanel(StringView title, StringView directory, StringView defaultName, StringView extension)
    {
        if (NoDialogs())
            return String();
        platform::FileDialogDesc d;
        d.title = String(title);
        d.directory = Folder(directory);
        d.defaultName = String(defaultName);
        AddExtensions(d, extension);
        return platform::SaveFileDialog(d);
    }

    String EditorUtility::SaveFilePanelInProject(StringView title, StringView defaultName, StringView extension, StringView, StringView path)
    {
        if (!ProjectDatabase::IsOpen())
            return String();
        String chosen = SaveFilePanel(title, path.Empty() ? StringView("Assets") : path, defaultName, extension);
        // Back to a path of the project.
        String root = ProjectDatabase::FullPath("");
        for (usize i = 0; i < root.Size(); i++)
            if (root[i] == '\\')
                root[i] = '/';
        while (!root.Empty() && root[root.Size() - 1] == '/')
            root = String(root.View().Substr(0, root.Size() - 1));
        if (chosen.Size() <= root.Size() + 1)
            return String();
        String lowerChosen(chosen), lowerRoot(root);
        for (String* s : {&lowerChosen, &lowerRoot})
            for (usize i = 0; i < s->Size(); i++)
                if ((*s)[i] >= 'A' && (*s)[i] <= 'Z')
                    (*s)[i] = char((*s)[i] - 'A' + 'a');
        if (!lowerChosen.View().StartsWith(lowerRoot.View()) || chosen[root.Size()] != '/')
            return String();
        String relative(chosen.View().Substr(root.Size() + 1));
        return ProjectDatabase::IsProjectPath(relative.View()) ? relative : String();
    }

    void EditorUtility::DirtyObjects(Array<engine::Object*>& out)
    {
        Array<engine::EntityId> gone;
        for (auto e : D().dirty)
        {
            if (engine::Object* o = engine::EntityIdToObject(e.key))
                out.Push(o);
            else
                gone.Push(e.key);
        }
        for (engine::EntityId id : gone)
            D().dirty.Remove(id);
    }
}
