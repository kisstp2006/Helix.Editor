using System;
using HelixEngine;

namespace HelixEditor
{
    /// <summary>The editor's way of making objects and components: what it adds (the Hierarchy's create items, Add
    /// Component, scripts through this class) is told by componentWasAdded, where the pipelines' companions join.</summary>
    public static class ObjectFactory
    {
        public static event Action<Component> componentWasAdded;

        public static T AddComponent<T>(GameObject gameObject) where T : Component => AddComponent(gameObject, typeof(T)) as T;

        public static Component AddComponent(GameObject gameObject, Type type)
        {
            if (gameObject == null || type == null)
                throw new ArgumentNullException(gameObject == null ? nameof(gameObject) : nameof(type));
            Component c = gameObject.AddComponent(type);
            if (c != null)
                ComponentWasAddedInternal(c);
            return c;
        }

        /// <summary>A GameObject of the name in the active scene with the components.</summary>
        public static GameObject CreateGameObject(string name, params Type[] types)
        {
            var go = new GameObject(name);
            foreach (Type t in types ?? Array.Empty<Type>())
                AddComponent(go, t);
            return go;
        }

        // A component the editor added another way (Add Component by class name): told as the others.
        internal static void ComponentWasAddedInternal(Component c)
        {
            if (c == null)
                return;
            PipelineCompanions.AddFor(c);
            try
            {
                componentWasAdded?.Invoke(c);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }
    }
}
