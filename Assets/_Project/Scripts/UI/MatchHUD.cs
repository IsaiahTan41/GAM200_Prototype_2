using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using KungFuCrashout.AI;
using KungFuCrashout.Core;
using KungFuCrashout.Fighters;

namespace KungFuCrashout.UI
{
    /// <summary>
    /// GDD BUILD fight HUD, on top of Prototype 2's health bars (UI Standards §6–8):
    ///   • Timer (top-centre; tapping it opens the pause menu — it's the Options button now)
    ///   • Names + archetype under the outer end of each bar, two round pips (gold = round won) under the inner end
    ///   • Red DAMAGE bar behind each health bar: holds after the last hit, then drains, so a combo's damage reads at a glance
    ///   • Callouts: ROUND 1 / FIGHT! / K.O. / TIME, popping in at the centre
    ///   • Feedback words over the fighters: PARRY! · CAUGHT! · STAGGER! · GUARD BREAK! · BREAK OUT! · "n HITS"
    ///   • "!" over the CPU while an Easy-tier AI winds up (BehaviourTreeAI.IsTelegraphing)
    ///   • KO letterbox bars, and the first-match gesture hints (fade after a few seconds or the first touch)
    ///   • Match over: YOU WIN / YOU LOSE / DRAW, both sides' pips, this phone's record, REMATCH (primary, bottom-right),
    ///     CHANGE FIGHTER and START
    /// Everything is optional: an empty slot is simply skipped.
    ///
    /// Setup: KungFu Crashout → GDD Build → Upgrade Fight Scene builds the widgets on the HUD canvas and wires this.
    /// Place in: Assets/_Project/Scripts/UI/MatchHUD.cs
    /// </summary>
    public class MatchHUD : MonoBehaviour
    {
        [Serializable]
        public class Side
        {
            public Slider healthBar;
            [Tooltip("Red Image behind the health fill (a child of the slider's Fill Area).")]
            public Image damageFill;
            public TMP_Text nameText;
            public Image[] pips = new Image[2];
            [Tooltip("Floating word over this fighter (PARRY!, STAGGER!…).")]
            public TMP_Text popup;
            [Tooltip("\"n HITS\" while this fighter is comboing the other one.")]
            public TMP_Text comboText;
            [HideInInspector] public float damageShown = 1f;
            [HideInInspector] public float damageHold;
            [HideInInspector] public float popupUntil;
            [HideInInspector] public float popupStart;
            [HideInInspector] public int lastCombo;
            [HideInInspector] public float comboUntil;
        }

        [SerializeField] MatchManager match;
        [SerializeField] Side left = new Side();
        [SerializeField] Side right = new Side();

        [Header("Centre")]
        [SerializeField] TMP_Text timerText;
        [SerializeField] TMP_Text calloutText;
        [Tooltip("Optional banner behind the callout (shown and hidden with it). Empty = just the text.")]
        [SerializeField] GameObject calloutRoot;
        [Tooltip("Red \"!\" over the CPU while it telegraphs (Easy tier).")]
        [SerializeField] TMP_Text telegraphText;

        [Header("Knockout")]
        [SerializeField] RectTransform letterboxTop;
        [SerializeField] RectTransform letterboxBottom;
        [Tooltip("Letterbox bar height (reference units).")]
        [SerializeField] float letterboxHeight = 110f;

        [Header("First-match hints")]
        [SerializeField] CanvasGroup hints;
        [Tooltip("Show the hints at the start of every match (off = only the first match on this phone).")]
        [SerializeField] bool alwaysShowHints = false;
        [SerializeField, Min(0.5f)] float hintTime = 4f;

        [Header("Match over")]
        [SerializeField] GameObject matchOverPanel;
        [SerializeField] TMP_Text resultText;
        [SerializeField] TMP_Text recordText;
        [SerializeField] Image[] resultPipsLeft = new Image[2];
        [SerializeField] Image[] resultPipsRight = new Image[2];
        [SerializeField] Button rematchButton;
        [SerializeField] Button changeFighterButton;
        [SerializeField] Button startButton;

