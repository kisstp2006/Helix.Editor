using System;
using System.Collections.Generic;
using System.Reflection;
using HelixEngine;
using Object = HelixEngine.Object;

namespace HelixEditor
{
    // The editors' OnSceneGui: for each component of the selected objects whose custom editor has one, an editor of it
    // (made again when the selection or its components change), its OnSceneGui called in the scene view's GUI.
    internal static class SceneGuiEditors
    {
        sealed class Entry
        {
            public Editor editor;
            public MethodInfo method;
            public Object target;
        }

        static readonly List<Entry> s_Entries = new();
        static readonly Dictionary<Type, MethodInfo> s_Methods = new();
        static bool s_Stale = true;
        static int s_Signature;

        internal static void SelectionChangedInternal() => s_Stale = true;

        internal static void OnSceneGuiInternal(SceneView view)
        {
            int signature = Signature();
            if (s_Stale || signature != s_Signature)
                Rebuild(signature);
            foreach (Entry e in s_Entries)
            {
                if (e.target == null || e.editor == null)
                    continue;
                try
                {
                    e.method.Invoke(e.editor, null);
                }
                catch (TargetInvocationException ex)
                {
                    Debug.LogException(ex.InnerException ?? ex);
                }
                finally
                {
                    Handles.matrix = Matrix4x4.identity;
                }
            }
        }

        // The selected objects' components as a number (a component added or removed changes it).
        static int Signature()
        {
            int h = 17;
            foreach (GameObject go in Selection.gameObjects)
                foreach (Component c in go.GetComponents<Component>())
                    h = h * 31 + (c != null ? c.GetHashCode() : 0);
            return h;
        }

        static void Rebuild(int signature)
        {
            s_Stale = false;
            s_Signature = signature;
            foreach (Entry e in s_Entries)
                if (e.editor != null)
                    Object.DestroyImmediate(e.editor);
            s_Entries.Clear();
            foreach (GameObject go in Selection.gameObjects)
                foreach (Component c in go.GetComponents<Component>())
                {
                    if (c == null)
                        continue;
                    Type editorType = Editors.For(c.GetType());
                    MethodInfo m = MethodOf(editorType);
                    if (m == null)
                        continue;
                    Editor editor = Editor.CreateEditor(c, editorType);
                    if (editor == null)
                        continue;
                    editor.hideFlags = HideFlags.HideAndDontSave;
                    s_Entries.Add(new Entry { editor = editor, method = m, target = c });
                }
        }

        // An editor class's own OnSceneGui (any access, no arguments; the base Editor has none).
        static MethodInfo MethodOf(Type editorType)
        {
            if (editorType == null)
                return null;
            if (!s_Methods.TryGetValue(editorType, out MethodInfo m))
            {
                for (Type t = editorType; t != null && t != typeof(Editor) && m == null; t = t.BaseType)
                    m = t.GetMethod("OnSceneGui", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly, null, Type.EmptyTypes, null);
                s_Methods[editorType] = m;
            }
            return m;
        }
    }
}
