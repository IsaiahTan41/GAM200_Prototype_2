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
    /// Character select in the Concept 13 / 14 layout ("Choose your fighter"), vs the CPU:
    ///   • Carousel in the centre: the focused fighter's card full size, its neighbours at 80 % with a 20 % black overlay.
    ///     The arrows (or tapping a side card) turn it; the focused fighter becomes P1's pick when it can be picked.
    ///   • P1 (left, green) and P2 = the CPU (right, red): big illustration, class / name / skill on the nameplate.
    ///     Small P1 / P2 markers sit on the cards each side has picked.
    ///   • Countdown (Time Limit, 90 s): at 0 the current picks go to the fight (Auto Ready At Zero).
    ///   • READY (short scroll) → "Waiting for the other player..." (long scroll, wave) for Waiting Time while the CPU
    ///     "locks in", then the fight loads. Back lantern / Android Back → Start screen.
    ///   • Playtest lock (as the old roster): 1P is always Locked Player, the CPU always Locked Cpu. Turning the carousel
    ///     onto anyone else shows why they can't be picked; P1 stays as it was.
    ///   • Not playable yet (an entry with no archetype, or a locked archetype): "coming soon".
    /// Built by Window → KungFu Crashout → GDD Build → Build Character Select (Concept 14), in the Roster scene.
    /// Place in: Assets/_Project/Scripts/UI/CharacterSelectMenu.cs
    /// </summary>
    public class CharacterSelectMenu : MonoBehaviour
    {
        [Serializable]
        public class Entry
        {
            [Tooltip("The fighter this card starts. Empty = shown but not playable yet (coming soon).")]
            public FighterArchetype archetype;
            public string displayName = "Monk Wu";
            [Tooltip("Class type, e.g. Shoto / Rushdown (nameplate and card label).")]
            public string classLabel = "Shoto";
            [Tooltip("The line under the name on the nameplate.")]
            public string skill = "";
            public Sprite card;
            [Tooltip("The big side illustration. Empty = left blank.")]
            public Sprite illustration;
            public bool Playable => archetype && archetype.playable;
        }

        [Serializable]
        public class Card
        {
            public RectTransform root;
            public Image art;
            public TMP_Text label;
            public GameObject p1Marker;
            public GameObject p2Marker;
            [Tooltip("Black, 20 % opacity, over the side cards.")]
            public Image dim;
            public Button button;
        }

        [Serializable]
        public class Side
        {
            public Image illustration;
            public TMP_Text classText;
            public TMP_Text nameText;
            public TMP_Text skillText;
        }

        [Header("Fighters (carousel order)")]
        [SerializeField] Entry[] entries = new Entry[0];

        [Header("Carousel")]
        [SerializeField] Card leftCard = new Card();
        [SerializeField] Card centreCard = new Card();
        [SerializeField] Card rightCard = new Card();
        [SerializeField] Button leftArrow;
        [SerializeField] Button rightArrow;
        [Tooltip("Pop on the centre card when the carousel turns (scale, eases back to 1).")]
        [SerializeField] float turnPop = 1.06f;

        [Header("Sides")]
        [SerializeField] Side p1 = new Side();
        [SerializeField] Side p2 = new Side();
        [Tooltip("Tapping the P2 illustration cycles the CPU's fighter (when the playtest lock is off).")]
        [SerializeField] Button p2CycleButton;

        [Header("Timer")]
        [SerializeField] TMP_Text timerText;
        [SerializeField, Min(1f)] float timeLimit = 90f;
        [SerializeField] bool autoReadyAtZero = true;

        [Header("Ready / waiting")]
        [SerializeField] Button readyButton;
        [SerializeField] GameObject readyScroll;
        [SerializeField] GameObject waitingScroll;
        [Tooltip("How long \"Waiting for the other player...\" shows before the fight loads (the CPU locking in).")]
        [SerializeField, Min(0f)] float waitingTime = 1.6f;
        [SerializeField] Button backButton;
        [SerializeField] TMP_Text toastText;

        [Header("Playtest lock")]
        [SerializeField] bool lockMatchup = true;
        [SerializeField] FighterArchetype lockedPlayer;
        [SerializeField] FighterArchetype lockedCpu;

        [Header("Scenes")]
        [SerializeField] string fightScene = "FightScene";
        [SerializeField] string backScene = "StartScreen";

        int focus, p1Pick, p2Pick;
        float timeLeft, toastUntil, popT = 1f, loadAt = -1f;
        bool committed;

        bool Locked => lockMatchup && lockedPlayer && lockedCpu && lockedPlayer.playable && lockedCpu.playable;
        int Count => entries != null ? entries.Length : 0;

        void Awake()
        {
            Time.timeScale = 1f;
            if (leftArrow) leftArrow.onClick.AddListener(() => Turn(-1));
            if (rightArrow) rightArrow.onClick.AddListener(() => Turn(1));
            if (leftCard.button) leftCard.button.onClick.AddListener(() => Turn(-1));
            if (rightCard.button) rightCard.button.onClick.AddListener(() => Turn(1));
            if (centreCard.button) centreCard.button.onClick.AddListener(() => TryPickP1(focus, true));
            if (readyButton) readyButton.onClick.AddListener(Ready);
            if (backButton) backButton.onClick.AddListener(Back);
            if (p2CycleButton) p2CycleButton.onClick.AddListener(CycleCpu);
        }

        void Start()
        {
            int fallback = FirstPlayable();
            p1Pick = IndexOf(Locked ? lockedPlayer : MatchSetup.PlayerPick, fallback);
            p2Pick = IndexOf(Locked ? lockedCpu : MatchSetup.CpuPick, NextPlayable(p1Pick, 1, p1Pick));
            focus = p1Pick;
            timeLeft = timeLimit;
            if (toastText) toastText.gameObject.SetActive(false);
            if (readyScroll) readyScroll.SetActive(true);
            if (waitingScroll) waitingScroll.SetActive(false);
            Refresh();
        }

        void Update()
        {
            float dt = Time.unscaledDeltaTime;
            var kb = Keyboard.current;
            if (!committed && kb != null)
            {
                if (kb.escapeKey.wasPressedThisFrame) Back();
                if (kb.leftArrowKey.wasPressedThisFrame) Turn(-1);
                if (kb.rightArrowKey.wasPressedThisFrame) Turn(1);
                if (kb.enterKey.wasPressedThisFrame) Ready();
            }

            if (!committed)
            {
                timeLeft = Mathf.Max(0f, timeLeft - dt);
                if (timeLeft <= 0f && autoReadyAtZero) Ready();
            }
            if (timerText)
            {
                string t = Mathf.CeilToInt(timeLeft).ToString();
                if (timerText.text != t) timerText.text = t;
            }

            if (centreCard.root && popT < 1f)
            {
                popT = Mathf.Min(1f, popT + dt / 0.18f);
                float k = 1f - (1f - popT) * (1f - popT);
                centreCard.root.localScale = Vector3.one * Mathf.Lerp(turnPop, 1f, k);
            }

            if (toastText && toastText.gameObject.activeSelf && Time.unscaledTime >= toastUntil) toastText.gameObject.SetActive(false);
            if (loadAt > 0f && Time.unscaledTime >= loadAt) { loadAt = -1f; SceneLoader.Load(fightScene); }
        }

        // ───────────── Picks ─────────────

        void Turn(int step)
        {
            if (committed || Count == 0) return;
            focus = Wrap(focus + step);
            popT = 0f;
            TryPickP1(focus, false);
        }

        /// <summary>The focused fighter becomes P1's pick if allowed; otherwise P1 stays and the reason shows.</summary>
        void TryPickP1(int i, bool tapped)
        {
            if (committed) return;
            var e = Get(i);
            if (e == null) return;
            if (!e.Playable) Toast($"{e.displayName}: coming soon");
            else if (Locked && e.archetype != lockedPlayer)
                Toast(e.archetype == lockedCpu ? $"Playtest: {e.displayName} is the CPU's fighter"
                                               : $"Playtest: P1 plays {NameOf(lockedPlayer)}");
            else
            {
                p1Pick = i;
                if (p2Pick == p1Pick && !Locked && CountPlayable() > 1) p2Pick = NextPlayable(p1Pick, 1, p1Pick);
            }
            Refresh();
        }

        void CycleCpu()
        {
            if (committed) return;
            if (Locked) { Toast($"Playtest: the CPU plays {NameOf(lockedCpu)}"); return; }
            p2Pick = NextPlayable(p2Pick, 1, -1);
            Refresh();
        }

        void Ready()
        {
            if (committed) return;
            var a = Get(p1Pick);
            var b = Get(p2Pick);
            if (a == null || !a.Playable || b == null || !b.Playable) { Toast("Pick a fighter first"); return; }
            committed = true;
            MatchSetup.PlayerPick = a.archetype;
            MatchSetup.CpuPick = b.archetype;
            if (readyScroll) readyScroll.SetActive(false);
            if (waitingScroll) waitingScroll.SetActive(true);
            foreach (var btn in new[] { leftArrow, rightArrow, leftCard.button, rightCard.button, centreCard.button, readyButton, p2CycleButton })
                if (btn) btn.interactable = false;
            loadAt = Time.unscaledTime + waitingTime;
            if (waitingTime <= 0f) { loadAt = -1f; SceneLoader.Load(fightScene); }
        }

        void Back()
        {
            if (committed) return;
            SceneLoader.Load(backScene);
        }

        // ───────────── View ─────────────

        void Refresh()
        {
            ShowCard(leftCard, Wrap(focus - 1), false);
            ShowCard(centreCard, focus, true);
            ShowCard(rightCard, Wrap(focus + 1), false);
            ShowSide(p1, Get(p1Pick));
            ShowSide(p2, Get(p2Pick));
            bool turn = Count > 1;
            if (leftArrow) leftArrow.gameObject.SetActive(turn);
            if (rightArrow) rightArrow.gameObject.SetActive(turn);
            bool sides = Count > 2;
            if (leftCard.root) leftCard.root.gameObject.SetActive(sides);
            if (rightCard.root) rightCard.root.gameObject.SetActive(sides);
        }

        void ShowCard(Card c, int i, bool centre)
        {
            var e = Get(i);
            if (c == null || e == null) return;
            if (c.art) { c.art.sprite = e.card; c.art.enabled = e.card; }
            if (c.label) c.label.text = e.classLabel;
            if (c.p1Marker) c.p1Marker.SetActive(i == p1Pick);
            if (c.p2Marker) c.p2Marker.SetActive(i == p2Pick);
            if (c.dim) { c.dim.sprite = e.card; c.dim.enabled = !centre && e.card; } // same shape as the card
        }

        static void ShowSide(Side s, Entry e)
        {
            if (s == null) return;
            if (s.illustration)
            {
                s.illustration.sprite = e?.illustration;
                s.illustration.enabled = e != null && e.illustration; // missing art = blank
            }
            if (s.classText) s.classText.text = e?.classLabel ?? "";
            if (s.nameText) s.nameText.text = e?.displayName ?? "";
            if (s.skillText) s.skillText.text = e?.skill ?? "";
        }

        void Toast(string text)
        {
            if (!toastText) return;
            toastText.text = text;
            toastText.gameObject.SetActive(true);
            toastUntil = Time.unscaledTime + 1.6f;
        }

        // ───────────── Helpers ─────────────

        Entry Get(int i) => i >= 0 && i < Count ? entries[i] : null;
        int Wrap(int i) => Count == 0 ? 0 : ((i % Count) + Count) % Count;

        string NameOf(FighterArchetype a)
        {
            foreach (var e in entries) if (e != null && e.archetype == a) return e.displayName;
            return a ? a.displayName : "?";
        }

        int IndexOf(FighterArchetype a, int fallback)
        {
            if (a) for (int i = 0; i < Count; i++) if (entries[i] != null && entries[i].archetype == a && entries[i].Playable) return i;
            return fallback;
        }

        int FirstPlayable()
        {
            for (int i = 0; i < Count; i++) if (entries[i] != null && entries[i].Playable) return i;
            return 0;
        }

        int NextPlayable(int from, int step, int avoid)
        {
            for (int k = 1; k <= Count; k++)
            {
                int i = Wrap(from + step * k);
                if (i != avoid && entries[i] != null && entries[i].Playable) return i;
            }
            return Mathf.Clamp(from, 0, Mathf.Max(0, Count - 1));
        }

        int CountPlayable()
        {
            int c = 0;
            foreach (var e in entries) if (e != null && e.Playable) c++;
            return c;
        }
    }
}
