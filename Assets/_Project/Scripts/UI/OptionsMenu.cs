using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using KungFuCrashout.Controls;
using KungFuCrashout.DebugTools;
using KungFuCrashout.Props;
using KungFuCrashout.Sound;

namespace KungFuCrashout.UI
{
    /// <summary>
    /// Options popup (the pause menu), opened by the HUD's Options button — and by any button in Also Opened By (the GDD
    /// match timer). Three tabs:
    ///   GAME  — Enemy AI toggle: ON = State Machine (moves, fetches and throws items, attacks), OFF = Practice Dummy
    ///           (TrainingDummy → Behaviour Mode)
    ///   AUDIO — Master (AudioListener), Music (MusicPlayer) and Sound Effects (AudioManager) volume sliders
    ///   TUNING — balance values for playtests (TuningPanel, M2D02); optional, added by Add Tuning Tab + Scrolling Pages
    ///
    /// While open: the game is paused (optional) and the fight touch controls are switched off, so taps on the menu never
    /// punch / block / pick things up. They come back once every finger has left the screen after closing.
    /// Settings are remembered on the device (PlayerPrefs), so they survive the round reset and app restarts.
    /// Android Back / Esc and the Open Key (P by default) also open and close it.
    ///
    /// Setup: KungFu Crashout → Build Options Menu (builds the popup and hooks the OptionsButton).
    /// Place in: Assets/_Project/Scripts/UI/OptionsMenu.cs
    /// </summary>
    [DefaultExecutionOrder(-45)] // before the fight input handlers, so a tap on the Options button is claimed first
    public class OptionsMenu : MonoBehaviour
    {
        [Header("Popup")]
        [SerializeField] Button optionsButton;
        [Tooltip("Other buttons that also open / close this menu, e.g. the match timer (Timer (Pause)).")]
        [SerializeField] Button[] alsoOpenedBy = new Button[0];
        [Tooltip("The popup itself (kept hidden until opened).")]
        [SerializeField] GameObject panel;
        [SerializeField] Button closeButton;

        [Header("Tabs")]
        [SerializeField] Button gameTab;
        [SerializeField] Button audioTab;
        [SerializeField] GameObject gamePage;
        [SerializeField] GameObject audioPage;
        [Tooltip("Optional third tab (TUNING). Empty = two tabs as before.")]
        [SerializeField] Button tuningTab;
        [SerializeField] GameObject tuningPage;
        [Tooltip("Open on the tab used last time (handy while tuning). Off = always open on GAME.")]
        [SerializeField] bool reopenLastTab = true;
        [SerializeField] Color tabSelectedColour = new Color(0.85f, 0.25f, 0.2f);
        [SerializeField] Color tabNormalColour = new Color(0.25f, 0.25f, 0.3f);

        [Header("Game")]
        [Tooltip("ON = State Machine enemy, OFF = Practice Dummy.")]
        [SerializeField] Toggle stateMachineToggle;
        [SerializeField] TMP_Text enemyModeLabel;
        [Tooltip("The opponent's TrainingDummy. Empty = found automatically (the one not on the Player).")]
        [SerializeField] TrainingDummy enemy;

        [Header("Audio")]
        [SerializeField] Slider masterSlider;
        [SerializeField] Slider musicSlider;
        [SerializeField] Slider sfxSlider;
        [SerializeField] TMP_Text masterValue;
        [SerializeField] TMP_Text musicValue;
        [SerializeField] TMP_Text sfxValue;

        [Header("Keyboard")]
        [Tooltip("Key that opens / closes the menu on desktop (Esc / Android Back always works too). None = Esc only.")]
        [SerializeField] Key openKey = Key.P;
        [Tooltip("Android Back / Esc also OPENS the menu. OFF (Start screen): it only closes it, so Back still quits there.")]
        [SerializeField] bool escapeOpens = true;

        [Header("Behaviour")]
        [Tooltip("Freeze the fight while the menu is open (music keeps playing).")]
        [SerializeField] bool pauseWhileOpen = true;
        [Tooltip("Remember the settings on this device (survives the round reset and restarting the app).")]
        [SerializeField] bool rememberSettings = true;

        const string KeyMaster = "kfc.volume.master";
        const string KeyMusic = "kfc.volume.music";
        const string KeySfx = "kfc.volume.sfx";
        const string KeyEnemyMode = "kfc.enemy.stateMachine";

        public bool IsOpen => panel && panel.activeSelf;

        /// <summary>Frame an options menu last opened / closed (so the Start screen's Back-to-quit ignores that press).</summary>
        public static int LastToggleFrame { get; private set; } = -10;

        /// <summary>True while any options menu is open, or one was opened / closed this or last frame.</summary>
        public static bool Busy
        {
            get
            {
                if (Time.frameCount - LastToggleFrame <= 1) return true;
                foreach (var m in FindObjectsByType<OptionsMenu>(FindObjectsSortMode.None)) if (m.IsOpen) return true;
                return false;
            }
        }

