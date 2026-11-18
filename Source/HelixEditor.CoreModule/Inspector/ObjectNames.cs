using System;
using HelixEngine;
using Object = HelixEngine.Object;

namespace HelixEditor
{
    /// <summary>The names the editor shows for fields, classes and objects.</summary>
    public static class ObjectNames
    {
        /// <summary>"m_LocalPosition" as "Local Position", "_speed" as "Speed", "kMax" as "Max".</summary>
        public static string NicifyVariableName(string name) => SerializedFields.Nicify(name);

        /// <summary>The object's class name ("Camera"; a script's own class name).</summary>
        public static string GetClassName(Object obj) => obj == null ? "" : obj.GetType().Name;

        /// <summary>The title of the object's inspector: a component's class nicified (a script's with " (Script)"),
        /// else the object's name, else its class.</summary>
        public static string GetInspectorTitle(Object obj)
        {
            if (obj == null)
                return "Nothing Selected";
            if (obj is Component)
            {
                string title = NicifyVariableName(obj.GetType().Name);
                return IsScript(obj.GetType()) ? title + " (Script)" : title;
            }
            return string.IsNullOrEmpty(obj.name) ? NicifyVariableName(obj.GetType().Name) : obj.name;
        }

        public static string GetDragAndDropTitle(Object obj) => obj == null ? "" : $"{obj.name} ({obj.GetType().Name})";

        /// <summary>The name a new object gets: "Name" if no sibling has it, else "Name (n)".</summary>
        public static string GetUniqueName(string[] existingNames, string name)
        {
            var taken = new System.Collections.Generic.HashSet<string>(existingNames ?? Array.Empty<string>());
            if (!taken.Contains(name))
                return name;
            int n = 1;
            while (taken.Contains($"{name} ({n})"))
                n++;
            return $"{name} ({n})";
        }

        // A class a project or the editor wrote in C# (a GameBehaviour), not one of the engine's.
        internal static bool IsScript(Type t) => typeof(GameBehaviour).IsAssignableFrom(t);
    }
}
