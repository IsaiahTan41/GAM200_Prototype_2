using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using KungFuCrashout.Core;
using KungFuCrashout.Fighters;
using KungFuCrashout.Props;

namespace KungFuCrashout.UI
{
    /// <summary>
    /// The fight HUD in the Concept 27 style (GamConcepts_UI_27 + fightHUD_info):
    ///   • Portrait in the profile ring (empty until portraits are supplied), name on the frame's name plate (Tillana Bold 25)
    ///   • Slanted health bars (SlantedBar): green / rose health with the two-colour gradient from the edge, the red
    ///     recoverable part, and the trailing damage bar that holds then drains. Full end by the timer.
    ///     Staggered (downed after an item hit) flashes amber, KO goes grey, get-up invincibility fades it.
    ///   • Timer in the diamond frame (Tillana Bold 70): tapping it opens the pause menu (forwards to the old pause button)
    ///   • Round coins (lit = round won), the outer coin first
    ///   • Combo text: "Light combo 3" (Tillana Bold 40 + 100, brown / orange outline and 3D depth via HudTextDepth)
    ///   • Status banners down each side (Barlow SemiBold 40, P1 cream, CPU orange): what the fighter is in now
    ///     (BLOCK, BLOCKSTUN, HITSTUN, DASH…) on the first banner, moments under it (PARRY, GUARD BREAK, 3 HITS…)
    ///   • ROUND 1 / FIGHT! / K.O. callouts (Aladin 160, brown outline + depth)
    /// Built by Window → KungFu Crashout → GDD Build → Apply Fight HUD Skin (Concept 27). Everything is optional.
    /// MatchHUD keeps running for the match-over panel, letterbox, hints and the CPU "!".
    /// Place in: Assets/_Project/Scripts/UI/FightHudConcept.cs
    /// </summary>
    public class FightHudConcept : MonoBehaviour
    {
        [Serializable]
        public class Side
        {
            [Header("Bar")]
            public SlantedBar health;
            public SlantedBar recoverable;
            public SlantedBar trailing;
            [Tooltip("Health gradient: at the full (timer) end, and at the moving edge.")]
            public Color healthAnchor = Color.white, healthLead = Color.white;
            [Tooltip("Red recoverable / damage gradient: next to the health, and at its outer edge.")]
            public Color redAnchor = Color.white, redLead = Color.white;

            [Header("Profile")]
            public Image portrait;
            public TMP_Text nameText;
            [Tooltip("Outer coin first (round 1).")]
            public Image[] coins = new Image[2];

            [Header("Combo text")]
            public RectTransform comboRoot;
            public TMP_Text comboWord;
            public TMP_Text comboNumber;

            [Header("Status banners (top first)")]
            public RectTransform[] banners = new RectTransform[3];

            // runtime
            [NonSerialized] public Fighter fighter;
            [NonSerialized] public ItemHolder holder;
            [NonSerialized] public float shown = 1f, shownGreen = 1f, damageShown = 1f, damageHold;
            [NonSerialized] public string comboLabel = ""; [NonSerialized] public int comboNum;
            [NonSerialized] public float comboUntil, comboPop;
            [NonSerialized] public string state = ""; [NonSerialized] public float stateHoldUntil;
            [NonSerialized] public readonly List<Moment> moments = new List<Moment>();
            [NonSerialized] public string[] bannerShown;
            [NonSerialized] public float[] bannerStart;
            [NonSerialized] public TMP_Text[] bannerText;
            [NonSerialized] public int lastHits;
        }

        public class Moment { public string key, text; public float until; }

        [SerializeField] MatchManager match;
        [SerializeField] Side left = new Side();
        [SerializeField] Side right = new Side();

        [Header("Timer")]
        [SerializeField] TMP_Text timerText;
        [SerializeField] Button timerButton;
        [Tooltip("The old Timer (Pause) button: tapping the new timer clicks it (opens the pause menu).")]
        [SerializeField] Button pauseButton;
        [SerializeField] Color timerColour = new Color32(0xF6, 0xEE, 0xDA, 0xFF);
        [SerializeField] Color timerLowColour = new Color(1f, 0.42f, 0.33f);

        [Header("Coins")]
        [SerializeField] Sprite coinLit;
        [SerializeField] Sprite coinUnlit;

        [Header("Callout (ROUND 1 / FIGHT! / K.O.)")]
        [SerializeField] TMP_Text calloutText;
        [SerializeField] float calloutPop = 1.35f;

