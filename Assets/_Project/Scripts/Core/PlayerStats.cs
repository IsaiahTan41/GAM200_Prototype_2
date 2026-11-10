using UnityEngine;

namespace KungFuCrashout.Core
{
    /// <summary>
    /// "This phone" stats (GDD → Multiplayer system: "Leaderboard system and in-game stats to keep track of wins").
    /// Online play is cut from the prototype, so these are the local record against the CPU, saved on the device:
    /// wins, losses, draws, win rate, current and best win streak, and the most-played fighter.
    /// Written on the Match over screen (MatchManager), shown there and on the Start screen's Record panel.
    /// Place in: Assets/_Project/Scripts/Core/PlayerStats.cs
    /// </summary>
    public static class PlayerStats
    {
        public enum Result { Win, Loss, Draw }

        const string Prefix = "kfc.stats.";

        public static int Wins => PlayerPrefs.GetInt(Prefix + "wins", 0);
        public static int Losses => PlayerPrefs.GetInt(Prefix + "losses", 0);
        public static int Draws => PlayerPrefs.GetInt(Prefix + "draws", 0);
        public static int Played => Wins + Losses + Draws;
        public static int Streak => PlayerPrefs.GetInt(Prefix + "streak", 0);
        public static int BestStreak => PlayerPrefs.GetInt(Prefix + "bestStreak", 0);
        /// <summary>0–1. Draws count as played, not won.</summary>
        public static float WinRate => Played == 0 ? 0f : Wins / (float)Played;
        public static string MostPlayed => PlayerPrefs.GetString(Prefix + "mostPlayed", "-");

        public static void Record(Result result, string fighterName)
        {
            switch (result)
            {
                case Result.Win:
                    Add("wins");
                    int s = Streak + 1;
                    PlayerPrefs.SetInt(Prefix + "streak", s);
                    if (s > BestStreak) PlayerPrefs.SetInt(Prefix + "bestStreak", s);
                    break;
                case Result.Loss:
                    Add("losses");
                    PlayerPrefs.SetInt(Prefix + "streak", 0);
                    break;
                default:
                    Add("draws");
                    break;
            }

            if (!string.IsNullOrEmpty(fighterName))
            {
                string key = Prefix + "plays." + fighterName;
                int plays = PlayerPrefs.GetInt(key, 0) + 1;
                PlayerPrefs.SetInt(key, plays);
                string top = MostPlayed;
                int topPlays = top == "-" ? 0 : PlayerPrefs.GetInt(Prefix + "plays." + top, 0);
                if (plays >= topPlays) PlayerPrefs.SetString(Prefix + "mostPlayed", fighterName);
            }
            PlayerPrefs.Save();
        }

        /// <summary>One line for the Match over screen, e.g. "Record 5W · 3L · streak 2 (best 4)".</summary>
        public static string Summary() =>
            $"Record {Wins}W · {Losses}L" + (Draws > 0 ? $" · {Draws}D" : "") + $"   ·   streak {Streak} (best {BestStreak})";

        public static void ResetAll()
        {
            foreach (var k in new[] { "wins", "losses", "draws", "streak", "bestStreak", "mostPlayed" })
                PlayerPrefs.DeleteKey(Prefix + k);
            PlayerPrefs.Save();
        }

        static void Add(string key) => PlayerPrefs.SetInt(Prefix + key, PlayerPrefs.GetInt(Prefix + key, 0) + 1);
    }
}
