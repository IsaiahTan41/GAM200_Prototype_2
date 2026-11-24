using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using KungFuCrashout.Core;

namespace KungFuCrashout.UI
{
    /// <summary>
    /// Leaderboard screen (GamConcepts_UI_19 + Leaderboard UI.txt), opened from the Start screen's trophy.
    ///   • Left: the ranking list (STATIC for now: the Players list below). 1st / 2nd / 3rd use their own border, number
    ///     icon and text colour; from 4th on the light and dark borders alternate. Fewer than Min Rows players: the
    ///     missing rows show "-" in the same format. The list scrolls when there are more rows than fit.
    ///   • The user's row is pinned under the list (static: You).
    ///   • The glow (Ranking Gradient) sits a layer above the rankings, between the second-last ranking and the user row,
    ///     to show there are more rankings; it disappears once the list is scrolled to the last player.
    ///   • Right: "Your Stats" — this phone's record (PlayerStats: the old Start-screen leaderboard popup): wins, losses,
    ///     win rate, best streak, most played.
    ///   • "This Phone" / "Online" switch their selected / unselected art; the glow sits behind the selected one.
    ///     Online has no data yet (the list stays as it is).
    ///   • Back lantern / Android Back → Start screen.
    /// Built by Window → KungFu Crashout → GDD Build → Build Leaderboard Screen (Concept 19).
    /// Place in: Assets/_Project/Scripts/UI/LeaderboardMenu.cs
    /// </summary>
    public class LeaderboardMenu : MonoBehaviour
    {
        [Serializable]
        public class Player
        {
            public string name = "-";
            public int wins;
            public int losses;
            [Tooltip("Win % shown. -1 = worked out from wins / (wins + losses).")]
            public int winPercent = -1;
            public Player() { }
            public Player(string n, int w, int l, int p) { name = n; wins = w; losses = l; winPercent = p; }
        }

        [Serializable]
        public class Row
        {
            public RectTransform root;
            public Image border;
            public Image icon;
            public TMP_Text rank, playerName, wins, losses, rate;
        }

        [Header("Ranking list (static for now)")]
        [SerializeField] List<Player> players = new List<Player>
        {
            new Player("jeromeisnthere", 41, 9, 89),
            new Player("sychjelly", 37, 12, 76),
            new Player("tazern", 30, 11, 73),
            new Player("oolonging", 28, 14, 67),
            new Player("tooer", 25, 15, 63),
        };
        [Tooltip("The user's pinned row.")]
        [SerializeField] int youRank = 67;
        [SerializeField] Player you = new Player("Jegnomes", 1, 99, 1);
        [Tooltip("Rows always shown; missing players show \"-\".")]
        [SerializeField, Min(1)] int minRows = 5;

        [Header("List parts")]
        [SerializeField] ScrollRect scroll;
        [SerializeField] RectTransform content;
        [Tooltip("Hidden template, copied for each ranking.")]
        [SerializeField] Row template = new Row();
        [SerializeField] Row userRow = new Row();
        [SerializeField] float rowHeight = 103f;
        [Tooltip("Glow between the second-last ranking and the user row.")]
        [SerializeField] Image moreGlow;

        [Header("Row art")]
        [SerializeField] Sprite firstBorder, secondBorder, thirdBorder, lightBorder, darkBorder;
        [SerializeField] Sprite firstIcon, secondIcon, thirdIcon;
        [SerializeField] Color firstText = new Color32(0xEB, 0xD9, 0x97, 0xFF);
        [SerializeField] Color secondText = new Color32(0xC2, 0xCF, 0xC8, 0xFF);
        [SerializeField] Color thirdText = new Color32(0xE3, 0xB4, 0x86, 0xFF);
        [SerializeField] Color otherText = new Color32(0xC8, 0xBA, 0xA0, 0xFF);

        [Header("Your Stats (this phone)")]
        [SerializeField] TMP_Text winsValue;
        [SerializeField] TMP_Text lossesValue;
        [SerializeField] TMP_Text winRateValue;
        [SerializeField] TMP_Text bestStreakValue;
        [SerializeField] TMP_Text mostPlayedValue;

        [Header("Tabs")]
        [SerializeField] Button thisPhoneButton;
        [SerializeField] Button onlineButton;
        [SerializeField] Sprite tabSelected, tabUnselected;
        [SerializeField] RectTransform tabGlow;

        [Header("Back")]
        [SerializeField] Button backButton;
        [SerializeField] string backScene = "StartScreen";

        readonly List<Row> rows = new List<Row>();
        bool online;

        void Awake()
        {
            Time.timeScale = 1f;
            if (backButton) backButton.onClick.AddListener(Back);
            if (thisPhoneButton) thisPhoneButton.onClick.AddListener(() => SetTab(false));
            if (onlineButton) onlineButton.onClick.AddListener(() => SetTab(true));
            if (scroll) scroll.onValueChanged.AddListener(_ => UpdateGlow());
        }

        void Start()
        {
            BuildList();
            FillStats();
            SetTab(false);
        }

        void Update()
        {
            var kb = Keyboard.current;
            if (kb != null && kb.escapeKey.wasPressedThisFrame) Back();
        }

        void LateUpdate() => UpdateGlow(); // layout settles a frame after Start

        // ───────────── List ─────────────

