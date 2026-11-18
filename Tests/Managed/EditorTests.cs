using System;
using System.Collections.Generic;
using HelixEditor.Docking;
using HelixEditor.SceneManagement;
using HelixEngine.SceneManagement;
using HelixEngine;
using HelixEngine.UI;

namespace HelixEditor.Tests
{
    // The editor's C# tests, run in the editor (-batchmode -executeMethod HelixEditor.Tests.EditorTests.Run): the dock
    // model's operations and its text form first, then the session's windows and their docking by the view's events,
    // one step a frame (the view is laid out between them). The editor ends with the number of failures.
    // A scripted importer of the tests: a .cube file holds a size; its asset is a texture of that size.
    [HelixEditor.AssetImporters.ScriptedImporter(1, "cube")]
    public sealed class CubeImporter : HelixEditor.AssetImporters.ScriptedImporter
    {
        public override void OnImportAsset(HelixEditor.AssetImporters.AssetImportContext ctx)
        {
            string text = System.IO.File.ReadAllText(System.IO.Path.Combine(EditorApplication.projectPathInternal, ctx.assetPath)).Trim();
            // "%<name>": a size of 3 that depends on a custom dependency.
            if (text.StartsWith("%"))
            {
                ctx.DependsOnCustomDependency(text.Substring(1));
                text = "3";
            }
            // "@<path>": the size is in another file, the import depending on it.
            if (text.StartsWith("@"))
            {
                string other = text.Substring(1);
                ctx.DependsOnSourceAsset(other);
                string full = System.IO.Path.Combine(EditorApplication.projectPathInternal, other);
                text = System.IO.File.Exists(full) ? System.IO.File.ReadAllText(full).Trim() : "1";
            }
            int size = int.TryParse(text, out int n) ? n : 1;
            var t = new Texture2D(size, size) { name = System.IO.Path.GetFileNameWithoutExtension(ctx.assetPath) };
            ctx.AddObjectToAsset("texture", t);
            ctx.SetMainObject(t);
        }
    }

    // Postprocessors of the assets under Assets/Post/ (the others' imports untouched): what they get, logged.
    public sealed class TestPostprocessor : AssetPostprocessor
    {
        internal static readonly List<string> s_Log = new();

        static bool Mine(string path) => path == "Assets/Post" || path.StartsWith("Assets/Post/", StringComparison.Ordinal);

        public override uint GetVersion() => 1;

        void OnPreprocessAsset()
        {
            if (Mine(assetPath))
                s_Log.Add("asset");
        }

        void OnPreprocessTexture()
        {
            if (!Mine(assetPath))
                return;
            s_Log.Add("pre texture");
            ((TextureImporter)assetImporter).isReadable = true;
        }

        void OnPostprocessTexture(Texture2D texture)
        {
            if (Mine(assetPath))
                s_Log.Add($"post texture {texture.width} {texture.isReadable}");
        }

        internal static int s_Refreshes;

        static void OnPostprocessAllAssets(string[] importedAssets, string[] deletedAssets, string[] movedAssets, string[] movedFromAssetPaths)
        {
            s_Refreshes++;
            foreach (string p in importedAssets)
                if (Mine(p))
                    s_Log.Add("imported " + p);
            foreach (string p in deletedAssets)
                if (Mine(p))
                    s_Log.Add("deleted " + p);
            for (int i = 0; i < movedAssets.Length; i++)
                if (Mine(movedAssets[i]))
                    s_Log.Add($"moved {movedFromAssetPaths[i]} {movedAssets[i]}");
        }
    }

    // The model messages of the assets under Assets/Kinds/.
    public sealed class KindsPostprocessor : AssetPostprocessor
    {
        internal static readonly List<string> s_Log = new();

        void OnPreprocessModel()
        {
            if (assetPath.StartsWith("Assets/Kinds/", StringComparison.Ordinal))
                s_Log.Add("pre model");
        }

        void OnPostprocessModel(GameObject root)
        {
            if (assetPath.StartsWith("Assets/Kinds/", StringComparison.Ordinal))
                s_Log.Add($"post model {root.name}");
        }
    }

    public sealed class LatePostprocessor : AssetPostprocessor
    {
        public override int GetPostprocessOrder() => 10;

        void OnPreprocessAsset()
        {
            if (assetPath.StartsWith("Assets/Post/", StringComparison.Ordinal))
                TestPostprocessor.s_Log.Add("late asset");
        }
    }

    // A modification processor of the assets under Assets/Guard/: refuses some operations, does others itself.
    public sealed class TestModificationProcessor : AssetModificationProcessor
    {
        internal static readonly List<string> s_Log = new();

        static bool Guarded(string path) => path.StartsWith("Assets/Guard/", StringComparison.Ordinal);

        static void OnWillCreateAsset(string assetName)
        {
            if (Guarded(assetName))
                s_Log.Add("create " + assetName);
        }

        static AssetDeleteResult OnWillDeleteAsset(string path, RemoveAssetOptions options)
        {
            if (path == "Assets/Guard/Keep.asset")
                return AssetDeleteResult.FailedDelete;
            if (path != "Assets/Guard/Self.asset")
                return AssetDeleteResult.DidNotDelete;
            string full = System.IO.Path.Combine(EditorApplication.projectPathInternal, path);
            System.IO.File.Delete(full);
            System.IO.File.Delete(full + ".meta");
            s_Log.Add($"deleted {path} {options}");
            return AssetDeleteResult.DidDelete;
        }

        static AssetMoveResult OnWillMoveAsset(string sourcePath, string destinationPath) =>
            destinationPath.StartsWith("Assets/Guard/Locked", StringComparison.Ordinal) ? AssetMoveResult.FailedMove : AssetMoveResult.DidNotMove;

        static string[] OnWillSaveAssets(string[] paths)
        {
            var kept = new List<string>();
            foreach (string p in paths)
                if (!p.EndsWith("NoSave.asset", StringComparison.Ordinal))
                    kept.Add(p);
                else
                    s_Log.Add("not saved " + p);
            return kept.ToArray();
        }

        static bool IsOpenForEdit(string[] paths, List<string> outNotEditablePaths, StatusQueryOptions statusQueryOptions)
        {
            foreach (string p in paths)
                if (p.Contains("ReadOnly"))
                    outNotEditablePaths.Add(p);
            return outNotEditablePaths.Count == 0;
        }

        static bool CanOpenForEdit(string[] paths, List<string> outNotEditablePaths, StatusQueryOptions statusQueryOptions) =>
            IsOpenForEdit(paths, outNotEditablePaths, statusQueryOptions);

        static bool MakeEditable(string[] paths, string prompt, List<string> outNotEditablePaths)
        {
            s_Log.Add("make editable " + string.Join(",", paths));
            return true;
        }
    }

    public enum InspectedMode
    {
        First,
        Second,
        Third,
    }

    [Serializable]
    public struct InspectedPair
    {
        public string key;
        public int value;
    }

    // A script of every kind of field the serialized properties read and write.
    public sealed class Inspected : GameBehaviour
    {
        public int count = 3;
        public float speed = 1.5f;
        public string title = "Hello";
        public bool on = true;
        public InspectedMode mode = InspectedMode.Second;
        public Vector3 offset = new Vector3(1, 2, 3);
        public Color tint = new Color(1, 0, 0, 1);
        public int[] numbers = { 1, 2, 3 };
        public Texture2D icon;
        public GameObject target;
        public InspectedPair pair = new InspectedPair { key = "a", value = 7 };
        [HideInInspector] public int hidden = 9;
        [Tooltip("How far")] public float range = 2;
        [Header("Limits")] [Range(0, 10)] public float limited = 5;
        [Space(30)] [Min(1)] public int atLeast = 3;
        [Multiline(3)] public string notes = "a";
        internal int validated;

        void OnValidate() => validated++;
    }

    // A drawer of the pair struct: one line, its key only.
    [CustomPropertyDrawer(typeof(InspectedPair))]
    public sealed class InspectedPairDrawer : PropertyDrawer
    {
        internal static int s_Drawn;

        public override void OnGui(Rect position, SerializedProperty property, GuiContent label)
        {
            s_Drawn++;
            EditorGui.PropertyField(position, property.FindPropertyRelative("key"), label);
        }

        public override Element CreatePropertyGui(SerializedProperty property) => new Label("pair: " + property.FindPropertyRelative("key").stringValue) { name = "pair-label" };
    }

    [CustomEditor(typeof(Inspected))]
    [CanEditMultipleObjects]
    public sealed class InspectedEditor : Editor
    {
        internal static int s_Enabled, s_Drawn;
        internal int targetsAtEnable;

        void OnEnable()
        {
            s_Enabled++;
            targetsAtEnable = targets.Length;
        }

        public override void OnInspectorGui()
        {
            s_Drawn++;
            DrawDefaultInspector();
        }
    }

    // A window whose OnGui draws an editor and fields (what it saw is kept for the tests).
    public sealed class GuiProbeWindow : EditorWindow
    {
        internal static Editor s_Editor;
        internal static int s_Passes;
        internal static string s_Error;
        internal static float s_ViewWidth;
        internal static Color s_Color = new Color(1, 0, 0, 1);
        internal static int s_Popup = 1;
        internal static bool s_Open = true;

        void OnGui()
        {
            try
            {
                s_Passes++;
                s_ViewWidth = EditorGuiUtility.currentViewWidth;
                if (s_Editor != null)
                    s_Open = EditorGuiLayout.InspectorTitlebar(s_Open, s_Editor);
                s_Editor?.OnInspectorGui();
                s_Color = EditorGuiLayout.ColorField("Tint", s_Color);
                s_Popup = EditorGuiLayout.Popup("Pick", s_Popup, new[] { "A", "B", "C" });
                EditorGuiLayout.HelpBox("A note", MessageType.Info);
                using (new EditorGui.IndentLevelScope())
                    EditorGuiLayout.Vector3Field("Offset", Vector3.one);
                EditorGuiLayout.ObjectField("Object", null, typeof(Texture2D), false);
                EditorGuiLayout.LayerField("Layer", 0);
                EditorGuiLayout.TagField("Tag", "Untagged");
                EditorGuiLayout.EnumFlagsField("Flags", HideFlags.DontSave);
                EditorGuiLayout.BoundsField("Bounds", new Bounds(Vector3.zero, Vector3.one));
            }
            catch (Exception e)
            {
                s_Error = e.ToString();
            }
        }
    }

    public static class EditorTests
    {
        static int s_Failures, s_Checks;
        static readonly Queue<(string name, Action step)> s_Steps = new();

        static void Check(bool condition, string what)
        {
            s_Checks++;
            if (condition)
                return;
            s_Failures++;
            Debug.LogError($"FAILED: {what}");
        }

        static void Equal<T>(T expected, T actual, string what) =>
            Check(EqualityComparer<T>.Default.Equals(expected, actual), $"{what}: expected '{expected}', got '{actual}'");

        static void Equal<T>(T[] expected, T[] actual, string what) =>
            Check(expected != null && actual != null && System.Linq.Enumerable.SequenceEqual(expected, actual),
                  $"{what}: expected [{(expected == null ? "null" : string.Join(", ", expected))}], got [{(actual == null ? "null" : string.Join(", ", actual))}]");

        public static void Run()
        {
            s_Steps.Enqueue(("the shell's view built anew", () =>
            {
                // As when its markup or style sheet changes on disk.
                var view = GameObject.Find("Shell").GetComponent<UIView>();
                view.source = view.source;
            }));
            s_Steps.Enqueue(("a frame", () => { }));
            s_Steps.Enqueue(("the shell again in its new view", () =>
            {
                Element root = GameObject.Find("Shell").GetComponent<UIView>().rootElement;
                Check(root.Find("menu-bar").childCount > 3, "the menu bar's titles");
                Check(root.Find("toolbar-middle").childCount > 0, "the toolbar's elements");
                Check(root.FindAll(null, "dock-area").Count > 0, "the dock's areas");
                Check(root.Find<Label>("title").text.EndsWith("Helix Engine"), "the title");
            }));
            s_Steps.Enqueue(("the layout's text round trip", TextRoundTrip));
            s_Steps.Enqueue(("docking among an area's tabs", DockCenter));
            s_Steps.Enqueue(("docking at an area's edges", DockEdges));
            s_Steps.Enqueue(("removing tabs", RemoveTabs));
            s_Steps.Enqueue(("floating", Floating));
            s_Steps.Enqueue(("moving splitters", Splitters));
            s_Steps.Enqueue(("the start layout's windows", StartWindows));
            s_Steps.Enqueue(("dragging a tab to an edge", DragToEdge));
            s_Steps.Enqueue(("after the drag", AfterDragToEdge));
            s_Steps.Enqueue(("clicking a tab", ClickTab));
            s_Steps.Enqueue(("after the click", AfterClickTab));
            s_Steps.Enqueue(("dragging a tab out", DragOut));
            s_Steps.Enqueue(("after dragging out", AfterDragOut));
            s_Steps.Enqueue(("dragging a splitter", DragSplitter));
            s_Steps.Enqueue(("the window requests", WindowRequests));
            s_Steps.Enqueue(("the presets", Presets));
            s_Steps.Enqueue(("the mode's menu titles", ModeTitles));
            // The menu bar is made again and laid out.
            s_Steps.Enqueue(("a frame", () => { }));
            s_Steps.Enqueue(("opening a menu of the menu bar", OpenMenu));
            s_Steps.Enqueue(("the menu", MenuOpened));
            s_Steps.Enqueue(("its submenu", SubmenuOpened));
            s_Steps.Enqueue(("closing a tab", CloseTabByButton));
            s_Steps.Enqueue(("after closing a tab", AfterCloseTab));
            s_Steps.Enqueue(("an only tab onto its own area", OwnAreaDrop));
            s_Steps.Enqueue(("after it", AfterOwnAreaDrop));
            s_Steps.Enqueue(("a window with OnGui", OpenGuiWindow));
            s_Steps.Enqueue(("its OnGui drawn", GuiWindowDrawn));
            EditorApplication.update += Next;
        }

        static void Next()
        {
            if (s_Steps.Count == 0)
            {
                EditorApplication.update -= Next;
                Debug.Log($"Editor tests: {s_Checks} checks, {s_Failures} failed.");
                EditorApplication.Exit(s_Failures == 0 ? 0 : 1);
                return;
            }
            var (name, step) = s_Steps.Dequeue();
            int before = s_Failures;
            try
            {
                step();
            }
            catch (Exception e)
            {
                s_Failures++;
                Debug.LogError($"FAILED: {name} threw {e}");
            }
            Debug.Log($"{(s_Failures == before ? "ok" : "FAILED")}: {name}");
        }

