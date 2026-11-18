using System;
using System.Collections.Generic;
using HelixEngine;
using Object = HelixEngine.Object;

namespace HelixEditor
{
    // The selection's C# members over the native registry: the lists, the filters by type, selectionChanged.
    public static partial class Selection
    {
        /// <summary>Raised when the selection changes.</summary>
        public static Action selectionChanged;

        public static Object[] objects
        {
            get => ObjectsInternal();
            set => SetObjectsInternal(value ?? Array.Empty<Object>());
        }

        public static GameObject[] gameObjects => GameObjectsInternal();

        /// <summary>The selected objects of scenes as Transforms, the top level only, prefab assets left out.</summary>
        public static Transform[] transforms => TransformsInternal(SelectionMode.TopLevel | SelectionMode.ExcludePrefab);

        public static EntityId[] entityIds
        {
            get => EntityIdsInternal();
            set => SetEntityIdsInternal(value ?? Array.Empty<EntityId>());
        }

        public static string[] assetGUIDs => AssetGuidsInternal();

        public static Object[] GetFiltered(Type type, SelectionMode mode)
        {
            var list = new List<Object>();
            foreach (Object o in FilteredInternal(mode))
                if (type == null || type.IsInstanceOfType(o))
                    list.Add(o);
                else if (o is GameObject go && typeof(Component).IsAssignableFrom(type) && go.GetComponent(type) is Component c)
                    list.Add(c);
            return list.ToArray();
        }

        public static T[] GetFiltered<T>(SelectionMode mode)
        {
            var list = new List<T>();
            foreach (Object o in GetFiltered(typeof(T), mode))
                if (o is T t)
                    list.Add(t);
            return list.ToArray();
        }

        public static Transform[] GetTransforms(SelectionMode mode) => TransformsInternal(mode);

#pragma warning disable CA2255
        [System.Runtime.CompilerServices.ModuleInitializer]
#pragma warning restore CA2255
        internal static void Init() => HelixEngine.Internal.ScriptBridge.WhenBindingsLoaded(() => changedInternal += () => selectionChanged?.Invoke());
    }
}
