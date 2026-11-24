using System;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using KungFuCrashout.Core;
using KungFuCrashout.Sound;

namespace KungFuCrashout.UI
{
    /// <summary>
    /// Level select ("Choose your level", GamConcepts_UI_20 + Level Select UI.txt), between the Roster and the fight:
    ///   • The scroll shows the viewed stage's illustration and its name (bottom-right). Tapping a stage card, or the
    ///     arrows, switches the view; the viewed stage is P1's pick.
    ///   • Cards: the picked one switches to its Selected art with the yellow glow behind it, drawn above the others.
    ///     Small P1 / P2 markers (the Roster's, rotated) sit on the cards each side picked; P2 = the CPU's stage (Cpu Stage).
    ///     The same markers show on the scroll when the stage on display is someone's pick.
    ///   • READY: only a playable stage (the Restaurant, for now) goes on — "Waiting for opponent" (wave) for Waiting Time,
    ///     then the fight. Other stages: "coming soon". Back lantern / Android Back → the Roster.
    /// Missing art stays blank (an empty illustration shows the scroll's dark backing).
    /// Built by Window → KungFu Crashout → GDD Build → Build Level Select (Concept 20), in the LevelSelect scene.
    /// Place in: Assets/_Project/Scripts/UI/LevelSelectMenu.cs
    /// </summary>
    public class LevelSelectMenu : MonoBehaviour
    {
        [Serializable]
        public class Stage
        {
            [Tooltip("Shown bottom-right on the scroll (Tillana Bold 30).")]
            public string displayName = "Fancy Chinese Restaurant";
            [Tooltip("MatchSetup.Stage value when this stage is played.")]
            public string id = "Restaurant";
            [Tooltip("Only playable stages go to the fight (the Restaurant for now).")]
            public bool playable;
            public Sprite cardSelected;
            public Sprite cardUnselected;
            [Tooltip("The scroll illustration (2424 x 1080 placeholders). Empty = blank.")]
            public Sprite display;
            [Tooltip("Which part of the illustration the scroll's window shows: 0 = its bottom, 1 = its top.")]
            [Range(0f, 1f)] public float displayFocus = 0.45f;
            [Tooltip("Extra zoom on top of filling the window (1 = just fills it).")]
            [Min(1f)] public float displayZoom = 1f;
        }

        [Serializable]
        public class Card
        {
            public RectTransform root;
            public Image art;
            public Image glow;
            public RectTransform p1Marker;
            public RectTransform p2Marker;
            public Button button;
        }

        [SerializeField] Stage[] stages = new Stage[0];
        [SerializeField] Card[] cards = new Card[0];

        [Header("Scroll")]
        [Tooltip("The window inside the scroll frame (a RectMask2D).")]
        [SerializeField] RectTransform window;
        [SerializeField] Image display;
        [SerializeField] TMP_Text locationText;
        [SerializeField] GameObject scrollP1Marker;
        [SerializeField] GameObject scrollP2Marker;
        [SerializeField] Button leftArrow;
        [SerializeField] Button rightArrow;
        [Tooltip("Played when an arrow (or the keyboard arrows) switches the stage. Random take each time.")]
        [SerializeField] SfxCue arrowSound;

        [Header("Card markers")]
        [Tooltip("Markers sit this far below the card's centre; when both are on one card they move apart by Spread.")]
        [SerializeField] float markerDrop = 68f;
        [SerializeField] float markerSpread = 30f;

        [Header("Opponent (CPU)")]
        [Tooltip("The stage the CPU picks (index in Stages). The red marker shows it.")]
        [SerializeField, Min(0)] int cpuStage = 0;

