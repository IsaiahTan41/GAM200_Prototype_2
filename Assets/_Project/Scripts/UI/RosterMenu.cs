using System;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using KungFuCrashout.Core;
using KungFuCrashout.Fighters;

namespace KungFuCrashout.UI
{
    /// <summary>
    /// The Roster (UI Standards §3, vs CPU): 1P preview always on the LEFT, CPU preview always on the RIGHT, the 3 × 2
    /// fighter grid in the centre with VS above it, difficulty chips under it, READY (primary) bottom-right, Back top-left,
    /// and the stage chip top-right ("Stage: Restaurant").
    ///   • Tap a tile = 1P's fighter. Locked archetypes (Zoner, Grappler, Footsies, Trapper) are grey with a lock and show
    ///     "Coming soon" — their names stay visible.
    ///   • Tap the CPU preview (or swipe it) to cycle the CPU's fighter through the playable ones.
    ///   • Easy / Normal / Hard = the behaviour-tree AI's tier (saved on the phone, GameSettings.Difficulty).
    ///   • READY stores the picks in MatchSetup and loads the fight. Back / Android Back → Start screen.
    ///   • Lock Matchup (playtest, on by default): 1P is always Locked Player (Nam-pi, the Monk) and the CPU always Locked
    ///     Cpu (Rushdown, the fists fighter), so each character plays with its own art. Other tiles show why they can't
    ///     be picked. Untick it to open the picks.
    /// The 1P cursor is a solid gold frame + "1P" tag; the CPU's is a white frame + "CPU" tag (shape and word, not only colour).
    ///
    /// Setup: KungFu Crashout → GDD Build → Build Roster + Start Screens. Place in: Assets/_Project/Scripts/UI/RosterMenu.cs
    /// </summary>
    public class RosterMenu : MonoBehaviour
    {
        [Serializable]
        public class Tile
        {
            public Button button;
            public Image background;
            public Outline frame;
            public TMP_Text nameText;
            public GameObject lockBadge;
            public GameObject playerTag;
            public GameObject cpuTag;
        }

        [Serializable]
        public class Preview
        {
            public Image pose;
            public TMP_Text nameText;
            public TMP_Text archetypeText;
            public TMP_Text signatureText;
        }

        [Header("Roster (same order as the tiles)")]
        [SerializeField] FighterArchetype[] roster = new FighterArchetype[6];
        [SerializeField] Tile[] tiles = new Tile[6];

        [Header("Previews")]
        [SerializeField] Preview playerPreview = new Preview();
        [SerializeField] Preview cpuPreview = new Preview();
        [Tooltip("Tapping the CPU preview cycles its fighter.")]
        [SerializeField] Button cpuCycleButton;

        [Header("Difficulty chips (Easy, Normal, Hard)")]
        [SerializeField] Button[] difficultyChips = new Button[3];

        [Header("Buttons")]
        [SerializeField] Button readyButton;
        [SerializeField] Button backButton;
        [SerializeField] TMP_Text toastText;
        [SerializeField] TMP_Text stageText;
        [SerializeField] TMP_Text recordText;

        [Header("Playtest lock")]
        [Tooltip("ON: 1P always plays Locked Player and the CPU always plays Locked Cpu (archetypes don't swap art yet).")]
        [SerializeField] bool lockMatchup = true;
        [SerializeField] FighterArchetype lockedPlayer;
        [SerializeField] FighterArchetype lockedCpu;

        [Header("Scenes")]
        [SerializeField] string fightScene = "FightScene";
        [SerializeField] string backScene = "StartScreen";

        [Header("Look")]
        [SerializeField] Color gold = new Color(1f, 0.78f, 0.2f);
        [SerializeField] Color lockedColour = new Color(0.35f, 0.35f, 0.38f);
        [SerializeField] Color chipColour = new Color(0.16f, 0.16f, 0.19f);

