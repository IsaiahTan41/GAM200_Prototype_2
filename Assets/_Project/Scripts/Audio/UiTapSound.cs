using UnityEngine;

namespace KungFuCrashout.Sound
{
    /// <summary>
    /// The UI's scene-change tap: plays Tap (SFX_PressStart) whenever a button / icon leads to another scene (every
    /// SceneLoader.Load: START / Singleplayer, READY, Back, Rematch, Change Fighter, Quit to Start…). The Roster's and Level
    /// Select's READY play it the moment they're pressed (their scene loads after a short animation; that load doesn't
    /// play it again).
    /// Plays on its own audio source that survives the scene change, so the sound isn't cut when the next scene loads.
    /// Follows the SFX volume setting (AudioManager.SfxVolume).
    /// Put one in each UI scene (Audio → Set Up Start Screen Voice + UI Tap Sound does it); the first one sets the sound.
    /// Place in: Assets/_Project/Scripts/Audio/UiTapSound.cs
    /// </summary>
    public class UiTapSound : MonoBehaviour
    {
        [Tooltip("The tap sound (SFX_PressStart).")]
        [SerializeField] SfxCue tap;
        [Tooltip("Play it when a button / icon changes scene. Off = silent scene changes.")]
        [SerializeField] bool playOnSceneChange = true;

        static SfxCue cue;
        static bool enabledFlag = true;
        static AudioSource source;

        void Awake()
        {
            if (tap) cue = tap;
            enabledFlag = playOnSceneChange;
        }

        static bool skipNextSceneChange;

        /// <summary>A button / icon changed scene (SceneLoader.Load): play the tap, unless the press already played it.</summary>
        public static void PlayForSceneChange()
        {
            if (skipNextSceneChange) { skipNextSceneChange = false; return; }
            PlayTap();
        }

        /// <summary>A press whose scene loads a moment later (READY's animation): play now, not again at the load.</summary>
        public static void PlayBeforeDelayedLoad()
        {
            PlayTap();
            skipNextSceneChange = true;
        }

        /// <summary>Play the tap now.</summary>
        public static void PlayTap()
        {
            if (!enabledFlag || !cue || !cue.HasSound) return;
            var clip = cue.NextClip();
            if (!clip) return;

            if (!source)
            {
                var go = new GameObject("UI Tap Sound (persistent)");
                DontDestroyOnLoad(go);
                source = go.AddComponent<AudioSource>();
                source.playOnAwake = false;
                source.spatialBlend = 0f;
                source.ignoreListenerPause = true; // pause menu buttons still tap
            }
            source.Stop();
            source.clip = clip;
            source.volume = cue.NextVolume() * Mathf.Clamp01(AudioManager.SfxVolume);
            source.pitch = cue.NextPitch();
            source.priority = cue.priority;
            source.Play();
        }
    }
}