        [Header("Ready")]
        [SerializeField] Button readyButton;
        [SerializeField] Image readyImage;
        [SerializeField] TMP_Text readyText;
        [Tooltip("The Online 1v1 screen's READY button, unselected (general). Empty = no art yet.")]
        [SerializeField] Sprite readySprite;
        [Tooltip("The same button, selected (waiting for the opponent). Empty = Ready Sprite.")]
        [SerializeField] Sprite waitingSprite;
        [Tooltip("Ready button height; its width follows the art (the right edge stays put, so the longer waiting art grows left).")]
        [SerializeField] float readyHeight = 120f;
        [SerializeField] Color readyTextColour = new Color32(0x40, 0x1D, 0x0E, 0xFF);
        [SerializeField] TMP_FontAsset readyFont;
        [SerializeField] float readySize = 60f;
        [SerializeField] float waitingSize = 36f;
        [SerializeField] string waitingLabel = "Waiting for opponent";
        [Tooltip("How long \"Waiting for opponent\" shows before the fight loads.")]
        [SerializeField, Min(0f)] float waitingTime = 1.4f;
        [SerializeField] TmpWave waitingWave;
        [SerializeField] Button backButton;
        [SerializeField] TMP_Text toastText;

        [Header("Scenes")]
        [SerializeField] string fightScene = "FightScene";
        [SerializeField] string backScene = "Roster";

        int viewed, cpuPick;
        bool committed;
        float toastUntil, loadAt = -1f;

        int Count => stages != null ? stages.Length : 0;

        void Awake()
        {
            Time.timeScale = 1f;
            for (int i = 0; i < cards.Length; i++)
            {
                int index = i;
                if (cards[i] != null && cards[i].button) cards[i].button.onClick.AddListener(() => View(index));
            }
            if (leftArrow) leftArrow.onClick.AddListener(() => Step(-1));
            if (rightArrow) rightArrow.onClick.AddListener(() => Step(1));
            if (readyButton) readyButton.onClick.AddListener(Ready);
            if (backButton) backButton.onClick.AddListener(Back);
        }

        void Start()
        {
            cpuPick = Mathf.Clamp(cpuStage, 0, Mathf.Max(0, Count - 1));
            viewed = Mathf.Max(0, IndexOfId(MatchSetup.Stage));
            if (toastText) toastText.gameObject.SetActive(false);
            if (waitingWave) waitingWave.enabled = false;
            SetReadyLook(false);
            Refresh();
        }

        void Update()
        {
            var kb = Keyboard.current;
            if (!committed && kb != null)
            {
                if (kb.escapeKey.wasPressedThisFrame) Back();
                if (kb.leftArrowKey.wasPressedThisFrame) Step(-1);
                if (kb.rightArrowKey.wasPressedThisFrame) Step(1);
                if (kb.enterKey.wasPressedThisFrame) Ready();
            }
            if (toastText && toastText.gameObject.activeSelf && Time.unscaledTime >= toastUntil) toastText.gameObject.SetActive(false);
            if (loadAt > 0f && Time.unscaledTime >= loadAt) { loadAt = -1f; SceneLoader.Load(fightScene); }
        }

        /// <summary>Arrow: switch to the next / previous stage with the swipe sound.</summary>
        void Step(int dir)
        {
            if (committed || Count == 0) return;
            AudioManager.Play(arrowSound);
            View(Wrap(viewed + dir));
        }

        void View(int i)
        {
            if (committed || i < 0 || i >= Count) return;
            viewed = i;
            Refresh();
        }

        void Ready()
        {
            if (committed) return;
            var s = Get(viewed);
            if (s == null) return;
            if (!s.playable) { Toast($"{s.displayName}: coming soon"); return; }
            committed = true;
            KungFuCrashout.Sound.UiTapSound.PlayBeforeDelayedLoad(); // on the press; the delayed load won't play it again
            MatchSetup.Stage = s.id;
            SetReadyLook(true);
            foreach (var c in cards) if (c != null && c.button) c.button.interactable = false;
            foreach (var b in new[] { leftArrow, rightArrow, readyButton }) if (b) b.interactable = false;
            if (waitingTime <= 0f) SceneLoader.Load(fightScene);
            else loadAt = Time.unscaledTime + waitingTime;
        }