        /// <summary>The asset database's tests on the project Tests/Projects/Basic.</summary>
        public static void RunAssets()
        {
            s_Steps.Enqueue(("the project's assets", ProjectAssets));
            s_Steps.Enqueue(("loading by path", LoadByPath));
            s_Steps.Enqueue(("a scripted importer", ScriptedImport));
            s_Steps.Enqueue(("an importer's settings", ImporterSettings));
            s_Steps.Enqueue(("the editor's scenes", Scenes));
            s_Steps.Enqueue(("the selection", SelectionOfObjects));
            s_Steps.Enqueue(("the hierarchy's rows", HierarchyRows));
            s_Steps.Enqueue(("the hierarchy's commands", HierarchyCommandsStep));
            s_Steps.Enqueue(("the hierarchy told of changes", HierarchyChanged));
            s_Steps.Enqueue(("the hierarchy's search", HierarchySearch));
            s_Steps.Enqueue(("a Transform's serialized properties", SerializedTransform));
            s_Steps.Enqueue(("a script's serialized properties", SerializedScript));
            s_Steps.Enqueue(("several objects' serialized properties", SerializedSeveral));
            s_Steps.Enqueue(("editors and drawers", EditorsAndDrawers));
            s_Steps.Enqueue(("the editor's GUI in a window", GuiInWindow));
            s_Steps.Enqueue(("a frame", () => { }));
            s_Steps.Enqueue(("a frame", () => { }));
            s_Steps.Enqueue(("what the window drew", GuiDrawn));
            s_Steps.Enqueue(("a retained inspector", RetainedInspector));
            s_Steps.Enqueue(("the Inspector of a GameObject", InspectorOfGameObject));
            s_Steps.Enqueue(("a frame", () => { }));
            s_Steps.Enqueue(("the Inspector left alone", InspectorIdle));
            s_Steps.Enqueue(("a frame", () => { }));
            s_Steps.Enqueue(("a frame", () => { }));
            s_Steps.Enqueue(("the Inspector not built again, its scroll kept", InspectorStillIdle));
            s_Steps.Enqueue(("a frame", () => { }));
            s_Steps.Enqueue(("the scroll after the layout", InspectorScrollKept));
            s_Steps.Enqueue(("the Inspector's header and components", InspectorShown));
            s_Steps.Enqueue(("a frame", () => { }));
            s_Steps.Enqueue(("the Inspector's component menu", InspectorComponentMenu));
            s_Steps.Enqueue(("a frame", () => { }));
            s_Steps.Enqueue(("the Inspector after a move", InspectorMoved));
            s_Steps.Enqueue(("a frame", () => { }));
            s_Steps.Enqueue(("the Inspector of several objects", InspectorSeveral));
            s_Steps.Enqueue(("a frame", () => { }));
            s_Steps.Enqueue(("the Inspector locked, in Debug mode", InspectorLockAndDebug));
            s_Steps.Enqueue(("a frame", () => { }));
            s_Steps.Enqueue(("the Inspector of an asset", InspectorAsset));
            s_Steps.Enqueue(("a frame", () => { }));
            s_Steps.Enqueue(("the asset shown", InspectorAssetShown));
            s_Steps.Enqueue(("guids", Guids));
            s_Steps.Enqueue(("objects and their assets", ObjectsAndAssets));
            s_Steps.Enqueue(("native assets", NativeAssets));
            s_Steps.Enqueue(("labels", Labels));
            s_Steps.Enqueue(("importer types and batches", ImporterTypesAndBatches));
            s_Steps.Enqueue(("asset postprocessors", Postprocessors));
            s_Steps.Enqueue(("modification processors", ModificationProcessors));
            s_Steps.Enqueue(("the importer kinds", ImporterKinds));
            s_Steps.Enqueue(("folders and moves", FoldersAndMoves));
            EditorApplication.update += Next;
        }

        static void Postprocessors()
        {
            string root = EditorApplication.projectPathInternal;
            System.IO.Directory.CreateDirectory(System.IO.Path.Combine(root, "Assets/Post"));
            System.IO.File.Copy(System.IO.Path.Combine(root, "Assets/Textures/Checker.png"), System.IO.Path.Combine(root, "Assets/Post/Tinted.png"));
            TestPostprocessor.s_Log.Clear();
            AssetDatabase.Refresh();
            Equal(new[] { "asset", "pre texture", "late asset", "imported Assets/Post", "imported Assets/Post/Tinted.png" }, TestPostprocessor.s_Log.ToArray(),
                  "before the import, in order, then the refresh's");
            AfterImports("the postprocessed import", () =>
            {
                Texture2D t = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Post/Tinted.png");
                Check(TestPostprocessor.s_Log.Contains("post texture 4 True"), $"after it, on its texture ({string.Join(", ", TestPostprocessor.s_Log)})");
                Check(t != null && t.isReadable, "the importer's change applied to the import");
                string meta = System.IO.File.ReadAllText(System.IO.Path.Combine(root, "Assets/Post/Tinted.png.meta"));
                Check(meta.Contains("isReadable: 0"), "the .meta as it was");
                TestPostprocessor.s_Log.Clear();
                Equal("", AssetDatabase.MoveAsset("Assets/Post/Tinted.png", "Assets/Post/Moved.png"), "moved");
                Equal(new[] { "moved Assets/Post/Tinted.png Assets/Post/Moved.png" }, TestPostprocessor.s_Log.ToArray(), "a move");
                TestPostprocessor.s_Log.Clear();
                Check(AssetDatabase.DeleteAsset("Assets/Post"), "deleted");
                Equal(new[] { "deleted Assets/Post", "deleted Assets/Post/Moved.png" }, TestPostprocessor.s_Log.ToArray(), "the deletions");
            });
        }