        void BuildList()
        {
            foreach (var r in rows) if (r.root) Destroy(r.root.gameObject);
            rows.Clear();
            if (template.root) template.root.gameObject.SetActive(false);
            int n = Mathf.Max(minRows, players.Count);
            for (int i = 0; i < n; i++)
            {
                if (!template.root || !content) break;
                var go = Instantiate(template.root.gameObject, content);
                go.name = $"Rank {i + 1}";
                go.SetActive(true);
                var row = Bind(go.transform, template);
                var rt = row.root;
                rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
                rt.pivot = new Vector2(0f, 1f);
                rt.anchoredPosition = new Vector2(0f, -i * rowHeight);
                Fill(row, i + 1, i < players.Count ? players[i] : null, false);
                rows.Add(row);
            }
            if (content) content.sizeDelta = new Vector2(content.sizeDelta.x, n * rowHeight);
            Fill(userRow, youRank, you, true);
            if (scroll) scroll.verticalNormalizedPosition = 1f;
        }

        /// <summary>The copy's parts, found by the template parts' relative paths.</summary>
        static Row Bind(Transform copy, Row t)
        {
            T Find<T>(Component part) where T : Component
            {
                if (!part) return null;
                if (part.transform == t.root) return copy.GetComponent<T>();
                var path = part.transform.name;
                for (var p = part.transform.parent; p && p != t.root; p = p.parent) path = p.name + "/" + path;
                var f = copy.Find(path);
                return f ? f.GetComponent<T>() : null;
            }
            return new Row
            {
                root = (RectTransform)copy,
                border = Find<Image>(t.border),
                icon = Find<Image>(t.icon),
                rank = Find<TMP_Text>(t.rank),
                playerName = Find<TMP_Text>(t.playerName),
                wins = Find<TMP_Text>(t.wins),
                losses = Find<TMP_Text>(t.losses),
                rate = Find<TMP_Text>(t.rate),
            };
        }

        void Fill(Row r, int rank, Player p, bool isUser)
        {
            if (r == null || !r.root) return;
            bool top3 = !isUser && rank <= 3;
            if (r.border && !isUser)
                r.border.sprite = rank == 1 ? firstBorder : rank == 2 ? secondBorder : rank == 3 ? thirdBorder
                                : (rank % 2 == 0 ? lightBorder : darkBorder); // 4 light, 5 dark, 6 light …
            if (r.icon)
            {
                r.icon.sprite = rank == 1 ? firstIcon : rank == 2 ? secondIcon : thirdIcon;
                r.icon.enabled = top3 && r.icon.sprite;
            }
            Color c = isUser ? otherText : rank == 1 ? firstText : rank == 2 ? secondText : rank == 3 ? thirdText : otherText;
            bool has = p != null && !string.IsNullOrEmpty(p.name) && p.name != "-";
            int pct = !has ? 0 : p.winPercent >= 0 ? p.winPercent
                     : (p.wins + p.losses) > 0 ? Mathf.RoundToInt(100f * p.wins / (p.wins + p.losses)) : 0;
            Set(r.rank, top3 && r.icon && r.icon.sprite ? "" : rank.ToString(), c);
            Set(r.playerName, has ? p.name : "-", c);
            Set(r.wins, has ? p.wins.ToString() : "-", c);
            Set(r.losses, has ? p.losses.ToString() : "-", c);
            Set(r.rate, has ? pct + "%" : "-", c);
        }

        static void Set(TMP_Text t, string s, Color c)
        {
            if (!t) return;
            t.text = s;
            t.color = c;
        }

        /// <summary>Glow on while there are more rankings below: hidden once scrolled to the last player.</summary>
        void UpdateGlow()
        {
            if (!moreGlow) return;
            bool more;
            float viewH = scroll && scroll.viewport ? scroll.viewport.rect.height : 0f;
            if (content && viewH > 0f && content.rect.height > viewH + 1f)
                more = scroll.verticalNormalizedPosition > 0.01f;               // not at the last player yet
            else
                more = youRank > players.Count;                                  // the list stops before the user's rank
            if (moreGlow.enabled != more) moreGlow.enabled = more;
        }

        // ───────────── Stats (the old Start-screen leaderboard popup) ─────────────

        void FillStats()
        {
            if (winsValue) winsValue.text = PlayerStats.Wins.ToString();
            if (lossesValue) lossesValue.text = PlayerStats.Losses.ToString();
            if (winRateValue) winRateValue.text = Mathf.RoundToInt(PlayerStats.WinRate * 100f) + "%";
            if (bestStreakValue) bestStreakValue.text = PlayerStats.BestStreak.ToString();
            if (mostPlayedValue) mostPlayedValue.text = string.IsNullOrEmpty(PlayerStats.MostPlayed) ? "-" : PlayerStats.MostPlayed;
        }

        [ContextMenu("Reset This Phone's Stats")]
        void ResetStats()
        {
            PlayerStats.ResetAll();
            FillStats();
        }

        // ───────────── Tabs / back ─────────────

        void SetTab(bool toOnline)
        {
            online = toOnline;
            SetTabLook(thisPhoneButton, !online);
            SetTabLook(onlineButton, online);
            var sel = online ? onlineButton : thisPhoneButton;
            if (tabGlow && sel)
            {
                var srt = (RectTransform)sel.transform;
                tabGlow.anchoredPosition = srt.anchoredPosition;
            }
        }

        void SetTabLook(Button b, bool selected)
        {
            if (!b) return;
            var img = b.targetGraphic as Image;
            if (img) img.sprite = selected ? tabSelected : tabUnselected;
        }

        void Back() => SceneLoader.Load(backScene);
    }
}
