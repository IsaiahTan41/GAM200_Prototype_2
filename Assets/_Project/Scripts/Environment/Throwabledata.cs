using UnityEngine;

namespace KungFuCrashout.Fighters
{
    /// <summary>
    /// What a thrown item does when it hits a fighter (M5). One asset per kind of item
    /// (Right-click in Project → Create → KungFu Crashout → Throwable Data), assigned on the item's Carryable.
    ///
    /// It IS an AttackData, so everything that already reacts to attacks — hit sparks, hitstop, sounds,
    /// screen shake (HitFeedback), the AI's guard-after-hit, the training dummy, dropping a carried item —
    /// works for thrown items with no changes. From AttackData it uses:
    ///   Damage, Weight, Hitstun, Chip Damage, Blockstun, Block Pushback.
    /// It ignores the timing, hitbox, lunge and the slide Knockback (the item's flight is the "attack").
    ///
    /// Instead of the slide, a clean hit LAUNCHES the target along the throw direction:
    ///   • Horizontal throw → knocked back and slightly up, then falls.
    ///   • Upward throw     → launched up and back at the throw's angle (kept inside the angle range).
    ///   • Downward throw   → still pops the target up a little (Min Launch Angle) so it reads as a hit.
    ///   • Hits a stage wall while flying back → bounces off it (Wall Bounce), up to Max Wall Bounces times.
    /// The target stays in hitstun until it lands. Then (Step 3d):
    ///   • Ground bounce — the first landing bounces it up once; the second one lands.
    ///   • DOWNED       — it lies on the floor, skidding to a stop, VULNERABLE for Downed Time: it can't
    ///                    act or block and takes Downed Damage Multiplier × damage. Then it gets up with a
    ///                    short invincibility so it can't be juggled forever.
    ///   • Wall splat   — (optional, instead of a wall bounce) sticks to the wall for a moment, then drops.
    /// Impact feel (used by HitFeedback): a longer freeze, a bigger shake and an optional slow-motion beat.
    ///
    /// Older assets keep their old numbers: right-click the asset's header → "Reset to crate defaults"
    /// to get the Step 3d values.
    ///
    /// Place in: Assets/_Project/Scripts/Fighters/ThrowableData.cs
    /// </summary>
    [CreateAssetMenu(menuName = "KungFu Crashout/Throwable Data", fileName = "NewThrowable")]
    public class ThrowableData : AttackData
    {
        [Header("Throw knockback (replaces the slide Knockback above)")]
        [Tooltip("Speed the target is launched at, along the throw direction (world units per second).")]
        [Min(0f)] public float launchSpeed = 11f;
        [Tooltip("Gravity while flying from this hit (world units per second²). Lower = longer, floatier, easier to read. 0 = the fighter's jump gravity.")]
        [Min(0f)] public float launchGravity = 18f;
        [Tooltip("Launch angle is kept at least this far above horizontal (degrees), so flat and downward throws still lift the target.")]
        [Range(0f, 80f)] public float minLaunchAngle = 35f;
        [Tooltip("Launch angle is kept at most this far above horizontal (degrees), so steep throws still push the target back.")]
        [Range(10f, 89f)] public float maxLaunchAngle = 65f;
        [Tooltip("Launch speed multiplier for the hit that KOs.")]
        [Min(1f)] public float koLaunchMultiplier = 1.5f;

        [Header("Landing")]
        [Tooltip("Fraction of the downward speed bounced back up on the first landing. 0 = no ground bounce.")]
        [Range(0f, 0.9f)] public float groundBounce = 0.4f;
        [Tooltip("Landing slower than this (world units per second, downward) doesn't bounce.")]
        [Min(0f)] public float minGroundBounceSpeed = 5f;
        [Tooltip("Seconds the sideways speed takes to slide to a stop after landing.")]
        [Min(0f)] public float skidTime = 0.3f;

        [Header("Downed (vulnerable after landing)")]
        [Tooltip("Seconds lying on the floor after landing. Can't act or block. 0 = no downed state (old behaviour).")]
        [Min(0f)] public float downedTime = 0.8f;
        [Tooltip("Damage multiplier for hits taken while downed.")]
        [Min(1f)] public float downedDamageMultiplier = 1.25f;
        [Tooltip("Invincible for this long after getting up (seconds), so the downed state can't be looped.")]
        [Min(0f)] public float getUpInvulnerability = 0.3f;

