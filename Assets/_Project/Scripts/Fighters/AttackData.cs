using UnityEngine;

namespace KungFuCrashout.Fighters
{
    /// <summary>Light or heavy hit. Picks the hit sound, hitstop length and screen shake (M8).</summary>
    public enum HitWeight { Light, Heavy }

    /// <summary>
    /// One attack's numbers. Each attack is an asset you can tweak in the Inspector
    /// without touching code (Right-click in Project → Create → KungFu Crashout → Attack Data).
    ///
    /// Every attack has three phases:
    ///   Startup  — wind-up, no hitbox yet (the attacker steps forward by `lunge`)
    ///   Active   — the hitbox is out and can hit
    ///   Recovery — cool-down; the next combo hit can be chained from here
    ///
    /// M3 adds what happens when the attack is blocked (chip damage, blockstun, pushback)
    /// and a light/heavy weight. The weight replaces the Udemy sample's FistLight / FistHeavy /
    /// KickLight / KickHeavy collider tags, which chose the reaction and the hit sound.
    ///
    /// Place in: Assets/_Project/Scripts/Fighters/AttackData.cs
    /// </summary>
    [CreateAssetMenu(menuName = "KungFu Crashout/Attack Data", fileName = "NewAttack")]
    public class AttackData : ScriptableObject
    {
        [Header("Damage")]
        [Min(0f)] public float damage = 5f;
        [Tooltip("Light or heavy. Heavy hits get a bigger sound, longer hitstop and screen shake.")]
        public HitWeight weight = HitWeight.Light;

        [Header("Timing (seconds)  — at 60 FPS, 1 frame ≈ 0.0167s")]
        [Tooltip("Wind-up before the hitbox appears.")]
        [Min(0f)] public float startup = 0.08f;
        [Tooltip("How long the hitbox stays out.")]
        [Min(0.01f)] public float active = 0.08f;
        [Tooltip("Cool-down after the hitbox disappears.")]
        [Min(0f)] public float recovery = 0.2f;

        [Header("Hitbox (relative to the fighter's centre, drawn for facing right)")]
        public Vector2 hitboxOffset = new Vector2(0.8f, 0.1f);
        public Vector2 hitboxSize = new Vector2(1f, 0.7f);

        [Header("On hit")]
        [Tooltip("How far the target slides back (world units).")]
        [Min(0f)] public float knockback = 0.4f;
        [Tooltip("How long the target can't act (seconds). Must be longer than the time until your next hit lands, or the combo can be escaped.")]
        [Min(0f)] public float hitstun = 0.3f;

        [Header("When blocked (M3)")]
        [Tooltip("Fraction of the damage that still gets through a block. Chip damage can never KO.")]
        [Range(0f, 1f)] public float chipDamage = 0.15f;
        [Tooltip("How long the blocker is stuck in block (seconds). 0 = automatic (60% of hitstun).")]
        [Min(0f)] public float blockstun = 0f;
        [Tooltip("How far the blocker slides back (world units).")]
        [Min(0f)] public float blockPushback = 0.25f;
        [Tooltip("Stamina this attack takes from a MANUAL block. -1 = automatic (Damage × the blocker's Block Stamina Per Damage). " +
                 "A value at or above the blocker's max stamina (e.g. 999) always breaks the guard. " +
                 "Auto-blocks at neutral always use the automatic cost.")]
        public float blockStaminaCost = -1f;

        [Header("Movement")]
        [Tooltip("How far the attacker steps forward during startup. Keeps combos in range.")]
        [Min(0f)] public float lunge = 0.15f;
        [Tooltip("SPECIALS: how far the attacker travels forward DURING the active frames, hitbox out the whole way " +
                 "(e.g. the Elbow Slide gap closer). 0 = stands still while hitting.")]
        [Min(0f)] public float activeTravel = 0f;

        [Header("Launch on hit (specials)")]
        [Tooltip("A clean hit launches the target into the air with these settings (e.g. the Uppercut). Its Downed Time = 0 " +
                 "lands them on their feet (pure juggle); above 0 = they land Downed (a stagger). Empty = no launch.")]
        public ThrowableData launchOnHit;
        [Tooltip("Launch direction for a target on your right (x is mirrored). (0.1, 1) = nearly straight up. " +
                 "The angle is kept inside the Launch On Hit asset's Min / Max Launch Angle.")]
        public Vector2 launchDirection = new Vector2(0.1f, 1f);

        public float TotalDuration => startup + active + recovery;
        public float Blockstun => blockstun > 0f ? blockstun : hitstun * 0.6f;
    }
}
