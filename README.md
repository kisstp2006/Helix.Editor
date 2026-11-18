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
| `Source/Helix/Editor/Assets/` | The asset database: `MetaFile` (a .meta's guid, labels and importer section, values by key path), `AssetImporters` (the importers' descriptions as data, `Data/Settings/Importers.json`), `ProjectDatabase` (one flat table of the project's files and folders by guid and path, kept in `Library/SourceAssetDB.txt`; .meta files written and kept with their assets; the refresh's changes; the engine's asset source of the project), `ArtifactStore` (the imports' artifacts in `Library/Artifacts` under keys of what made them: guid, importer and version, settings, content, the postprocessors' versions and the dependencies' hashes; `Library/ArtifactDB.txt`; imports on the engine's async loads, the artifact written as an import integrates), `AssetDatabase` (the bound members), `ImportNative` (scripted importers, the import context, the postprocessors' calls around the engine's wrapped loaders), `AssetModification` (the modification processors' calls), `EditorUtility` (dirty objects) |
| `Source/Helix/Editor/Scenes/`, `Source/Helix/Editor/Selection/` | `EditorSceneManager` (new, open, save, save as, close, the active scene, dirty scenes, the scenes' events; the open scenes kept in `Library/LastSceneManagerSetup.txt`) and `Selection` (the selected objects and the active one, the change event) |
| `Source/Helix/Editor/Inspector/` | `SerializedNative` (an object's serialized form with its type tree read into a flat table of properties; changes written back as modifications; several objects of one class together; values written back unchanged are no change) and `InspectorNative` (the component classes, C++ and C# scripts alike, added by name; a component moved among its GameObject's) |
| `Source/HelixEditor.CoreModule/Assets/` | `AssetDatabase` (paths and guids, loads, `FindAssets` by name, `t:` and `l:`, folders, move, rename, copy, delete, `CreateAsset` and the objects of native assets, `SaveAssets`, labels, `GetDependencies`, custom dependencies, importer types, the version control queries), `GUID`, `AssetImporter` and its classes (`TextureImporter`, `ModelImporter`, `AudioImporter`, `TrueTypeFontImporter`, `MonoImporter`, `DefaultImporter`), `ScriptedImporter` with `ScriptedImporterAttribute` and `AssetImportContext` (objects, the main object, dependencies on sources, artifacts and custom values), `AssetPostprocessor`, `AssetModificationProcessor` |
| `Source/HelixEditor.CoreModule/Windows/` | `EditorWindow` (`GetWindow` (also docked beside another window's tab), `CreateWindow`, `Show`, `Close`, `Focus`, `titleContent`, `rootElement`, `position`, `docked`, `maximized`, the messages `OnEnable`, `OnDisable`, `OnDestroy`, `Update`, `OnFocus`, `OnLostFocus`, `OnGui` drawn by an `ImguiContainer`, `mouseOverWindow`), `EditorGuiUtility.Load` (an asset of the editor's data folder), `IHasCustomMenu`, `ShowButton` (drawn in the tab strip); the Hierarchy (the open scenes' objects as a tree: selection, rename, drag into and between objects, search, the Create and context menus), the Inspector (a GameObject's header and its components under title bars with their menus, Add Component grouped by `AddComponentMenu` and `Data/Settings/AddComponentMenu.json`, several objects together, lock, Normal and Debug modes, an asset's editor) and the other main windows (Scene, Game, Project, Console: their content comes with the later steps) |
| `Source/HelixEditor.CoreModule/Inspector/` | `SerializedObject` and `SerializedProperty` (every kind, arrays, copies and paths), `Editor` with `CustomEditor` and `CanEditMultipleObjects`, `PropertyDrawer` and `DecoratorDrawer` with `CustomPropertyDrawer` (the drawers of Range, Min, Delayed, Multiline, TextArea, Header, Space), `TransformInspector`, `ObjectNames`; the retained side (`InspectorElement`, `PropertyField`, `Bind`, `BindProperty`, `TrackPropertyValue`, `ObjectField`, `ColorField`, `LayerField`, `TagField`) |
| `Source/HelixEditor.CoreModule/Gui/` | `EditorGui`, `EditorGuiLayout`, `EditorGuiUtility`, `EditorStyles` over the immediate GUI: the fields of every kind (numbers with label drag and expressions), popups and masks, objects with a picker, colors with the color picker window, foldouts, help boxes, the scopes, `PropertyField`, `InspectorTitlebar`; `GenericMenu` |
| `Source/HelixEditor.CoreModule/Docking/` | `DockLayout` (splits, dock areas, floating containers; docking among tabs or at an edge, floating, removing, splitters; the text form), `DockManager` (the session's windows by tab, presets, the project's saved layouts) and the view (tab strips with close buttons, tabs dragged to reorder, dock or float with the drop zone lit in any window, splitters, the tab menu, middle click closes); floating containers in windows of their own (`FloatingWindow`: a display of the host, moved by its tab strip, by its only tab, docked back by dragging a tab onto another window's area) |
| `Skins/HelixClassic.json` | The skin Helix Classic: colors, metrics (the shell's sizes, compact; the dock's floating sizes, read as custom style properties) and each part's look (gradient, border, lit and shaded lines, inner shadow, corners, drop shadow) |
| `Tools/GenerateSkin.py` | Draws a skin's parts into 9-slice images and writes its style sheet (`Data/Skin/<name>.hss`) |
| `Tools/GenerateIcons.py` | Draws the editor's icons from shapes and writes their classes (`Data/Skin/Icons.hss`) |
| `Tools/GenerateData.py` | Writes the data folder's scene, script assets, settings and index |
| `Data/` | The editor's data folder: the importers (`Settings/Importers.json`), the shell (`UI/Shell.hxml`, `UI/Shell.hss`), the skin, the icons, the logo, the scene, the layout presets (`Layouts/*.layout`: Default, 2 by 3, 4 Split, Tall, Wide), the main windows' markup (`UI/Windows/*.hxml`), the floating window's (`UI/Floating.hxml`), the editor mode (`Modes/Default.mode`: the main menu's titles in order, where a plugin's new title goes), the pipelines (`Settings/Pipelines.json`), the serialized fields inspectors leave out and the struct kinds (`Settings/SerializedFields.json`), the Add Component groups of the engine's classes (`Settings/AddComponentMenu.json`) |
| `Tests/` | The registries, preferences, .meta files and the project table; the C# tests (`Tests/Managed`: the dock model, the layout's text, the windows, docking by the view's events, the menus, OnGui; the asset database on `Tests/Projects/Basic` with a scripted importer, postprocessors and a modification processor; scenes, the selection, the Hierarchy, serialized objects, editors and drawers, the editor's GUI and the Inspector; the artifacts over two runs on one project; with the editor's window the floating windows and the main window maximized and restored) run inside the editor on copies of their projects; the shell drawn in batch mode against `Tests/Golden/` on both pipelines (the machine's fonts: compared only off CI) |

## Running

```
HelixEditor -projectPath <folder>
HelixEditor -projectPath <folder> -pipeline physical
```

Every argument of the player works too (`-force-glcore`, `-force-device-index <n>`, `-screen-width`, `-screen-height`).
The project's files get their .meta files and are imported into its `Library/` as it opens and as the editor comes
back to the front; `Application.dataPath` is its `Assets` folder. The project's script assemblies are loaded from
`Library/ScriptAssemblies` (their importers, postprocessors, windows and menu items take part from the start).
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

## An importer and a postprocessor from C#

```csharp
using HelixEditor;
using HelixEditor.AssetImporters;
using HelixEngine;

[ScriptedImporter(1, "cube")]
public sealed class CubeImporter : ScriptedImporter
{
    public override void OnImportAsset(AssetImportContext ctx)
    {
        var t = new Texture2D(8, 8);
        ctx.AddObjectToAsset("texture", t);
        ctx.SetMainObject(t);
    }
}

public sealed class Pixels : AssetPostprocessor
{
    void OnPreprocessTexture()
    {
        if (assetPath.StartsWith("Assets/Pixel/"))
            ((TextureImporter)assetImporter).filterMode = FilterMode.Point;
    }
}
```

## An editor and a drawer from C#

```csharp
using HelixEditor;
using HelixEngine;

public sealed class Spinner : GameBehaviour
{
    [Range(0, 360)] public float speed = 90;
    public Limits wobble;
}

[System.Serializable]
public struct Limits { public float min, max; }

[CustomEditor(typeof(Spinner)), CanEditMultipleObjects]
public sealed class SpinnerEditor : Editor
{
    public override void OnInspectorGui()
    {
        DrawDefaultInspector();
        EditorGuiLayout.HelpBox($"{targets.Length} selected", MessageType.Info);
    }
}

[CustomPropertyDrawer(typeof(Limits))]
public sealed class LimitsDrawer : PropertyDrawer
{
    public override void OnGui(Rect position, SerializedProperty property, GuiContent label)
    {
        Rect r = EditorGui.PrefixLabel(position, label);
        float half = r.width / 2;
        SerializedProperty min = property.FindPropertyRelative("min"), max = property.FindPropertyRelative("max");
        min.floatValue = EditorGui.FloatField(new Rect(r.x, r.y, half - 2, r.height), min.floatValue);
        max.floatValue = EditorGui.FloatField(new Rect(r.x + half, r.y, half, r.height), max.floatValue);
    }
}
```

Helix.Samples' `Projects/InspectorDemo` is a project of these.

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