        readonly List<Behaviour> pausedInput = new List<Behaviour>();
        readonly List<int> buttonTouches = new List<int>(); // fingers that started on the Options button
        Coroutine resumeInput;
        float timeScaleBeforeOpen = 1f;
        int currentTab;

        const int GameTab = 0, AudioTab = 1, TuningTab = 2;

        void Awake()
        {
            if (panel) panel.SetActive(false);
            if (optionsButton) optionsButton.onClick.AddListener(Toggle);
            foreach (var b in alsoOpenedBy) if (b && b != optionsButton) b.onClick.AddListener(Toggle);
            if (closeButton) closeButton.onClick.AddListener(Close);
            if (gameTab) gameTab.onClick.AddListener(() => ShowTab(GameTab));
            if (audioTab) audioTab.onClick.AddListener(() => ShowTab(AudioTab));
            if (tuningTab) tuningTab.onClick.AddListener(() => ShowTab(TuningTab));
        }

        void Start()
        {
            if (!enemy) enemy = FindEnemyDummy();

            // Load (or read the scene's current values), apply, then listen.
            float master = Load(KeyMaster, AudioListener.volume);
            float music = Load(KeyMusic, MusicPlayer.Instance ? MusicPlayer.Instance.Volume : 1f);
            float sfx = Load(KeySfx, AudioManager.SfxVolume);
            bool stateMachine = rememberSettings && PlayerPrefs.HasKey(KeyEnemyMode)
                ? PlayerPrefs.GetInt(KeyEnemyMode) == 1
                : enemy && enemy.Mode == TrainingDummy.BehaviourMode.StateMachine;

            SetupSlider(masterSlider, master, OnMaster);
            SetupSlider(musicSlider, music, OnMusic);
            SetupSlider(sfxSlider, sfx, OnSfx);
            OnMaster(master);
            OnMusic(music);
            OnSfx(sfx);

            if (stateMachineToggle)
            {
                stateMachineToggle.SetIsOnWithoutNotify(stateMachine);
                stateMachineToggle.onValueChanged.AddListener(OnEnemyMode);
            }
            OnEnemyMode(stateMachine);

            ShowTab(GameTab);

            // Saved tuning values apply from the start of the fight, not only once the TUNING page is opened.
            foreach (var page in GetComponentsInChildren<TuningPanel>(true)) page.ApplySaved();

            // The GDD AI page lives inside the (hidden) panel, so it wouldn't run until the menu is first opened:
            // apply its saved choices (AI on / off, show thinking) now.
            foreach (var page in GetComponentsInChildren<AISettingsPanel>(true)) page.ApplySaved();
        }

        void Update()
        {
            ClaimButtonTouches();

            // Android Back button arrives as Escape.
            var kb = Keyboard.current;
            if (kb != null && ((kb.escapeKey.wasPressedThisFrame && (IsOpen || escapeOpens)) ||
                               (openKey != Key.None && kb[openKey].wasPressedThisFrame))) Toggle();
        }

        void OnDestroy()
        {
            if (IsOpen && pauseWhileOpen) Time.timeScale = 1f;
        }

        // ───────────── Open / close ─────────────

        public void Toggle()
        {
            if (IsOpen) Close();
            else Open();
        }

        public void Open()
        {
            if (!panel || IsOpen) return;
            panel.SetActive(true);
            LastToggleFrame = Time.frameCount;
            transform.SetAsLastSibling(); // draw over the HUD
            ShowTab(reopenLastTab ? currentTab : GameTab);
            PauseFightInput();
            if (pauseWhileOpen)
            {
                timeScaleBeforeOpen = Time.timeScale > 0f ? Time.timeScale : 1f;
                Time.timeScale = 0f;
            }
        }

        public void Close()
        {
            if (!panel || !IsOpen) return;
            panel.SetActive(false);
            LastToggleFrame = Time.frameCount;
            if (pauseWhileOpen) Time.timeScale = timeScaleBeforeOpen;
            if (rememberSettings) PlayerPrefs.Save();
            if (resumeInput != null) StopCoroutine(resumeInput);
            resumeInput = StartCoroutine(ResumeFightInputWhenReleased());
        }

        void ShowTab(int tab)
        {
            if (tab == TuningTab && !tuningPage) tab = GameTab;
            currentTab = tab;
            if (gamePage) gamePage.SetActive(tab == GameTab);
            if (audioPage) audioPage.SetActive(tab == AudioTab);
            if (tuningPage) tuningPage.SetActive(tab == TuningTab);
            Tint(gameTab, tab == GameTab);
            Tint(audioTab, tab == AudioTab);
            Tint(tuningTab, tab == TuningTab);
        }

        void Tint(Button tab, bool selected)
        {
            if (tab && tab.targetGraphic) tab.targetGraphic.color = selected ? tabSelectedColour : tabNormalColour;
        }

        // ───────────── Fight input off while open ─────────────

        void PauseFightInput()
        {
            if (resumeInput != null) { StopCoroutine(resumeInput); resumeInput = null; }
            foreach (var b in FightInputs())
            {
                if (!b || !b.enabled) continue;
                b.enabled = false;
                if (!pausedInput.Contains(b)) pausedInput.Add(b);
            }
        }

