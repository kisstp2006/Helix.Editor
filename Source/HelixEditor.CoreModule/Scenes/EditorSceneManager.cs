using HelixEngine.SceneManagement;

namespace HelixEditor.SceneManagement
{
    // The callbacks of the editor's scene events (the native events raise them).
    public static partial class EditorSceneManager
    {
        public delegate void SceneOpenedCallback(Scene scene, OpenSceneMode mode);
        public delegate void NewSceneCreatedCallback(Scene scene, NewSceneSetup setup, NewSceneMode mode);
        public delegate void SceneSavedCallback(Scene scene);
        public delegate void SceneDirtiedCallback(Scene scene);
        public delegate void SceneClosingCallback(Scene scene, bool removingScene);
        public delegate void SceneClosedCallback(Scene scene);

        public static int sceneCount => SceneManager.sceneCount;
        public static int loadedSceneCount => SceneManager.loadedSceneCount;
        public static Scene GetSceneAt(int index) => SceneManager.GetSceneAt(index);
        public static Scene GetSceneByPath(string scenePath) => SceneManager.GetSceneByPath(scenePath);
        public static Scene GetSceneByName(string name) => SceneManager.GetSceneByName(name);

        /// <summary>Saves the given scenes (the untitled ones ask where).</summary>
        public static bool SaveScenes(Scene[] scenes)
        {
            bool all = true;
            foreach (Scene s in scenes ?? System.Array.Empty<Scene>())
                all &= SaveScene(s);
            return all;
        }

        /// <summary>Asks to save the modified open scenes (in batch mode: saves them); false when cancelled.</summary>
        public static bool SaveCurrentModifiedScenesIfUserWantsTo()
        {
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene s = SceneManager.GetSceneAt(i);
                if (s.isDirty && !SaveScene(s))
                    return false;
            }
            return true;
        }
    }
}
