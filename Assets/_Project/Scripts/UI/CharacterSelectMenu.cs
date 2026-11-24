using System;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using KungFuCrashout.Core;
using KungFuCrashout.Fighters;
using KungFuCrashout.Sound;

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
    ///   • Tap To Confirm (default ON): the P1 side starts blank. Turning the carousel only browses; tapping the centre
    ///     card flashes it yellow, then that fighter's portrait slides in on the P1 side and their Voice plays. READY
    ///     needs a confirmed fighter. The CPU side (right) is unchanged: its fighter shows from the start.
    ///     Mirror On P1: the fighter's portrait on the P1 side is an exact mirror of the P2 portrait (same size, mirrored
    ///     position), e.g. Lord Yuan. OFF = the old browse-to-pick behaviour.
    ///   • Voices: Window → KungFu Crashout → Audio → Set Up Roster Character Voices.
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
            [Tooltip("Spoken when this fighter is confirmed (and when revealed as the CPU).")]
            public SfxCue voice;
            [Tooltip("On the P1 (left) side, show this portrait as an exact mirror of the P2 (right) portrait: same size, " +
                     "mirrored position. For art laid out for the right side (Lord Yuan).")]
            public bool mirrorOnP1;
            [Tooltip("Mirror the illustration on the P2 / CPU (right) side so it faces the centre.")]
            public bool mirrorOnP2;
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
        [Tooltip("Played each time the carousel turns (arrows, side cards, keyboard arrows). Random take each time.")]
        [SerializeField] SfxCue turnSound;

        [Header("Sides")]
        [SerializeField] Side p1 = new Side();
        [SerializeField] Side p2 = new Side();
        [Tooltip("Tapping the P2 illustration cycles the CPU's fighter (when the playtest lock is off).")]
        [SerializeField] Button p2CycleButton;

        [Header("Tap to confirm")]
        [Tooltip("ON: sides start blank; tap the centre card to confirm (yellow flash → portrait revealed + voice). " +
                 "OFF: the old behaviour (turning the carousel picks P1, sides always filled).")]
        [SerializeField] bool tapToConfirm = true;
        [SerializeField] Color flashColour = new Color(1f, 0.84f, 0.12f, 0.9f);
        [Tooltip("Flash as a solid yellow silhouette of the card. OFF = a yellow tint over the card art.")]
        [SerializeField] bool flashSilhouette = true;
        [Tooltip("Material for the silhouette flash (GUI/Text Shader; made by the voices menu so the shader ships in builds).")]
        [SerializeField] Material flashMaterial;
        [SerializeField, Min(0.05f)] float flashTime = 0.45f;
        [SerializeField, Min(1)] int flashCount = 2;
        [Tooltip("Portrait fade / slide-in time after the flash.")]
        [SerializeField, Min(0f)] float revealTime = 0.25f;
        [Tooltip("How far (px) the portrait slides in from the screen edge.")]
        [SerializeField] float revealSlide = 60f;
        [Tooltip("Playtest lock: P1 may pick any playable fighter, only the CPU is locked. OFF = P1 is Locked Player only.")]
        [SerializeField] bool lockOnlyCpu = true;

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
        int shownP1 = -1, shownP2 = -1;            // tap-to-confirm: what the sides show (-1 = blank)
        float flashT = -1f, p1RevealT = 1f;
        Image flashImg;
        Vector2 p1Base, p1Size, p1Target, p2Base, p2Size;

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
            if (readyButton) readyButton.onClick.AddListener(() => Ready(false));
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
            if (p1.illustration) { p1Base = p1Target = p1.illustration.rectTransform.anchoredPosition; p1Size = p1.illustration.rectTransform.sizeDelta; }
            if (p2.illustration) { p2Base = p2.illustration.rectTransform.anchoredPosition; p2Size = p2.illustration.rectTransform.sizeDelta; }
            shownP2 = p2Pick;                               // CPU side as before: shown from the start
            shownP1 = tapToConfirm ? -1 : p1Pick;
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
                if (kb.spaceKey.wasPressedThisFrame) TryPickP1(focus, true);
                if (kb.enterKey.wasPressedThisFrame)
                {
                    if (tapToConfirm && shownP1 < 0 && flashT < 0f) TryPickP1(focus, true);
                    else Ready(false);
                }
            }

            if (!committed)
            {
                timeLeft = Mathf.Max(0f, timeLeft - dt);
                if (timeLeft <= 0f && autoReadyAtZero) Ready(true);
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

            UpdateFlash(dt);
            if (p1RevealT < 1f) { p1RevealT = revealTime > 0f ? Mathf.Min(1f, p1RevealT + dt / revealTime) : 1f; Slide(p1, p1Target, -1f, p1RevealT); }

            if (toastText && toastText.gameObject.activeSelf && Time.unscaledTime >= toastUntil) toastText.gameObject.SetActive(false);
            if (loadAt > 0f && Time.unscaledTime >= loadAt) { loadAt = -1f; SceneLoader.Load(fightScene); }
        }

        // ───────────── Picks ─────────────

        void Turn(int step)
        {
            if (committed || Count == 0) return;
            focus = Wrap(focus + step);
            popT = 0f;
            AudioManager.Play(turnSound);
            if (tapToConfirm) { StopFlash(); Refresh(); }   // browsing only: the tap confirms
            else TryPickP1(focus, false);
        }

        bool CanPickP1(Entry e, bool toast)
        {
            string why = null;
            if (e == null) return false;
            if (!e.Playable) why = $"{e.displayName}: coming soon";
            else if (Locked && !lockOnlyCpu && e.archetype != lockedPlayer)
                why = e.archetype == lockedCpu ? $"Playtest: {e.displayName} is the CPU's fighter"
                                               : $"Playtest: P1 plays {NameOf(lockedPlayer)}";
            if (why != null && toast) Toast(why);
            return why == null;
        }

        /// <summary>The focused fighter becomes P1's pick if allowed; otherwise P1 stays and the reason shows.</summary>
        void TryPickP1(int i, bool tapped)
        {
            if (committed) return;
            var e = Get(i);
            if (e == null) return;
            if (CanPickP1(e, true))
            {
                p1Pick = i;
                if (p2Pick == p1Pick && !Locked && CountPlayable() > 1) p2Pick = NextPlayable(p1Pick, 1, p1Pick);
                if (tapToConfirm && tapped) StartFlash();
                else if (!tapToConfirm) shownP1 = p1Pick;
            }
            Refresh();
        }

        void CycleCpu()
        {
            if (committed) return;
            if (Locked) { Toast($"Playtest: the CPU plays {NameOf(lockedCpu)}"); return; }
            p2Pick = NextPlayable(p2Pick, 1, -1);
            shownP2 = p2Pick;
            Refresh();
        }

        void Ready(bool auto)
        {
            if (committed) return;
            if (tapToConfirm)
            {
                if (flashT >= 0f) FinishFlash();                 // READY mid-flash: confirm now
                if (shownP1 < 0)
                {
                    if (!auto) { Toast("Tap a fighter to confirm"); return; }
                    int i = CanPickP1(Get(focus), false) ? focus : CanPickP1(Get(p1Pick), false) ? p1Pick : FirstPlayable();
                    p1Pick = shownP1 = i;                        // time ran out: take the focused fighter
                }
                p1Pick = shownP1;
            }
            var a = Get(p1Pick);
            var b = Get(p2Pick);
            if (a == null || !a.Playable || b == null || !b.Playable) { Toast("Pick a fighter first"); return; }
            committed = true;
            KungFuCrashout.Sound.UiTapSound.PlayBeforeDelayedLoad(); // on the press; the delayed load won't play it again
            MatchSetup.PlayerPick = a.archetype;
            MatchSetup.CpuPick = b.archetype;
            if (readyScroll) readyScroll.SetActive(false);
            if (waitingScroll) waitingScroll.SetActive(true);
            foreach (var btn in new[] { leftArrow, rightArrow, leftCard.button, rightCard.button, centreCard.button, readyButton, p2CycleButton })
                if (btn) btn.interactable = false;
            Refresh();
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
            ShowSide(p1, Get(shownP1), true);
            LayoutP1(Get(shownP1));
            ShowSide(p2, Get(shownP2), false);
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
            if (c.p1Marker) c.p1Marker.SetActive(i == shownP1);
            if (c.p2Marker) c.p2Marker.SetActive(i == shownP2);
            if (c.dim) { c.dim.sprite = e.card; c.dim.enabled = !centre && e.card; } // same shape as the card
        }

        static void ShowSide(Side s, Entry e, bool isP1)
        {
            if (s == null) return;
            if (s.illustration)
            {
                s.illustration.sprite = e?.illustration;
                s.illustration.enabled = e != null && e.illustration; // missing art / not confirmed = blank
                bool mirror = e != null && (isP1 ? e.mirrorOnP1 : e.mirrorOnP2);
                var sc = s.illustration.rectTransform.localScale;
                sc.x = Mathf.Abs(sc.x) * (mirror ? -1f : 1f);         // pivot is centred, so it flips in place
                s.illustration.rectTransform.localScale = sc;
            }
            if (s.classText) s.classText.text = e?.classLabel ?? "";
            if (s.nameText) s.nameText.text = e?.displayName ?? "";
            if (s.skillText) s.skillText.text = e?.skill ?? "";
        }

        // ───────────── Confirm flash / reveal ─────────────

        void StartFlash()
        {
            var img = FlashImage();
            popT = 0f;
            if (!img) { FinishFlash(); return; }
            img.sprite = centreCard.art ? centreCard.art.sprite : null;
            img.preserveAspect = centreCard.art && centreCard.art.preserveAspect;
            img.gameObject.SetActive(true);
            flashT = 0f;
            UpdateFlash(0f);
        }

        void UpdateFlash(float dt)
        {
            if (flashT < 0f) return;
            flashT += dt;
            if (flashT >= flashTime) { FinishFlash(); return; }
            float a = Mathf.Abs(Mathf.Sin(Mathf.PI * flashCount * flashT / flashTime));
            if (flashImg) { var c = flashColour; c.a *= a; flashImg.color = c; }
        }

        void StopFlash()
        {
            flashT = -1f;
            if (flashImg) flashImg.gameObject.SetActive(false);
        }

        /// <summary>End of the flash: the confirmed fighter's portrait slides in on the P1 side and their voice plays.</summary>
        void FinishFlash()
        {
            StopFlash();
            if (!CanPickP1(Get(p1Pick), false)) return;
            shownP1 = p1Pick;
            p1RevealT = 0f;
            Refresh();                                   // sets the P1 layout (p1Target) for this fighter
            Slide(p1, p1Target, -1f, 0f);
            Voice(Get(shownP1));
        }

        /// <summary>P1 portrait rect: its own layout, or for Mirror On P1 an exact mirror of the P2 portrait's rect
        /// (same size, x mirrored across the stage), so art drawn for the right side isn't stretched.</summary>
        void LayoutP1(Entry e)
        {
            if (!p1.illustration) return;
            var rt = p1.illustration.rectTransform;
            bool mirror = e != null && e.mirrorOnP1 && p2.illustration;
            if (mirror)
            {
                var p2rt = p2.illustration.rectTransform;
                var parent = rt.parent as RectTransform;
                // Both portraits sit on the same stage, anchored top-left: mirror x across the stage width.
                float w = parent ? parent.rect.width : 2424f;
                p1Target = new Vector2(w - p2Base.x, p2Base.y);
                rt.sizeDelta = p2Size;
                if (p2rt.parent != rt.parent) p1Target = p1Base;   // different parents: keep the P1 spot
            }
            else
            {
                p1Target = p1Base;
                rt.sizeDelta = p1Size;
            }
            if (p1RevealT >= 1f) rt.anchoredPosition = p1Target;
            // scale: ShowSide already mirrored it (x = -1); keep the P2 portrait's y scale
        }

        static void Voice(Entry e)
        {
            if (e != null && e.voice) AudioManager.Play(e.voice);
        }

        /// <summary>Fade + slide in from the screen edge (dir -1 = from the left, +1 = from the right).</summary>
        void Slide(Side s, Vector2 basePos, float dir, float t)
        {
            if (s == null || !s.illustration) return;
            float k = 1f - (1f - t) * (1f - t);
            s.illustration.rectTransform.anchoredPosition = basePos + new Vector2(dir * revealSlide * (1f - k), 0f);
            var c = s.illustration.color; c.a = k; s.illustration.color = c;
        }

        Image FlashImage()
        {
            if (flashImg) return flashImg;
            var host = centreCard.art ? centreCard.art.rectTransform : centreCard.root;
            if (!host) return null;
            var go = new GameObject("Confirm Flash", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            var rt = (RectTransform)go.transform;
            rt.SetParent(host, false);
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            rt.SetAsLastSibling();
            flashImg = go.GetComponent<Image>();
            flashImg.raycastTarget = false;
            if (flashSilhouette)
            {
                // GUI/Text Shader draws colour × texture alpha: a solid silhouette of the card in the flash colour.
                if (flashMaterial) flashImg.material = flashMaterial;
                else { var sh = Shader.Find("GUI/Text Shader"); if (sh) flashImg.material = new Material(sh); }
            }
            go.SetActive(false);
            return flashImg;
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
