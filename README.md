# Helix.Editor

The editor of the Helix engine: the host HelixEditor (the engine, both render pipelines, the UI and the canvas UI on a
window with the editor's own frame) and the editor written mostly in C# over a native module of registries, so that
menus, toolbars and later windows, overlays and skins come from data and plugins slot in.

## Contents

| Path | Content |
|---|---|
| `Source/Helix/Editor/Host/` | The host HelixEditor: the player core on a window with a frame of its own (elements of the class `editor-caption` move it), the modules registered, the editor's data folder, `-projectPath`, `-pipeline` |
| `Source/Helix/Editor/Shell/` | The registries of the native module HelixEditor: `EditorMenus` (items by path and priority, groups, shortcuts, check marks, validation; the menu bar's titles) and `EditorToolbar` (the main toolbar's elements by id in left, middle and right groups, strips, presets of their places) |
| `Source/Helix/Editor/Application/` | `EditorApplication` (update, the play mode's flags, Exit, ExecuteMenuItem, the main window's frame) and `EditorPrefs` (a text file of the user's) |
| `Source/HelixEditor.CoreModule/` | The C# side (namespace `HelixEditor`): the bindings and `MenuItem`, `Menu`, `EditorApplication.delayCall` and the play mode events, `Tool` and `Tools`, the main toolbar's elements (`MainToolbarElementAttribute`, `MainToolbarButton`, `MainToolbarToggle`, `MainToolbarDropdown`), the editor's own menu items and toolbar elements, `EditorShell` (the shell built from the registries) |
| `Skins/HelixClassic.json` | The skin Helix Classic: colors and each part's look (gradient, border, lit and shaded lines, inner shadow, corners, drop shadow) |
| `Tools/GenerateSkin.py` | Draws a skin's parts into 9-slice images and writes its style sheet (`Data/Skin/<name>.hss`) |
| `Tools/GenerateIcons.py` | Draws the editor's icons from shapes and writes their classes (`Data/Skin/Icons.hss`) |
| `Tools/GenerateData.py` | Writes the data folder's scene, script assets, settings and index |
| `Data/` | The editor's data folder: the shell (`UI/Shell.hxml`, `UI/Shell.hss`), the skin, the icons, the logo, the scene |
| `Tests/` | The registries and preferences; the shell drawn in batch mode against `Tests/Golden/` on both pipelines (the machine's fonts: compared only off CI) |

## Running

```
HelixEditor -projectPath <folder>
HelixEditor -projectPath <folder> -pipeline physical
```

Every argument of the player works too (`-force-glcore`, `-force-device-index <n>`, `-screen-width`, `-screen-height`).

## A menu item and a toolbar element from C#

```csharp
using HelixEditor;
using HelixEditor.Toolbars;

static class MyTools
{
    [MenuItem("Tools/Say Hello %#h", false, 10)]
    static void SayHello() => HelixEngine.Debug.Log("Hello");

    [MainToolbarElement("MyTools/Hello", MainToolbarDockPosition.Right, 5)]
    static MainToolbarElement Hello() => new MainToolbarButton(new MainToolbarContent("Hello", null, "Says hello"), SayHello);
}
```

## Build

```
git clone https://github.com/kisstp2006/Helix.Editor
git -C Helix.Editor submodule update --init
cmake --preset windows-release
cmake --build --preset windows-release
ctest --preset windows-release
```

The .NET 11 SDK is needed (`HELIX_DOTNET_ROOT` or `DOTNET_ROOT`). After a change of the skin or the icons run
`python Tools/GenerateSkin.py`, `python Tools/GenerateIcons.py` and `python Tools/GenerateData.py`.

## License

GPL-3.0, see `LICENSE`.