        [Header("Health bars")]
        [Tooltip("How fast the bars slide to the new health (fraction of the bar per second). 0 = jump.")]
        [SerializeField, Min(0f)] float slideSpeed = 2.5f;
        [SerializeField, Min(0f)] float damageHoldTime = 0.7f;
        [SerializeField, Min(0f)] float damageDrainSpeed = 0.7f;
        [Tooltip("Trailing damage bar while red recoverable health is on (the red part is the recoverable health then).")]
        [SerializeField] Color trailingWithRed = new Color(1f, 1f, 1f, 0.6f);
        [SerializeField] Color staggeredColour = new Color(1f, 0.72f, 0f);
        [SerializeField] Color staggeredFlashColour = Color.white;
        [SerializeField, Min(0f)] float staggeredFlashRate = 8f;
        [SerializeField] Color koColour = new Color(0.42f, 0.42f, 0.42f);
        [SerializeField, Range(0f, 1f)] float invincibleAlpha = 0.5f;

        [Header("Combo text")]
        [SerializeField, Min(0f)] float comboHoldTime = 0.8f;
        [SerializeField] float comboGap = 22f;
        [SerializeField] float comboNumberPop = 1.35f;

        [Header("Status banners")]
        [SerializeField] bool showStatus = true;
        [Tooltip("Short states stay up at least this long so they can be read.")]
        [SerializeField, Min(0f)] float stateHoldTime = 0.35f;
        [Tooltip("How long a moment (PARRY, GUARD BREAK, 3 HITS…) stays up.")]
        [SerializeField, Min(0.1f)] float momentTime = 0.9f;
        [SerializeField, Min(0.01f)] float bannerSlideTime = 0.12f;
        [SerializeField] float bannerSlideDistance = 60f;
        [Tooltip("Hits in a row before the attacker's side shows \"n HITS\".")]
        [SerializeField, Min(2)] int hitsToShow = 2;

        readonly List<Action> unsubscribe = new List<Action>();
        float calloutStart = -1f, calloutUntil = -1f;

        void Awake()
        {
            if (!match) match = FindFirstObjectByType<MatchManager>();
            left.fighter = match ? match.Player : null;
            right.fighter = match ? match.Opponent : null;
            if (timerButton) timerButton.onClick.AddListener(() => { if (pauseButton) pauseButton.onClick.Invoke(); });
            Prepare(left);
            Prepare(right);
        }

        void Prepare(Side s)
        {
            int n = s.banners != null ? s.banners.Length : 0;
            s.bannerShown = new string[n];
            s.bannerStart = new float[n];
            s.bannerText = new TMP_Text[n];
            for (int i = 0; i < n; i++)
            {
                if (!s.banners[i]) continue;
                s.bannerText[i] = s.banners[i].GetComponentInChildren<TMP_Text>(true);
                s.banners[i].gameObject.SetActive(false);
                s.bannerShown[i] = "";
            }
            if (s.comboRoot) s.comboRoot.gameObject.SetActive(false);
            if (s.portrait) s.portrait.enabled = s.portrait.sprite;
            if (s.fighter) s.holder = s.fighter.GetComponent<ItemHolder>();
        }

        void OnEnable()
        {
            if (!match) return;
            match.Callout += ShowCallout;
            match.RoundStarting += OnRoundStarting;
            match.RoundEnded += OnRoundEnded;
            match.MatchEnded += OnMatchEnded;
            Hook(left);
            Hook(right);
            RefreshCoins();
        }

        void OnDisable()
        {
            if (match)
            {
                match.Callout -= ShowCallout;
                match.RoundStarting -= OnRoundStarting;
                match.RoundEnded -= OnRoundEnded;
                match.MatchEnded -= OnMatchEnded;
            }
            foreach (var u in unsubscribe) u();
            unsubscribe.Clear();
        }

        void Start()
        {
            SetName(left);
            SetName(right);
            if (calloutText) calloutText.gameObject.SetActive(false);
            RefreshCoins();
        }

