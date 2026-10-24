using UnityEngine;
using UnityEngine.Audio;

namespace KungFuCrashout.Sound
{
    /// <summary>
    /// Background music that starts when the game starts and keeps playing across scene loads.
    ///
    ///   • Starts Track on game start (Play mode / app launch) with a short fade-in, looping forever
    ///   • PERSISTENT: survives scene changes (DontDestroyOnLoad). A scene with its own MusicPlayer doesn't
    ///     restart the music: same track → keeps playing seamlessly; different track → crossfades to it
    ///   • Two Audio Sources for smooth crossfades (e.g. menu music → fight music later)
    ///   • Real time: unaffected by the hit freeze / slow motion, and keeps playing under a pause menu
    ///     (ignores AudioListener.pause) unless you call Pause()
    ///   • Optional Music Mixer Group (for the "MusicLevel" volume slider later)
    ///
    /// Script: MusicPlayer.Instance.Play(clip) / Stop() / Pause() / Resume() / Volume = 0.5f.
    /// For a title-screen Play button instead: untick Play On Start and hook the button to StartMusic().
    /// Setup: KungFu Crashout → Set Up Background Music.
    /// Place in: Assets/_Project/Scripts/Audio/MusicPlayer.cs
    /// </summary>
    public class MusicPlayer : MonoBehaviour
    {
        [Tooltip("The music that starts with the game.")]
        [SerializeField] AudioClip track;
        [SerializeField] bool playOnStart = true;
        [Tooltip("Keep playing (without restarting) when another scene loads.")]
        [SerializeField] bool persistAcrossScenes = true;

        [Header("Level")]
        [SerializeField, Range(0f, 1f)] float volume = 0.6f;
        [Tooltip("Fade-in when the music first starts (real seconds).")]
        [SerializeField, Min(0f)] float fadeInTime = 1.5f;
        [Tooltip("Crossfade length when switching tracks (real seconds).")]
        [SerializeField, Min(0f)] float crossfadeTime = 1f;

        [Header("Routing (optional)")]
        [SerializeField] AudioMixerGroup musicGroup;

        public static MusicPlayer Instance { get; private set; }

        /// <summary>The track currently playing (or fading in), if any.</summary>
        public AudioClip CurrentTrack => current && current.isPlaying ? current.clip : null;

        public float Volume
        {
            get => volume;
            set { volume = Mathf.Clamp01(value); }
        }

        AudioSource a, b, current, previous;
        float fadeT = 1f, fadeLength;   // 0 → 1 over the crossfade
        bool stopping;

        void Awake()
        {
            // A persistent player already exists: hand our track over (no restart if it's the same) and go.
            if (Instance && Instance != this)
            {
                if (playOnStart && track && Instance.CurrentTrack != track) Instance.Play(track, crossfadeTime);
                Destroy(gameObject);
                return;
            }

            Instance = this;
            if (persistAcrossScenes)
            {
                transform.SetParent(null);       // DontDestroyOnLoad only works on root objects
                DontDestroyOnLoad(gameObject);
            }

            a = MakeSource("Music A");
            b = MakeSource("Music B");
            current = a;
            previous = b;
        }

        void Start()
        {
            if (Instance != this) return; // a duplicate that's being destroyed
            if (playOnStart) StartMusic();
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        AudioSource MakeSource(string label)
        {
            var go = new GameObject(label);
            go.transform.SetParent(transform, false);
            var s = go.AddComponent<AudioSource>();
            s.playOnAwake = false;
            s.loop = true;
            s.spatialBlend = 0f;           // 2D
            s.priority = 0;                // never dropped for sound effects
            s.ignoreListenerPause = true;  // keeps going under a pause menu that pauses the listener
            s.outputAudioMixerGroup = musicGroup;
            s.volume = 0f;
            return s;
        }

        // ───────────── Public API ─────────────

        /// <summary>Start the inspector Track (e.g. from a Play button). Does nothing if it's already playing.</summary>
        public void StartMusic()
        {
            if (track && CurrentTrack != track) Play(track, fadeInTime);
        }

        /// <summary>Crossfade to a clip. Same clip already playing = no change.</summary>
        public void Play(AudioClip clip, float fade = -1f)
        {
            if (!clip || !a) return;
            if (CurrentTrack == clip && !stopping) return;

            // Swap: the current source fades out, the other fades in with the new clip.
            previous = current;
            current = current == a ? b : a;
            current.clip = clip;
            current.volume = 0f;
            current.Play();

            stopping = false;
            fadeLength = fade < 0f ? crossfadeTime : fade;
            fadeT = fadeLength > 0f ? 0f : 1f;
            ApplyFade();
        }

        public void Stop(float fade = 1f)
        {
            if (!current || !current.isPlaying) return;
            previous = current;
            current = current == a ? b : a;
            current.Stop();
            stopping = true;
            fadeLength = fade;
            fadeT = fade > 0f ? 0f : 1f;
            ApplyFade();
        }

        public void Pause() { if (a) { a.Pause(); b.Pause(); } }
        public void Resume() { if (a) { a.UnPause(); b.UnPause(); } }

        // ───────────── Fading ─────────────

        void Update()
        {
            if (fadeT < 1f)
            {
                fadeT = Mathf.MoveTowards(fadeT, 1f, Time.unscaledDeltaTime / Mathf.Max(0.01f, fadeLength));
                if (fadeT >= 1f && previous) previous.Stop();
            }
            ApplyFade(); // also picks up Volume changes
        }

        void ApplyFade()
        {
            if (!current) return;
            // Equal-power curves so the crossfade doesn't dip in the middle.
            float fadeIn = Mathf.Sin(fadeT * Mathf.PI * 0.5f);
            float fadeOut = Mathf.Cos(fadeT * Mathf.PI * 0.5f);
            current.volume = stopping ? 0f : volume * fadeIn;
            if (previous && previous != current) previous.volume = volume * fadeOut;
        }
    }
}