        [Header("Feel")]
        [Tooltip("Seconds the red damage bar waits after a hit before draining.")]
        [SerializeField] float damageHoldTime = 0.7f;
        [Tooltip("Damage bar drain speed (full bars per second).")]
        [SerializeField] float damageDrainSpeed = 0.7f;
        [SerializeField] float calloutPop = 1.35f;
        [SerializeField] float popupTime = 0.7f;
        [SerializeField] Color pipOn = new Color(1f, 0.78f, 0.2f);   // gold = round won
        [SerializeField] Color pipOff = new Color(1f, 1f, 1f, 0.22f);
        [SerializeField] Color timerLowColour = new Color(1f, 0.35f, 0.3f);

        const string HintsSeenKey = "kfc.hints.seen";

        GameObject CalloutGO => calloutRoot ? calloutRoot : calloutText.gameObject;

        Fighter leftFighter, rightFighter;
        BehaviourTreeAI cpuBrain;
        float calloutStart = -1f, calloutUntil = -1f;
        float letterbox;           // 0 hidden → 1 shown
        bool letterboxOn;
        float hintsUntil = -1f;
        Camera cam;
        readonly List<Action> unsubscribe = new List<Action>();

        void Awake()
        {
            if (!match) match = FindFirstObjectByType<MatchManager>();
            leftFighter = match ? match.Player : null;
            rightFighter = match ? match.Opponent : null;
            cpuBrain = rightFighter ? rightFighter.GetComponent<BehaviourTreeAI>() : null;

            if (rematchButton) rematchButton.onClick.AddListener(() => match.Rematch());
            if (changeFighterButton) changeFighterButton.onClick.AddListener(() => match.ChangeFighter());
            if (startButton) startButton.onClick.AddListener(() => match.QuitToStart());
        }

        void OnEnable()
        {
            if (!match) return;
            match.Callout += ShowCallout;
            match.RoundStarting += OnRoundStarting;
            match.RoundEnded += OnRoundEnded;
            match.RoundEndedBy += OnRoundEndedBy;
            match.MatchEnded += OnMatchEnded;
            Hook(left, leftFighter);
            Hook(right, rightFighter);
        }

        void OnDisable()
        {
            if (!match) return;
            match.Callout -= ShowCallout;
            match.RoundStarting -= OnRoundStarting;
            match.RoundEnded -= OnRoundEnded;
            match.RoundEndedBy -= OnRoundEndedBy;
            match.MatchEnded -= OnMatchEnded;
            foreach (var u in unsubscribe) u();
            unsubscribe.Clear();
        }

        void Start()
        {
            cam = Camera.main;
            SetName(left, leftFighter);
            SetName(right, rightFighter);
            if (calloutText) CalloutGO.SetActive(false);
            if (telegraphText) telegraphText.gameObject.SetActive(false);
            if (matchOverPanel) matchOverPanel.SetActive(false);
            foreach (var s in new[] { left, right })
            {
                if (s.popup) s.popup.gameObject.SetActive(false);
                if (s.comboText) s.comboText.gameObject.SetActive(false);
            }
            SetLetterbox(0f);
            RefreshPips();

            bool showHints = hints && (alwaysShowHints || PlayerPrefs.GetInt(HintsSeenKey, 0) == 0);
            if (hints) hints.alpha = showHints ? 1f : 0f;
            if (showHints) { hintsUntil = Time.unscaledTime + hintTime; PlayerPrefs.SetInt(HintsSeenKey, 1); }
        }

        void Update()
        {
            float dt = Time.unscaledDeltaTime;
            UpdateSide(left, leftFighter, dt);
            UpdateSide(right, rightFighter, dt);
            UpdateTimer();
            UpdateCallout();
            UpdateTelegraph();
            UpdateLetterbox(dt);
            UpdateHints(dt);
        }

        // ───────────── Bars, names, combo counter, popups ─────────────

        void Hook(Side s, Fighter f)
        {
            if (!f) return;
            Action<Fighter, AttackData> parried = (a, atk) => Popup(s, atk is ThrowableData ? "CATCH!" : "PARRY!", new Color(1f, 0.92f, 0.3f));
            Action guardBroken = () => Popup(s, "GUARD BREAK!", new Color(1f, 0.55f, 0.15f));
            Action broke = () => Popup(s, "BREAK OUT!", new Color(0.6f, 0.9f, 1f));
            Action<Fighter, ThrowableData, HitResult, Vector2> byItem = (t, d, r, dir) =>
            {
                if (r == HitResult.Hit && f.IsLaunched) Popup(s, "STAGGER!", new Color(1f, 0.72f, 0f));
            };
            Action<float> damaged = amount => s.damageHold = damageHoldTime;

            f.Parried += parried;
            f.GuardBroken += guardBroken;
            f.ComboBroken += broke;
            f.HitByItem += byItem;
            f.Health.Damaged += damaged;
            unsubscribe.Add(() =>
            {
                f.Parried -= parried;
                f.GuardBroken -= guardBroken;
                f.ComboBroken -= broke;
                f.HitByItem -= byItem;
                f.Health.Damaged -= damaged;
            });
        }

