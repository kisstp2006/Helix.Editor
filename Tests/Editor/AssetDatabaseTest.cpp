#include <Helix/Core/Strings/Format.h>
#include <Helix/Editor/Assets/ArtifactStore.h>
#include <Helix/Editor/Assets/AssetDatabase.h>
#include <Helix/Editor/Assets/AssetImporters.h>
#include <Helix/Editor/Assets/MetaFile.h>
#include <Helix/Editor/Assets/ProjectDatabase.h>
#include <Helix/Platform/IO/FileSystem.h>
#include <Helix/Testing/Test.h>

using namespace helix;
using namespace helix::editor;

namespace
{
    constexpr StringView kImporters = R"({
      "default": "DefaultImporter", "folder": "DefaultImporter",
      "importers": [
        {"name": "TextureImporter", "version": 1, "mainType": "Texture2D", "extensions": ["png"],
         "settings": ["serializedVersion: 13", "mipmaps:", "  enableMipMap: 1", "isReadable: 0"]},
        {"name": "TextScriptImporter", "version": 1, "mainType": "TextAsset", "extensions": ["txt"], "settings": ["externalObjects: {}"]},
        {"name": "DefaultImporter", "version": 1, "mainType": "", "extensions": [], "settings": ["externalObjects: {}"]}
      ]})";

    // A project folder of its own, removed after.
    struct TempProject
    {
        String root;
        TempProject()
        {
            root = platform::JoinPath(platform::TempDirectory(), Format("helix-project-{}", Guid::Generate().ToString()));
            platform::CreateDirectories(platform::JoinPath(root, "Assets/Textures"));
            AssetImporters::Clear();
            String error;
            AssetImporters::Load(kImporters, error);
        }
        ~TempProject()
        {
            ProjectDatabase::Close();
            Remove(root);
        }
        void Write(StringView path, StringView text)
        {
            String full = platform::JoinPath(root, path);
            platform::CreateDirectories(platform::GetDirectoryName(full));
            platform::WriteFile(full, Span<const u8>(reinterpret_cast<const u8*>(text.Data()), text.Size()));
        }
        bool Exists(StringView path) { return platform::FileExists(platform::JoinPath(root, path)); }
        static void Remove(StringView full)
        {
            if (platform::DirectoryExists(full))
            {
                Array<String> names;
                platform::ListDirectory(full, [](const platform::DirectoryEntry& e, void* user) { static_cast<Array<String>*>(user)->Push(String(e.name)); }, &names);
                for (const String& n : names)
                    Remove(platform::JoinPath(full, n));
                platform::RemoveEmptyDirectory(full);
            }
            else
                platform::RemoveFile(full);
        }
    };

    bool Has(const Array<String>& list, StringView item)
    {
        for (const String& s : list)
            if (s == item)
                return true;
        return false;
    }
}

HX_TEST(AssetDatabase, MetaFilesRoundTripAndKeepTheirSection)
{
    MetaFile m;
    m.guid = Guid::Generate();
    m.importer = String("TextureImporter");
    m.settings.Push(String("serializedVersion: 13"));
    m.settings.Push(String("mipmaps:"));
    m.settings.Push(String("  enableMipMap: 1"));
    m.SetValue("userData", "");
    m.SetValue("assetBundleName", "");
    m.SetValue("isReadable", "1");
    String text = m.Write();
    HX_CHECK(text.View().StartsWith("fileFormatVersion: 2\nguid: "));
    HX_CHECK(text.View().Find("TextureImporter:\n  serializedVersion: 13\n  mipmaps:\n    enableMipMap: 1\n  isReadable: 1\n  userData: \n") !=
             StringView::npos);
    MetaFile back;
    String error;
    HX_REQUIRE(MetaFile::Parse(text.View(), back, error));
    HX_CHECK(back.guid == m.guid && back.importer == "TextureImporter" && !back.folder);
    HX_CHECK(back.Write() == text);
    HX_CHECK(back.Value("isReadable") == "1" && back.Value("enableMipMap").Empty());
    // Nested keys by path: read, changed, added under a parent or with a new parent.
    HX_CHECK(back.Value("mipmaps.enableMipMap") == "1");
    back.SetValue("mipmaps.enableMipMap", "0");
    back.SetValue("mipmaps.sRGBTexture", "1");
    back.SetValue("textureSettings.filterMode", "0");
    HX_CHECK(back.Value("mipmaps.enableMipMap") == "0" && back.Value("mipmaps.sRGBTexture") == "1" && back.Value("textureSettings.filterMode") == "0");
    String nested = back.Write();
    HX_CHECK(nested.View().Find("  mipmaps:\n    enableMipMap: 0\n    sRGBTexture: 1\n") != StringView::npos);
    HX_CHECK(nested.View().Find("  textureSettings:\n    filterMode: 0\n  userData: \n") != StringView::npos);
    // A folder's, Windows line ends, unknown top lines.
    HX_REQUIRE(MetaFile::Parse("fileFormatVersion: 2\r\nguid: 0123456789abcdef0123456789abcdef\r\nfolderAsset: yes\r\ntimeCreated: 1\r\nDefaultImporter:\r\n  externalObjects: {}\r\n", back, error));
    HX_CHECK(back.folder && back.importer == "DefaultImporter" && back.guid.ToString() == "0123456789abcdef0123456789abcdef");
    HX_CHECK(!MetaFile::Parse("fileFormatVersion: 2\nguid: nope\n", back, error));
}

