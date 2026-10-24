using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;

namespace KungFuCrashout.Sound
{
    /// <summary>
    /// Plays SfxCues on a pool of 2D Audio Sources, so overlapping hits never cut each other off.
    ///   • Per cue: Max Voices (oldest copy is replaced) and Cooldown (double-triggers ignored)
    ///   • Pool full: replaces the least important sound playing (Priority), so hits beat whooshes
    ///   • Runs on real time, so impacts sound during the hit freeze (audio ignores Time.timeScale)
    ///   • Optional SFX Mixer Group routes everything through the AudioMixer (exposed "SFXLevel" for settings)
    ///
    /// Call: AudioManager.Play(cue). One per scene (KungFu Crashout → Set Up Punch Audio creates it).
    /// Place in: Assets/_Project/Scripts/Audio/AudioManager.cs
    /// </summary>
    public class AudioManager : MonoBehaviour
    {
        [Tooltip("How many sounds can play at once. 10–16 is plenty on phones.")]
        [SerializeField, Range(4, 32)] int voices = 12;
        [Tooltip("Optional: the AudioMixer's SFX group (volume slider, ducking).")]
        [SerializeField] AudioMixerGroup sfxGroup;
        [Tooltip("Log every play to the Console (for testing variety).")]
        [SerializeField] bool logPlays = false;

        public static AudioManager Instance { get; private set; }

        class Voice
        {
            public AudioSource source;
            public SfxCue cue;
            public float startTime;
            public int priority;
        }

        readonly List<Voice> pool = new List<Voice>();
        readonly Dictionary<SfxCue, float> lastPlay = new Dictionary<SfxCue, float>();

        void Awake()
        {
            if (Instance && Instance != this)
            {
                Debug.LogWarning("[AudioManager] More than one in the scene; this one is disabled.", this);
                enabled = false;
                return;
            }
            Instance = this;

            for (int i = 0; i < voices; i++)
            {
                var go = new GameObject($"SFX Voice {i + 1}");
                go.transform.SetParent(transform, false);
                var src = go.AddComponent<AudioSource>();
                src.playOnAwake = false;
                src.spatialBlend = 0f; // 2D: one screen, no distance fall-off
                src.outputAudioMixerGroup = sfxGroup;
                pool.Add(new Voice { source = src, priority = 256 });
            }
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        /// <summary>Play a cue. Safe to call with null or when no AudioManager exists (does nothing).</summary>
        public static void Play(SfxCue cue)
        {
            if (Instance) Instance.PlayCue(cue);
        }

        public void PlayCue(SfxCue cue)
        {
            if (!cue || !cue.HasSound) return;

            float now = Time.unscaledTime;
            if (lastPlay.TryGetValue(cue, out float t) && now - t < cue.cooldown) return;
            lastPlay[cue] = now;

            var voice = PickVoice(cue);
            if (voice == null) return;

            var src = voice.source;
            src.Stop();
            src.priority = cue.priority;
            src.volume = cue.NextVolume();
            src.pitch = cue.NextPitch();

            if (cue.resourceOverride)
            {
                src.resource = cue.resourceOverride; // e.g. an Audio Random Container
            }
            else
            {
                var clip = cue.NextClip();
                if (!clip) return;
                src.resource = clip;
                if (logPlays) Debug.Log($"[AudioManager] {cue.name} → {clip.name} (vol {src.volume:0.00}, pitch {src.pitch:0.00})");
            }
            src.Play();

            voice.cue = cue;
            voice.startTime = now;
            voice.priority = cue.priority;
        }

        /// <summary>A free voice; else this cue's oldest copy if it's at Max Voices; else the least important sound.</summary>
        Voice PickVoice(SfxCue cue)
        {
            int copies = 0;
            Voice oldestCopy = null, free = null, steal = null;

            foreach (var v in pool)
            {
                bool busy = v.source.isPlaying;
                if (!busy) { free ??= v; continue; }

                if (v.cue == cue)
                {
                    copies++;
                    if (oldestCopy == null || v.startTime < oldestCopy.startTime) oldestCopy = v;
                }

                // Least important (higher number), then oldest.
                if (steal == null || v.priority > steal.priority ||
                    (v.priority == steal.priority && v.startTime < steal.startTime))
                    steal = v;
            }

            if (copies >= cue.maxVoices) return oldestCopy;
            if (free != null) return free;
            return steal != null && steal.priority >= cue.priority ? steal : null; // never cut a more important sound
        }
    }
}
