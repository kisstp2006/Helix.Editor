using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using HelixEngine.Internal;

namespace HelixEditor
{
    /// <summary>A static method as an item of the editor's menus (its path, the last part the item's name; a shortcut
    /// after a space: % ctrl, # shift, &amp; alt, _ alone), or as the item's validation.</summary>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
    public sealed class MenuItem : Attribute
    {
        public string menuItem;
        public bool validate;
        public int priority;

        public MenuItem(string itemName) : this(itemName, false) { }
        public MenuItem(string itemName, bool isValidateFunction) : this(itemName, isValidateFunction, 1000) { }
        public MenuItem(string itemName, bool isValidateFunction, int priority)
        {
            menuItem = itemName;
            validate = isValidateFunction;
            this.priority = priority;
        }
    }

    public static partial class Menu
    {
        public static bool GetChecked(string menuPath) => EditorMenus.GetChecked(menuPath);
        public static void SetChecked(string menuPath, bool isChecked) => EditorMenus.SetChecked(menuPath, isChecked);
        public static bool MenuItemExists(string menuPath) => EditorMenus.Exists(menuPath);
    }

    // The menu items of C#: every assembly's MenuItem methods registered in the native registry, run and validated here.
    internal static class MenuItems
    {
        static readonly Dictionary<string, MethodInfo> s_Execute = new();
        static readonly Dictionary<string, MethodInfo> s_Validate = new();
        static bool s_Hooked;

#pragma warning disable CA2255
        [ModuleInitializer]
#pragma warning restore CA2255
        internal static void Init() => ScriptBridge.WhenBindingsLoaded(Start);

        static void Start()
        {
            if (s_Hooked)
                return;
            s_Hooked = true;
            EditorMenus.managedExecute += path =>
            {
                if (s_Execute.TryGetValue(path, out MethodInfo m))
                    Run(m);
            };
            EditorMenus.managedValidate += path =>
            {
                bool ok = !s_Validate.TryGetValue(path, out MethodInfo m) || Run(m) is bool b && b;
                EditorMenus.SetValidatedInternal(ok);
            };
            ScriptBridge.OnAssembly(Scan);
        }

        static object Run(MethodInfo m)
        {
            try
            {
                return m.Invoke(null, null);
            }
            catch (TargetInvocationException e)
            {
                HelixEngine.Debug.LogException(e.InnerException ?? e);
                return null;
            }
        }

        // The item's path without its shortcut, and the shortcut as the menus show it.
        internal static (string path, string shortcut) Split(string itemName)
        {
            int space = itemName.LastIndexOf(' ');
            if (space < 0 || space + 1 >= itemName.Length)
                return (itemName, "");
            string tail = itemName.Substring(space + 1);
            if (tail[0] != '%' && tail[0] != '#' && tail[0] != '&' && tail[0] != '_')
                return (itemName, "");
            var text = new System.Text.StringBuilder();
            int i = 0;
            for (; i < tail.Length; i++)
            {
                char c = tail[i];
                if (c == '%')
                    text.Append("Ctrl+");
                else if (c == '#')
                    text.Append("Shift+");
                else if (c == '&')
                    text.Append("Alt+");
                else if (c == '_')
                    continue;
                else
                    break;
            }
            text.Append(tail.Substring(i).ToUpperInvariant());
            return (itemName.Substring(0, space), text.ToString());
        }

        static void Scan(Assembly assembly)
        {
            Type[] types;
            try
            {
                types = assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException e)
            {
                types = Array.FindAll(e.Types, t => t != null);
            }
            foreach (Type t in types)
                foreach (MethodInfo m in t.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
                    foreach (MenuItem a in m.GetCustomAttributes<MenuItem>(false))
                    {
                        (string path, string shortcut) = Split(a.menuItem);
                        if (a.validate)
                            s_Validate[path] = m;
                        else
                        {
                            s_Execute[path] = m;
                            EditorMenus.AddManagedInternal(path, a.priority, shortcut, true);
                        }
                    }
        }
    }
}