        void Back()
        {
            if (committed) return;
            SceneLoader.Load(backScene);
        }

        // ───────────── View ─────────────

        void Refresh()
        {
            var s = Get(viewed);
            ShowDisplay(s);
            if (locationText) locationText.text = s != null ? s.displayName : "";
            if (scrollP1Marker) scrollP1Marker.SetActive(true);               // the stage on display is P1's pick
            if (scrollP2Marker) scrollP2Marker.SetActive(viewed == cpuPick);

            for (int i = 0; i < cards.Length; i++)
            {
                var c = cards[i];
                var st = Get(i);
                if (c == null) continue;
                bool picked = i == viewed;
                if (c.art)
                {
                    c.art.sprite = st == null ? null : picked ? (st.cardSelected ? st.cardSelected : st.cardUnselected) : st.cardUnselected;
                    c.art.enabled = c.art.sprite;
                }
                if (c.glow) c.glow.enabled = picked;
                bool p1 = picked, p2 = i == cpuPick;
                PlaceMarker(c.p1Marker, p1, p1 && p2 ? -markerSpread : 0f);
                PlaceMarker(c.p2Marker, p2, p1 && p2 ? markerSpread : 0f);
            }
            // The picked card (and its glow) draws above the others.
            if (viewed < cards.Length && cards[viewed] != null && cards[viewed].root) cards[viewed].root.SetAsLastSibling();
        }

        void PlaceMarker(RectTransform m, bool on, float x)
        {
            if (!m) return;
            m.gameObject.SetActive(on);
            m.anchoredPosition = new Vector2(x, -markerDrop);
        }

        /// <summary>Fill the scroll's window with the illustration (cover: full width, cropped top / bottom at Display Focus).</summary>
        void ShowDisplay(Stage s)
        {
            if (!display) return;
            var sprite = s?.display;
            display.sprite = sprite;
            display.enabled = sprite;
            if (!sprite || !window) return;
            Vector2 win = window.rect.size;
            float aspect = sprite.rect.width / Mathf.Max(1f, sprite.rect.height);
            float w = Mathf.Max(win.x, win.y * aspect) * Mathf.Max(1f, s.displayZoom);
            float h = w / aspect;
            var rt = display.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(w, h);
            rt.anchoredPosition = new Vector2(0f, (h - win.y) * (0.5f - s.displayFocus));
        }

        void SetReadyLook(bool waiting)
        {
            if (readyImage)
            {
                var sprite = waiting && waitingSprite ? waitingSprite : readySprite;
                readyImage.sprite = sprite;
                readyImage.color = sprite ? Color.white : new Color(1f, 1f, 1f, 0f); // no art yet: invisible but still tappable
                if (sprite)
                {
                    float aspect = sprite.rect.width / Mathf.Max(1f, sprite.rect.height);
                    readyImage.rectTransform.sizeDelta = new Vector2(readyHeight * aspect, readyHeight);
                }
            }
            if (readyText)
            {
                if (readyFont) readyText.font = readyFont;
                readyText.text = waiting ? waitingLabel : "READY";
                readyText.fontSize = waiting ? waitingSize : readySize;
                readyText.color = readyTextColour;
            }
            if (waitingWave) waitingWave.enabled = waiting;
        }

        void Toast(string text)
        {
            if (!toastText) return;
            toastText.text = text;
            toastText.gameObject.SetActive(true);
            toastUntil = Time.unscaledTime + 1.6f;
        }

        Stage Get(int i) => i >= 0 && i < Count ? stages[i] : null;
        int Wrap(int i) => Count == 0 ? 0 : ((i % Count) + Count) % Count;

        int IndexOfId(string id)
        {
            for (int i = 0; i < Count; i++) if (stages[i] != null && stages[i].id == id) return i;
            return 0;
        }
    }
}