        int playerIndex, cpuIndex;
        float toastUntil;
        Vector2 cpuSwipeStart;
        bool cpuSwiping;

        void Awake()
        {
            Time.timeScale = 1f;
            for (int i = 0; i < tiles.Length; i++)
            {
                int index = i;
                if (tiles[i] != null && tiles[i].button) tiles[i].button.onClick.AddListener(() => OnTile(index));
            }
            for (int i = 0; i < difficultyChips.Length; i++)
            {
                var d = (Difficulty)i;
                if (difficultyChips[i]) difficultyChips[i].onClick.AddListener(() => { GameSettings.Difficulty = d; Refresh(); });
            }
            if (cpuCycleButton) cpuCycleButton.onClick.AddListener(() => CycleCpu(1));
            if (readyButton) readyButton.onClick.AddListener(Ready);
            if (backButton) backButton.onClick.AddListener(Back);
        }

        void Start()
        {
            // Keep the last picks (Change fighter from Match over returns here with both kept).
            playerIndex = IndexOf(MatchSetup.PlayerPick, FirstPlayable(0));
            cpuIndex = IndexOf(MatchSetup.CpuPick, NextPlayable(playerIndex, 1));
            if (Locked)
            {
                playerIndex = IndexOf(lockedPlayer, playerIndex);
                cpuIndex = IndexOf(lockedCpu, cpuIndex);
            }
            if (stageText) stageText.text = $"Stage: {MatchSetup.Stage}";
            if (recordText)
                recordText.text = PlayerStats.Played == 0 ? "This phone: no matches yet"
                    : $"This phone: {PlayerStats.Wins}W · {PlayerStats.Losses}L · win rate {PlayerStats.WinRate:P0}";
            if (toastText) toastText.gameObject.SetActive(false);
            Refresh();
        }

        void Update()
        {
            var kb = Keyboard.current;
            if (kb != null && kb.escapeKey.wasPressedThisFrame) Back();
            if (toastText && toastText.gameObject.activeSelf && Time.unscaledTime >= toastUntil) toastText.gameObject.SetActive(false);
            CheckCpuSwipe();
        }

        // ───────────── Picks ─────────────

        void OnTile(int i)
        {
            var a = Get(i);
            if (!a) return;
            if (!a.playable)
            {
                Toast($"{a.displayName}: coming soon");
                return;
            }
            if (Locked && a != lockedPlayer)
            {
                Toast(a == lockedCpu ? $"Playtest: {a.displayName} is the CPU this round of testing"
                                     : $"Playtest: 1P plays {lockedPlayer.displayName}");
                return;
            }
            playerIndex = i;
            if (cpuIndex == playerIndex && CountPlayable() > 1) cpuIndex = NextPlayable(playerIndex, 1);
            Refresh();
        }

        void CycleCpu(int step)
        {
            if (Locked) { Toast($"Playtest: the CPU plays {lockedCpu.displayName}"); return; }
            cpuIndex = NextPlayable(cpuIndex, step);
            Refresh();
        }

        /// <summary>Swiping across the CPU preview cycles it too (UI Standards: "swipe a preview to cycle fighters").</summary>
        void CheckCpuSwipe()
        {
            if (!cpuCycleButton) return;
            var ts = Touchscreen.current;
            if (ts == null) return;
            var touch = ts.primaryTouch;
            var rt = (RectTransform)cpuCycleButton.transform;
            if (touch.press.wasPressedThisFrame && RectTransformUtility.RectangleContainsScreenPoint(rt, touch.position.ReadValue(), null))
            {
                cpuSwipeStart = touch.position.ReadValue();
                cpuSwiping = true;
            }
            else if (cpuSwiping && touch.press.wasReleasedThisFrame)
            {
                cpuSwiping = false;
                float dx = touch.position.ReadValue().x - cpuSwipeStart.x;
                if (Mathf.Abs(dx) > Mathf.Min(Screen.width, Screen.height) * 0.08f) CycleCpu(dx > 0f ? -1 : 1);
            }
        }

