#include <Helix/Editor/Application/EditorPrefs.h>
#include <Helix/Editor/Shell/EditorMenus.h>
#include <Helix/Editor/Shell/EditorToolbar.h>
#include <Helix/Platform/IO/FileSystem.h>
#include <Helix/Testing/Test.h>

using namespace helix;
using namespace helix::editor;

namespace
{
    i32 g_runs = 0;
    bool g_valid = true;
    void Run(void*) { g_runs++; }
    bool Valid(void*) { return g_valid; }
}

HX_TEST(Menus, ItemsOrderByPriorityWithGroupsAndSubmenus)
{
    EditorMenus::ClearInternal();
    EditorMenus::AddTitle("Edit", 10);
    EditorMenus::AddTitle("File", 0);
    EditorMenus::Add("File/Save", 20, "Ctrl+S", {&Run, nullptr, nullptr});
    EditorMenus::Add("File/New Scene", 0, "Ctrl+N", {&Run, nullptr, nullptr});
    EditorMenus::Add("File/Open Scene", 1, "", {&Run, &Valid, nullptr});
    EditorMenus::Add("File/Recent/One", 5, "", {&Run, nullptr, nullptr});
    EditorMenus::Add("Edit/Undo", 0, "", {&Run, nullptr, nullptr});
    EditorMenus::Add("Tools/Thing", 0, "", {&Run, nullptr, nullptr});
    Array<String> titles = EditorMenus::TitlesInternal();
    HX_REQUIRE(titles.Size() == 3);
    HX_CHECK(titles[0] == StringView("File") && titles[1] == StringView("Edit") && titles[2] == StringView("Tools"));
    // New Scene 0, Open Scene 1, Recent 5, a separator (5 to 20), Save 20.
    Array<MenuEntryData> file = EditorMenus::Entries("File");
    HX_REQUIRE(file.Size() == 5);
    HX_CHECK(file[0].name == StringView("New Scene") && file[0].shortcut == StringView("Ctrl+N"));
    HX_CHECK(file[1].name == StringView("Open Scene") && file[2].name == StringView("Recent") && file[2].submenu);
    HX_CHECK(file[3].separator && file[4].name == StringView("Save") && file[4].path == StringView("File/Save"));
    Array<MenuEntryData> recent = EditorMenus::Entries("File/Recent");
    HX_CHECK(recent.Size() == 1 && recent[0].path == StringView("File/Recent/One"));
    // Run and validation.
    g_runs = 0;
    HX_CHECK(EditorMenus::Execute("File/Save") && g_runs == 1);
    g_valid = false;
    HX_CHECK(!EditorMenus::Validate("File/Open Scene") && !EditorMenus::Execute("File/Open Scene") && g_runs == 1);
    HX_CHECK(!EditorMenus::Entries("File")[1].enabled);
    g_valid = true;
    EditorMenus::SetChecked("File/Save", true);
    HX_CHECK(EditorMenus::GetChecked("File/Save") && EditorMenus::Entries("File")[4].checkedState);
    u32 version = EditorMenus::VersionInternal();
    HX_CHECK(EditorMenus::Remove("Tools/Thing") && !EditorMenus::Exists("Tools/Thing") && EditorMenus::VersionInternal() != version);
    HX_CHECK(EditorMenus::TitlesInternal().Size() == 2 && !EditorMenus::Execute("Tools/Thing"));
    EditorMenus::ClearInternal();
}

HX_TEST(Toolbar, ElementsByGroupAndOrderTogglesAndPresets)
{
    EditorToolbar::ClearInternal();
    i32 clicks = 0;
    auto add = [&](StringView id, ToolbarGroup group, i32 order, ToolbarElementKind kind) {
        ToolbarElementData d;
        d.id = String(id);
        d.group = group;
        d.order = order;
        d.kind = kind;
        EditorToolbar::Add(d, {[](void* u) { ++*static_cast<i32*>(u); }, &clicks});
    };
    add("Editor/Play", ToolbarGroup::Middle, 0, ToolbarElementKind::Toggle);
    add("Editor/Step", ToolbarGroup::Middle, 2, ToolbarElementKind::Button);
    add("Editor/Pause", ToolbarGroup::Middle, 1, ToolbarElementKind::Toggle);
    add("Editor/Layout", ToolbarGroup::Right, 0, ToolbarElementKind::Dropdown);
    Array<ToolbarElementData> middle = EditorToolbar::Elements(ToolbarGroup::Middle);
    HX_REQUIRE(middle.Size() == 3);
    HX_CHECK(middle[0].id == StringView("Editor/Play") && middle[1].id == StringView("Editor/Pause") && middle[2].id == StringView("Editor/Step"));
    // A toggle flips, then runs.
    HX_CHECK(EditorToolbar::Click("Editor/Play") && EditorToolbar::GetOn("Editor/Play") && clicks == 1);
    EditorToolbar::SetEnabled("Editor/Step", false);
    HX_CHECK(!EditorToolbar::Click("Editor/Step") && clicks == 1);
    // A preset keeps places: Step to the left, then back.
    String preset = EditorToolbar::SavePreset();
    EditorToolbar::Move("Editor/Step", ToolbarGroup::Left, 3);
    HX_CHECK(EditorToolbar::Elements(ToolbarGroup::Left).Size() == 1 && EditorToolbar::Elements(ToolbarGroup::Middle).Size() == 2);
    EditorToolbar::ApplyPreset(preset);
    HX_CHECK(EditorToolbar::Elements(ToolbarGroup::Left).Empty() && EditorToolbar::Elements(ToolbarGroup::Middle).Size() == 3);
    HX_CHECK(EditorToolbar::Remove("Editor/Layout") && EditorToolbar::Elements(ToolbarGroup::Right).Empty());
    EditorToolbar::ClearInternal();
}

HX_TEST(Prefs, ValuesKeptInTheUsersFile)
{
    String path = platform::JoinPath(platform::TempDirectory(), "helix-editor-prefs-test/EditorPrefs.txt");
    platform::RemoveFile(path);
    EditorPrefs::SetPath(path);
    EditorPrefs::SetInt("count", 42);
    EditorPrefs::SetFloat("scale", 1.5f);
    EditorPrefs::SetString("name", "tab\there\nline \\ slash");
    EditorPrefs::SetBool("on", true);
    // Read again from the file.
    EditorPrefs::SetPath(path);
    HX_CHECK(EditorPrefs::GetInt("count") == 42 && EditorPrefs::GetFloat("scale") == 1.5f && EditorPrefs::GetBool("on"));
    HX_CHECK(EditorPrefs::GetString("name") == StringView("tab\there\nline \\ slash"));
    HX_CHECK(EditorPrefs::GetInt("missing", 7) == 7 && EditorPrefs::GetInt("name", 3) == 3 && EditorPrefs::HasKey("count"));
    EditorPrefs::DeleteKey("count");
    HX_CHECK(!EditorPrefs::HasKey("count"));
    EditorPrefs::DeleteAll();
    EditorPrefs::SetPath(path);
    HX_CHECK(!EditorPrefs::HasKey("name"));
    platform::RemoveFile(path);
    platform::RemoveEmptyDirectory(platform::GetDirectoryName(path));
    EditorPrefs::SetPath("");
}
