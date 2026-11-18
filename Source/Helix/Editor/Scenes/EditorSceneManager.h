#pragma once

#include <Helix/Core/Basics/Tags.h>
#include <Helix/Core/Strings/String.h>
#include <Helix/Engine/Event/Event.h>
#include <Helix/Engine/Scene/Scene.h>
#include <Helix/Scripting/Bindings/Interop.h>

// The editor's scenes (the reference's EditorSceneManager): the scenes of the project the user edits, apart from the
// shell's objects (in the DontDestroyOnLoad scene). New scenes (empty or with a camera and a light), scenes opened from
// the project (alone or beside the open ones), saved (to their paths or others), closed; their changed flags. The open
// scenes are written to Library/LastSceneManagerSetup.txt, and opened again with the project.
namespace helix::editor
{
    HX_ENUM(Namespace = "HelixEditor.SceneManagement")
    enum class NewSceneSetup : i32
    {
        EmptyScene = 0,
        DefaultGameObjects = 1
    };

    HX_ENUM(Namespace = "HelixEditor.SceneManagement")
    enum class NewSceneMode : i32
    {
        Single = 0,
        Additive = 1
    };

    HX_ENUM(Namespace = "HelixEditor.SceneManagement")
    enum class OpenSceneMode : i32
    {
        Single = 0,
        Additive = 1,
        AdditiveWithoutLoading = 2
    };

    HX_CLASS(Namespace = "HelixEditor.SceneManagement", Static)
    class EditorSceneManager
    {
    public:
        HX_FUNCTION(Name = "NewScene") static engine::Scene NewSceneApi(NewSceneSetup setup) { return NewScene(setup, NewSceneMode::Single); }
        HX_FUNCTION() static engine::Scene NewScene(NewSceneSetup setup, NewSceneMode mode);
        // A scene file of the project (an invalid scene when it cannot be opened).
        HX_FUNCTION(Name = "OpenScene") static engine::Scene OpenSceneApi(StringView scenePath) { return OpenScene(scenePath, OpenSceneMode::Single); }
        HX_FUNCTION() static engine::Scene OpenScene(StringView scenePath, OpenSceneMode mode);
        // To its path, or to another (an untitled scene asks for one outside batch mode); a copy leaves the scene as
        // it is.
        HX_FUNCTION(Name = "SaveScene") static bool SaveSceneApi(engine::Scene scene) { return SaveScene(scene, StringView(), false); }
        HX_FUNCTION(Name = "SaveScene") static bool SaveSceneApi(engine::Scene scene, StringView dstScenePath) { return SaveScene(scene, dstScenePath, false); }
        HX_FUNCTION() static bool SaveScene(engine::Scene scene, StringView dstScenePath, bool saveAsCopy);
        // The open scenes with changes.
        HX_FUNCTION() static bool SaveOpenScenes();
        // False for the last open scene; removeScene false keeps nothing yet (an unloaded scene is not listed).
        HX_FUNCTION() static bool CloseScene(engine::Scene scene, bool removeScene);
        HX_FUNCTION() static bool MarkSceneDirty(engine::Scene scene);
        HX_FUNCTION() static void MarkAllScenesDirty();
        HX_FUNCTION() static engine::Scene GetActiveScene();
        HX_FUNCTION() static bool SetActiveScene(engine::Scene scene);

        // The project's last open scenes again (else a new scene with the default objects); the shell calls it once its
        // own objects are kept apart.
        HX_FUNCTION(Internal) static void RestoreSetupInternal();

        HX_EVENT(Delegate = "global::HelixEditor.SceneManagement.EditorSceneManager.SceneOpenedCallback")
        static engine::Event<engine::Scene, OpenSceneMode>& sceneOpened();
        HX_EVENT(Delegate = "global::HelixEditor.SceneManagement.EditorSceneManager.NewSceneCreatedCallback")
        static engine::Event<engine::Scene, NewSceneSetup, NewSceneMode>& newSceneCreated();
        HX_EVENT(Delegate = "global::HelixEditor.SceneManagement.EditorSceneManager.SceneSavedCallback")
        static engine::Event<engine::Scene>& sceneSaved();
        HX_EVENT(Delegate = "global::HelixEditor.SceneManagement.EditorSceneManager.SceneDirtiedCallback")
        static engine::Event<engine::Scene>& sceneDirtied();
        HX_EVENT(Delegate = "global::HelixEditor.SceneManagement.EditorSceneManager.SceneClosingCallback")
        static engine::Event<engine::Scene, bool>& sceneClosing();
        HX_EVENT(Delegate = "global::HelixEditor.SceneManagement.EditorSceneManager.SceneClosedCallback")
        static engine::Event<engine::Scene>& sceneClosed();
    };
}
