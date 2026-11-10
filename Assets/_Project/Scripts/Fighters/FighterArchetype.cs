using UnityEngine;

namespace KungFuCrashout.Fighters
{
    /// <summary>
    /// One roster character (GDD → "Wide variety of characters" / "Unique character mechanics").
    /// Holds everything that makes an archetype play differently, as data:
    ///   • Move set — light combo, heavy, and the three directional specials (swipe + tap)
    ///   • Movement — dash distance / duration / cooldown (a Rushdown dashes further and more often)
    ///   • Roster info — name, archetype, signature move, colour, preview pose, and whether it's playable yet
    ///
    /// Applied to a Fighter at the start of the fight by ArchetypeLoadout (the Roster's picks, or its fallback).
    /// The prototype ships two playable archetypes (GDD: "Main priority for prototype"):
    ///   Nam-pi   — Shoto all-rounder, the baseline the rest of the cast is measured against
    ///   Rushdown — fast and aggressive: longer, quicker dashes, faster lights, gap-closing specials
    /// The other four (Zoner, Grappler, Footsies, Trapper) are listed on the Roster as locked, "coming soon".
    ///
    /// Create: KungFu Crashout → GDD Build → Create GDD Data Assets (or Right-click → Create → KungFu Crashout → Fighter Archetype).
    /// Place in: Assets/_Project/Scripts/Fighters/FighterArchetype.cs
    /// </summary>
    [CreateAssetMenu(menuName = "KungFu Crashout/Fighter Archetype", fileName = "Archetype_New")]
    public class FighterArchetype : ScriptableObject
    {
        [Header("Roster")]
        public string displayName = "Nam-pi";
        [Tooltip("Shown under the name, e.g. \"Shoto · all-rounder\".")]
        public string archetypeLabel = "Shoto · all-rounder";
        [Tooltip("Shown on the Roster preview.")]
        public string signatureMove = "Elbow Slide";
        [TextArea(2, 4)] public string description = "";
        [Tooltip("Locked archetypes show as grey tiles with a lock (\"Coming soon\").")]
        public bool playable = true;
        [Tooltip("Tile / name colour on the Roster.")]
        public Color colour = new Color(0.85f, 0.25f, 0.2f);
        [Tooltip("Pose shown large on the Roster preview. Empty = a coloured silhouette.")]
        public Sprite preview;

        [Header("Move set (empty = keep the Fighter's own)")]
        public AttackData[] lightCombo = new AttackData[3];
        public AttackData heavyAttack;
        [Tooltip("Swipe sideways + tap.")]
        public AttackData sideSpecial;
        [Tooltip("Swipe up + tap.")]
        public AttackData upSpecial;
        [Tooltip("Swipe down + tap.")]
        public AttackData downSpecial;

        [Header("Movement (0 = keep the Fighter's own)")]
        [Min(0f)] public float dashDistance = 2.5f;
        [Min(0f)] public float dashDuration = 0.18f;
        [Min(0f)] public float dashCooldown = 0.15f;
    }
}