        static void ImporterKinds()
        {
            string root = EditorApplication.projectPathInternal;
            System.IO.Directory.CreateDirectory(System.IO.Path.Combine(root, "Assets/Kinds"));
            System.IO.File.WriteAllText(System.IO.Path.Combine(root, "Assets/Kinds/Tri.obj"), "o Tri\nv 0 0 0\nv 1 0 0\nv 0 1 0\nf 1 2 3\n");
            string shaders = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Environment.ProcessPath), "Shaders", "Helix.UI.Shaders.dll");
            System.IO.File.Copy(shaders, System.IO.Path.Combine(root, "Assets/Kinds/UI.dll"));
            KindsPostprocessor.s_Log.Clear();
            AssetDatabase.Refresh();
            Equal(new[] { "pre model" }, KindsPostprocessor.s_Log.ToArray(), "the model preprocessed");
            AfterImports("the kinds imported", () =>
            {
                GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Kinds/Tri.obj");
                Check(model != null, "a model's root");
                bool mesh = false;
                foreach (HelixEngine.Object o in AssetDatabase.LoadAllAssetsAtPath("Assets/Kinds/Tri.obj"))
                    mesh |= o is Mesh m && m.vertexCount == 3;
                Check(mesh, "its mesh");
                Equal(typeof(ModelImporter), AssetDatabase.GetImporterType("Assets/Kinds/Tri.obj"), "the model importer");
                Check(!string.IsNullOrEmpty(AssetDatabase.ArtifactPathInternal("Assets/Kinds/Tri.obj")), "the model's artifact");
                Check(KindsPostprocessor.s_Log.Count == 2 && KindsPostprocessor.s_Log[1].StartsWith("post model ", StringComparison.Ordinal),
                      $"the model postprocessed on its root ({string.Join(", ", KindsPostprocessor.s_Log)})");
                bool shader = false;
                foreach (HelixEngine.Object o in AssetDatabase.LoadAllAssetsAtPath("Assets/Kinds/UI.dll"))
                    shader |= o is Shader;
                Check(shader, "a shader assembly's shaders");
                Check(AssetDatabase.DeleteAsset("Assets/Kinds"), "the kinds deleted");
            });
        }

        static void ModificationProcessors()
        {
            TestModificationProcessor.s_Log.Clear();
            AssetDatabase.CreateFolder("Assets", "Guard");
            var keep = new Texture2D(1, 1) { name = "Keep" };
            AssetDatabase.CreateAsset(keep, "Assets/Guard/Keep.asset");
            Equal(new[] { "create Assets/Guard/Keep.asset.meta" }, TestModificationProcessor.s_Log.ToArray(), "told of the new asset");
            Check(!AssetDatabase.DeleteAsset("Assets/Guard/Keep.asset") && AssetDatabase.AssetPathExists("Assets/Guard/Keep.asset"), "a deletion refused");
            AssetDatabase.CreateAsset(new Texture2D(1, 1) { name = "Self" }, "Assets/Guard/Self.asset");
            Check(AssetDatabase.DeleteAsset("Assets/Guard/Self.asset") && !AssetDatabase.AssetPathExists("Assets/Guard/Self.asset"), "deleted by the processor");
            Check(TestModificationProcessor.s_Log.Contains("deleted Assets/Guard/Self.asset DeleteAssets"), "with the options");
            Check(AssetDatabase.MoveAsset("Assets/Guard/Keep.asset", "Assets/Guard/Locked.asset") != "", "a move refused");
            Check(AssetDatabase.RenameAsset("Assets/Guard/Keep.asset", "Locked") != "" && AssetDatabase.AssetPathExists("Assets/Guard/Keep.asset"), "a rename too");
            var noSave = new Texture2D(1, 1) { name = "NoSave" };
            AssetDatabase.CreateAsset(noSave, "Assets/Guard/NoSave.asset");
            EditorUtility.SetDirty(keep);
            EditorUtility.SetDirty(noSave);
            AssetDatabase.SaveAssets();
            Check(!EditorUtility.IsDirty(keep) && EditorUtility.IsDirty(noSave), "the processor picked what is saved");
            Check(TestModificationProcessor.s_Log.Contains("not saved Assets/Guard/NoSave.asset"), "told of the saves");
            Check(AssetDatabase.IsOpenForEdit("Assets/Guard/Keep.asset") && AssetDatabase.IsOpenForEdit(keep), "open for edit");
            Check(!AssetDatabase.IsOpenForEdit("Assets/Guard/ReadOnly.asset", out string message) && message != "", "not open for edit, with why");
            var notEditable = new List<string>();
            AssetDatabase.IsOpenForEdit(new[] { "Assets/Guard/Keep.asset", "Assets/Guard/ReadOnly.asset" }, notEditable);
            Equal(new[] { "Assets/Guard/ReadOnly.asset" }, notEditable.ToArray(), "the paths not editable");
            Check(!AssetDatabase.CanOpenForEdit("Assets/Guard/ReadOnly.asset") && AssetDatabase.CanOpenForEdit(keep), "can open for edit");
            Check(AssetDatabase.IsMetaFileOpenForEdit(keep), "its .meta open for edit");
            Check(AssetDatabase.MakeEditable("Assets/Guard/Keep.asset") && TestModificationProcessor.s_Log.Contains("make editable Assets/Guard/Keep.asset"),
                  "made editable");
            EditorUtility.ClearDirty(noSave);
            Check(AssetDatabase.DeleteAsset("Assets/Guard") && !AssetDatabase.IsValidFolder("Assets/Guard"), "the folder deleted with what it holds");
        }

        static string[] RootNames(Scene scene) => Array.ConvertAll(scene.GetRootGameObjects(), g => g.name);

        static void Scenes()
        {
            // The project had none: a new scene with the default objects, the shell's objects apart.
            Scene first = EditorSceneManager.GetActiveScene();
            Equal("Untitled", first.name, "a new scene as the project opened");
            Equal(new[] { "Main Camera", "Directional Light" }, RootNames(first), "its default objects");
            Equal(1, SceneManager.sceneCount, "the shell's objects in no scene of the user");
            Check(GameObject.Find("Main Camera").CompareTag("MainCamera"), "the camera's tag");
            var log = new List<string>();
            EditorSceneManager.SceneDirtiedCallback dirtied = s => log.Add("dirtied " + s.name);
            EditorSceneManager.SceneSavedCallback saved = s => log.Add("saved " + s.name);
            EditorSceneManager.SceneOpenedCallback opened = (s, m) => log.Add($"opened {s.name} {m}");
            EditorSceneManager.NewSceneCreatedCallback created = (s, setup, m) => log.Add($"created {setup} {m}");
            EditorSceneManager.sceneDirtied += dirtied;
            EditorSceneManager.sceneSaved += saved;
            EditorSceneManager.sceneOpened += opened;
            EditorSceneManager.newSceneCreated += created;
            try
            {
                Check(EditorSceneManager.MarkSceneDirty(first) && first.isDirty, "marked dirty");
                EditorSceneManager.MarkSceneDirty(first);
                Check(!EditorSceneManager.SaveScene(first), "an untitled scene not saved in batch mode (no dialog)");
                Equal("", EditorUtility.OpenFilePanel("Open", "Assets", "helix"), "no dialogs in batch mode");
                Check(EditorSceneManager.SaveScene(first, "Assets/Scenes/Main.helix"), "saved under a path");
                Check(System.IO.File.Exists(System.IO.Path.Combine(EditorApplication.projectPathInternal, "Assets/Scenes/Main.helix")), "its file");
                Check(AssetDatabase.AssetPathExists("Assets/Scenes/Main.helix"), "an asset of the project");
                Equal("Assets/Scenes/Main.helix", first.path, "its path");
                Equal("Main", first.name, "its name from the path");
                Check(!first.isDirty, "clean after saving");
                Check(!EditorSceneManager.SaveScene(first, "Notes.helix"), "no path outside the project");

                Scene empty = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene);
                Check(empty.IsValid() && empty == EditorSceneManager.GetActiveScene() && empty.rootCount == 0, "a new empty scene, active");
                Equal(1, SceneManager.sceneCount, "alone");

                Scene main = EditorSceneManager.OpenScene("Assets/Scenes/Main.helix");
                Check(main.IsValid() && main.isLoaded && main == EditorSceneManager.GetActiveScene(), "opened again");
                Equal(new[] { "Main Camera", "Directional Light" }, RootNames(main), "with its objects");
                Equal(1, SceneManager.sceneCount, "opened alone");

                Scene extra = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
                Equal(2, SceneManager.sceneCount, "an additive new scene beside it");
                Check(main == EditorSceneManager.GetActiveScene(), "the active one kept");
                Check(EditorSceneManager.CloseScene(extra, true), "closed");
                Equal(1, SceneManager.sceneCount, "one left");
                Check(!EditorSceneManager.CloseScene(main, true), "the last one stays");
                Check(!EditorSceneManager.OpenScene("Assets/Text/Notes.txt").IsValid(), "no scene of a text file");

                string setup = System.IO.File.ReadAllText(System.IO.Path.Combine(EditorApplication.projectPathInternal, "Library/LastSceneManagerSetup.txt"));
                Equal("Assets/Scenes/Main.helix *\n", setup, "the open scenes kept for the next start");
                Equal(new[] { "dirtied Untitled", "saved Main", "created EmptyScene Single", "opened Main Single", "created EmptyScene Additive" },
                      log.ToArray(), "the events");
            }
            finally
            {
                EditorSceneManager.sceneDirtied -= dirtied;
                EditorSceneManager.sceneSaved -= saved;
                EditorSceneManager.sceneOpened -= opened;
                EditorSceneManager.newSceneCreated -= created;
            }
        }

        static void SelectionOfObjects()
        {
            GameObject camera = GameObject.Find("Main Camera");
            GameObject light = GameObject.Find("Directional Light");
            int changes = 0;
            Action changed = () => changes++;
            Selection.selectionChanged += changed;
            try
            {
                Selection.activeObject = camera;
                Check(Selection.activeGameObject == camera && Selection.activeTransform == camera.transform, "the active object");
                Equal(camera.GetEntityId(), Selection.activeEntityId, "its id");
                Check(Selection.count == 1 && Selection.Contains(camera) && !Selection.Contains(light), "one selected");
                Selection.activeObject = camera;
                Equal(1, changes, "told once (the same selection again tells nothing)");

                Selection.objects = new HelixEngine.Object[] { light, camera };
                Check(Selection.activeGameObject == camera && Selection.count == 2, "several, the active one kept");
                Equal(2, Selection.gameObjects.Length, "as GameObjects");
                Equal(2, Selection.entityIds.Length, "as ids");
                Equal(2, changes, "told again");

                light.transform.SetParent(camera.transform);
                Equal(new[] { camera.transform }, Selection.transforms, "the top level only");
                Equal(2, Selection.GetTransforms(SelectionMode.Unfiltered).Length, "or all");
                Equal(1, Selection.GetFiltered<Camera>(SelectionMode.Unfiltered).Length, "a component type through the GameObjects");
                Selection.activeObject = camera;
                Equal(2, Selection.GetFiltered(typeof(GameObject), SelectionMode.Deep).Length, "the children too (Deep)");
                Equal(0, Selection.assetGUIDs.Length, "no assets");

                Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Textures/Checker.png");
                Selection.objects = new HelixEngine.Object[] { camera, texture };
                Equal(new[] { AssetDatabase.AssetPathToGUID("Assets/Textures/Checker.png") }, Selection.assetGUIDs, "the selected asset's guid");
                Equal(new HelixEngine.Object[] { texture }, Selection.GetFiltered(typeof(HelixEngine.Object), SelectionMode.Assets), "the assets only");
                Equal(1, Selection.GetTransforms(SelectionMode.Unfiltered).Length, "an asset has no Transform of a scene");

                Selection.SetActiveObjectWithContext(light, camera);
                Check(Selection.activeObject == light && Selection.activeContext == camera, "with a context");
                Selection.objects = new HelixEngine.Object[] { camera, light };
                HelixEngine.Object.DestroyImmediate(light);
                Check(Selection.count == 1 && Selection.activeGameObject == camera, "a destroyed object drops out");
                Selection.activeObject = null;
                Check(Selection.count == 0 && Selection.activeObject == null, "nothing selected");
            }
            finally
            {
                Selection.selectionChanged -= changed;
            }
        }

        // The rows' texts of the Hierarchy (as shown), after a rebuild.
        static string[] HierarchyTexts(HierarchyWindow h)
        {
            h.RebuildInternal();
            TreeView tree = h.treeInternal;
            int count = tree.viewController.GetItemsCount();
            var texts = new string[count];
            for (int i = 0; i < count; i++)
            {
                var row = tree.GetRootElementForIndex(i);
                texts[i] = row?.Find<Label>("name")?.text ?? "?";
            }
            return texts;
        }

        static int RowOf(HierarchyWindow h, string text) => Array.IndexOf(HierarchyTexts(h), text);

        static void HierarchyRows()
        {
            var h = EditorWindow.GetWindow<HierarchyWindow>();
            // After the scenes step: Main (the light destroyed by the selection step, not marked as a change).
            Equal(new[] { "Main", "Main Camera" }, HierarchyTexts(h), "the scene and its objects");
            GameObject camera = GameObject.Find("Main Camera");
            Selection.activeObject = camera;
            Equal(new[] { RowOf(h, "Main Camera") }, System.Linq.Enumerable.ToArray(h.treeInternal.selectedIndices), "the selection shown");
            h.treeInternal.SetSelection(RowOf(h, "Main"));
            Equal(0, Selection.count, "a scene's row selects no object");
            h.treeInternal.SetSelection(RowOf(h, "Main Camera"));
            Check(Selection.activeGameObject == camera, "a row selects its object");
        }

        static void HierarchyCommandsStep()
        {
            var h = EditorWindow.GetWindow<HierarchyWindow>();
            Scene scene = EditorSceneManager.GetActiveScene();
            EditorSceneManager.SaveScene(scene);
            Check(EditorApplication.ExecuteMenuItem("GameObject/Create Empty"), "Create Empty");
            GameObject a = Selection.activeGameObject;
            Check(a != null && a.name == "GameObject" && a.transform.parent == null, "a new root object, selected");
            Check(scene.isDirty, "its scene changed");
            h.RebuildInternal();
            Check(h.renameFieldInternal != null && h.renameFieldInternal.value == "GameObject", "its name edited at once");
            h.renameFieldInternal.value = "Renamed";
            using (KeyDownEvent e = KeyDownEvent.GetPooled('\r', KeyCode.Return, EventModifiers.None))
            {
                e.target = h.renameFieldInternal;
                h.renameFieldInternal.SendEvent(e);
            }
            Check(a.name == "Renamed" && !h.renamingInternal, "renamed by Enter");
            EditorApplication.ExecuteMenuItem("GameObject/Create Empty");
            GameObject b = Selection.activeGameObject;
            h.RebuildInternal();
            h.BeginRenameInternal(b);
            using (KeyDownEvent e = KeyDownEvent.GetPooled((char)27, KeyCode.Escape, EventModifiers.None))
            {
                e.target = h.renameFieldInternal;
                h.renameFieldInternal.SendEvent(e);
            }
            Equal("GameObject", b.name, "Escape keeps the name");
            Check(EditorApplication.ExecuteMenuItem("GameObject/Create Empty Child"), "Create Empty Child");
            GameObject child = Selection.activeGameObject;
            Check(child.transform.parent == b.transform && child.name == "GameObject", "a child of the active object");
            Selection.activeObject = b;
            EditorApplication.ExecuteMenuItem("GameObject/Create Empty Child");
            Equal("GameObject (1)", Selection.activeGameObject.name, "a sibling's name taken: a number");
            Selection.activeObject = b;
            Check(EditorApplication.ExecuteMenuItem("Edit/Duplicate"), "Duplicate");
            GameObject copy = Selection.activeGameObject;
            Check(copy != b && copy.name == "GameObject (1)" && copy.transform.GetSiblingIndex() == b.transform.GetSiblingIndex() + 1,
                  "a copy after it, numbered");
            Equal(2, copy.transform.childCount, "with its children");
            Check(EditorApplication.ExecuteMenuItem("Edit/Delete"), "Delete");
            Check(copy == null && Selection.count == 0, "the copy deleted");
            Check(EditorApplication.ExecuteMenuItem("GameObject/Camera") && Selection.activeGameObject.GetComponent<Camera>() != null, "a camera");
            Check(EditorApplication.ExecuteMenuItem("GameObject/Light/Directional Light") &&
                  Selection.activeGameObject.GetComponent<Light>().type == LightType.Directional, "a light");
            Equal(new[] { "Main*", "Main Camera", "Renamed", "GameObject", "GameObject", "GameObject (1)", "Camera", "Directional Light" },
                  HierarchyTexts(h), "the rows (expanded to the selection)");
        }

        static int s_HierarchyChanges;

        static void HierarchyChanged()
        {
            var h = EditorWindow.GetWindow<HierarchyWindow>();
            GameObject renamed = GameObject.Find("Renamed");
            GameObject camera = GameObject.Find("Main Camera");
            // Into the camera, before its first child: the world position stays.
            renamed.transform.position = new Vector3(3, 0, 0);
            camera.transform.position = new Vector3(1, 1, 1);
            EditorApplication.hierarchyChanged += () => s_HierarchyChanges++;
            HierarchyWindowMove(renamed, camera.transform, 0);
            Check(renamed.transform.parent == camera.transform && renamed.transform.GetSiblingIndex() == 0, "moved into another");
            Check((renamed.transform.position - new Vector3(3, 0, 0)).magnitude < 1e-4f, "its world position kept");
            renamed.SetActive(false);
            string[] texts = HierarchyTexts(h);
            Check(Array.IndexOf(texts, "Renamed") == Array.IndexOf(texts, "Main Camera") + 1, "shown under it");
            Element row = HierarchyWindow.RowElementInternal(h.treeInternal.GetRootElementForIndex(Array.IndexOf(texts, "Renamed")));
            Check(row != null && row.ClassListContains("hierarchy-inactive"), "an inactive object dim");
            s_Steps.Enqueue(("the change told", () => Check(s_HierarchyChanges > 0, "EditorApplication.hierarchyChanged raised")));
        }

        // The Hierarchy's drop (its commands' Move, the window's drop target aside).
        static void HierarchyWindowMove(GameObject go, Transform parent, int index)
        {
            var type = typeof(HierarchyWindow).Assembly.GetType("HelixEditor.HierarchyCommands");
            type.GetMethod("Move", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)
                .Invoke(null, new object[] { new List<GameObject> { go }, go.scene, parent, index });
        }

        static void HierarchySearch()
        {
            var h = EditorWindow.GetWindow<HierarchyWindow>();
            h.searchFieldInternal.value = "light";
            Equal(new[] { "Main*", "Directional Light" }, HierarchyTexts(h), "the objects whose names hold the text, as a list");
            h.searchFieldInternal.value = "";
            Check(HierarchyTexts(h).Length > 2, "all again");
            var hidden = new GameObject("Editor Only") { hideFlags = HideFlags.HideInHierarchy };
            Check(Array.IndexOf(HierarchyTexts(h), "Editor Only") < 0, "an object hidden in the hierarchy not shown");
            HelixEngine.Object.DestroyImmediate(hidden);
        }

        static List<string> VisibleNames(SerializedObject so)
        {
            var names = new List<string>();
            SerializedProperty it = so.GetIterator();
            for (bool enter = true; it.NextVisible(enter); enter = false)
                names.Add(it.name);
            return names;
        }

        static void SerializedTransform()
        {
            GameObject camera = GameObject.Find("Main Camera");
            camera.transform.localPosition = new Vector3(1, 2, 3);
            Scene scene = camera.scene;
            EditorSceneManager.SaveScene(scene);
            var so = new SerializedObject(camera.transform);
            Check(so.targetObject == camera.transform && !so.isEditingMultipleObjects, "its object");
            List<string> names = VisibleNames(so);
            Check(names.Contains("m_LocalPosition") && names.Contains("m_LocalRotation") && names.Contains("m_LocalScale"), $"the fields ({string.Join(", ", names)})");
            Check(!names.Contains("m_GameObject") && !names.Contains("m_ObjectHideFlags"), "the bookkeeping left out");
            SerializedProperty position = so.FindProperty("m_LocalPosition");
            Equal(SerializedPropertyType.Vector3, position.propertyType, "a Vector3");
            Equal("Local Position", position.displayName, "its display name");
            Equal(new Vector3(1, 2, 3), position.vector3Value, "its value");
            Equal(2f, so.FindProperty("m_LocalPosition.y").floatValue, "a field of it");
            Equal(SerializedPropertyType.Float, so.FindProperty("m_LocalPosition.y").propertyType, "a float");
            Check(!position.hasVisibleChildren && position.hasChildren, "a composite value: one field");
            position.vector3Value = new Vector3(5, 6, 7);
            Check(so.hasModifiedProperties, "a change waiting");
            Equal(new Vector3(1, 2, 3), camera.transform.localPosition, "not yet in the object");
            Equal(new Vector3(5, 6, 7), position.vector3Value, "shown at once");
            Check(so.ApplyModifiedProperties(), "applied");
            Equal(new Vector3(5, 6, 7), camera.transform.localPosition, "in the object");
            Check(!so.hasModifiedProperties && scene.isDirty, "its scene changed");
            camera.transform.localScale = new Vector3(2, 2, 2);
            so.Update();
            Equal(new Vector3(2, 2, 2), so.FindProperty("m_LocalScale").vector3Value, "read again by Update");
            Equal(SerializedPropertyType.Quaternion, so.FindProperty("m_LocalRotation").propertyType, "a Quaternion");
            so.Dispose();
        }

        static void SerializedScript()
        {
            GameObject host = new GameObject("Inspected");
            var c = host.AddComponent<Inspected>();
            GameObject other = GameObject.Find("Main Camera");
            Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Textures/Checker.png");
            var so = new SerializedObject(c);
            List<string> names = VisibleNames(so);
            Check(names.Contains("count") && names.Contains("numbers") && names.Contains("pair"), $"the script's fields ({string.Join(", ", names)})");
            Check(!names.Contains("hidden"), "HideInInspector left out");
            Equal(3, so.FindProperty("count").intValue, "an int");
            Equal(SerializedPropertyType.Integer, so.FindProperty("count").propertyType, "an Integer");
            Equal(1.5f, so.FindProperty("speed").floatValue, "a float");
            Equal("Hello", so.FindProperty("title").stringValue, "a string");
            Check(so.FindProperty("on").boolValue && so.FindProperty("on").propertyType == SerializedPropertyType.Boolean, "a bool");
            SerializedProperty mode = so.FindProperty("mode");
            Equal(SerializedPropertyType.Enum, mode.propertyType, "an enum");
            Equal(1, mode.enumValueIndex, "its index");
            Equal(new[] { "First", "Second", "Third" }, mode.enumDisplayNames, "its names");
            Equal(new Color(1, 0, 0, 1), so.FindProperty("tint").colorValue, "a color");
            Equal(SerializedPropertyType.Color, so.FindProperty("tint").propertyType, "a Color");
            Equal("How far", so.FindProperty("range").tooltip, "a tooltip");
            SerializedProperty numbers = so.FindProperty("numbers");
            Check(numbers.isArray && numbers.arraySize == 3, "an array");
            Equal(2, numbers.GetArrayElementAtIndex(1).intValue, "an element");
            Equal("Element 1", numbers.GetArrayElementAtIndex(1).displayName, "its name");
            Equal("Size", so.FindProperty("numbers.Array.size").displayName, "the size's name");
            Equal("a", so.FindProperty("pair").FindPropertyRelative("key").stringValue, "a struct's field");
            Check(so.FindProperty("pair").hasVisibleChildren, "a struct's fields shown");
            Equal(SerializedPropertyType.ObjectReference, so.FindProperty("icon").propertyType, "a reference");

            so.FindProperty("count").intValue = 10;
            so.FindProperty("speed").floatValue = 0.25f;
            so.FindProperty("title").stringValue = "World";
            so.FindProperty("on").boolValue = false;
            mode.enumValueIndex = 2;
            so.FindProperty("tint").colorValue = new Color(0, 1, 0, 0.5f);
            so.FindProperty("offset").vector3Value = new Vector3(9, 8, 7);
            so.FindProperty("icon").objectReferenceValue = texture;
            so.FindProperty("target").objectReferenceValue = other;
            so.FindProperty("pair.value").intValue = 70;
            Check(so.ApplyModifiedProperties(), "applied");
            Check(c.count == 10 && c.speed == 0.25f && c.title == "World" && !c.on && c.mode == InspectedMode.Third, "the values in the script");
            Equal(new Color(0, 1, 0, 0.5f), c.tint, "the color");
            Equal(new Vector3(9, 8, 7), c.offset, "the vector");
            Check(c.icon == texture && c.target == other, "the references (an asset, an object of the scene)");
            Equal(70, c.pair.value, "the struct's field");
            Check(c.validated > 0, "OnValidate after the change");
            Check(so.FindProperty("icon").objectReferenceValue == texture, "the reference read back");

            numbers = so.FindProperty("numbers");
            numbers.InsertArrayElementAtIndex(1);
            Equal(4, numbers.arraySize, "an element inserted");
            so.ApplyModifiedProperties();
            Equal(new[] { 1, 2, 2, 3 }, c.numbers, "a copy of it after it");
            numbers = so.FindProperty("numbers");
            numbers.DeleteArrayElementAtIndex(0);
            so.ApplyModifiedProperties();
            Equal(new[] { 2, 2, 3 }, c.numbers, "an element deleted");
            numbers = so.FindProperty("numbers");
            Check(numbers.MoveArrayElement(2, 0), "moved");
            so.ApplyModifiedProperties();
            Equal(new[] { 3, 2, 2 }, c.numbers, "an element moved");
            so.FindProperty("numbers").arraySize = 1;
            so.ApplyModifiedProperties();
            Equal(new[] { 3 }, c.numbers, "made shorter");
            c.count = 42;
            so.Update();
            Equal(42, so.FindProperty("count").intValue, "read again");
            so.Dispose();
            HelixEngine.Object.DestroyImmediate(host);
        }

        static void SerializedSeveral()
        {
            var a = new GameObject("A");
            var b = new GameObject("B");
            a.transform.localPosition = new Vector3(1, 0, 0);
            b.transform.localPosition = new Vector3(2, 0, 0);
            var so = new SerializedObject(new HelixEngine.Object[] { a.transform, b.transform });
            Check(so.isEditingMultipleObjects && so.targetObjects.Length == 2, "two objects");
            Check(so.FindProperty("m_LocalPosition").hasMultipleDifferentValues, "their positions differ");
            Check(so.FindProperty("m_LocalPosition.x").hasMultipleDifferentValues && !so.FindProperty("m_LocalPosition.y").hasMultipleDifferentValues,
                  "where they differ");
            Check(!so.FindProperty("m_LocalScale").hasMultipleDifferentValues, "their scales do not");
            so.FindProperty("m_LocalPosition.x").floatValue = 4;
            so.ApplyModifiedProperties();
            Check(a.transform.localPosition.x == 4 && b.transform.localPosition.x == 4, "a change goes to both");
            Check(!so.FindProperty("m_LocalPosition").hasMultipleDifferentValues, "the same now");
            bool threw = false;
            try
            {
                new SerializedObject(new HelixEngine.Object[] { a.transform, a });
            }
            catch (ArgumentException)
            {
                threw = true;
            }
            Check(threw, "objects of different classes refused");
            so.Dispose();
            HelixEngine.Object.DestroyImmediate(a);
            HelixEngine.Object.DestroyImmediate(b);
        }

        static object Internal(string type, string member, params object[] args)
        {
            Type t = typeof(EditorGui).Assembly.GetType(type, true);
            var m = t.GetMethod(member, System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
            return m.Invoke(null, args);
        }

        static GameObject s_Inspected;

        static void EditorsAndDrawers()
        {
            s_Inspected = new GameObject("Inspected Host");
            var c = s_Inspected.AddComponent<Inspected>();
            int enabled = InspectedEditor.s_Enabled;
            Editor e = Editor.CreateEditor(c);
            Check(e is InspectedEditor && e.target == c, "the registered editor of the type");
            Check(InspectedEditor.s_Enabled == enabled + 1 && ((InspectedEditor)e).targetsAtEnable == 1, "its targets known as OnEnable runs");
            Check(e.serializedObject != null && e.serializedObject.targetObject == c, "its serialized object");
            Editor generic = Editor.CreateEditor(s_Inspected);
            Equal("GenericInspector", generic.GetType().Name, "the default inspector without one");
            Editor transform = Editor.CreateEditor(s_Inspected.transform);
            Equal("TransformInspector", transform.GetType().Name, "the Transform's own editor");
            HelixEngine.Object.DestroyImmediate(transform);
            Editor cached = e;
            Editor.CreateCachedEditor(c, null, ref cached);
            Check(cached == e, "a cached editor kept");
            Editor.CreateCachedEditor(s_Inspected.transform, null, ref cached);
            Check(cached != e && cached.target == s_Inspected.transform, "made anew for other targets");
            HelixEngine.Object.DestroyImmediate(cached);
            HelixEngine.Object.DestroyImmediate(generic);

            var so = e.serializedObject;
            Equal(18f, EditorGui.GetPropertyHeight(so.FindProperty("count")), "a line");
            Equal(18f, EditorGui.GetPropertyHeight(so.FindProperty("pair")), "a struct through its drawer");
            Equal(27f + 18f, EditorGui.GetPropertyHeight(so.FindProperty("limited")), "a header above a slider");
            Equal(30f + 18f, EditorGui.GetPropertyHeight(so.FindProperty("atLeast")), "a space above a field");
            Equal(3 * 13 + 6f, EditorGui.GetPropertyHeight(so.FindProperty("notes")), "a multiline string");
            SerializedProperty numbers = so.FindProperty("numbers");
            numbers.isExpanded = false;
            Equal(18f, EditorGui.GetPropertyHeight(numbers), "a closed array");
            numbers.isExpanded = true;
            Equal(18f + 4 * 20f, EditorGui.GetPropertyHeight(numbers), "an open array: its size and its items");
            Equal(36f, EditorGui.GetPropertyHeight(SerializedPropertyType.Bounds, null), "bounds on two lines");

            Equal(6.0, Evaluate("2*3"), "an expression");
            Equal(0.75, Evaluate("(1+2)/4"), "with parentheses");
            Equal(-2.5, Evaluate("-5/2"), "with a sign");
            Check(!(bool)Internal("HelixEditor.EditorGui+Expression", "TryEvaluate", "2*", 0.0), "not an expression");

            var menu = new GenericMenu();
            int ran = 0;
            menu.AddItem(new GuiContent("One"), true, () => ran++);
            menu.AddItem(new GuiContent("Sub/Two"), false, d => ran += (int)d, 10);
            menu.AddSeparator("");
            menu.AddDisabledItem(new GuiContent("Off"));
            menu.AddItem(new GuiContent("One"), false, () => ran += 100);
            Equal(4, menu.GetItemCount(), "items (a duplicate left out)");
            var items = typeof(GenericMenu).GetProperty("itemsInternal", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).GetValue(menu)
                        as System.Collections.Generic.IEnumerable<(string path, bool on, bool enabled, Action run)>;
            foreach (var i in items)
                if (i.enabled)
                    i.run();
            Equal(11, ran, "each item's callback");
            GuiProbeWindow.s_Editor = e;
        }

        static double Evaluate(string text)
        {
            var args = new object[] { text, 0.0 };
            Type t = typeof(EditorGui).Assembly.GetType("HelixEditor.EditorGui+Expression", true);
            bool ok = (bool)t.GetMethod("TryEvaluate", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic).Invoke(null, args);
            return ok ? (double)args[1] : double.NaN;
        }

        static void GuiInWindow()
        {
            var w = EditorWindow.GetWindow<GuiProbeWindow>("Probe", typeof(SceneView));
            w.Focus();
            w.Repaint();
        }

        static void GuiDrawn()
        {
            Check(GuiProbeWindow.s_Passes > 0, $"the window's OnGui ran ({GuiProbeWindow.s_Passes})");
            Check(GuiProbeWindow.s_Error == null, $"without errors ({GuiProbeWindow.s_Error})");
            Check(GuiProbeWindow.s_ViewWidth > 100, $"the view's width known ({GuiProbeWindow.s_ViewWidth})");
            Check(InspectedEditor.s_Drawn > 0, "the custom editor drew");
            Check(InspectedPairDrawer.s_Drawn > 0, "the struct's drawer drew");
            EditorWindow.GetWindow<GuiProbeWindow>().Close();
        }

        static void RetainedInspector()
        {
            var c = s_Inspected.GetComponent<Inspected>();
            c.count = 3;
            Editor generic = Editor.CreateEditor(c, typeof(Editor));
            var inspector = new HelixEditor.UIElements.InspectorElement(generic);
            EditorWindow.GetWindow<InspectorWindow>().rootElement.Add(inspector);
            var count = inspector.Find<HelixEditor.UIElements.PropertyField>("PropertyField:count");
            Check(count != null, "a field of each property");
            var field = count?.Find<IntegerField>();
            Check(field != null && field.value == 3, $"an int as an IntegerField with its value ({field?.value})");
            field.value = 7;
            Equal(7, c.count, "a change written into the object");
            Check(inspector.Find<Label>("pair-label")?.text == "pair: a", "the drawer's own retained GUI");
            Check(inspector.Find<HelixEditor.UIElements.PropertyField>("PropertyField:hidden") == null, "a hidden field left out");
            var title = inspector.Find<HelixEditor.UIElements.PropertyField>("PropertyField:title")?.Find<TextField>();
            Check(title != null && title.value == "World" || title?.value == "Hello", "a string as a TextField");
            c.count = 12;
            int frames = 0;
            Action wait = null;
            wait = () =>
            {
                if (field.value != 12 && ++frames < 120)
                {
                    s_Steps.Enqueue(("the object's change shown", wait));
                    return;
                }
                Equal(12, field.value, "the object's own change shown");
                inspector.RemoveFromHierarchy();
                HelixEngine.Object.DestroyImmediate(generic);
                if (GuiProbeWindow.s_Editor != null)
                    HelixEngine.Object.DestroyImmediate(GuiProbeWindow.s_Editor);
                GuiProbeWindow.s_Editor = null;
                HelixEngine.Object.DestroyImmediate(s_Inspected);
            };
            s_Steps.Enqueue(("the object's change shown", wait));
        }

        static GameObject s_InspectorHost, s_InspectorOther;

        static InspectorWindow Inspector => EditorWindow.GetWindow<InspectorWindow>();

        static Element Section(string type) => Inspector.contentInternal.Find("component:" + type);

        static IEnumerable<(string path, bool on, bool enabled, Action run)> MenuItems(GenericMenu menu) =>
            typeof(GenericMenu).GetProperty("itemsInternal", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).GetValue(menu)
                as IEnumerable<(string path, bool on, bool enabled, Action run)>;

        static void RunItem(GenericMenu menu, string path)
        {
            foreach (var i in MenuItems(menu))
                if (i.path == path)
                {
                    Check(i.enabled, $"'{path}' enabled");
                    if (i.enabled)
                        i.run();
                    return;
                }
            Check(false, $"'{path}' in the menu");
        }

        static bool ItemEnabled(GenericMenu menu, string path)
        {
            foreach (var i in MenuItems(menu))
                if (i.path == path)
                    return i.enabled;
            return false;
        }

        static void InspectorOfGameObject()
        {
            s_InspectorHost = new GameObject("Inspector Host");
            s_InspectorHost.AddComponent<Inspected>();
            s_InspectorOther = new GameObject("Inspector Other");
            s_InspectorOther.AddComponent<Inspected>();
            s_InspectorOther.AddComponent<Light>();
            Inspector.isLocked = false;
            Inspector.inspectorMode = InspectorMode.Normal;
            Selection.activeObject = s_InspectorHost;
        }

        static int s_Rebuilds;

        static void InspectorIdle() => s_Rebuilds = Inspector.rebuildsInternal;

        static void InspectorStillIdle()
        {
            Equal(s_Rebuilds, Inspector.rebuildsInternal, "nothing changed: not built again");
            using (var so = new SerializedObject(s_InspectorHost.GetComponent<Inspected>()))
            {
                so.FindProperty("count").intValue = so.FindProperty("count").intValue;
                so.FindProperty("speed").floatValue = so.FindProperty("speed").floatValue;
                so.FindProperty("title").stringValue = so.FindProperty("title").stringValue;
                Check(!so.hasModifiedProperties, "a value written back as it is: no change");
            }
            // The custom editor's IMGUI as tall as what it laid out.
            var gui = Section("Inspected")?.Find<ImguiContainer>();
            Check(gui != null && gui.layout.height > 100, $"the IMGUI editor's height ({gui?.layout.height})");
            Inspector.scrollInternal.scrollOffset = new Vector2(0, 40);
            Check(Inspector.scrollInternal.scrollOffset.y == 40, $"scrolled ({Inspector.scrollInternal.scrollOffset.y})");
            Inspector.RebuildInternal();
        }

        static void InspectorScrollKept()
        {
            Equal(40f, Inspector.scrollInternal.scrollOffset.y, "built again for the same objects: the scroll kept");
            Inspector.scrollInternal.scrollOffset = Vector2.zero;
        }

        static void InspectorShown()
        {
            Element content = Inspector.contentInternal;
            Check(content.Find("inspector-game-object") != null, "the GameObject's header");
            var name = content.Find<TextField>("inspector-name");
            Equal("Inspector Host", name?.value, "its name");
            Check(Section("Transform") != null && Section("Inspected") != null, "a section of each component");
            Equal("Inspected (Script)", Section("Inspected")?.Find<Label>("component-name")?.text, "a script's title");
            Equal("Transform", Section("Transform")?.Find<Label>("component-name")?.text, "an engine class's title");
            Check(Section("Inspected")?.Find("component-enabled") != null, "a behaviour's enabled toggle");
            Check(Section("Transform")?.Find("component-enabled") == null, "none on the Transform");
            Check(Section("Inspected")?.Find("inspector:Inspected") != null, "the script's editor");

            name.value = "Renamed Host";
            Equal("Renamed Host", s_InspectorHost.name, "a rename written");
            var active = content.Find<Toggle>("inspector-active");
            active.value = false;
            Check(!s_InspectorHost.activeSelf, "deactivated by its toggle");
            active.value = true;
            Check(s_InspectorHost.activeSelf, "and active again");
            content.Find<Toggle>("inspector-static").value = true;
            Check(s_InspectorHost.isStatic, "made static");
            content.Find<Toggle>("inspector-static").value = false;
            Check(!s_InspectorHost.isStatic, "and not");
            var layer = content.Find<HelixEditor.UIElements.LayerField>("inspector-layer");
            layer.value = 5;
            Equal(5, s_InspectorHost.layer, "its layer");
            layer.value = 0;

            var position = Section("Transform")?.Find<Vector3Field>("transform-position");
            var rotation = Section("Transform")?.Find<Vector3Field>("transform-rotation");
            Check(position != null && rotation != null && Section("Transform").Find<Vector3Field>("transform-scale") != null, "the Transform's own fields");
            position.value = new Vector3(1, 2, 3);
            Equal(new Vector3(1, 2, 3), s_InspectorHost.transform.localPosition, "a position written");
            rotation.value = new Vector3(0, 90, 0);
            Check(Math.Abs(s_InspectorHost.transform.localEulerAngles.y - 90) < 0.01f, $"a rotation written ({s_InspectorHost.transform.localEulerAngles})");
            rotation.value = new Vector3(0, 370, 0);
            Equal(new Vector3(0, 370, 0), rotation.value, "the typed angles kept while the rotation is theirs");
            s_InspectorHost.transform.localScale = new Vector3(2, 2, 2);

            var custom = new List<string>();
            var menu = new GenericMenu();
            ((IHasCustomMenu)Inspector).AddItemsToMenu(menu);
            foreach (var i in MenuItems(menu))
                custom.Add(i.path);
            Equal(new[] { "Normal", "Debug", "Lock" }, custom.ToArray(), "the window's own menu items");

            // Add Component: the list of classes, searched, one chosen.
            Element popup = Inspector.ShowAddComponentInternal();
            Check(popup != null && popup.panel != null, "the Add Component list shown");
            var search = popup?.Find<TextField>("add-component-search");
            search.value = "light";
            Check(popup.Find("add:Light") != null, "the engine's class found by its name");
            Check(popup.Find("add:Transform") == null && popup.Find("add:Scripts/EditorShell") == null, "no Transform, none of the editor's classes");
            search.value = "inspected";
            Check(popup.Find("add:Scripts/Inspected") != null, "a script under its group (" +
                  string.Join(",", (System.Collections.Generic.IEnumerable<string>)popup.GetType().GetProperty("shownClassesInternal", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).GetValue(popup)) + ")");
            search.value = "light";
            popup.GetType().GetMethod("Choose", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Invoke(popup, new object[] { "Light" });
            Check(s_InspectorHost.GetComponent<Light>() != null, "the class added");
            Check(popup.panel == null, "and the list closed");
        }

        static void InspectorComponentMenu()
        {
            Check(Section("Light") != null, "the added component's section");
            var c = s_InspectorHost.GetComponent<Inspected>();
            GenericMenu transformMenu = Inspector.ComponentMenuInternal(s_InspectorHost.transform);
            Check(!ItemEnabled(transformMenu, "Remove Component") && !ItemEnabled(transformMenu, "Move Up"), "the Transform neither removed nor moved");
            RunItem(transformMenu, "Reset");
            Equal(Vector3.one, s_InspectorHost.transform.localScale, "the Transform reset");
            Equal(Vector3.zero, s_InspectorHost.transform.localPosition, "its position too");

            c.count = 42;
            c.title = "Copied";
            RunItem(Inspector.ComponentMenuInternal(c), "Copy Component");
            c.count = 1;
            c.title = "Changed";
            RunItem(Inspector.ComponentMenuInternal(c), "Paste Component Values");
            Equal(42, c.count, "a value pasted");
            Equal("Copied", c.title, "a string pasted");
            Check(!ItemEnabled(Inspector.ComponentMenuInternal(s_InspectorHost.GetComponent<Light>()), "Paste Component Values"), "no paste into another class");
            RunItem(Inspector.ComponentMenuInternal(c), "Reset");
            Equal(3, c.count, "a script reset to its class's values");
            Equal("Hello", c.title, "its string too");

            Light light = s_InspectorHost.GetComponent<Light>();
            GenericMenu lightMenu = Inspector.ComponentMenuInternal(light);
            Check(!ItemEnabled(lightMenu, "Move Down"), "the last one not moved down");
            RunItem(lightMenu, "Move Up");
            Component[] all = s_InspectorHost.GetComponents<Component>();
            Check(all.Length == 3 && all[1] == light && all[2] == c, "moved up above the script");
        }

        static void InspectorMoved()
        {
            Element content = Inspector.contentInternal;
            var order = new List<string>();
            foreach (Element e in content.Children())
                if (e.name != null && e.name.StartsWith("component:"))
                    order.Add(e.name.Substring(10));
            Equal(new[] { "Transform", "Light", "Inspected" }, order.ToArray(), "the sections in the components' order");
            RunItem(Inspector.ComponentMenuInternal(s_InspectorHost.GetComponent<Light>()), "Remove Component");
            Check(s_InspectorHost.GetComponent<Light>() == null, "the component removed");
            Selection.objects = new HelixEngine.Object[] { s_InspectorHost, s_InspectorOther };
        }

        static void InspectorSeveral()
        {
            Element content = Inspector.contentInternal;
            Check(Section("Transform") != null && Section("Inspected") != null, "the components both have");
            Check(Section("Light") == null, "not one only the other has");
            Equal("-", content.Find<TextField>("inspector-name")?.value, "names that differ shown as a dash");
            int notes = 0;
            foreach (Element e in content.Children())
                if (e is Label l && l.text.StartsWith("Components that are only"))
                    notes++;
            Equal(1, notes, "the note of the components left out");
            content.Find<Toggle>("inspector-static").value = true;
            Check(s_InspectorHost.isStatic && s_InspectorOther.isStatic, "a change made to both");
            content.Find<Toggle>("inspector-static").value = false;

            Selection.activeObject = s_InspectorHost;
            Inspector.RebuildInternal();
            Inspector.isLocked = true;
            Selection.activeObject = s_InspectorOther;
        }

        static void InspectorLockAndDebug()
        {
            Equal("Renamed Host", Inspector.contentInternal.Find<TextField>("inspector-name")?.value, "locked: the objects kept");
            Inspector.isLocked = false;
            Inspector.RebuildInternal();
            Equal("Inspector Other", Inspector.contentInternal.Find<TextField>("inspector-name")?.value, "unlocked: the selection's");
            Inspector.inspectorMode = InspectorMode.Debug;
            Inspector.RebuildInternal();
            Check(Section("Transform")?.Find("PropertyField:m_LocalRotation") != null, "Debug: the Transform's own fields");
            Check(Section("Inspected")?.Find("PropertyField:hidden") != null, "Debug: a hidden field shown");
            foreach (Editor e in Inspector.editorsInternal)
                Equal("GenericInspector", e.GetType().Name, "Debug: the default inspector each");
            Inspector.inspectorMode = InspectorMode.Normal;
            Inspector.RebuildInternal();
            Check(Section("Transform")?.Find("transform-position") != null, "Normal again");
        }

        static void InspectorAsset()
        {
            Selection.activeObject = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Textures/Checker.png");
        }

        static void InspectorAssetShown()
        {
            Element content = Inspector.contentInternal;
            Check(content.Find("inspector-object") != null, "an asset's header");
            Equal("Checker", content.Find<Label>("inspector-object-name")?.text, "its name");
            Check(content.Find("inspector:Texture2D") != null, "its editor");
            Selection.objects = Array.Empty<HelixEngine.Object>();
            Inspector.RebuildInternal();
            Equal(0, Inspector.contentInternal.childCount, "nothing selected: nothing shown");
            HelixEngine.Object.DestroyImmediate(s_InspectorHost);
            HelixEngine.Object.DestroyImmediate(s_InspectorOther);
        }

        static void Guids()
        {
            GUID g = AssetDatabase.GUIDFromAssetPath("Assets/Textures/Checker.png");
            Check(!g.Empty(), "a guid of a path");
            Equal(AssetDatabase.AssetPathToGUID("Assets/Textures/Checker.png"), g.ToString(), "the same as its text");
            Equal("Assets/Textures/Checker.png", AssetDatabase.GUIDToAssetPath(g), "and back");
            Check(GUID.TryParse(g.ToString().ToUpperInvariant(), out GUID upper) && upper == g, "parsed in either case");
            Check(!GUID.TryParse("xyz", out GUID bad) && bad.Empty(), "not a guid");
            Check(GUID.Generate() != GUID.Generate(), "new guids");
            Texture2D t = AssetDatabase.LoadAssetByGUID<Texture2D>(g);
            Check(t != null && t.width == 4, "loaded by guid");
            GUID[] found = AssetDatabase.FindAssetGUIDs("checker t:Texture2D");
            Check(found.Length == 1 && found[0] == g, "found as guids");
            Check(AssetDatabase.GUIDFromAssetPath("Assets/None.png").Empty(), "no guid for no asset");
        }

        static void ObjectsAndAssets()
        {
            Texture2D t = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Textures/Checker.png");
            Equal("Assets/Textures/Checker.png", AssetDatabase.GetAssetPath(t), "an object's asset path");
            Equal("Assets/Textures/Checker.png", AssetDatabase.GetAssetOrScenePath(t), "or scene path");
            Check(AssetDatabase.Contains(t) && AssetDatabase.IsMainAsset(t) && !AssetDatabase.IsSubAsset(t), "the main object of an asset");
            Check(AssetDatabase.IsForeignAsset(t) && !AssetDatabase.IsNativeAsset(t), "imported from another format");
            Check(AssetDatabase.TryGetGUIDAndLocalFileIdentifier(t, out string guid, out long id), "its guid and fileID");
            Equal(AssetDatabase.AssetPathToGUID("Assets/Textures/Checker.png"), guid, "the guid");
            Equal(2800000L, id, "the main texture's fileID");
            var loose = new Texture2D(1, 1);
            Check(!AssetDatabase.Contains(loose) && AssetDatabase.GetAssetPath(loose) == "", "an object of no asset");
            Check(!AssetDatabase.TryGetGUIDAndLocalFileIdentifier(loose, out _, out _), "no guid for it");
            HelixEngine.Object.DestroyImmediate(loose);
            Equal(typeof(Texture2D), AssetDatabase.GetMainAssetTypeAtPath("Assets/Textures/Checker.png"), "the main type");
            Equal(typeof(Texture2D), AssetDatabase.GetTypeFromPathAndFileID("Assets/Textures/Checker.png", 2800000), "the type of a fileID");
            Equal(null, AssetDatabase.GetMainAssetTypeAtPath("Assets/None.png"), "no type for no asset");
            Check(AssetDatabase.IsMainAssetAtPathLoaded("Assets/Textures/Checker.png"), "loaded");
            Equal("Assets/Textures/Checker.png.meta", AssetDatabase.GetTextMetaFilePathFromAssetPath("Assets/Textures/Checker.png"), "the .meta's path");
            Equal("Assets/Textures/Checker.png", AssetDatabase.GetAssetPathFromTextMetaFilePath("Assets/Textures/Checker.png.meta"), "the asset's path");
            Equal("Assets/Textures/Checker 1.png", AssetDatabase.GenerateUniqueAssetPath("Assets/Textures/Checker.png"), "a free path");
            Equal("Assets/Textures/New.png", AssetDatabase.GenerateUniqueAssetPath("Assets/Textures/New.png"), "a free path as it is");
            Equal("", AssetDatabase.GenerateUniqueAssetPath("Nowhere/X.png"), "no path outside the project");
        }

        static int Documents(string path)
        {
            string text = System.IO.File.ReadAllText(System.IO.Path.Combine(EditorApplication.projectPathInternal, path));
            int n = 0;
            for (int at = text.IndexOf("--- !h!", StringComparison.Ordinal); at >= 0; at = text.IndexOf("--- !h!", at + 1, StringComparison.Ordinal))
                n++;
            return n;
        }

        static void NativeAssets()
        {
            var made = new Texture2D(2, 2) { name = "Made" };
            AssetDatabase.CreateAsset(made, "Assets/Made.asset");
            Check(AssetDatabase.AssetPathExists("Assets/Made.asset"), "an asset made");
            Equal("Assets/Made.asset", AssetDatabase.GetAssetPath(made), "the object is its asset");
            Check(AssetDatabase.IsMainAsset(made) && AssetDatabase.IsNativeAsset(made), "its native main object");
            Check(ReferenceEquals(AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Made.asset"), made), "loaded as the same object");
            Equal(1, Documents("Assets/Made.asset"), "one object in its file");
            var sub = new Texture2D(1, 1) { name = "Sub" };
            AssetDatabase.AddObjectToAsset(sub, made);
            Check(AssetDatabase.IsSubAsset(sub) && AssetDatabase.GetAssetPath(sub) == "Assets/Made.asset", "an object added");
            Check(EditorUtility.IsDirty(made), "the asset dirty until saved");
            Equal(1, Documents("Assets/Made.asset"), "not written yet");
            AssetDatabase.SaveAssets();
            Check(!EditorUtility.IsDirty(made), "saved");
            Equal(2, Documents("Assets/Made.asset"), "two objects in its file");
            Equal(2, AssetDatabase.LoadAllAssetsAtPath("Assets/Made.asset").Length, "two objects loaded");
            AssetDatabase.SetMainObject(sub, "Assets/Made.asset");
            Check(AssetDatabase.IsMainAsset(sub) && AssetDatabase.IsSubAsset(made), "the main object set");
            AssetDatabase.RemoveObjectFromAsset(made);
            Check(!AssetDatabase.Contains(made), "an object removed");
            AssetDatabase.SaveAssetIfDirty(sub);
            Equal(1, Documents("Assets/Made.asset"), "one object left in its file");
            EditorUtility.SetDirty(sub);
            EditorUtility.SetDirty(sub);
            // Marked by SetMainObject, RemoveObjectFromAsset and twice here.
            Equal(4, EditorUtility.GetDirtyCount(sub), "each mark counted");
            AssetDatabase.SaveAssetIfDirty(AssetDatabase.GUIDFromAssetPath("Assets/Made.asset"));
            Check(!EditorUtility.IsDirty(sub), "saved by guid");
            // Made again at the same path: the old one replaced.
            string oldGuid = AssetDatabase.AssetPathToGUID("Assets/Made.asset");
            var again = new Texture2D(3, 3) { name = "Again" };
            AssetDatabase.CreateAsset(again, "Assets/Made.asset");
            Check(AssetDatabase.AssetPathToGUID("Assets/Made.asset") != oldGuid && AssetDatabase.IsMainAsset(again), "replaced");
            Equal(new[] { "Assets/Made.asset" }, AssetDatabase.GetDependencies("Assets/Made.asset"), "a native asset depends on itself only");
            Check(AssetDatabase.GetAssetDependencyHash("Assets/Made.asset").isValid, "its dependency hash");
            AssetDatabase.DeleteAsset("Assets/Made.asset");
            HelixEngine.Object.DestroyImmediate(made);
        }

        static void Labels()
        {
            Texture2D t = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Textures/Checker.png");
            Equal(0, AssetDatabase.GetLabels(t).Length, "no labels");
            AssetDatabase.SetLabels(t, new[] { "Pattern", "Grid", "Grid" });
            Equal(new[] { "Pattern", "Grid" }, AssetDatabase.GetLabels(t), "labels set once each");
            string meta = System.IO.File.ReadAllText(System.IO.Path.Combine(EditorApplication.projectPathInternal, "Assets/Textures/Checker.png.meta"));
            Check(meta.Contains("labels:\n- Pattern\n- Grid\n"), "in the .meta");
            Equal(new[] { AssetDatabase.AssetPathToGUID("Assets/Textures/Checker.png") }, AssetDatabase.FindAssets("l:grid"), "found by label");
            Equal(0, AssetDatabase.FindAssets("l:none").Length, "not by another");
            Equal(new[] { "Grid" }, AssetDatabase.GetLabels(AssetDatabase.GUIDFromAssetPath("Assets/Textures/Checker.png")).AsSpan(1).ToArray(), "by guid");
            AssetDatabase.ClearLabels(t);
            Equal(0, AssetDatabase.GetLabels(t).Length, "cleared");
        }

        static void ImporterTypesAndBatches()
        {
            Equal(typeof(TextureImporter), AssetDatabase.GetImporterType("Assets/Textures/Checker.png"), "an asset's importer");
            Equal(typeof(CubeImporter), AssetDatabase.GetImporterType(AssetDatabase.GUIDFromAssetPath("Assets/Data/Board.cube")), "a scripted one");
            Equal(typeof(TextureImporter), AssetDatabase.GetDefaultImporter("Assets/Textures/Checker.png"), "the default importer");
            Check(Array.IndexOf(AssetDatabase.GetAvailableImporters("Assets/Data/Board.cube"), typeof(CubeImporter)) >= 0, "the scripted importer available");
            Equal(new[] { typeof(TextureImporter), null }, AssetDatabase.GetImporterTypes(new[] { "Assets/Textures/Checker.png", "Assets/None.png" }), "of several");
            Check(AssetDatabase.CopyAssets(new[] { "Assets/Text/Notes.txt", "Assets/Text/Notes.txt" }, new[] { "Assets/Text/A.txt", "Assets/Text/B.txt" }),
                  "copied in one batch");
            Check(AssetDatabase.AssetPathExists("Assets/Text/A.txt") && AssetDatabase.AssetPathExists("Assets/Text/B.txt"), "both copies");
            Check(!AssetDatabase.CopyAssets(new[] { "Assets/Text/A.txt" }, new string[0]), "lists of different lengths");
            var failed = new List<string>();
            Check(!AssetDatabase.DeleteAssets(new[] { "Assets/Text/A.txt", "Assets/Text/None.txt", "Assets/Text/B.txt" }, failed), "one deletion failed");
            Equal(new[] { "Assets/Text/None.txt" }, failed.ToArray(), "the failed path");
            Check(!AssetDatabase.AssetPathExists("Assets/Text/A.txt") && !AssetDatabase.AssetPathExists("Assets/Text/B.txt"), "the others deleted");
            AssetDatabase.DisallowAutoRefresh();
            AssetDatabase.AllowAutoRefresh();
        }

        static void ProjectAssets()
        {
            var paths = new List<string>(AssetDatabase.GetAllAssetPaths());
            Check(paths.Contains("Assets/Textures/Checker.png") && paths.Contains("Assets/Text/Notes.txt") && paths.Contains("Assets/Textures"),
                  "the project's files and folders");
            string guid = AssetDatabase.AssetPathToGUID("Assets/Textures/Checker.png");
            Equal(32, guid.Length, "a guid");
            Equal("Assets/Textures/Checker.png", AssetDatabase.GUIDToAssetPath(guid), "and back");
            Check(System.IO.File.Exists(System.IO.Path.Combine(EditorApplication.projectPathInternal, "Assets/Textures/Checker.png.meta")), "its .meta written");
            Equal("TextureImporter", AssetDatabase.ImporterNameInternal("Assets/Textures/Checker.png"), "the texture importer");
            Equal("TextScriptImporter", AssetDatabase.ImporterNameInternal("Assets/Text/Notes.txt"), "the text importer");
            var textures = AssetDatabase.FindAssets("t:Texture2D");
            Check(textures.Length == 1 && textures[0] == guid, "found by type");
            Equal(1, AssetDatabase.FindAssets("notes", new[] { "Assets/Text" }).Length, "found by name in a folder");
            Equal(0, AssetDatabase.FindAssets("notes", new[] { "Assets/Textures" }).Length, "not in another folder");
            Check(AssetDatabase.IsValidFolder("Assets/Text") && !AssetDatabase.IsValidFolder("Assets/Text/Notes.txt"), "folders");
        }

        static void LoadByPath()
        {
            Texture2D t = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Textures/Checker.png");
            Check(t != null && t.width == 4 && t.height == 4, "the texture loaded by its path");
            Check(AssetDatabase.LoadMainAssetAtPath("Assets/Textures/Checker.png") == t, "its main object");
            Check(AssetDatabase.LoadAssetAtPath<Material>("Assets/Textures/Checker.png") == null, "no material in it");
            Check(AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/None.png") == null, "nothing at a missing path");
        }

        /// <summary>A first run imports the project and writes the artifacts; the second loads from them.</summary>
        public static void RunArtifactsFirst()
        {
            int frames = 0;
            Action written = null;
            written = () =>
            {
                if (AssetDatabase.ImportsPendingInternal() > 0 && ++frames < 300)
                {
                    s_Steps.Enqueue(("the artifacts written", written));
                    return;
                }
                ArtifactsWritten();
            };
            s_Steps.Enqueue(("a cube that depends on another file", () =>
            {
                string root = EditorApplication.projectPathInternal;
                System.IO.File.WriteAllText(System.IO.Path.Combine(root, "Assets/Data/Size.txt"), "6");
                System.IO.File.WriteAllText(System.IO.Path.Combine(root, "Assets/Data/Linked.cube"), "@Assets/Data/Size.txt");
                System.IO.File.WriteAllText(System.IO.Path.Combine(root, "Assets/Data/Custom.cube"), "%cube-size");
                Check(EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene(), "Assets/Scenes/Start.helix"), "a scene saved");
                AssetDatabase.RegisterCustomDependency("cube-size", Hash128.Compute("one"));
                AssetDatabase.Refresh();
            }));
            s_Steps.Enqueue(("the artifacts written", written));
            EditorApplication.update += Next;
        }

        public static void RunArtifactsSecond()
        {
            s_Steps.Enqueue(("a stray artifact swept", () => Equal(4, ArtifactFiles(), "only the entries' files")));
            s_Steps.Enqueue(("the project as it was left", () => Equal(0, TestPostprocessor.s_Refreshes, "no changes reported as it opens")));
            s_Steps.Enqueue(("the last scene opened again", () => Equal("Assets/Scenes/Start.helix", EditorSceneManager.GetActiveScene().path, "the active scene")));
            s_Steps.Enqueue(("loaded from the artifacts", LoadedFromArtifacts));
            s_Steps.Enqueue(("a changed setting imports again", SettingChanged));
            s_Steps.Enqueue(("a changed dependency imports again", DependencyChanged));
            s_Steps.Enqueue(("a custom dependency's new value imports again", CustomChanged));
            EditorApplication.update += Next;
        }

        static void ArtifactsWritten()
        {
            Equal(0, AssetDatabase.ImportsPendingInternal(), "no imports left");
            Check(AssetDatabase.ArtifactsWrittenInternal() >= 4, $"the project's assets imported ({AssetDatabase.ArtifactsWrittenInternal()})");
            Equal("", AssetDatabase.ArtifactPathInternal("Assets/Text/Notes.txt"), "no artifact of a kind without a loader");
            foreach (string p in new[] { "Assets/Textures/Checker.png", "Assets/Data/Board.cube", "Assets/Data/Linked.cube" })
            {
                string a = AssetDatabase.ArtifactPathInternal(p);
                Check(!string.IsNullOrEmpty(a) && System.IO.File.Exists(a), $"the artifact of {p}");
            }
            Check(System.IO.File.Exists(System.IO.Path.Combine(EditorApplication.projectPathInternal, "Library/ArtifactDB.txt")), "the artifact database");
            Equal(4, ArtifactFiles(), "an artifact each");
            Equal(new[] { "Assets/Data/Linked.cube", "Assets/Data/Size.txt" }, AssetDatabase.GetDependencies("Assets/Data/Linked.cube"), "the recorded dependencies");
            Equal(new[] { "Assets/Data/Linked.cube" }, AssetDatabase.GetDependencies("Assets/Data/Linked.cube", false).AsSpan(0, 1).ToArray(), "itself first");
            // A file no entry names (an editor stopped before it saved its database): swept by the next start.
            System.IO.Directory.CreateDirectory(System.IO.Path.Combine(ArtifactsFolder, "zz"));
            System.IO.File.WriteAllText(System.IO.Path.Combine(ArtifactsFolder, "zz", "zz000000000000000000000000000000"), "stray");
        }

        static void LoadedFromArtifacts()
        {
            Equal(0, AssetDatabase.ImportsPendingInternal(), "nothing to import");
            Equal(0, AssetDatabase.ArtifactsWrittenInternal(), "no artifact written again");
            Texture2D t = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Textures/Checker.png");
            Check(t != null && t.width == 4, "the texture from its artifact");
            Texture2D b = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Data/Board.cube");
            Check(b != null && b.width == 8 && b.name == "Board", "the scripted importer's texture from its artifact");
            Texture2D l = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Data/Linked.cube");
            Check(l != null && l.width == 6, "the size from the file it depends on");
        }

        // Steps that run once the imports in progress finished (or 300 frames passed).
        static void AfterImports(string name, Action step)
        {
            int frames = 0;
            Action wait = null;
            wait = () =>
            {
                if (AssetDatabase.ImportsPendingInternal() > 0 && ++frames < 300)
                {
                    s_Steps.Enqueue((name, wait));
                    return;
                }
                step();
            };
            s_Steps.Enqueue((name, wait));
        }

        static string ArtifactsFolder => System.IO.Path.Combine(EditorApplication.projectPathInternal, "Library/Artifacts");

        static int ArtifactFiles() =>
            System.IO.Directory.Exists(ArtifactsFolder) ? System.IO.Directory.GetFiles(ArtifactsFolder, "*", System.IO.SearchOption.AllDirectories).Length : 0;

        static void DependencyChanged()
        {
            string before = AssetDatabase.ArtifactPathInternal("Assets/Data/Linked.cube");
            Texture2D l = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Data/Linked.cube");
            System.IO.File.WriteAllText(System.IO.Path.Combine(EditorApplication.projectPathInternal, "Assets/Data/Size.txt"), "5");
            AssetDatabase.Refresh();
            Check(l != null && l.width == 5, "the loaded dependent imported again into its object");
            AfterImports("the dependent's new artifact", () =>
            {
                string after = AssetDatabase.ArtifactPathInternal("Assets/Data/Linked.cube");
                Check(!string.IsNullOrEmpty(after) && after != before, "a new artifact of the dependent");
                Check(!string.IsNullOrEmpty(AssetDatabase.ArtifactPathInternal("Assets/Data/Board.cube")), "the other cube's artifact kept");
                Check(!System.IO.File.Exists(before), "the superseded artifact removed");
                Equal(4, ArtifactFiles(), "one artifact a loaded asset");
                // A deleted asset's artifact goes with it.
                string board = AssetDatabase.ArtifactPathInternal("Assets/Data/Board.cube");
                Check(AssetDatabase.DeleteAsset("Assets/Data/Board.cube"), "the cube deleted");
                Check(!System.IO.File.Exists(board), "the deleted asset's artifact removed");
                Equal(3, ArtifactFiles(), "the artifacts left");
            });
        }

        static void CustomChanged()
        {
            string before = AssetDatabase.ArtifactPathInternal("Assets/Data/Custom.cube");
            Hash128 hash = AssetDatabase.GetAssetDependencyHash("Assets/Data/Custom.cube");
            Check(!string.IsNullOrEmpty(before), "the custom cube's artifact");
            // The same value: nothing to import.
            AssetDatabase.RegisterCustomDependency("cube-size", Hash128.Compute("one"));
            AssetDatabase.Refresh();
            Equal(before, AssetDatabase.ArtifactPathInternal("Assets/Data/Custom.cube"), "the same value keeps the artifact");
            AssetDatabase.RegisterCustomDependency("cube-size", Hash128.Compute("two"));
            AssetDatabase.Refresh();
            Equal("", AssetDatabase.ArtifactPathInternal("Assets/Data/Custom.cube"), "a new value: no longer current");
            AfterImports("the custom cube's new artifact", () =>
            {
                string after = AssetDatabase.ArtifactPathInternal("Assets/Data/Custom.cube");
                Check(!string.IsNullOrEmpty(after) && after != before, "a new artifact");
                Check(AssetDatabase.GetAssetDependencyHash("Assets/Data/Custom.cube") != hash, "a new dependency hash");
                Equal(1u, AssetDatabase.UnregisterCustomDependencyPrefixFilter("cube-"), "unregistered by prefix");
            });
        }

        static void SettingChanged()
        {
            var importer = (TextureImporter)AssetImporter.GetAtPath("Assets/Textures/Checker.png");
            string before = AssetDatabase.ArtifactPathInternal("Assets/Textures/Checker.png");
            importer.maxTextureSize = 1024;
            importer.SaveAndReimport();
            // A loaded asset is imported again at once (its artifact written as it integrates).
            int frames = 0;
            Action newArtifact = null;
            newArtifact = () =>
            {
                // The import finishes on a later frame (its decode runs on jobs).
                if (AssetDatabase.ImportsPendingInternal() > 0 && ++frames < 300)
                {
                    s_Steps.Enqueue(("the new artifact", newArtifact));
                    return;
                }
                string after = AssetDatabase.ArtifactPathInternal("Assets/Textures/Checker.png");
                Check(!string.IsNullOrEmpty(after) && after != before, "a new artifact under a new key");
                Texture2D t = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Textures/Checker.png");
                Check(t != null && t.width == 4, "the texture after its reimport");
            };
            s_Steps.Enqueue(("the new artifact", newArtifact));
        }

        static void ScriptedImport()
        {
            Equal("ScriptedImporter", AssetDatabase.ImporterNameInternal("Assets/Data/Board.cube"), "the .meta's section");
            var importer = AssetImporter.GetAtPath("Assets/Data/Board.cube");
            Check(importer is CubeImporter, $"the importer's class ({importer?.GetType().Name})");
            Texture2D t = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Data/Board.cube");
            Check(t != null && t.width == 8 && t.height == 8, "the importer's texture of the file's size");
            Check(t != null && t.name == "Board", "named by the importer");
            Check(AssetDatabase.LoadMainAssetAtPath("Assets/Data/Board.cube") == t, "its main object");
            Check(AssetDatabase.FindAssets("board").Length == 1, "found");
        }

        static void ImporterSettings()
        {
            var importer = AssetImporter.GetAtPath("Assets/Textures/Checker.png") as TextureImporter;
            Check(importer != null, "a TextureImporter");
            if (importer == null)
                return;
            Check(!importer.isReadable && importer.mipmapEnabled && importer.maxTextureSize == 2048, "the defaults of its .meta");
            Texture2D before = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Textures/Checker.png");
            Check(before != null && !before.isReadable, "not readable as imported");
            importer.isReadable = true;
            importer.filterMode = FilterMode.Point;
            importer.userData = "kept";
            importer.SaveAndReimport();
            var again = AssetImporter.GetAtPath("Assets/Textures/Checker.png") as TextureImporter;
            Check(again.isReadable && again.filterMode == FilterMode.Point && again.userData == "kept", "the settings in its .meta");
            Texture2D after = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Textures/Checker.png");
            Check(after == before, "the same texture object");
            Check(after != null && after.isReadable && after.filterMode == FilterMode.Point, "imported again with them");
            var model = AssetImporter.GetAtPath("Assets/Text/Notes.txt");
            Check(model != null && model.GetType() == typeof(AssetImporter), "a text file's plain importer");
            Check(AssetImporter.GetAtPath("Assets/None.png") == null, "none for no asset");
        }

        static void FoldersAndMoves()
        {
            string guid = AssetDatabase.AssetPathToGUID("Assets/Textures/Checker.png");
            string folder = AssetDatabase.CreateFolder("Assets", "Art");
            Check(folder.Length == 32 && AssetDatabase.IsValidFolder("Assets/Art"), "a folder made");
            Equal("", AssetDatabase.MoveAsset("Assets/Textures/Checker.png", "Assets/Art/Checker.png"), "moved");
            Equal(guid, AssetDatabase.AssetPathToGUID("Assets/Art/Checker.png"), "the guid stays");
            Check(AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Art/Checker.png") != null, "loaded from its new place");
            Equal("", AssetDatabase.RenameAsset("Assets/Art/Checker.png", "Board"), "renamed");
            Check(AssetDatabase.AssetPathExists("Assets/Art/Board.png"), "with its extension");
            using (new AssetDatabase.AssetEditingScope())
                Check(AssetDatabase.CopyAsset("Assets/Art/Board.png", "Assets/Art/Board 2.png"), "copied");
            Check(AssetDatabase.AssetPathToGUID("Assets/Art/Board 2.png") != guid, "the copy's own guid");
            Check(AssetDatabase.DeleteAsset("Assets/Art/Board 2.png") && !AssetDatabase.AssetPathExists("Assets/Art/Board 2.png"), "deleted");
        }

        /// <summary>The tests of floating windows in windows of their own (the editor with its window, no batch mode).</summary>
        public static void RunWindowed()
        {
            s_Steps.Enqueue(("windows of their own", () => Check(DockManager.osWindows, "the host has tool windows")));
            s_Steps.Enqueue(("floating a tab", FloatInWindow));
            s_Steps.Enqueue(("a frame", () => { }));
            s_Steps.Enqueue(("its window", FloatingWindowShown));
            s_Steps.Enqueue(("its area menu", OpenFloatingMenu));
            s_Steps.Enqueue(("a frame", () => { }));
            s_Steps.Enqueue(("a frame", () => { }));
            s_Steps.Enqueue(("the menu in a popup window", FloatingMenuShown));
            s_Steps.Enqueue(("its only tab moves it", DragOnlyTab));
            s_Steps.Enqueue(("a frame", () => { }));
            s_Steps.Enqueue(("after moving it by its tab", AfterDragOnlyTab));
            s_Steps.Enqueue(("the window moved", MoveFloatingWindow));
            s_Steps.Enqueue(("a frame", () => { }));
            s_Steps.Enqueue(("after the move", AfterMoveFloatingWindow));
            s_Steps.Enqueue(("dragging its tab onto the main window", DragBackToMain));
            s_Steps.Enqueue(("a frame", () => { }));
            s_Steps.Enqueue(("docked again", DockedBack));
            s_Steps.Enqueue(("the main window maximized", () => { s_Width = Screen.width; WindowCall("ToggleMaximizeWindowInternal"); }));
            s_Steps.Enqueue(("a frame", () => { }));
            s_Steps.Enqueue(("a frame", () => { }));
            s_Steps.Enqueue(("maximized, then restored", () =>
            {
                Check((bool)WindowCall("IsWindowMaximizedInternal"), "maximized");
                WindowCall("ToggleMaximizeWindowInternal");
            }));
            s_Steps.Enqueue(("a frame", () => { }));
            s_Steps.Enqueue(("a frame", () => { }));
            s_Steps.Enqueue(("restored to its size", () =>
            {
                Check(!(bool)WindowCall("IsWindowMaximizedInternal"), "not maximized");
                Equal(s_Width, Screen.width, "its width as before");
            }));
            EditorApplication.update += Next;
        }

        static int s_Width;

        static object WindowCall(string name) =>
            typeof(EditorApplication).GetMethod(name, System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public)
                .Invoke(null, null);

        /// <summary>For a look: the editor's fields drawn by an editor of a script in a window.</summary>
        public static void ShowGuiProbe()
        {
            var host = new GameObject("Probe");
            GuiProbeWindow.s_Editor = Editor.CreateEditor(host.AddComponent<Inspected>());
            EditorWindow.GetWindow<GuiProbeWindow>("Probe", typeof(SceneView)).Focus();
        }

        /// <summary>For a look: the scene's camera selected (a script added) in the Inspector.</summary>
        public static void ShowInspectorLook()
        {
            GameObject cam = GameObject.Find("Main Camera");
            cam.AddComponent<Inspected>();
            Selection.activeObject = cam;
        }

        /// <summary>For a look: the Inspector's Add Component list with a search.</summary>
        public static void ShowAddComponentLook()
        {
            ShowInspectorLook();
            // Once the Inspector is laid out (its button has its place).
            int frames = 0;
            Action open = null;
            open = () =>
            {
                if (++frames < 4)
                    return;
                EditorApplication.update -= open;
                EditorWindow.GetWindow<InspectorWindow>().ShowAddComponentInternal();
            };
            EditorApplication.update += open;
        }

        /// <summary>For a look: the Hierarchy's search focused with some text.</summary>
        public static void FocusHierarchySearch()
        {
            var h = EditorWindow.GetWindow<HierarchyWindow>();
            h.searchFieldInternal.Focus();
            h.searchFieldInternal.value = "Cam";
        }

        /// <summary>The editor ends with a floating window open (its window and its view go with it).</summary>
        public static void RunQuitWithFloating()
        {
            s_Steps.Enqueue(("floating a tab", FloatInWindow));
            s_Steps.Enqueue(("a frame", () => { }));
            s_Steps.Enqueue(("its window", FloatingWindowShown));
            s_Steps.Enqueue(("its object destroyed", () =>
            {
                foreach (GameObject go in HelixEngine.Object.FindObjectsByType<GameObject>(FindObjectsSortMode.None))
                    if (go.name.StartsWith("Floating Window", StringComparison.Ordinal))
                        HelixEngine.Object.DestroyImmediate(go);
            }));
            s_Steps.Enqueue(("a frame", () => { }));
            EditorApplication.update += Next;
        }

        static FloatingContainer s_Floating;

        static void FloatInWindow()
        {
            DockManager.LoadPreset("Default");
            // Beside the main window (dragged by its tab it docks only over another window's area).
            Rect main = EditorApplication.DisplayRectInternal(0);
            DockManager.FloatTab("InspectorWindow", new Rect(main.x + main.width + 40, main.y + 140, 360, 260));
            s_Floating = DockManager.layout.ContainerOf(DockManager.layout.AreaOf("InspectorWindow"));
            Check(s_Floating != null && DockManager.HasWindow(s_Floating), "the container has a window of its own");
            Check(Root.FindAll(null, "dock-floating").Count == 0, "not drawn over the main view");
        }

        static DockView FloatingView()
        {
            foreach (DockView v in DockManager.Views())
                if (v.container == s_Floating)
                    return v;
            return null;
        }

        static void FloatingWindowShown()
        {
            DockView v = FloatingView();
            Check(v != null && v.display > 0, "a view on a display of its own");
            if (v == null)
                return;
            Rect r = EditorApplication.DisplayRectInternal(v.display);
            Check(r.width == 360 && r.height == 260, $"the window's size ({r.width} x {r.height})");
            Check(v.root.FindAll(null, "dock-tab").Count == 1, "the inspector's tab in it");
            Check(v.root.Find(null, "dock-tab-strip")?.ClassListContains("editor-caption") == true, "its strip moves the window");
            Check(HelixEngine.Display.displays.Length > v.display && HelixEngine.Display.displays[v.display].active, "an active display");
        }

        static void OpenFloatingMenu()
        {
            DockView v = FloatingView();
            Element menu = v?.root.Find(null, "dock-area-menu");
            Check(menu != null, "the floating window's area menu button");
            if (menu != null)
                Send<PointerDownEvent>(menu, Center(menu.worldBound));
            Check(PopupMenuWindow.isOpen, "a popup window opened");
        }

        static void FloatingMenuShown()
        {
            Check(PopupMenuWindow.isOpen, "the popup window stays");
            int popup = PopupMenuWindow.displayInternal;
            Check(popup > 0, $"its display ({popup})");
            if (popup <= 0)
                return;
            Rect r = EditorApplication.DisplayRectInternal(popup);
            Check(r.width >= 60 && r.height >= 40 && !(r.width == 200 && r.height == 120), $"sized to the menu ({r.width} x {r.height})");
            Element popupRoot = PopupMenuWindow.rootInternal;
            Check(popupRoot != null && popupRoot.FindAll(null, GenericDropdownMenu.itemHssClassName).Count >= 3, "the menu's items in it");
            PopupMenuWindow.CloseInternal();
            Check(!PopupMenuWindow.isOpen, "closed");
        }

        static Rect s_BeforeTabDrag;

        static void DragOnlyTab()
        {
            DockView v = FloatingView();
            Element tab = v?.root.Find(null, "dock-tab");
            if (tab == null)
            {
                Check(false, "the floating window's tab");
                return;
            }
            s_BeforeTabDrag = s_Floating.rect;
            Vector2 from = Center(tab.worldBound);
            // The window goes with the pointer: after the move the pointer is where it took the tab, in the window's
            // space; no other area under it, nothing docks.
            Send<PointerDownEvent>(tab, from);
            Send<PointerMoveEvent>(tab, from + new Vector2(40, 30));
            Send<PointerUpEvent>(tab, from);
        }

        static void AfterDragOnlyTab()
        {
            Check(DockManager.HasWindow(s_Floating) && DockManager.layout.floating.Contains(s_Floating), "still a floating window");
            Check(Mathf.Abs(s_Floating.rect.x - s_BeforeTabDrag.x - 40) < 1 && Mathf.Abs(s_Floating.rect.y - s_BeforeTabDrag.y - 30) < 1,
                  $"moved with the pointer ({s_Floating.rect.x - s_BeforeTabDrag.x}, {s_Floating.rect.y - s_BeforeTabDrag.y})");
        }

        static Rect s_MovedTo;

        static void MoveFloatingWindow()
        {
            DockView v = FloatingView();
            if (v == null)
                return;
            Rect r = EditorApplication.DisplayRectInternal(v.display);
            s_MovedTo = new Rect(r.x + 30, r.y + 20, 380, 280);
            // As the system would: the window moved and sized.
            EditorApplication.SetDisplayRectInternal(v.display, s_MovedTo);
        }

        static void AfterMoveFloatingWindow()
        {
            Check(s_Floating.rect == s_MovedTo, $"the container follows its window ({s_Floating.rect.x}, {s_Floating.rect.y}, {s_Floating.rect.width}, {s_Floating.rect.height})");
        }

        static void DragBackToMain()
        {
            DockView v = FloatingView();
            if (v == null)
                return;
            Element tab = v.root.Find(null, "dock-tab");
            Element hierarchy = AreaElement("HierarchyWindow");
            Check(tab != null && hierarchy != null, "the tab and the hierarchy's area");
            if (tab == null || hierarchy == null)
                return;
            // The hierarchy's middle on the desktop, in the floating window's space; the window goes with the pointer, so
            // when it is up the pointer is where it took the tab, in the window's space.
            Vector2 desktop = DockManager.mainView.ToDesktop(Center(hierarchy.worldBound));
            Vector2 from = Center(tab.worldBound);
            Send<PointerDownEvent>(tab, from);
            Send<PointerMoveEvent>(tab, v.FromDesktop(desktop));
            Send<PointerUpEvent>(tab, from);
        }

        static void DockedBack()
        {
            DockArea area = DockManager.layout.AreaOf("InspectorWindow");
            Check(area != null && area == DockManager.layout.AreaOf("HierarchyWindow"), "docked with the hierarchy");
            Check(DockManager.layout.floating.Count == 0 && !DockManager.HasWindow(s_Floating), "the floating window went");
            Check(FloatingView() == null, "no view of it");
        }

        // ---------------------------------------------------------------------------------- the model

        const string kDefault = "layout 1\n" +
                                "split column 0.7 0.3\n" +
                                "  split row 0.22 0.52 0.26\n" +
                                "    area 0 HierarchyWindow\n" +
                                "    area 1 SceneView GameView\n" +
                                "    area 0 InspectorWindow\n" +
                                "  area 0 ProjectWindow ConsoleWindow\n" +
                                "float 10 20 300 200\n" +
                                "  area 0 Lighting\n";

        static float Sum(DockSplit s)
        {
            float t = 0;
            foreach (float f in s.shares)
                t += f;
            return t;
        }

        static void TextRoundTrip()
        {
            DockLayout l = DockLayout.Read(kDefault);
            Equal(kDefault, l.Write(), "the text written back");
            var top = l.root as DockSplit;
            Check(top != null && top.column && top.children.Count == 2, "the top split is a column of two");
            var areas = new List<DockArea>(l.Areas());
            Equal(5, areas.Count, "the areas");
            Check(l.AreaOf("SceneView") == l.AreaOf("GameView"), "the scene and the game share an area");
            Equal("GameView", l.AreaOf("GameView").selectedTab, "the selected tab");
            Equal(1, l.floating.Count, "the floating containers");
            Check(l.ContainerOf(l.AreaOf("Lighting")) == l.floating[0], "the floating tab's container");
            Check(l.ContainerOf(l.AreaOf("SceneView")) == null, "a docked tab has no container");
            Equal(300f, l.floating[0].rect.width, "the floating container's width");
            // An empty layout and its text.
            Equal("layout 1\n", new DockLayout().Write(), "an empty layout's text");
            Check(DockLayout.Read("layout 1\n").root == null, "an empty layout read");
        }

        static void DockCenter()
        {
            DockLayout l = DockLayout.Read(kDefault);
            DockArea scene = l.AreaOf("SceneView");
            // Reordered within its area.
            l.Dock("GameView", scene, DropZone.Center, 0);
            Equal("GameView SceneView", string.Join(" ", scene.tabs), "reordered");
            Equal(0, scene.selected, "the moved tab is selected");
            // From another area: that area goes, the row keeps its share.
            l.Dock("InspectorWindow", scene, DropZone.Center, 1);
            Equal("GameView InspectorWindow SceneView", string.Join(" ", scene.tabs), "taken in");
            var row = (DockSplit)((DockSplit)l.root).children[0];
            Equal(2, row.children.Count, "the row without the inspector's area");
            Check(Math.Abs(Sum(row) - 1) < 1e-4f, "the row's shares still add to one");
            // At the end (-1).
            l.Dock("ConsoleWindow", scene, DropZone.Center);
            Equal("ConsoleWindow", scene.tabs[scene.tabs.Count - 1], "at the end");
            Equal("area 0 ProjectWindow", l.Write().Split('\n')[5].Trim(), "the project's area keeps its other tab");
        }

        static void DockEdges()
        {
            DockLayout l = DockLayout.Read(kDefault);
            DockArea hierarchy = l.AreaOf("HierarchyWindow");
            // Below the hierarchy: a column split in the row's place of the hierarchy.
            DockArea made = l.Dock("ConsoleWindow", hierarchy, DropZone.Bottom);
            Check(made != null && made.tabs.Count == 1 && made.tabs[0] == "ConsoleWindow", "a new area for the tab");
            DockSplit column = hierarchy.parent;
            Check(column != null && column.column && column.children.Count == 2, "a column of the hierarchy and the console");
            Check(column.children[0] == hierarchy && column.children[1] == made, "the console below");
            Equal(0.22f, ((DockSplit)l.root).children[0] is DockSplit row ? row.shares[0] : -1, "the row's share for them is the hierarchy's");
            // Right of the inspector: the row's own split takes it, halving the inspector's share.
            DockArea inspector = l.AreaOf("InspectorWindow");
            DockArea right = l.Dock("ProjectWindow", inspector, DropZone.Right);
            DockSplit r = inspector.parent;
            Check(r == right.parent && !r.column, "beside it in the row");
            Equal(r.children.IndexOf(inspector) + 1, r.children.IndexOf(right), "after it");
            Check(Math.Abs(r.shares[r.children.IndexOf(inspector)] - 0.13f) < 1e-4f, "the inspector's share halved");
            Check(Math.Abs(Sum(r) - 1) < 1e-4f, "the row's shares still add to one");
            // The project's area emptied: the top column holds the row alone, so the row is the root.
            Check(l.root == r, "a split of one child is replaced by it");
            // An area's only tab onto its own edge stays.
            Check(l.Dock("ProjectWindow", right, DropZone.Left) == right, "its own only tab onto its edge stays");
            // Left of the root area of a floating container.
            DockArea lighting = l.AreaOf("Lighting");
            l.Dock("GameView", lighting, DropZone.Left);
            Check(l.ContainerOf(l.AreaOf("GameView")) == l.floating[0], "docked into the floating container");
            Check(l.floating[0].root is DockSplit fs && !fs.column, "its root is a row now");
        }

        static void RemoveTabs()
        {
            DockLayout l = DockLayout.Read(kDefault);
            Check(!l.RemoveTab("Nothing"), "an unknown tab");
            DockArea scene = l.AreaOf("SceneView");
            Check(l.RemoveTab("GameView"), "a tab out");
            Equal(0, scene.selected, "the selection moves back from the end");
            l.RemoveTab("HierarchyWindow");
            var row = (DockSplit)((DockSplit)l.root).children[0];
            Equal(2, row.children.Count, "the emptied area goes");
            Check(Math.Abs(row.shares[0] - 0.74f) < 1e-4f, "its share to its neighbor");
            l.RemoveTab("Lighting");
            Equal(0, l.floating.Count, "an emptied floating container goes");
            l.maximized = l.AreaOf("InspectorWindow");
            l.RemoveTab("InspectorWindow");
            Check(l.maximized == null, "a maximized area that goes is no longer maximized");
            foreach (string t in new[] { "SceneView", "ProjectWindow", "ConsoleWindow" })
                l.RemoveTab(t);
            Check(l.root == null, "all tabs out");
        }

        static void Floating()
        {
            DockLayout l = DockLayout.Read(kDefault);
            FloatingContainer f = l.Float("InspectorWindow", new Rect(40, 50, 320, 240));
            Equal(2, l.floating.Count, "a container of its own");
            Check(l.ContainerOf(l.AreaOf("InspectorWindow")) == f, "the tab's container");
            Check(l.Write().Contains("float 40 50 320 240\n  area 0 InspectorWindow\n"), "written");
            DockLayout back = DockLayout.Read(l.Write());
            Equal(l.Write(), back.Write(), "read back");
            // Floated again: out of its own container into a new one.
            l.Float("InspectorWindow", new Rect(0, 0, 100, 100));
            Equal(2, l.floating.Count, "the emptied container went");
        }

        static void Splitters()
        {
            DockLayout l = DockLayout.Read(kDefault);
            var top = (DockSplit)l.root;
            DockLayout.MoveSplitter(top, 0, -0.2f);
            Check(Math.Abs(top.shares[0] - 0.5f) < 1e-4f && Math.Abs(top.shares[1] - 0.5f) < 1e-4f, "moved");
            DockLayout.MoveSplitter(top, 0, 0.48f);
            Check(Math.Abs(top.shares[0] - 0.5f) < 1e-4f, "not below the minimum");
            Equal("split column 0.5 0.5", l.Write().Split('\n')[1], "written");
        }

        // -------------------------------------------------------------------------------- the session

        static Element Root => DockManager.viewRoot;

        static Element AreaElement(string tab)
        {
            foreach (Element a in Root.FindAll(null, "dock-area"))
                foreach (Element t in a.FindAll(null, "dock-tab"))
                    if (t.Find<Label>(null, "dock-tab-label")?.text == TitleOf(tab))
                        return a;
            return null;
        }

        static Element TabElement(string tab)
        {
            foreach (Element t in Root.FindAll(null, "dock-tab"))
                if (t.Find<Label>(null, "dock-tab-label")?.text == TitleOf(tab))
                    return t;
            return null;
        }

        static string TitleOf(string tab) => DockManager.WindowOf(tab)?.titleContent.text ?? tab;

        static Vector2 Center(Rect r) => new Vector2(r.x + r.width * 0.5f, r.y + r.height * 0.5f);

        static void StartWindows()
        {
            Check(Root != null, "the view is attached to the shell");
            DockLayout l = DockManager.layout;
            Equal(4, new List<DockArea>(l.Areas()).Count, "the default layout's areas");
            foreach (string t in new[] { "HierarchyWindow", "SceneView", "GameView", "InspectorWindow", "ProjectWindow", "ConsoleWindow" })
            {
                EditorWindow w = DockManager.WindowOf(t);
                Check(w != null && w.GetType().Name == t, $"the window of {t}");
                Check(w != null && w.docked, $"{t} is docked");
            }
            Check(EditorWindow.HasOpenInstances<SceneView>(), "the scene view is open");
            Equal(4, Root.FindAll(null, "dock-area").Count, "the view's areas");
            Equal(3, Root.FindAll(null, "dock-splitter").Count, "the view's splitters");
            Equal(6, Root.FindAll(null, "dock-tab").Count, "the view's tabs");
            Check(TabElement("SceneView") != null, "the scene's tab shows its title");
            Check(TabElement("SceneView")?.ClassListContains("active") == true, "the scene's tab is the selected one");
            Check(AreaElement("SceneView")?.Find(null, "editor-window-root")?.parent?.ClassListContains("dock-content") == true,
                  "the selected window's content is in its area");
            Check(Root.worldBound.width > 100 && Root.worldBound.height > 100, "the view is laid out");
        }

        sealed class Pointer : IPointerEvent
        {
            public int pointerId { get; set; }
            public string pointerType => HelixEngine.UI.PointerType.mouse;
            public bool isPrimary => true;
            public int button { get; set; }
            public int pressedButtons { get; set; }
            public Vector3 position { get; set; }
            public Vector3 localPosition => position;
            public Vector3 deltaPosition => Vector3.zero;
            public float deltaTime => 0;
            public int clickCount { get; set; }
            public float pressure => 0;
            public float tangentialPressure => 0;
            public float altitudeAngle => 0;
            public float azimuthAngle => 0;
            public float twist => 0;
            public Vector2 tilt => Vector2.zero;
            public HelixEngine.UI.PenStatus penStatus => HelixEngine.UI.PenStatus.None;
            public Vector2 radius => Vector2.zero;
            public Vector2 radiusVariance => Vector2.zero;
            public EventModifiers modifiers => EventModifiers.None;
            public bool shiftKey => false;
            public bool ctrlKey => false;
            public bool commandKey => false;
            public bool altKey => false;
            public bool actionKey => false;
        }

        static void Send<T>(Element target, Vector2 p, int button = 0) where T : PointerEventBase<T>, new()
        {
            var data = new Pointer { pointerId = 0, button = button, position = new Vector3(p.x, p.y, 0), pressedButtons = 1, clickCount = 1 };
            using T e = PointerEventBase<T>.GetPooled(data);
            target.SendEvent(e);
        }

        // A drag of an element from a point to another, through a point between them (past the drag's start).
        static void Drag(Element e, Vector2 from, Vector2 to)
        {
            Send<PointerDownEvent>(e, from);
            Send<PointerMoveEvent>(e, (from + to) * 0.5f);
            Send<PointerMoveEvent>(e, to);
            Send<PointerUpEvent>(e, to);
        }

        static void DragToEdge()
        {
            Element tab = TabElement("GameView"), hierarchy = AreaElement("HierarchyWindow");
            Check(tab != null && hierarchy != null, "the game's tab and the hierarchy's area");
            Rect h = hierarchy.worldBound;
            Drag(tab, Center(tab.worldBound), new Vector2(h.x + h.width * 0.5f, h.y + h.height * 0.9f));
        }

        static void AfterDragToEdge()
        {
            DockLayout l = DockManager.layout;
            DockArea game = l.AreaOf("GameView"), hierarchy = l.AreaOf("HierarchyWindow");
            Check(game != null && game.tabs.Count == 1, "the game has an area of its own");
            Check(game?.parent != null && game.parent == hierarchy.parent && game.parent.column, "below the hierarchy");
            Equal("SceneView", l.AreaOf("SceneView").selectedTab, "the scene is its area's tab");
            Equal(5, Root.FindAll(null, "dock-area").Count, "the view drew the new area");
            Check(DockManager.WindowOf("GameView").docked, "the game is docked");
            string saved = System.IO.Path.Combine(EditorApplication.projectPathInternal, "UserSettings", "Layouts", "CurrentLayout.layout");
            Check(System.IO.File.Exists(saved) && System.IO.File.ReadAllText(saved) == l.Write(), "the layout is saved as the project's current one");
        }

        static void ClickTab()
        {
            // The project and the console share an area: a click selects the console's tab.
            Element tab = TabElement("ConsoleWindow");
            Check(tab != null && !tab.ClassListContains("active"), "the console's tab is not selected");
            Vector2 p = Center(tab.worldBound);
            Send<PointerDownEvent>(tab, p);
            Send<PointerUpEvent>(tab, p);
        }

        static void AfterClickTab()
        {
            Equal("ConsoleWindow", DockManager.layout.AreaOf("ConsoleWindow").selectedTab, "the console is selected");
            Check(TabElement("ConsoleWindow")?.ClassListContains("active") == true, "its tab is drawn selected");
            Check(EditorWindow.focusedWindow is ConsoleWindow, "the console has the focus");
            Check(AreaElement("ConsoleWindow")?.Find(null, "dock-content")?.Find(null, "editor-window-root") ==
                  DockManager.WindowOf("ConsoleWindow").rootElement, "the console's content shows");
        }

        static void DragOut()
        {
            // Out of every area: the view's own corner (the floating layer holds no area there).
            Element tab = TabElement("InspectorWindow");
            Rect root = Root.worldBound;
            Rect scene = AreaElement("SceneView").worldBound;
            // The scene's middle is a drop on it; past the root's bottom edge is no area.
            Drag(tab, Center(tab.worldBound), new Vector2(scene.x + scene.width * 0.5f, root.y + root.height + 40));
        }

        static void AfterDragOut()
        {
            DockLayout l = DockManager.layout;
            Equal(1, l.floating.Count, "a floating container");
            Check(l.ContainerOf(l.AreaOf("InspectorWindow")) == l.floating[0], "the inspector floats");
            Check(!DockManager.WindowOf("InspectorWindow").docked, "the inspector's window is not docked");
            Equal(1, Root.FindAll(null, "dock-floating").Count, "the view draws the container");
            Check(Root.Find(null, "dock-floating")?.ClassListContains("hx-shadow") == true, "with a shadow");
        }

        static void DragSplitter()
        {
            DockLayout l = DockManager.layout;
            var top = (DockSplit)l.root;
            float before = top.shares[0];
            Element splitter = null;
            foreach (Element s in Root.FindAll(null, "dock-splitter", "column"))
                if (s.parent?.parent == Root.Find(null, "dock-docked"))
                    splitter = s;
            Check(splitter != null, "the top column's splitter");
            if (splitter == null)
                return;
            Rect owner = splitter.parent.worldBound;
            Vector2 p = Center(splitter.worldBound);
            Send<PointerDownEvent>(splitter, p);
            Send<PointerMoveEvent>(splitter, p - new Vector2(0, owner.height * 0.1f));
            Send<PointerUpEvent>(splitter, p - new Vector2(0, owner.height * 0.1f));
            Check(Math.Abs(top.shares[0] - (before - 0.1f * Sum(top))) < 1e-3f, $"the share moved by a tenth (from {before} to {top.shares[0]})");
        }

        static void WindowRequests()
        {
            // GetWindow of an open window focuses it and selects its tab.
            ProjectWindow project = EditorWindow.GetWindow<ProjectWindow>();
            Check(project == DockManager.WindowOf("ProjectWindow"), "the open window");
            Equal("ProjectWindow", DockManager.layout.AreaOf("ProjectWindow").selectedTab, "its tab selected");
            Check(project.hasFocus, "it has the focus");
            // Closing a window takes its tab out.
            DockManager.WindowOf("ConsoleWindow").Close();
            Check(DockManager.layout.AreaOf("ConsoleWindow") == null, "the closed window's tab is out");
            Check(!EditorWindow.HasOpenInstances<ConsoleWindow>(), "no console open");
            // A window not in the layout floats when shown.
            ConsoleWindow console = EditorWindow.GetWindow<ConsoleWindow>();
            Check(console != null && !console.docked, "a new console floats");
            Check(DockManager.layout.ContainerOf(DockManager.layout.AreaOf("ConsoleWindow")) != null, "in a container");
            // Maximized and back.
            SceneView scene = EditorWindow.GetWindow<SceneView>();
            scene.maximized = true;
            Equal(1, Root.FindAll(null, "dock-area").Count, "only the maximized area drawn");
            scene.maximized = false;
            Check(Root.FindAll(null, "dock-area").Count > 1, "all areas again");
            Check(DockManager.WindowTypes() is IEnumerable<string> types && new List<string>(types).Contains("HierarchyWindow"), "the window types");
        }

        // ------------------------------------------------------------------------------------ menus, tabs

        static Element PanelRoot => Root.panel.visualTree;

        static Element MenuTitle(string title)
        {
            foreach (Label l in PanelRoot.FindAll<Label>(null, "menu-title"))
                if (l.text == title)
                    return l;
            return null;
        }

        static List<Element> MenuBoxes() => PanelRoot.FindAll(null, GenericDropdownMenu.containerOuterHssClassName);

        static Element MenuItem(Element box, string label)
        {
            foreach (Element item in box.FindAll(null, GenericDropdownMenu.itemHssClassName))
                if (item.Find<Label>(null, GenericDropdownMenu.labelHssClassName)?.text == label)
                    return item;
            return null;
        }

        static void ModeTitles()
        {
            var titles = new List<string>(EditorMenus.TitlesInternal());
            Equal("File Edit Assets GameObject Component Window Help", string.Join(" ", titles), "the default mode's titles");
            // A plugin's new title goes before Window (the mode's newMenusBefore), a second one after the first.
            Menu.AddMenuItem("Tools/Helix Test", "", false, 0, () => { }, null);
            Menu.AddMenuItem("Plugins/Helix Test", "", false, 0, () => { }, null);
            titles = new List<string>(EditorMenus.TitlesInternal());
            Equal("File Edit Assets GameObject Component Tools Plugins Window Help", string.Join(" ", titles), "new titles before Window");
            Menu.RemoveMenuItem("Tools/Helix Test");
            Menu.RemoveMenuItem("Plugins/Helix Test");
            titles = new List<string>(EditorMenus.TitlesInternal());
            Check(!titles.Contains("Tools") && !titles.Contains("Plugins"), "titles without items do not show");
        }

        static void OpenMenu()
        {
            Element title = MenuTitle("GameObject");
            Check(title != null, "the GameObject title");
            if (title != null)
                Send<PointerDownEvent>(title, Center(title.worldBound));
        }

        static void MenuOpened()
        {
            List<Element> boxes = MenuBoxes();
            Equal(1, boxes.Count, "one menu open");
            if (boxes.Count == 0)
                return;
            Rect box = boxes[0].worldBound, title = MenuTitle("GameObject").worldBound;
            Check(box.width > title.width, $"the menu is wider than its title ({box.width} against {title.width})");
            Check(System.Math.Abs(box.x - title.x) < 1, "under its title");
            Element create = MenuItem(boxes[0], "Create Empty");
            Check(create?.Find<Label>(null, "helix-base-dropdown__shortcut") != null, "the shortcut in a column of its own");
            Element sub = MenuItem(boxes[0], "3D Object");
            Check(sub != null && sub.ClassListContains("helix-base-dropdown__submenu"), "3D Object is a submenu");
            Check(MenuItem(boxes[0], "Cube") == null && MenuItem(boxes[0], "3D Object/Cube") == null, "its items are not in the menu");
            if (sub != null)
                Send<PointerEnterEvent>(sub, Center(sub.worldBound));
        }

        static void SubmenuOpened()
        {
            List<Element> boxes = MenuBoxes();
            Equal(2, boxes.Count, "the submenu opened");
            if (boxes.Count == 2)
            {
                Check(MenuItem(boxes[1], "Cube") != null, "Cube in the submenu");
                Check(boxes[1].worldBound.x >= boxes[0].worldBound.x + boxes[0].worldBound.width - 12, "beside its parent");
            }
            // Escape closes them all.
            Element menu = PanelRoot.Find(null, GenericDropdownMenu.hssClassName);
            if (menu != null)
                Send<PointerDownEvent>(menu, new Vector2(1, PanelRoot.worldBound.height - 1));
            Equal(0, MenuBoxes().Count, "a press outside closes the menu");
        }

        static void CloseTabByButton()
        {
            DockManager.LoadPreset("Default");
            Element tab = TabElement("GameView");
            Element close = tab?.Find(null, "dock-tab-close");
            Check(close != null, "the tab's close button");
            if (close == null)
                return;
            Send<PointerDownEvent>(close, Center(close.worldBound));
            Send<PointerUpEvent>(close, Center(close.worldBound));
            Check(DockManager.layout.AreaOf("GameView") == null, "the closed tab is out");
            // The middle button closes the console.
            Element console = TabElement("ConsoleWindow");
            if (console != null)
                Send<PointerDownEvent>(console, Center(console.worldBound), 2);
        }

        static void AfterCloseTab()
        {
            Check(!EditorWindow.HasOpenInstances<GameView>(), "the game view closed");
            Check(DockManager.layout.AreaOf("ConsoleWindow") == null && !EditorWindow.HasOpenInstances<ConsoleWindow>(), "the middle button closed the console");
            // The pointer over an area makes its window mouseOverWindow.
            Element area = AreaElement("InspectorWindow");
            if (area != null)
                Send<PointerEnterEvent>(area, Center(area.worldBound));
            Check(EditorWindow.mouseOverWindow is InspectorWindow, "mouseOverWindow");
        }

        static string s_LayoutBefore;

        static void OwnAreaDrop()
        {
            DockManager.LoadPreset("Default");
            s_LayoutBefore = DockManager.layout.Write();
            Element tab = TabElement("HierarchyWindow"), area = AreaElement("HierarchyWindow");
            if (tab == null || area == null)
            {
                Check(false, "the hierarchy's tab and area");
                return;
            }
            Rect r = area.worldBound;
            // Onto its own area's bottom edge: no target of its own tab.
            Drag(tab, Center(tab.worldBound), new Vector2(r.x + r.width * 0.5f, r.y + r.height * 0.9f));
        }

        static void AfterOwnAreaDrop()
        {
            Equal(s_LayoutBefore, DockManager.layout.Write(), "the layout as it was");
            Check(DockManager.layout.floating.Count == 0, "nothing floats");
        }

        // A window whose content is immediate GUI.
        public sealed class TestGuiWindow : EditorWindow
        {
            internal static int s_Repaints;
            internal static bool s_Pressed;

            void OnGui()
            {
                if (Event.current.type == EventType.Repaint)
                    s_Repaints++;
                if (Gui.Button(new Rect(4, 4, 120, 20), "Press"))
                    s_Pressed = true;
            }
        }

        static void OpenGuiWindow()
        {
            TestGuiWindow w = EditorWindow.GetWindow<TestGuiWindow>();
            Check(w.rootElement.Find<ImguiContainer>() != null, "an ImguiContainer for OnGui");
        }

        static void GuiWindowDrawn()
        {
            Check(TestGuiWindow.s_Repaints > 0, "OnGui drawn in the panel");
            var c = EditorWindow.GetWindow<TestGuiWindow>().rootElement.Find<ImguiContainer>();
            if (c != null)
            {
                Rect r = c.worldBound;
                var p = new Vector2(r.x + 20, r.y + 12);
                Send<PointerDownEvent>(c, p);
                Send<PointerUpEvent>(c, p);
            }
            Check(TestGuiWindow.s_Pressed, "its button pressed by the pointer");
            EditorWindow.GetWindow<TestGuiWindow>().Close();
            Check(!EditorWindow.HasOpenInstances<TestGuiWindow>(), "closed");
        }

        // For a screenshot of the menu (-executeMethod with -frames and -screenshot): the GameObject menu and its
        // 3D Object submenu open.
        public static void ShowMenus()
        {
            int frame = 0;
            EditorApplication.update += () =>
            {
                frame++;
                if (frame == 1)
                    OpenMenu();
                else if (frame == 2)
                {
                    List<Element> boxes = MenuBoxes();
                    Element sub = boxes.Count > 0 ? MenuItem(boxes[0], "3D Object") : null;
                    if (sub != null)
                        Send<PointerEnterEvent>(sub, Center(sub.worldBound));
                }
            };
        }

        static void Presets()
        {
            var presets = new List<string>(DockManager.Presets());
            foreach (string p in new[] { "Default", "2 by 3", "4 Split", "Tall", "Wide" })
                Check(presets.Contains(p), $"the preset {p}");
            foreach (string p in presets)
            {
                Check(DockManager.LoadPreset(p), $"the preset {p} loads");
                foreach (DockArea a in DockManager.layout.Areas())
                    foreach (string t in a.tabs)
                        Check(DockManager.WindowOf(t) != null, $"the preset {p}'s window {t}");
            }
            Check(DockManager.LoadPreset("Default"), "back to the default");
            Check(DockManager.layout.floating.Count == 0, "the default floats nothing");
            Check(!EditorWindow.HasOpenInstances<ConsoleWindow>() || DockManager.layout.AreaOf("ConsoleWindow") != null,
                  "a window whose tab the preset lacks closed");
            DockManager.Save("Mine");
            Check(new List<string>(DockManager.SavedLayouts()).Contains("Mine"), "a saved layout listed");
            Check(!new List<string>(DockManager.SavedLayouts()).Contains("CurrentLayout"), "the current one is not");
            Check(DockManager.LoadSaved("Mine"), "it loads");
            Check(!DockManager.LoadText("split sideways\n  area x"), "a broken layout is refused");
        }
    }
}