HX_TEST(AssetDatabase, MetaLabelsRoundTripAndStayOutOfTheSection)
{
    constexpr StringView text = "fileFormatVersion: 2\nguid: 0123456789abcdef0123456789abcdef\nlabels:\n- Grid\n- Pattern\nTextureImporter:\n  isReadable: 0\n";
    MetaFile meta;
    String error;
    HX_REQUIRE(MetaFile::Parse(text, meta, error));
    HX_CHECK(meta.importer == "TextureImporter" && meta.labels.Size() == 2 && meta.labels[0] == "Grid" && meta.labels[1] == "Pattern");
    HX_CHECK(meta.Write() == text);
    meta.labels.Clear();
    HX_CHECK(meta.Write().View().Find("labels") == StringView::npos);
}

HX_TEST(AssetDatabase, TheProjectsFilesGetMetaFilesAndKeepTheirGuids)
{
    TempProject p;
    p.Write("Assets/Textures/A.png", "png");
    p.Write("Assets/Notes.txt", "hello");
    p.Write("Assets/.hidden", "x");
    p.Write("Assets/Backup~/Old.txt", "x");
    p.Write("Packages/com.helix.tools/Runtime/Tool.txt", "tool");
    p.Write("Packages/manifest.json", "{}");
    HX_REQUIRE(ProjectDatabase::Open(p.root));
    AssetChanges first = ProjectDatabase::Refresh();
    // Assets/Textures, its png, the txt, the package's folder and file (a package's own folder has none).
    HX_CHECK(first.metasWritten == 5);
    HX_CHECK(Has(first.imported, "Assets/Textures/A.png") && Has(first.imported, "Assets/Notes.txt") && Has(first.imported, "Packages/com.helix.tools/Runtime/Tool.txt"));
    HX_CHECK(p.Exists("Assets/Textures/A.png.meta") && p.Exists("Assets/Textures.meta") && !p.Exists("Assets/.hidden.meta") && !p.Exists("Packages/manifest.json.meta"));
    HX_CHECK(!ProjectDatabase::ByPath("Assets/Backup~") && !ProjectDatabase::ByPath("Assets/.hidden"));
    MetaFile meta;
    HX_REQUIRE(ProjectDatabase::ReadMeta("Assets/Textures/A.png", meta));
    HX_CHECK(meta.importer == "TextureImporter" && meta.Value("isReadable") == "0" && meta.Value("assetBundleName").Empty());
    HX_REQUIRE(ProjectDatabase::ReadMeta("Assets/Textures", meta));
    HX_CHECK(meta.folder && meta.importer == "DefaultImporter");
    String guid = AssetDatabase::AssetPathToGUID("assets/textures/a.png");
    HX_CHECK(guid.Size() == 32 && AssetDatabase::GUIDToAssetPath(guid.View()) == "Assets/Textures/A.png");
    // Again: nothing changed, the guids stay.
    AssetChanges again = ProjectDatabase::Refresh();
    HX_CHECK(again.Empty() && again.metasWritten == 0);
    HX_CHECK(AssetDatabase::AssetPathToGUID("Assets/Textures/A.png") == guid);
    // Moved outside the editor with its .meta: the same guid at the new path.
    platform::RenameFile(platform::JoinPath(p.root, "Assets/Textures/A.png"), platform::JoinPath(p.root, "Assets/B.png"));
    platform::RenameFile(platform::JoinPath(p.root, "Assets/Textures/A.png.meta"), platform::JoinPath(p.root, "Assets/B.png.meta"));
    AssetChanges moved = ProjectDatabase::Refresh();
    HX_CHECK(moved.movedFrom.Size() == 1 && moved.movedFrom[0] == "Assets/Textures/A.png" && moved.movedTo[0] == "Assets/B.png");
    HX_CHECK(AssetDatabase::AssetPathToGUID("Assets/B.png") == guid && AssetDatabase::GUIDToAssetPath(guid.View()) == "Assets/B.png");
    // Changed: imported again.
    p.Write("Assets/Notes.txt", "hello again");
    HX_CHECK(Has(ProjectDatabase::Refresh().imported, "Assets/Notes.txt"));
    // A file copied with its .meta: the copy gets a new guid.
    p.Write("Assets/C.png", "png");
    Array<u8> bytes;
    platform::ReadFile(platform::JoinPath(p.root, "Assets/B.png.meta"), bytes);
    platform::WriteFile(platform::JoinPath(p.root, "Assets/C.png.meta"), Span<const u8>(bytes.Data(), bytes.Size()));
    AssetChanges copied = ProjectDatabase::Refresh();
    HX_CHECK(copied.metasWritten == 1);
    String c = AssetDatabase::AssetPathToGUID("Assets/C.png"), b = AssetDatabase::AssetPathToGUID("Assets/B.png");
    HX_CHECK(c.Size() == 32 && b.Size() == 32 && c != b);
    // Deleted outside: its .meta goes, the asset is reported deleted.
    platform::RemoveFile(platform::JoinPath(p.root, "Assets/C.png"));
    AssetChanges deleted = ProjectDatabase::Refresh();
    HX_CHECK(Has(deleted.deleted, "Assets/C.png") && deleted.metasRemoved == 1 && !p.Exists("Assets/C.png.meta"));
}

