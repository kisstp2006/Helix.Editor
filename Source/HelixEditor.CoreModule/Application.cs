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
}