        [Header("Wall bounce")]
        [Tooltip("Fraction of the sideways speed kept when the launched target hits a stage wall. 0 = no bounce, it slides down the wall.")]
        [Range(0f, 1f)] public float wallBounce = 0.5f;
        [Tooltip("Upward speed added on the bounce (world units per second).")]
        [Min(0f)] public float wallBounceLift = 4f;
        [Tooltip("Most wall bounces per launch.")]
        [Min(0)] public int maxWallBounces = 1;
        [Tooltip("Slower than this sideways (world units per second) and it doesn't bounce, it just stops at the wall.")]
        [Min(0f)] public float minBounceSpeed = 2f;
        [Tooltip("WALL SPLAT instead of bouncing: stick to the wall for Wall Splat Time, then slide off and drop. Good for heavy items.")]
        public bool wallSplat = false;
        [Min(0f)] public float wallSplatTime = 0.3f;

        [Header("Impact feel (HitFeedback)")]
        [Tooltip("Real seconds the game freezes on a clean item hit (a heavy melee hit is 0.09).")]
        [Min(0f)] public float impactHitstop = 0.14f;
        [Tooltip("Screen shake on a clean item hit (world units).")]
        [Min(0f)] public float impactShake = 0.25f;
        [Tooltip("Slow motion right after the freeze: time scale (e.g. 0.3) and how long (real seconds). Time 0 = off.")]
        [Range(0.05f, 1f)] public float slowMotionScale = 0.3f;
        [Min(0f)] public float slowMotionTime = 0.15f;

        /// <summary>
        /// Launch velocity for a throw travelling along direction (world space).
        /// side = which way the target is pushed (+1 right, -1 left); used when the throw is near-vertical.
        /// </summary>
        public Vector2 LaunchVelocity(Vector2 direction, int side, bool ko)
        {
            if (Mathf.Abs(direction.x) > 0.05f) side = direction.x > 0f ? 1 : -1;

            float angle = Mathf.Atan2(direction.y, Mathf.Abs(direction.x)) * Mathf.Rad2Deg;
            angle = Mathf.Clamp(angle, minLaunchAngle, Mathf.Max(minLaunchAngle, maxLaunchAngle)) * Mathf.Deg2Rad;

            float speed = launchSpeed * (ko ? koLaunchMultiplier : 1f);
            return new Vector2(Mathf.Cos(angle) * side, Mathf.Sin(angle)) * speed;
        }

        /// <summary>Starting numbers for a thrown crate. Called when the asset is created and for the built-in default.</summary>
        [ContextMenu("Reset to crate defaults")]
        public void ApplyThrowDefaults()
        {
            launchSpeed = 11f; launchGravity = 18f;
            minLaunchAngle = 35f; maxLaunchAngle = 65f; koLaunchMultiplier = 1.5f;
            groundBounce = 0.4f; minGroundBounceSpeed = 5f; skidTime = 0.3f;
            downedTime = 0.8f; downedDamageMultiplier = 1.25f; getUpInvulnerability = 0.3f;
            wallBounce = 0.5f; wallBounceLift = 4f; maxWallBounces = 1; minBounceSpeed = 2f;
            wallSplat = false; wallSplatTime = 0.3f;
            impactHitstop = 0.14f; impactShake = 0.25f; slowMotionScale = 0.3f; slowMotionTime = 0.15f;

            damage = 12f;
            weight = HitWeight.Heavy;
            hitstun = 0.35f;
            chipDamage = 0.2f;
            blockstun = 0.3f;
            blockPushback = 0.5f;
            knockback = 0f;   // unused for throws
            startup = 0f;     // unused for throws
            active = 0.01f;   // unused for throws
            recovery = 0f;    // unused for throws
            lunge = 0f;       // unused for throws
            hitboxOffset = Vector2.zero;
        }

        void Reset() => ApplyThrowDefaults();
    }
}