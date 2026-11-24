using UnityEngine;

namespace KungFuCrashout.Feedback
{
    /// <summary>
    /// One visual effect: a single painted frame (e.g. VFX_Monk_Hit) brought to life with a scale pop, a fade,
    /// an optional drift and an optional 2D light flash at the point of impact (VFX_Notes: "a normal spotlight
    /// at the point of impact, in the effect's primary colour").
    ///
    /// Played by VfxPlayer (pooled). FighterVfx / ItemVfx decide WHEN; this asset decides HOW it looks.
    /// Effects run on scaled time, so they freeze during hitstop and the pause menu.
    ///
    /// Create: Window → KungFu Crashout → VFX → 1. Import VFX Folder (makes one per sprite),
    /// or Right-click → Create → KungFu Crashout → VFX Cue.
    /// Place in: Assets/_Project/Scripts/Feedback/VfxCue.cs
    /// </summary>
    [CreateAssetMenu(menuName = "KungFu Crashout/VFX Cue", fileName = "VFX_NewCue")]
    public class VfxCue : ScriptableObject
    {
        [Tooltip("The painted effect (single frame). Animated cues use Frames below instead.")]
        public Sprite sprite;

        [Header("Animation (sprite sheet). Empty Frames = the single Sprite above")]
        [Tooltip("Frames in play order (sliced from the sheet). An empty slot is a blank frame.")]
        public Sprite[] frames = new Sprite[0];
        [Tooltip("Frames per second (the artist's value from the VFX notes).")]
        [Min(1f)] public float fps = 15f;
        [Tooltip("Loop the frames (idle / held effects). One-shots played by VfxPlayer stop after Lifetime.")]
        public bool loop;
        [Tooltip("Played straight after this one ends, in the same place (e.g. Block Start → Block Idle).")]
        public VfxCue next;
        [Tooltip("Wait this many game frames (60 fps, like the attack frame data) before the first frame shows, " +
                 "to line the effect up with the character's animation.")]
        [Min(0)] public int startDelayFrames;
        [Tooltip("Animated cues: also use the pop (Start / Peak / End Scale) and the fade-out below. " +
                 "Off = plays exactly as drawn (the art animates its own growth and fade).")]
        public bool popAndFade;
        [Tooltip("Stays attached to its owner while it plays (moves with the fighter's lunge / the item). " +
                 "Off = stays where it spawned (dust, impacts).")]
        public bool followOwner;
        [Tooltip("Size of the drawn content in world units at Scale 1 (set by the importer). The held-item aura is " +
                 "fitted to the item with it.")]
        public Vector2 contentSize;

        public bool IsAnimated => frames != null && frames.Length > 0;
        public bool HasArt => IsAnimated || sprite;
        /// <summary>One pass through the frames (seconds).</summary>
        public float Duration => IsAnimated ? frames.Length / Mathf.Max(1f, fps) : lifetime;
        /// <summary>How long VfxPlayer shows it: one pass, or Lifetime for single frames and loops.</summary>
        public float Life => IsAnimated && !loop ? Duration : lifetime;
        /// <summary>Start delay in seconds (60 fps game frames).</summary>
        public float Delay => startDelayFrames / 60f;

        /// <summary>The sprite to show at age t (seconds). Null = a blank frame.</summary>
        public Sprite FrameAt(float t)
        {
            if (!IsAnimated) return sprite;
            int n = frames.Length;
            int i = Mathf.FloorToInt(Mathf.Max(0f, t) * fps);
            i = loop ? i % n : Mathf.Min(i, n - 1);
            return frames[i];
        }

        /// <summary>The first drawn frame (for sizes and bounds).</summary>
        public Sprite FirstSprite
        {
            get
            {
                if (!IsAnimated) return sprite;
                foreach (var f in frames) if (f) return f;
                return sprite;
            }
        }
        [Tooltip("Multiplies the sprite's colour (white = as painted).")]
        public Color tint = Color.white;
        [Range(0f, 1f)] public float alpha = 1f;