        void Hook(Side s)
        {
            var f = s.fighter;
            if (!f) return;
            var health = f.GetComponent<Health>();
            Action<float> damaged = _ => s.damageHold = damageHoldTime;
            Action<Fighter, AttackData> parried = (a, atk) => AddMoment(s, "parry", atk is ThrowableData ? "CATCH" : "PARRY");
            Action guardBroken = () => AddMoment(s, "guard", "GUARD BREAK");
            Action broke = () => AddMoment(s, "break", "BREAK OUT");
            Action<Fighter, ThrowableData, HitResult, Vector2> byItem = (t, d, r, dir) =>
            {
                if (r == HitResult.Hit && f.IsLaunched) AddMoment(s, "stagger", "STAGGER");
            };
            if (health) health.Damaged += damaged;
            f.Parried += parried;
            f.GuardBroken += guardBroken;
            f.ComboBroken += broke;
            f.HitByItem += byItem;
            unsubscribe.Add(() =>
            {
                if (health) health.Damaged -= damaged;
                if (!f) return;
                f.Parried -= parried;
                f.GuardBroken -= guardBroken;
                f.ComboBroken -= broke;
                f.HitByItem -= byItem;
            });
        }

        void Update()
        {
            float dt = Time.unscaledDeltaTime;
            UpdateBar(left, dt);
            UpdateBar(right, dt);
            UpdateCombo(left, true, dt);
            UpdateCombo(right, false, dt);
            UpdateStatus(left, true);
            UpdateStatus(right, false);
            UpdateTimer();
            UpdateCallout();
        }

        // ───────────── Names, portraits, coins ─────────────

        void SetName(Side s)
        {
            if (s.nameText && s.fighter) s.nameText.text = s.fighter.DisplayName;
        }

        /// <summary>Show a portrait in a side's ring (null = empty ring). For when portraits are supplied.</summary>
        public void SetPortrait(bool playerSide, Sprite sprite)
        {
            var s = playerSide ? left : right;
            if (!s.portrait) return;
            s.portrait.sprite = sprite;
            s.portrait.enabled = sprite;
        }

        void RefreshCoins()
        {
            if (!match) return;
            SetCoins(left.coins, match.PlayerWins);
            SetCoins(right.coins, match.OpponentWins);
        }

        void SetCoins(Image[] coins, int wins)
        {
            if (coins == null) return;
            for (int i = 0; i < coins.Length; i++)
                if (coins[i]) coins[i].sprite = i < wins ? coinLit : coinUnlit;
        }

        // ───────────── Health bar ─────────────

        void UpdateBar(Side s, float dt)
        {
            var f = s.fighter;
            if (!f || !f.Health || !s.health) return;
            var h = f.Health;
            bool redMode = f.RecoverableNeutralDamage;
            float hp = h.Normalized;
            float green = redMode ? Mathf.Min(h.GreenNormalized, hp) : hp;

            float step = slideSpeed > 0f ? slideSpeed * dt : 1f;
            s.shown = hp > s.shown ? hp : Mathf.MoveTowards(s.shown, hp, step);       // heals jump, damage slides
            s.shownGreen = green > s.shownGreen ? green : Mathf.MoveTowards(s.shownGreen, green, step);
            s.shownGreen = Mathf.Min(s.shownGreen, s.shown);

            if (s.damageShown < s.shown) s.damageShown = s.shown;
            else if (s.damageHold > 0f) s.damageHold -= dt;
            else s.damageShown = Mathf.MoveTowards(s.damageShown, s.shown, damageDrainSpeed * dt);

            // Colours by state (as FighterHealthBar): staggered flash, launched amber, KO grey, invincible faded.
            Color a = s.healthAnchor, l = s.healthLead;
            if (f.State == FighterState.KO) { a = koColour; l = koColour; }
            else if (f.State == FighterState.Downed)
            {
                bool flash = Mathf.Repeat(Time.unscaledTime * staggeredFlashRate, 1f) < 0.5f;
                a = l = flash ? staggeredFlashColour : staggeredColour;
            }
            else if (f.IsLaunched) { a = l = staggeredColour; }
            if (f.IsInvulnerable && f.State != FighterState.KO) { a.a *= invincibleAlpha; l.a *= invincibleAlpha; }

            s.health.SetColours(a, l);
            s.health.SetRange(0f, s.shownGreen);

            if (s.recoverable)
            {
                s.recoverable.SetColours(s.redAnchor, s.redLead);
                s.recoverable.SetRange(s.shownGreen, redMode ? s.shown : s.shownGreen);
            }
            if (s.trailing)
            {
                if (redMode) s.trailing.SetColours(trailingWithRed, trailingWithRed);
                else s.trailing.SetColours(s.redAnchor, s.redLead);
                s.trailing.SetRange(s.shown, s.damageShown);
            }
        }