        void SetName(Side s, Fighter f)
        {
            if (!s.nameText || !f) return;
            var loadout = f.GetComponent<ArchetypeLoadout>();
            string label = loadout && loadout.Applied ? loadout.Applied.archetypeLabel : "";
            s.nameText.text = string.IsNullOrEmpty(label) ? f.DisplayName.ToUpperInvariant()
                                                          : $"{f.DisplayName.ToUpperInvariant()}  <size=70%><alpha=#99>{label}";
        }

        void UpdateSide(Side s, Fighter f, float dt)
        {
            if (!f) return;
            float hp = f.Health.Normalized;

            // Red damage bar: holds, then drains toward the health bar.
            if (s.damageShown < hp) s.damageShown = hp;                 // healed / new round
            else if (s.damageHold > 0f) s.damageHold -= dt;
            else s.damageShown = Mathf.MoveTowards(s.damageShown, hp, damageDrainSpeed * dt);
            if (s.damageFill && s.healthBar && s.healthBar.fillRect)
            {
                var fill = s.healthBar.fillRect;
                var dmg = s.damageFill.rectTransform;
                bool rtl = s.healthBar.direction == Slider.Direction.RightToLeft;
                dmg.anchorMin = new Vector2(rtl ? 1f - s.damageShown : 0f, fill.anchorMin.y);
                dmg.anchorMax = new Vector2(rtl ? 1f : s.damageShown, fill.anchorMax.y);
                dmg.offsetMin = fill.offsetMin;
                dmg.offsetMax = fill.offsetMax;
            }

            // Combo counter on the ATTACKER's side: hits this fighter has landed in a row on the other one.
            if (s.comboText)
            {
                int n = f.Opponent ? f.Opponent.ComboHitsTaken : 0;
                if (n >= 2 && n != s.lastCombo)
                {
                    s.comboText.text = $"{n} HITS";
                    s.comboText.gameObject.SetActive(true);
                    s.comboText.transform.localScale = Vector3.one * 1.3f;
                    s.comboUntil = Time.unscaledTime + 1.1f;
                }
                s.lastCombo = n;
                if (s.comboText.gameObject.activeSelf)
                {
                    s.comboText.transform.localScale = Vector3.Lerp(s.comboText.transform.localScale, Vector3.one, 12f * dt);
                    if (Time.unscaledTime >= s.comboUntil) s.comboText.gameObject.SetActive(false);
                }
            }

            // Floating word over the fighter.
            if (s.popup && s.popup.gameObject.activeSelf)
            {
                float t = (Time.unscaledTime - s.popupStart) / Mathf.Max(0.01f, popupTime);
                if (t >= 1f) s.popup.gameObject.SetActive(false);
                else
                {
                    FollowFighter(s.popup.rectTransform, f, 2.3f + t * 0.5f);
                    s.popup.alpha = t < 0.7f ? 1f : 1f - (t - 0.7f) / 0.3f;
                    s.popup.transform.localScale = Vector3.one * Mathf.Lerp(1.4f, 1f, Mathf.Clamp01(t * 5f));
                }
            }
        }

        void Popup(Side s, string word, Color colour)
        {
            if (!s.popup) return;
            s.popup.text = word;
            s.popup.color = colour;
            s.popup.alpha = 1f;
            s.popupStart = Time.unscaledTime;
            s.popup.gameObject.SetActive(true);
        }

        void FollowFighter(RectTransform rt, Fighter f, float heightAbove)
        {
            if (!cam) cam = Camera.main;
            if (!cam) return;
            Vector3 p = cam.WorldToScreenPoint(f.transform.position + Vector3.up * heightAbove);
            rt.position = new Vector3(p.x, p.y, 0f);
        }

        // ───────────── Timer, callouts, telegraph ─────────────

