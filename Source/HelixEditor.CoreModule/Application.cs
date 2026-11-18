using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using HelixEngine.Internal;

namespace HelixEditor
{
    public enum PlayModeStateChange
    {
        EnteredEditMode = 0,
        ExitingEditMode = 1,
        EnteredPlayMode = 2,
        ExitingPlayMode = 3,
    }

    public enum PauseState
    {
        Paused = 0,
        Unpaused = 1,
    }

    public static partial class EditorApplication
    {
        public delegate void CallbackFunction();

        /// <summary>Called once on the next editor update, then cleared.</summary>
        public static CallbackFunction delayCall;
        public static event Action<PlayModeStateChange> playModeStateChanged;
        public static event Action<PauseState> pauseStateChanged;

        static bool s_Hooked;

        internal static void Hook()
        {
            if (s_Hooked)
                return;
            s_Hooked = true;
            update += () =>
            {
                CallbackFunction calls = delayCall;
                delayCall = null;
                calls?.Invoke();
            };
            playModeChangedNative += () =>
            {
                bool playing = isPlaying;
                playModeStateChanged?.Invoke(playing ? PlayModeStateChange.ExitingEditMode : PlayModeStateChange.ExitingPlayMode);
                playModeStateChanged?.Invoke(playing ? PlayModeStateChange.EnteredPlayMode : PlayModeStateChange.EnteredEditMode);
            };
            pauseChangedNative += () => pauseStateChanged?.Invoke(isPaused ? PauseState.Paused : PauseState.Unpaused);
        }
    }

    internal static class ApplicationHooks
    {
#pragma warning disable CA2255
        [ModuleInitializer]
#pragma warning restore CA2255
        internal static void Init() => ScriptBridge.WhenBindingsLoaded(EditorApplication.Hook);
    }

    /// <summary>The scene view's tools; the current one shows lit in the main toolbar.</summary>
    public enum Tool
    {
        View = 0,
        Move = 1,
        Rotate = 2,
        Scale = 3,
        Rect = 4,
        Transform = 5,
        Custom = 6,
        None = -1,
    }

    public static class Tools
    {
        static Tool s_Current = Tool.Move;

        public static Tool current
        {
            get => s_Current;
            set
            {
                if (s_Current == value)
                    return;
                s_Current = value;
                foreach (string id in DefaultToolbar.ToolIds)
                    Toolbars.MainToolbar.Refresh(id);
            }
        }
    }
}