HX_TEST(AssetDatabase, MovesCopiesDeletesAndFolders)
{
    TempProject p;
    p.Write("Assets/Textures/A.png", "png");
    p.Write("Assets/Readme.txt", "text");
    HX_REQUIRE(ProjectDatabase::Open(p.root));
    ProjectDatabase::Refresh();
    String guid = AssetDatabase::AssetPathToGUID("Assets/Textures/A.png");
    // Folders: made with a free name.
    String art = AssetDatabase::CreateFolder("Assets", "Art");
    HX_CHECK(art.Size() == 32 && AssetDatabase::IsValidFolder("Assets/Art"));
    String art1 = AssetDatabase::CreateFolder("Assets", "Art");
    HX_CHECK(art1 != art && AssetDatabase::GUIDToAssetPath(art1.View()) == "Assets/Art 1");
    HX_CHECK(AssetDatabase::CreateFolder("Nowhere", "X").Empty());
    Array<String> subs = AssetDatabase::GetSubFolders("Assets");
    HX_CHECK(Has(subs, "Assets/Art") && Has(subs, "Assets/Art 1") && Has(subs, "Assets/Textures") && subs.Size() == 3);
    // Moved with its .meta (the guid stays); refused onto a taken path or into a missing folder.
    HX_CHECK(AssetDatabase::MoveAsset("Assets/Textures/A.png", "Assets/Art/A.png").Empty());
    HX_CHECK(AssetDatabase::AssetPathToGUID("Assets/Art/A.png") == guid && p.Exists("Assets/Art/A.png.meta") && !p.Exists("Assets/Textures/A.png.meta"));
    HX_CHECK(!AssetDatabase::MoveAsset("Assets/Readme.txt", "Assets/Art/A.png").Empty());
    HX_CHECK(!AssetDatabase::ValidateMoveAsset("Assets/Readme.txt", "Assets/None/R.txt").Empty());
    HX_CHECK(!AssetDatabase::MoveAsset("Assets/Art", "Assets/Art/Inner").Empty());
    // Renamed: the extension stays.
    HX_CHECK(AssetDatabase::RenameAsset("Assets/Art/A.png", "Hero").Empty());
    HX_CHECK(AssetDatabase::AssetPathToGUID("Assets/Art/Hero.png") == guid);
    // Copied: a new guid, the settings of the original.
    HX_CHECK(AssetDatabase::CopyAsset("Assets/Art/Hero.png", "Assets/Art/Hero2.png"));
    String copy = AssetDatabase::AssetPathToGUID("Assets/Art/Hero2.png");
    HX_CHECK(copy.Size() == 32 && copy != guid && AssetDatabase::ImporterNameInternal("Assets/Art/Hero2.png") == "TextureImporter");
    // Found by name and type, in folders.
    Array<String> found = AssetDatabase::FindAssetsInternal("hero t:Texture2D", "");
    HX_CHECK(found.Size() == 2 && Has(found, guid.View()) && Has(found, copy.View()));
    HX_CHECK(AssetDatabase::FindAssetsInternal("t:TextAsset", "Assets/Art").Empty());
    HX_CHECK(AssetDatabase::FindAssetsInternal("readme", "").Size() == 1);
    HX_CHECK(AssetDatabase::FindAssetsInternal("t:Folder", "Assets").Size() == 3);
    // Deleted with its .meta; a folder with what it holds.
    HX_CHECK(AssetDatabase::DeleteAsset("Assets/Art/Hero2.png") && !p.Exists("Assets/Art/Hero2.png.meta") && !AssetDatabase::AssetPathExists("Assets/Art/Hero2.png"));
    HX_CHECK(AssetDatabase::DeleteAsset("Assets/Art"));
    HX_CHECK(!AssetDatabase::IsValidFolder("Assets/Art") && AssetDatabase::GUIDToAssetPath(guid.View()).Empty() && !p.Exists("Assets/Art.meta"));
    HX_CHECK(!AssetDatabase::DeleteAsset("Assets"));
    // Batched: one refresh at the end.
    AssetDatabase::StartAssetEditing();
    p.Write("Assets/Later.txt", "later");
    AssetDatabase::Refresh();
    HX_CHECK(!AssetDatabase::AssetPathExists("Assets/Later.txt"));
    AssetDatabase::StopAssetEditing();
    HX_CHECK(AssetDatabase::AssetPathExists("Assets/Later.txt"));
}

