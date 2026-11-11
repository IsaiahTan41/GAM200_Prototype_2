using System.Reflection;
using UnityEngine;
using KungFuCrashout.Core;

namespace KungFuCrashout.Fighters
{
    /// <summary>
    /// Puts the chosen roster archetype (FighterArchetype) onto this Fighter before the fight starts.
    ///   Side = Player → the 1P pick from the Roster (MatchSetup.PlayerPick)
    ///   Side = CPU    → the 2P / CPU pick (MatchSetup.CpuPick)
    ///   Nothing picked (e.g. FightScene opened straight from the editor) → Fallback.
    ///
    /// Fighter.cs is deliberately left exactly as in Prototype 2 (so the tuned combat feel can't regress); its move set and
    /// dash numbers are private Inspector fields, so they're written here by name. If a field is ever renamed in Fighter,
    /// the Console says which one, and the rest still apply.
    ///
    /// Setup: KungFu Crashout → GDD Build → Upgrade Fight Scene adds one to each fighter.
    /// Place in: Assets/_Project/Scripts/Fighters/ArchetypeLoadout.cs
    /// </summary>
    [RequireComponent(typeof(Fighter))]
    [DefaultExecutionOrder(-200)] // before Fighter, the AI and the HUD read the move set
    public class ArchetypeLoadout : MonoBehaviour
    {
        public enum Side { Player, Cpu }

        [SerializeField] Side side = Side.Player;
        [Tooltip("Used when nothing was picked on the Roster.")]
        [SerializeField] FighterArchetype fallback;
        [Tooltip("TESTING: always use the Fallback, even when the Roster picked something (e.g. the CPU Monk copy).")]
        [SerializeField] bool ignoreRosterPick = false;
        [Tooltip("Added after the archetype's name on the HUD, e.g. \" · CPU\" for a mirror match.")]
        [SerializeField] string nameSuffix = "";

        /// <summary>The archetype applied this fight (null = the Fighter's own Inspector set-up).</summary>
        public FighterArchetype Applied { get; private set; }
        public Side FighterSide => side;

        const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

        void Awake()
        {
            var pick = side == Side.Player ? MatchSetup.PlayerPick : MatchSetup.CpuPick;
            Apply(pick && !ignoreRosterPick ? pick : fallback);
        }

        public void Apply(FighterArchetype a)
        {
            if (!a) return;
            var fighter = GetComponent<Fighter>();
            Applied = a;

            Set(fighter, "displayName", a.displayName + nameSuffix, !string.IsNullOrEmpty(a.displayName));
            if (a.lightCombo != null && a.lightCombo.Length > 0 && a.lightCombo[0])
                Set(fighter, "lightCombo", (AttackData[])a.lightCombo.Clone(), true);
            Set(fighter, "heavyAttack", a.heavyAttack, a.heavyAttack);
            Set(fighter, "sideSpecial", a.sideSpecial, a.sideSpecial);
            Set(fighter, "upSpecial", a.upSpecial, a.upSpecial);
            Set(fighter, "downSpecial", a.downSpecial, a.downSpecial);
            Set(fighter, "dashDistance", a.dashDistance, a.dashDistance > 0f);
            Set(fighter, "dashDuration", a.dashDuration, a.dashDuration > 0f);
            Set(fighter, "dashCooldown", a.dashCooldown, a.dashCooldown > 0f);
        }

        void Set(Fighter fighter, string field, object value, bool when)
        {
            if (!when) return;
            var f = typeof(Fighter).GetField(field, Flags);
            if (f == null)
            {
                Debug.LogWarning($"[ArchetypeLoadout] Fighter has no field '{field}' any more; that part of the archetype wasn't applied.", this);
                return;
            }
            f.SetValue(fighter, value);
        }
    }
}
