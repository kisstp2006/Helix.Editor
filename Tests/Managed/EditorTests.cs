using System;
using System.Collections.Generic;
using HelixEditor.Docking;
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
