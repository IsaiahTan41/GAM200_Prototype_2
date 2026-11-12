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
        [Tooltip("The painted effect.")]
        public Sprite sprite;
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
            if (t < popTime) return Mathf.Lerp(startScale, peakScale, EaseOut(t / popTime));
            float rest = Mathf.Max(0.001f, lifetime - popTime);
            return Mathf.Lerp(peakScale, endScale, EaseOut((t - popTime) / rest));
        }

        /// <summary>Alpha at age t (seconds).</summary>
        public float AlphaAt(float t)
        {
            float u = Mathf.Clamp01(t / lifetime);
            if (u <= fadeStart) return alpha;
            float k = (u - fadeStart) / Mathf.Max(0.001f, 1f - fadeStart);
            return alpha * (1f - k * k);
        }

        static float EaseOut(float x) { x = Mathf.Clamp01(x); return 1f - (1f - x) * (1f - x); }
    }
}
