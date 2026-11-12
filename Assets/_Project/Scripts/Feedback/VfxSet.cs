using System;
using System.Collections.Generic;
using UnityEngine;
using KungFuCrashout.Fighters;

namespace KungFuCrashout.Feedback
{
    /// <summary>
    /// One character's effects (e.g. VfxSet_Monk): which cue plays for each attack, hit, block, parry and movement.
    /// FighterVfx reads it. A second character (Rushdown) only needs its own VfxSet, no code.
    ///
    /// Attack effects play on the attack's first ACTIVE frame (the clip's HitboxOn event, or AttackData timing),
    /// at the hitbox, so the slash lines up with the frame that can hit.
    ///
    /// Create: Window → KungFu Crashout → VFX → 1. Import VFX Folder makes VfxSet_Monk,
    /// or Right-click → Create → KungFu Crashout → VFX Set.
    /// Place in: Assets/_Project/Scripts/Feedback/VfxSet.cs
    /// </summary>
    [CreateAssetMenu(menuName = "KungFu Crashout/VFX Set", fileName = "VfxSet_New")]
    public class VfxSet : ScriptableObject
    {
        [Serializable]
        public struct AttackVfx
        {
            [Tooltip("Played at the hitbox (the slash).")]
            public VfxCue strike;
            [Tooltip("Played at the attacker's feet (the kicked-up dust).")]
            public VfxCue dust;
            [Tooltip("Size multiplier for both, e.g. 1.3 to reuse a light slash for a heavy.")]
            [Min(0f)] public float scale;

            public bool IsEmpty => !strike && !dust;
            public float Scale => scale > 0f ? scale : 1f;
        }

        [Serializable]
        public struct AttackOverride
        {
            public AttackData attack;
            public AttackVfx vfx;
        }

        [Header("Attacks (first active frame)")]
        [Tooltip("Light combo steps 1, 2, 3… Steps past the end reuse the last entry.")]
        public AttackVfx[] lightCombo = new AttackVfx[3];
        public AttackVfx heavy;
        [Tooltip("All three specials (unless overridden below).")]
        public AttackVfx special;
        [Tooltip("Specific moves that need their own look (e.g. the up special). Checked first.")]
        public List<AttackOverride> perAttack = new List<AttackOverride>();

        [Header("Landing a hit (played by the attacker)")]
        [Tooltip("Clean light hit, at the contact point, streaks pointing away from the attacker.")]
        public VfxCue hit;
        [Tooltip("Clean heavy hit, special or thrown item. Empty = Hit.")]
        public VfxCue heavyHit;
        [Tooltip("The round-ending hit (played on top of the hit). Empty = none.")]
        public VfxCue ko;

        [Header("Defending (played by the defender)")]
        [Tooltip("Blocked a hit (blue lotus at the feet).")]
        public VfxCue block;
        [Tooltip("Parried a hit (yellow lotus at the feet).")]
        public VfxCue parry;
        [Tooltip("Parried a hit: the burst at the contact point, on top of Parry.")]
        public VfxCue parryImpact;
        [Tooltip("Guard broken. Empty = none.")]
        public VfxCue guardBreak;

        [Header("Movement")]
        public VfxCue dash;
        public VfxCue jump;
        public VfxCue land;
        [Tooltip("Landing Downed after an item launch, and ground bounces.")]
        public VfxCue knockdown;

        /// <summary>The effects for the fighter's current attack.</summary>
        public AttackVfx For(Fighter fighter, AttackData attack)
        {
            foreach (var o in perAttack)
                if (o.attack == attack && !o.vfx.IsEmpty) return o.vfx;

            if (fighter.IsDoingSpecial) return special;

            int step = fighter.ComboStep; // 1, 2, 3 in the light combo; 0 otherwise
            if (step >= 1 && lightCombo != null && lightCombo.Length > 0)
                return lightCombo[Mathf.Min(step, lightCombo.Length) - 1];

            return heavy;
        }
    }
}