        /// <summary>Wait until every finger is off the screen, so the closing tap isn't read as an attack.</summary>
        IEnumerator ResumeFightInputWhenReleased()
        {
            while (AnyPress()) yield return null;
            yield return null;
            foreach (var b in pausedInput) if (b) b.enabled = true;
            pausedInput.Clear();
            resumeInput = null;
        }

        static IEnumerable<Behaviour> FightInputs()
        {
            foreach (var b in FindObjectsByType<TouchInputHandler>(FindObjectsSortMode.None)) yield return b;
            foreach (var b in FindObjectsByType<TapAnywhereInputHandler>(FindObjectsSortMode.None)) yield return b;
            foreach (var b in FindObjectsByType<GestureArbiterInput>(FindObjectsSortMode.None)) yield return b;
            foreach (var b in FindObjectsByType<ItemTapHandler>(FindObjectsSortMode.None)) yield return b;
            foreach (var b in FindObjectsByType<KeyboardDebugInput>(FindObjectsSortMode.None)) yield return b; // test keys too
        }

        static bool AnyPress()
        {
            var ts = Touchscreen.current;
            if (ts != null)
                foreach (var t in ts.touches)
                    if (t.press.isPressed) return true;
            var mouse = Mouse.current;
            return mouse != null && mouse.leftButton.isPressed;
        }

        /// <summary>
        /// The Options button overlaps the fight-touch area, so a finger that starts on it is claimed (TouchClaims):
        /// the fight controls ignore it instead of reading it as an attack.
        /// </summary>
        void ClaimButtonTouches()
        {
            var ts = Touchscreen.current;
            if (ts == null || (!optionsButton && alsoOpenedBy.Length == 0)) return;

            foreach (var t in ts.touches)
            {
                int id = t.touchId.ReadValue();
                if (t.press.wasPressedThisFrame && OnAnOpenButton(t.position.ReadValue()))
                {
                    TouchClaims.Claim(id);
                    if (!buttonTouches.Contains(id)) buttonTouches.Add(id);
                }
                else if (t.press.wasReleasedThisFrame && buttonTouches.Remove(id))
                {
                    TouchClaims.Release(id);
                }
            }
        }

        bool OnAnOpenButton(Vector2 screenPos)
        {
            if (Hit(optionsButton, screenPos)) return true;
            foreach (var b in alsoOpenedBy) if (Hit(b, screenPos)) return true;
            return false;
        }

        static bool Hit(Button b, Vector2 screenPos) =>
            b && b.isActiveAndEnabled &&
            RectTransformUtility.RectangleContainsScreenPoint((RectTransform)b.transform, screenPos, null);

        // ───────────── Settings ─────────────

        void OnEnemyMode(bool stateMachine)
        {
            if (enemy) enemy.Mode = stateMachine ? TrainingDummy.BehaviourMode.StateMachine : TrainingDummy.BehaviourMode.Dummy;
            if (enemyModeLabel)
                enemyModeLabel.text = stateMachine
                    ? "Enemy: STATE MACHINE  (moves, fetches and throws items, attacks)"
                    : "Enemy: PRACTICE DUMMY  (training dummy settings)";
            if (rememberSettings) PlayerPrefs.SetInt(KeyEnemyMode, stateMachine ? 1 : 0);
        }

        void OnMaster(float v)
        {
            AudioListener.volume = v;
            ShowValue(masterValue, v);
            Save(KeyMaster, v);
        }

        void OnMusic(float v)
        {
            if (MusicPlayer.Instance) MusicPlayer.Instance.Volume = v;
            ShowValue(musicValue, v);
            Save(KeyMusic, v);
        }

        void OnSfx(float v)
        {
            AudioManager.SfxVolume = v;
            ShowValue(sfxValue, v);
            Save(KeySfx, v);
        }

        static void SetupSlider(Slider s, float value, UnityEngine.Events.UnityAction<float> onChange)
        {
            if (!s) return;
            s.minValue = 0f;
            s.maxValue = 1f;
            s.wholeNumbers = false;
            s.SetValueWithoutNotify(value);
            s.onValueChanged.AddListener(onChange);
        }

        static void ShowValue(TMP_Text label, float v)
        {
            if (label) label.text = Mathf.RoundToInt(v * 100f) + "%";
        }

        float Load(string key, float fallback) =>
            rememberSettings && PlayerPrefs.HasKey(key) ? Mathf.Clamp01(PlayerPrefs.GetFloat(key)) : fallback;

        void Save(string key, float v)
        {
            if (rememberSettings) PlayerPrefs.SetFloat(key, v);
        }

        static TrainingDummy FindEnemyDummy()
        {
            TrainingDummy any = null;
            foreach (var d in FindObjectsByType<TrainingDummy>(FindObjectsSortMode.None))
            {
                string n = d.name.ToLowerInvariant();
                if (n.Contains("opponent") || n.Contains("enemy")) return d;
                if (!n.Contains("player") && !any) any = d;
            }
            return any;
        }
    }
}
