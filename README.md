# Helix.Editor

The editor of the Helix engine: the host HelixEditor (the engine, both render pipelines, the UI and the canvas UI on a
window with the editor's own frame) and the editor written mostly in C# over a native module of registries, so that
menus, toolbars, windows and their layouts, and later overlays and skins come from data and plugins slot in.

## Contents

| Path | Content |
|---|---|
| `Source/Helix/Editor/Host/` | The host HelixEditor: the player core on a window with a frame of its own (elements of the class `editor-caption` move it), the modules registered (the build writes their table from its list), the editor's data folder, `-projectPath`, `-pipeline` (a short name of `Data/Settings/Pipelines.json` or a pipeline asset's class), the floating windows as tool windows of the player |
| `Source/Helix/Editor/Shell/` | The registries of the native module HelixEditor: `EditorMenus` (items by path and priority, groups, shortcuts, check marks, validation; the menu bar's titles) and `EditorToolbar` (the main toolbar's elements by id in left, middle and right groups, strips, presets of their places) |
| `Source/Helix/Editor/Application/` | `EditorApplication` (update, the play mode's flags, Exit, ExecuteMenuItem, the main window's frame) and `EditorPrefs` (a text file of the user's) |
| `Source/HelixEditor.CoreModule/` | The C# side (namespace `HelixEditor`): the bindings and `MenuItem`, `Menu`, `EditorApplication.delayCall` and the play mode events, `Tool` and `Tools`, the main toolbar's elements (`MainToolbarElementAttribute`, `MainToolbarButton`, `MainToolbarToggle`, `MainToolbarDropdown`), the editor's own menu items and toolbar elements, `EditorShell` (the shell built from the registries), `-executeMethod` |
| `Source/HelixEditor.CoreModule/Windows/` | `EditorWindow` (`GetWindow`, `CreateWindow`, `Show`, `Close`, `Focus`, `titleContent`, `rootElement`, `position`, `docked`, `maximized`, the messages `OnEnable`, `OnDisable`, `OnDestroy`, `Update`, `OnFocus`, `OnLostFocus`, `OnGui` drawn by an `ImguiContainer`, `mouseOverWindow`), `EditorGuiUtility.Load` (an asset of the editor's data folder) and the main windows (Hierarchy, Scene, Game, Inspector, Project, Console: their UI from their markup; their content comes with the later steps) |
| `Source/HelixEditor.CoreModule/Docking/` | `DockLayout` (splits, dock areas, floating containers; docking among tabs or at an edge, floating, removing, splitters; the text form), `DockManager` (the session's windows by tab, presets, the project's saved layouts) and the view (tab strips with close buttons, tabs dragged to reorder, dock or float with the drop zone lit in any window, splitters, the tab menu, middle click closes); floating containers in windows of their own (`FloatingWindow`: a display of the host, moved by its tab strip, by its only tab, docked back by dragging a tab onto another window's area) |
| `Skins/HelixClassic.json` | The skin Helix Classic: colors, metrics (the shell's sizes, compact; the dock's floating sizes, read as custom style properties) and each part's look (gradient, border, lit and shaded lines, inner shadow, corners, drop shadow) |
| `Tools/GenerateSkin.py` | Draws a skin's parts into 9-slice images and writes its style sheet (`Data/Skin/<name>.hss`) |
| `Tools/GenerateIcons.py` | Draws the editor's icons from shapes and writes their classes (`Data/Skin/Icons.hss`) |
| `Tools/GenerateData.py` | Writes the data folder's scene, script assets, settings and index |
| `Data/` | The editor's data folder: the shell (`UI/Shell.hxml`, `UI/Shell.hss`), the skin, the icons, the logo, the scene, the layout presets (`Layouts/*.layout`: Default, 2 by 3, 4 Split, Tall, Wide), the main windows' markup (`UI/Windows/*.hxml`), the floating window's (`UI/Floating.hxml`), the editor mode (`Modes/Default.mode`: the main menu's titles in order, where a plugin's new title goes), the pipelines (`Settings/Pipelines.json`) |
| `Tests/` | The registries and preferences; the C# tests (`Tests/Managed`: the dock model, the layout's text, the windows, docking by the view's events, the menus, OnGui; with the editor's window the floating windows) run inside the editor; the shell drawn in batch mode against `Tests/Golden/` on both pipelines (the machine's fonts: compared only off CI) |

## Running

```
HelixEditor -projectPath <folder>
HelixEditor -projectPath <folder> -pipeline physical
```

Every argument of the player works too (`-force-glcore`, `-force-device-index <n>`, `-screen-width`, `-screen-height`).
`-executeMethod <Namespace.Class.Method>` runs a static method once the shell is up (with `-batchmode` it ends the
editor through `EditorApplication.Exit`).

## Layouts

The window layout is kept per project (`UserSettings/Layouts/CurrentLayout.layout`, written on every change); the
Layout dropdown loads a preset of `Data/Layouts` or a saved layout and saves the current one. A layout is text:

```
layout 1
split column 0.7 0.3
  split row 0.22 0.52 0.26
    area 0 HierarchyWindow
    area 0 SceneView GameView
    area 0 InspectorWindow
  area 0 ProjectWindow ConsoleWindow
float 100 80 320 240
  area 0 MyWindow
```

A split lists its children's shares, an area its selected tab and its tabs (window classes by name).

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

## A window from C#

```csharp
using HelixEditor;
using HelixEngine.UI;

public sealed class MyWindow : EditorWindow
{
    [MenuItem("Window/My Window")]
    static void Open() => GetWindow<MyWindow>("Mine");

    void OnEnable() => rootElement.Add(new Label("Hello"));
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