        void UpdateTimer()
        {
            if (!timerText || !match) return;
            int secs = Mathf.CeilToInt(match.TimeLeft);
            timerText.text = secs.ToString();
            bool low = match.Phase == MatchPhase.Fighting && secs <= 10;
            timerText.color = low && Mathf.Repeat(Time.unscaledTime * 2f, 1f) < 0.5f ? timerLowColour : Color.white;
        }

        void ShowCallout(string text, float seconds)
        {
            if (!calloutText) return;
            calloutText.text = text;
            CalloutGO.SetActive(true);
            calloutStart = Time.unscaledTime;
            calloutUntil = seconds > 0f ? Time.unscaledTime + seconds : -1f; // 0 = stay up (match over)
        }

        void UpdateCallout()
        {
            if (!calloutText || !CalloutGO.activeSelf) return;
            float t = Mathf.Clamp01((Time.unscaledTime - calloutStart) / 0.15f);
            calloutText.transform.localScale = Vector3.one * Mathf.Lerp(calloutPop, 1f, 1f - (1f - t) * (1f - t));
            if (calloutUntil > 0f && Time.unscaledTime >= calloutUntil) CalloutGO.SetActive(false);
        }

        void UpdateTelegraph()
        {
            if (!telegraphText) return;
            bool on = cpuBrain && cpuBrain.isActiveAndEnabled && cpuBrain.IsTelegraphing && rightFighter;
            if (telegraphText.gameObject.activeSelf != on) telegraphText.gameObject.SetActive(on);
            if (!on) return;
            FollowFighter(telegraphText.rectTransform, rightFighter, 2.4f);
            telegraphText.transform.localScale = Vector3.one * (1f + 0.15f * Mathf.Sin(Time.unscaledTime * 20f));
        }

        // ───────────── Round events ─────────────

        void OnRoundStarting(int round)
        {
            letterboxOn = false;
            if (matchOverPanel) matchOverPanel.SetActive(false);
            left.damageShown = right.damageShown = 1f;
            RefreshPips();
        }

        void OnRoundEnded(Fighter winner) => RefreshPips();

        void OnRoundEndedBy(Fighter winner, bool knockout)
        {
            if (knockout) letterboxOn = true;
        }

        void OnMatchEnded(Fighter winner)
        {
            RefreshPips();
            if (calloutText) CalloutGO.SetActive(false);
            if (resultText)
            {
                resultText.text = winner == leftFighter ? "YOU WIN" : winner == rightFighter ? "YOU LOSE" : "DRAW";
                resultText.color = winner == leftFighter ? pipOn : winner == rightFighter ? new Color(0.9f, 0.3f, 0.25f) : Color.white;
            }
            if (recordText) recordText.text = PlayerStats.Summary();
            SetPips(resultPipsLeft, match.PlayerWins);
            SetPips(resultPipsRight, match.OpponentWins);
            if (matchOverPanel) matchOverPanel.SetActive(true);
        }

        void RefreshPips()
        {
            if (!match) return;
            SetPips(left.pips, match.PlayerWins);
            SetPips(right.pips, match.OpponentWins);
        }

        void SetPips(Image[] pips, int wins)
        {
            if (pips == null) return;
            for (int i = 0; i < pips.Length; i++)
                if (pips[i]) pips[i].color = i < wins ? pipOn : pipOff;
        }

        // ───────────── Letterbox, hints ─────────────

        void UpdateLetterbox(float dt)
        {
            float target = letterboxOn ? 1f : 0f;
            if (Mathf.Approximately(letterbox, target)) return;
            letterbox = Mathf.MoveTowards(letterbox, target, dt / 0.25f);
            SetLetterbox(letterbox);
        }

        void SetLetterbox(float t)
        {
            float h = letterboxHeight * (1f - (1f - t) * (1f - t));
            if (letterboxTop) letterboxTop.sizeDelta = new Vector2(letterboxTop.sizeDelta.x, h);
            if (letterboxBottom) letterboxBottom.sizeDelta = new Vector2(letterboxBottom.sizeDelta.x, h);
        }

        void UpdateHints(float dt)
        {
            if (!hints || hints.alpha <= 0f) return;
            bool touched = UnityEngine.InputSystem.Touchscreen.current != null &&
                           UnityEngine.InputSystem.Touchscreen.current.primaryTouch.press.isPressed &&
                           match && match.Phase == MatchPhase.Fighting;
            if (Time.unscaledTime >= hintsUntil || touched) hints.alpha = Mathf.MoveTowards(hints.alpha, 0f, dt / 0.5f);
        }
    }
}
