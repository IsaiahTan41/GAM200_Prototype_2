using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using KungFuCrashout.Fighters;

namespace KungFuCrashout.UI
{
    /// <summary>
    /// Playtesting switch in the pause menu (GAME page) that applies to EVERY fighter in the scene:
    ///   Heavy Uses Animation — ticked = the heavy plays its animation (Monk_HeavyAttack: held leap → slam, frame data);
    ///                          unticked = the original pose-based heavy.
    ///   Use Frame Data       — ticked = attacks with frame data run on the 60 fps frame counter; unticked = clip events.
    /// The choice lasts until the app closes (scene reloads and rematches keep it); the Fighter's Inspector value is the
    /// default at app start.
    /// Setup: Window → KungFu Crashout → GDD Build → Add Heavy Animation Toggle (pause menu)
    ///        (Set Up Monk Heavy Attack adds it too).
    /// Place in: Assets/_Project/Scripts/UI/FighterSettingToggle.cs
    /// </summary>
    [RequireComponent(typeof(Toggle))]
    public class FighterSettingToggle : MonoBehaviour
    {
        public enum Setting { HeavyUsesAnimation, UseFrameData }

        [SerializeField] Setting setting = Setting.HeavyUsesAnimation;
        [Tooltip("Optional: shows which mode is active.")]
        [SerializeField] TMP_Text label;
        [SerializeField] string heavyOnText = "Heavy: NEW animation (held leap → slam)";
        [SerializeField] string heavyOffText = "Heavy: ORIGINAL (poses)";
        [SerializeField] string framesOnText = "Attack timing: FRAME DATA (60 fps)";
        [SerializeField] string framesOffText = "Attack timing: clip events (original)";

        static bool? sessionHeavy, sessionFrames;
        Toggle toggle;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Init()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
            ApplySession();
        }

        static void OnSceneLoaded(Scene scene, LoadSceneMode mode) => ApplySession();

        /// <summary>Re-apply this session's choices to the fighters of a freshly loaded scene.</summary>
        static void ApplySession()
        {
            if (!sessionHeavy.HasValue && !sessionFrames.HasValue) return;
            foreach (var f in FindObjectsByType<Fighter>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (sessionHeavy.HasValue) f.HeavyUsesAnimation = sessionHeavy.Value;
                if (sessionFrames.HasValue) f.UseFrameData = sessionFrames.Value;
            }
        }

        void Awake()
        {
            toggle = GetComponent<Toggle>();
            toggle.onValueChanged.AddListener(OnChanged);
        }

        void OnEnable() => Refresh(); // the menu opens: show the current mode

        void OnDestroy()
        {
            if (toggle) toggle.onValueChanged.RemoveListener(OnChanged);
        }

        void OnChanged(bool on)
        {
            if (setting == Setting.HeavyUsesAnimation) sessionHeavy = on; else sessionFrames = on;
            ApplySession();
            Refresh();
        }

        void Refresh()
        {
            if (!toggle) return;
            var any = FindFirstObjectByType<Fighter>();
            bool on = any && (setting == Setting.HeavyUsesAnimation ? any.HeavyUsesAnimation : any.UseFrameData);
            toggle.SetIsOnWithoutNotify(on);
            toggle.interactable = any;
            if (!label) return;
            label.text = setting == Setting.HeavyUsesAnimation ? (on ? heavyOnText : heavyOffText) : (on ? framesOnText : framesOffText);
        }
    }
}
