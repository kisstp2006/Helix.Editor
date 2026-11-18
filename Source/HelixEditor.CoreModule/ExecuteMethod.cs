using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using HelixEngine;
using HelixEngine.Internal;

[assembly: InternalsVisibleTo("HelixEditor.Tests")]

namespace HelixEditor
{
    // -executeMethod <Namespace.Class.Method>: a static method of a loaded assembly run once the shell is up (a batch
    // run's job or tests; the method ends the editor with EditorApplication.Exit when it is done).
    internal static class ExecuteMethod
    {
        static string s_Method;
        static bool s_Done;

#pragma warning disable CA2255
        [ModuleInitializer]
#pragma warning restore CA2255
        internal static void Init() => ScriptBridge.WhenBindingsLoaded(Start);

        static void Start()
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i + 1 < args.Length; i++)
                if (args[i] == "-executeMethod")
                    s_Method = args[i + 1];
            if (s_Method != null)
                EditorApplication.update += Tick;
        }

        static int s_Frames;

        static void Tick()
        {
            // After the shell's first frames (its Start and the layout).
            if (s_Done || ++s_Frames < 3)
                return;
            s_Done = true;
            int dot = s_Method.LastIndexOf('.');
            string typeName = dot > 0 ? s_Method.Substring(0, dot) : "";
            string methodName = dot > 0 ? s_Method.Substring(dot + 1) : s_Method;
            foreach (Assembly a in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type t = a.GetType(typeName, false);
                MethodInfo m = t?.GetMethod(methodName, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null);
                if (m == null)
                    continue;
                try
                {
                    m.Invoke(null, null);
                }
                catch (TargetInvocationException e)
                {
                    Debug.LogException(e.InnerException ?? e);
                    EditorApplication.Exit(1);
                }
                return;
            }
            Debug.LogError($"-executeMethod: no static method {s_Method}");
            EditorApplication.Exit(1);
        }
    }
}