        [Header("Placement")]
        [Tooltip("World units from the spawn point. X points the way the fighter faces (when Flip With Facing is on).")]
        public Vector2 offset;
        [Tooltip("Mirror the sprite and the offset when the owner faces left.")]
        public bool flipWithFacing = true;
        [Tooltip("Tick if the art is painted facing LEFT (so it needs mirroring when facing right).")]
        public bool paintedFacingLeft = false;
        [Tooltip("Fixed rotation (degrees). Mirrored with facing.")]
        public float rotation = 0f;
        [Tooltip("Random extra rotation, ± degrees, so repeats don't look identical.")]
        [Min(0f)] public float randomRotation = 0f;
        [Tooltip("Sorting order relative to the owner's sprite. + = in front, − = behind.")]
        public int sortingOffset = 5;

        [Header("Size")]
        [Tooltip("Overall size. 1 = as painted next to the Monk (imported at the same pixels-per-unit).")]
        [Min(0f)] public float scale = 1f;
        [Tooltip("Size on the first frame, as a fraction of Scale.")]
        [Min(0f)] public float startScale = 0.7f;
        [Tooltip("Size at the end of the pop.")]
        [Min(0f)] public float peakScale = 1.1f;
        [Tooltip("Size at the end of its life (grows / settles from the peak).")]
        [Min(0f)] public float endScale = 1f;
        [Tooltip("Seconds to pop from Start to Peak. Short = punchy.")]
        [Min(0.001f)] public float popTime = 0.05f;

        [Header("Timing")]
        [Tooltip("Seconds on screen.")]
        [Min(0.01f)] public float lifetime = 0.25f;
        [Tooltip("When the fade-out starts, as a fraction of Lifetime.")]
        [Range(0f, 1f)] public float fadeStart = 0.4f;
        [Tooltip("Movement per second (X points the way the owner faces), e.g. dust drifting backwards.")]
        public Vector2 drift;

        [Header("Light flash (URP 2D point light at the effect)")]
        public bool light = false;
        public Color lightColour = new Color(1f, 0.9f, 0.6f);
        [Min(0f)] public float lightIntensity = 1.5f;
        [Min(0.01f)] public float lightRadius = 2f;
        [Tooltip("360 = round glow. Less = a spotlight cone pointing in Light Direction.")]
        [Range(1f, 360f)] public float lightAngle = 360f;
        [Tooltip("Cone direction in degrees (0 = up, 90 = forward). Mirrored with facing.")]
        public float lightDirection = 0f;
        [Tooltip("Seconds the flash takes to fade out.")]
        [Min(0.01f)] public float lightTime = 0.12f;
        public Vector2 lightOffset;

        [Header("Looping (held effects only, e.g. VFX_Monk_Holding)")]
        [Tooltip("Pulses per second while shown.")]
        [Min(0f)] public float pulseSpeed = 2.5f;
        [Tooltip("How much it breathes (fraction of its size).")]
        [Range(0f, 0.5f)] public float pulseAmount = 0.08f;

        /// <summary>Size multiplier at age t (seconds).</summary>
        public float ScaleAt(float t)
        {
            if (IsAnimated && !popAndFade) return 1f;
            if (t < popTime) return Mathf.Lerp(startScale, peakScale, EaseOut(t / popTime));
            float rest = Mathf.Max(0.001f, Life - popTime);
            return Mathf.Lerp(peakScale, endScale, EaseOut((t - popTime) / rest));
        }

        /// <summary>Alpha at age t (seconds).</summary>
        public float AlphaAt(float t)
        {
            if (IsAnimated && !popAndFade) return alpha;
            float u = Mathf.Clamp01(t / Life);
            if (u <= fadeStart) return alpha;
            float k = (u - fadeStart) / Mathf.Max(0.001f, 1f - fadeStart);
            return alpha * (1f - k * k);
        }

        static float EaseOut(float x) { x = Mathf.Clamp01(x); return 1f - (1f - x) * (1f - x); }
    }
}