HX_TEST(AssetDatabase, ArtifactKeysFollowTheFileTheSettingsAndTheImporter)
{
    TempProject p;
    p.Write("Assets/Textures/A.png", "png");
    HX_REQUIRE(ProjectDatabase::Open(p.root));
    ProjectDatabase::Refresh();
    const AssetRecord* r = ProjectDatabase::ByPath("Assets/Textures/A.png");
    HX_REQUIRE(r != nullptr);
    String key = ArtifactStore::KeyOf(*r);
    HX_CHECK(key.Size() == 32 && key == ArtifactStore::KeyOf(*r));
    // Off (no host turned the imports on): nothing is current, nothing queued.
    HX_CHECK(ArtifactStore::Current(*r).Empty());
    ArtifactStore::Queue(*r);
    HX_CHECK(ArtifactStore::Pending() == 0);
    // The file's content.
    p.Write("Assets/Textures/A.png", "png2");
    String content = ArtifactStore::KeyOf(*r);
    HX_CHECK(content != key);
    // The .meta's settings.
    MetaFile meta;
    HX_REQUIRE(ProjectDatabase::ReadMeta("Assets/Textures/A.png", meta));
    meta.SetValue("isReadable", "1");
    HX_CHECK(ProjectDatabase::WriteMeta("Assets/Textures/A.png", meta));
    String settings = ArtifactStore::KeyOf(*r);
    HX_CHECK(settings != content);
    // The importer's version.
    String json(kImporters);
    usize at = json.View().Find(R"("version": 1)");
    HX_REQUIRE(at != StringView::npos);
    String bumped = Format("{}\"version\": 2{}", json.View().Substr(0, at), json.View().Substr(at + 12));
    AssetImporters::Clear();
    String error;
    HX_REQUIRE(AssetImporters::Load(bumped.View(), error));
    HX_CHECK(ArtifactStore::KeyOf(*r) != settings);
}