        void Ready()
        {
            MatchSetup.PlayerPick = Get(playerIndex);
            MatchSetup.CpuPick = Get(cpuIndex);
            SceneLoader.Load(fightScene);
        }

        void Back() => SceneLoader.Load(backScene);

        // ───────────── View ─────────────

        void Refresh()
        {
            for (int i = 0; i < tiles.Length; i++)
            {
                var t = tiles[i];
                var a = Get(i);
                if (t == null) continue;
                bool playable = a && a.playable;
                if (t.nameText) t.nameText.text = a ? a.displayName.ToUpperInvariant() : "-";
                if (t.background) t.background.color = playable ? a.colour : lockedColour;
                if (t.lockBadge) t.lockBadge.SetActive(!playable);
                if (t.playerTag) t.playerTag.SetActive(i == playerIndex);
                if (t.cpuTag) t.cpuTag.SetActive(i == cpuIndex);
                if (t.frame)
                {
                    t.frame.enabled = i == playerIndex || i == cpuIndex;
                    t.frame.effectColor = i == playerIndex ? gold : Color.white;
                    t.frame.effectDistance = i == playerIndex ? new Vector2(8f, -8f) : new Vector2(4f, -4f);
                }
            }

            Show(playerPreview, Get(playerIndex));
            Show(cpuPreview, Get(cpuIndex));

            int diff = (int)GameSettings.Difficulty;
            for (int i = 0; i < difficultyChips.Length; i++)
            {
                var chip = difficultyChips[i];
                if (!chip) continue;
                if (chip.targetGraphic) chip.targetGraphic.color = chipColour;
                var outline = chip.GetComponent<Outline>();
                if (outline) { outline.enabled = i == diff; outline.effectColor = gold; }
            }
        }

        static void Show(Preview p, FighterArchetype a)
        {
            if (p == null || !a) return;
            if (p.nameText) { p.nameText.text = a.displayName.ToUpperInvariant(); p.nameText.color = a.colour; }
            if (p.archetypeText) p.archetypeText.text = a.archetypeLabel;
            if (p.signatureText) p.signatureText.text = string.IsNullOrEmpty(a.signatureMove) ? "" : $"Signature: {a.signatureMove}";
            if (p.pose)
            {
                p.pose.sprite = a.preview;
                p.pose.preserveAspect = true;
                p.pose.color = a.preview ? Color.white : new Color(a.colour.r, a.colour.g, a.colour.b, 0.6f);
            }
        }

        void Toast(string text)
        {
            if (!toastText) return;
            toastText.text = text;
            toastText.gameObject.SetActive(true);
            toastUntil = Time.unscaledTime + 1.2f;
        }

        // ───────────── Helpers ─────────────

        /// <summary>The playtest lock is on and both locked archetypes are set and playable.</summary>
        bool Locked => lockMatchup && lockedPlayer && lockedCpu && lockedPlayer.playable && lockedCpu.playable;

        FighterArchetype Get(int i) => roster != null && i >= 0 && i < roster.Length ? roster[i] : null;

        int IndexOf(FighterArchetype a, int fallback)
        {
            if (a) for (int i = 0; i < roster.Length; i++) if (roster[i] == a && a.playable) return i;
            return fallback;
        }

        int FirstPlayable(int from) => NextPlayable(from - 1, 1);

        int NextPlayable(int from, int step)
        {
            int n = roster.Length;
            for (int k = 1; k <= n; k++)
            {
                int i = ((from + step * k) % n + n) % n;
                if (roster[i] && roster[i].playable) return i;
            }
            return Mathf.Clamp(from, 0, n - 1);
        }

        int CountPlayable()
        {
            int c = 0;
            foreach (var a in roster) if (a && a.playable) c++;
            return c;
        }
    }
}
