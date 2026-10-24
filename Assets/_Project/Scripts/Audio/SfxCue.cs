using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;

namespace KungFuCrashout.Sound
{
    /// <summary>
    /// One sound EFFECT made of several variations, e.g. "Punch Light" = Punch_4…7.
    /// Every play picks a clip (shuffled, never the same one twice in a row) and nudges its volume and pitch,
    /// so repeated hits never sound copy-pasted. AudioManager plays it on a pooled Audio Source and enforces
    /// Max Voices and Cooldown.
    ///
    /// Create: Right-click → Create → KungFu Crashout → Sfx Cue (or KungFu Crashout → Set Up Punch Audio).
    /// Optional: put a hand-made Audio Random Container in Resource Override to use that instead of Clips.
    /// Place in: Assets/_Project/Scripts/Audio/SfxCue.cs
    /// </summary>
    [CreateAssetMenu(menuName = "KungFu Crashout/Sfx Cue", fileName = "SFX_NewCue")]
    public class SfxCue : ScriptableObject
    {
        public enum Order
        {
            /// <summary>Plays every clip once in a random order, then reshuffles (no repeat across the reshuffle).</summary>
            Shuffle,
            /// <summary>Any clip each time, but not one of the last Avoid Repeating Last clips.</summary>
            Random,
            /// <summary>In list order.</summary>
            Sequential
        }

        [Tooltip("The variations. Drag in several takes of the same sound.")]
        public AudioClip[] clips = new AudioClip[0];
        [Tooltip("Optional: an Audio Random Container (or any audio resource) to play instead of Clips. Its own randomisation applies.")]
        public AudioResource resourceOverride;

        [Header("Variation")]
        public Order order = Order.Shuffle;
        [Tooltip("Random order only: how many recent clips can't be picked again.")]
        [Min(0)] public int avoidRepeatingLast = 2;
        [Range(0f, 1f)] public float volume = 1f;
        [Tooltip("Random volume change each play, as a fraction (0.1 = up to ±10%, about ±1 dB).")]
        [Range(0f, 0.5f)] public float volumeVariation = 0.1f;
        [Tooltip("Base pitch. Below 1 = deeper and heavier.")]
        [Range(0.5f, 2f)] public float pitch = 1f;
        [Tooltip("Random pitch change each play, as a fraction (0.04 = ±4%, about ±70 cents).")]
        [Range(0f, 0.3f)] public float pitchVariation = 0.04f;

        [Header("Limits")]
        [Tooltip("Most copies of this cue playing at once. More hits than this replace the oldest copy.")]
        [Min(1)] public int maxVoices = 3;
        [Tooltip("Ignore a new play within this many seconds of the last (stops double-triggers stacking into one loud hit).")]
        [Min(0f)] public float cooldown = 0.03f;
        [Tooltip("0 = most important, 256 = least. Hits should beat whooshes and ambience when voices run out.")]
        [Range(0, 256)] public int priority = 64;

        // ── Runtime state (not saved) ──
        readonly List<int> bag = new List<int>();
        readonly List<int> recent = new List<int>();
        int sequential = -1;
        int last = -1;

        public bool HasSound => resourceOverride || (clips != null && clips.Length > 0);

        /// <summary>The next clip to play (null if the cue only has a Resource Override).</summary>
        public AudioClip NextClip()
        {
            if (clips == null || clips.Length == 0) return null;
            int n = clips.Length;
            if (n == 1) return clips[0];

            int index;
            switch (order)
            {
                case Order.Sequential:
                    index = sequential = (sequential + 1) % n;
                    break;

                case Order.Random:
                    int avoid = Mathf.Min(avoidRepeatingLast, n - 1);
                    do index = UnityEngine.Random.Range(0, n);
                    while (recent.Contains(index));
                    recent.Add(index);
                    while (recent.Count > avoid) recent.RemoveAt(0);
                    break;

                default: // Shuffle
                    if (bag.Count == 0) Refill(n);
                    index = bag[bag.Count - 1];
                    bag.RemoveAt(bag.Count - 1);
                    break;
            }

            last = index;
            return clips[index];
        }

        void Refill(int n)
        {
            for (int i = 0; i < n; i++) bag.Add(i);
            for (int i = n - 1; i > 0; i--) // Fisher–Yates
            {
                int j = UnityEngine.Random.Range(0, i + 1);
                (bag[i], bag[j]) = (bag[j], bag[i]);
            }
            // Don't start the new round with the clip that just played (it's taken from the end).
            if (bag[n - 1] == last) (bag[n - 1], bag[0]) = (bag[0], bag[n - 1]);
        }

        public float NextVolume() => Mathf.Clamp01(volume * (1f + UnityEngine.Random.Range(-volumeVariation, volumeVariation)));
        public float NextPitch() => pitch * (1f + UnityEngine.Random.Range(-pitchVariation, pitchVariation));

        void OnEnable()
        {
            bag.Clear();
            recent.Clear();
            sequential = -1;
            last = -1;
        }
    }
}