        // ───────────── Combo text ─────────────

        void UpdateCombo(Side s, bool leftSide, float dt)
        {
            if (!s.comboRoot || !s.fighter) return;
            var f = s.fighter;
            float now = Time.unscaledTime;

            if (f.State == FighterState.Attacking && !f.IsThrowing)
            {
                string label; int num = 0;
                var attack = f.CurrentAttack;
                if (f.IsAirAttacking) { label = "Air combo"; num = f.AirComboStep; }
                else if (f.IsDoingSpecial)
                    label = attack && attack == f.SpecialFor(SpecialMove.Side) ? "Side special"
                          : attack && attack == f.SpecialFor(SpecialMove.Up) ? "Up special"
                          : attack && attack == f.SpecialFor(SpecialMove.Down) ? "Down special" : "Special";
                else if (f.ComboStep >= 1) { label = "Light combo"; num = f.ComboStep; }
                else if (attack && attack == f.HeavyAttackData) label = "Heavy attack";
                else label = "Attack";

                s.comboUntil = now + comboHoldTime;
                if (label != s.comboLabel || num != s.comboNum || !s.comboRoot.gameObject.activeSelf)
                {
                    s.comboLabel = label;
                    s.comboNum = num;
                    s.comboPop = 1f;
                    LayoutCombo(s, leftSide);
                }
            }

            bool show = now < s.comboUntil;
            if (s.comboRoot.gameObject.activeSelf != show) s.comboRoot.gameObject.SetActive(show);
            if (show && s.comboNumber)
            {
                s.comboPop = Mathf.MoveTowards(s.comboPop, 0f, dt / 0.15f);
                float k = 1f - (1f - s.comboPop) * (1f - s.comboPop);
                s.comboNumber.transform.localScale = Vector3.one * Mathf.Lerp(1f, comboNumberPop, k);
            }
        }

        /// <summary>"Light combo 3": the word, then the number after it, reading left to right on both sides.</summary>
        void LayoutCombo(Side s, bool leftSide)
        {
            if (s.comboWord) s.comboWord.text = s.comboLabel;
            bool hasNum = s.comboNum > 0;
            if (s.comboNumber)
            {
                s.comboNumber.gameObject.SetActive(hasNum);
                if (hasNum) s.comboNumber.text = s.comboNum.ToString();
            }
            if (!s.comboWord || !s.comboNumber) return;

            float wordW = s.comboWord.GetPreferredValues(s.comboLabel).x;
            float numW = hasNum ? s.comboNumber.GetPreferredValues(s.comboNumber.text).x : 0f;
            var w = s.comboWord.rectTransform;
            var n = s.comboNumber.rectTransform;
            if (leftSide)
            {
                w.anchoredPosition = new Vector2(0f, w.anchoredPosition.y);
                n.anchoredPosition = new Vector2(wordW + comboGap, n.anchoredPosition.y);
            }
            else
            {
                n.anchoredPosition = new Vector2(0f, n.anchoredPosition.y);
                w.anchoredPosition = new Vector2(hasNum ? -(numW + comboGap) : 0f, w.anchoredPosition.y);
            }
        }

        // ───────────── Status banners ─────────────

        void AddMoment(Side s, string key, string text)
        {
            float until = Time.unscaledTime + momentTime;
            foreach (var m in s.moments)
                if (m.key == key) { m.text = text; m.until = until; return; }
            s.moments.Insert(0, new Moment { key = key, text = text, until = until });
        }

        /// <summary>What the fighter is in right now, for the first banner ("" = nothing to show).</summary>
        string StateName(Side s)
        {
            var f = s.fighter;
            switch (f.State)
            {
                case FighterState.KO: return "K.O.";
                case FighterState.Downed: return "STAGGERED";
                case FighterState.Hitstun:
                    return f.IsGuardBroken ? "GUARD BROKEN" : f.IsParryStunned ? "PARRIED" : f.IsLaunched ? "LAUNCHED" : "HITSTUN";
                case FighterState.Blocking:
                    return f.IsParryActive ? "PARRY WINDOW" : f.InBlockstun ? "BLOCKSTUN" : "BLOCK";
                case FighterState.Dashing: return "DASH";
                case FighterState.Airborne: return f.IsLaunched ? "LAUNCHED" : "";
                case FighterState.Attacking: return f.IsThrowing ? "ITEM THROW" : ""; // attacks are in the combo text
            }
            if (s.holder && s.holder.IsCarrying) return "HOLDING ITEM";
            return "";
        }

