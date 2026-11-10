using TMPro;
using UnityEngine;
using UnityEngine.UI;
using KungFuCrashout.Core;

namespace KungFuCrashout.UI
{
    /// <summary>
    /// Leaderboard popup on the Start screen (UI Standards §5), "This phone" tab: wins, losses, win rate, current and best
    /// streak, most-played fighter (PlayerStats). The Online tab is shown greyed until multiplayer exists.
    /// Setup: KungFu Crashout → GDD Build → Build Roster + Start Screens. Place in: Assets/_Project/Scripts/UI/RecordPanel.cs
    /// </summary>
    public class RecordPanel : MonoBehaviour
    {
        [SerializeField] Button openButton;
        [SerializeField] GameObject panel;
        [SerializeField] Button closeButton;
        [SerializeField] TMP_Text statsText;
        [SerializeField] Button resetButton;

        void Awake()
        {
            if (panel) panel.SetActive(false);
            if (openButton) openButton.onClick.AddListener(Open);
            if (closeButton) closeButton.onClick.AddListener(Close);
            if (resetButton) resetButton.onClick.AddListener(() => { PlayerStats.ResetAll(); Fill(); });
        }

        public bool IsOpen => panel && panel.activeSelf;

        public void Open()
        {
            Fill();
            if (panel) panel.SetActive(true);
        }

        public void Close()
        {
            if (panel) panel.SetActive(false);
        }

        void Fill()
        {
            if (!statsText) return;
            statsText.text =
                $"<b>Wins</b>\t\t{PlayerStats.Wins}\n" +
                $"<b>Losses</b>\t\t{PlayerStats.Losses}\n" +
                $"<b>Draws</b>\t\t{PlayerStats.Draws}\n" +
                $"<b>Win rate</b>\t{PlayerStats.WinRate:P0}\n" +
                $"<b>Streak</b>\t\t{PlayerStats.Streak}   (best {PlayerStats.BestStreak})\n" +
                $"<b>Most played</b>\t{PlayerStats.MostPlayed}";
        }
    }
}
