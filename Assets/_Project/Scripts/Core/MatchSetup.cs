using KungFuCrashout.Fighters;

namespace KungFuCrashout.Core
{
    /// <summary>
    /// What the Roster chose for the next fight, carried across the scene load (static, so no GameObject needed):
    /// the two archetypes and the stage. Difficulty lives in GameSettings (saved on the phone).
    /// Also lets runtime-spawned things (e.g. stage items) find the human player without scene references.
    /// Place in: Assets/_Project/Scripts/Core/MatchSetup.cs
    /// </summary>
    public static class MatchSetup
    {
        /// <summary>1P's archetype (null = the fighter's fallback).</summary>
        public static FighterArchetype PlayerPick { get; set; }
        /// <summary>CPU / 2P archetype (null = the fighter's fallback).</summary>
        public static FighterArchetype CpuPick { get; set; }
        /// <summary>Stage shown on the Roster chip. The prototype has one: the Restaurant.</summary>
        public static string Stage { get; set; } = "Restaurant";

        /// <summary>The human player's fighter in the running fight (set by MatchManager).</summary>
        public static Fighter Player { get; set; }
    }
}