        void UpdateStatus(Side s, bool leftSide)
        {
            if (s.banners == null || s.banners.Length == 0 || !s.fighter) return;
            float now = Time.unscaledTime;

            if (!showStatus)
            {
                for (int i = 0; i < s.banners.Length; i++) if (s.banners[i] && s.banners[i].gameObject.activeSelf) s.banners[i].gameObject.SetActive(false);
                return;
            }

            // Hits in a row on the attacker's side.
            int hits = s.fighter.Opponent ? s.fighter.Opponent.ComboHitsTaken : 0;
            if (hits >= hitsToShow && hits != s.lastHits) AddMoment(s, "hits", $"{hits} HITS");
            s.lastHits = hits;

            string current = StateName(s);
            if (current.Length > 0) { s.state = current; s.stateHoldUntil = now + stateHoldTime; }
            else if (now >= s.stateHoldUntil) s.state = "";

            s.moments.RemoveAll(m => now >= m.until);

            int slot = 0;
            if (s.state.Length > 0) Show(s, slot++, s.state, leftSide, now);
            foreach (var m in s.moments)
            {
                if (slot >= s.banners.Length) break;
                Show(s, slot++, m.text, leftSide, now);
            }
            for (; slot < s.banners.Length; slot++)
            {
                if (s.banners[slot] && s.banners[slot].gameObject.activeSelf) s.banners[slot].gameObject.SetActive(false);
                s.bannerShown[slot] = "";
            }
        }

        void Show(Side s, int i, string text, bool leftSide, float now)
        {
            var b = s.banners[i];
            if (!b) return;
            if (!b.gameObject.activeSelf) { b.gameObject.SetActive(true); s.bannerShown[i] = ""; }
            if (s.bannerShown[i] != text)
            {
                if (s.bannerText[i]) s.bannerText[i].text = text;
                if (s.bannerShown[i].Length == 0) s.bannerStart[i] = now; // slides in when it appears, not on every change
                s.bannerShown[i] = text;
            }
            // Slide in from the screen edge.
            float t = Mathf.Clamp01((now - s.bannerStart[i]) / bannerSlideTime);
            float ease = 1f - (1f - t) * (1f - t);
            float x = (1f - ease) * bannerSlideDistance * (leftSide ? -1f : 1f);
            b.anchoredPosition = new Vector2(x, b.anchoredPosition.y);
        }

        // ───────────── Timer, callouts, rounds ─────────────

        void UpdateTimer()
        {
            if (!timerText || !match) return;
            int secs = Mathf.CeilToInt(match.TimeLeft);
            string txt = secs.ToString();
            if (timerText.text != txt) timerText.text = txt;
            bool low = match.Phase == MatchPhase.Fighting && secs <= 10;
            timerText.color = low && Mathf.Repeat(Time.unscaledTime * 2f, 1f) < 0.5f ? timerLowColour : timerColour;
        }

        void ShowCallout(string text, float seconds)
        {
            if (!calloutText) return;
            calloutText.text = text;
            calloutText.gameObject.SetActive(true);
            calloutStart = Time.unscaledTime;
            calloutUntil = seconds > 0f ? Time.unscaledTime + seconds : -1f;
        }

        void UpdateCallout()
        {
            if (!calloutText || !calloutText.gameObject.activeSelf) return;
            float t = Mathf.Clamp01((Time.unscaledTime - calloutStart) / 0.15f);
            calloutText.transform.localScale = Vector3.one * Mathf.Lerp(calloutPop, 1f, 1f - (1f - t) * (1f - t));
            if (calloutUntil > 0f && Time.unscaledTime >= calloutUntil) calloutText.gameObject.SetActive(false);
        }

        void OnRoundStarting(int round)
        {
            left.damageShown = right.damageShown = 1f;
            left.shown = left.shownGreen = right.shown = right.shownGreen = 1f;
            left.moments.Clear();
            right.moments.Clear();
            RefreshCoins();
        }

        void OnRoundEnded(Fighter winner) => RefreshCoins();

        void OnMatchEnded(Fighter winner)
        {
            RefreshCoins();
            if (calloutText) calloutText.gameObject.SetActive(false);
        }
    }
}
