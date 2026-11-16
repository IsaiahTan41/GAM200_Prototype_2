using TMPro;
using UnityEngine;
using UnityEngine.UI;
using KungFuCrashout.Props;

namespace KungFuCrashout.UI
{
    /// <summary>
    /// Playtesting switches in the pause menu (GAME page). Setting picks which:
    ///   Original Item Behaviour — ticked = items live where placed; unticked = the GDD stage-item rules (all asleep,
    ///     woken by shockwaves). Switching lays the stage out again straight away.
    ///   Original Shockwaves     — ticked = the original shockwave rule (the hit rolls one chance, the nearest sleeping
    ///     item wakes); unticked = Radius (every sleeping item within the hit's reach rolls its own chance, by priority).
    /// The choice lasts until the app closes (see StageItemDirector).
    ///
    /// Setup: Window → KungFu Crashout → GDD Build → Add Item Mode Toggles (pause menu).
    /// Place in: Assets/_Project/Scripts/UI/ItemModeToggle.cs
    /// </summary>
    [RequireComponent(typeof(Toggle))]
    public class ItemModeToggle : MonoBehaviour
    {
        public enum Setting { OriginalItemBehaviour, OriginalShockwaves }

        [SerializeField] Setting setting = Setting.OriginalItemBehaviour;
        [Tooltip("Empty = the one in the scene.")]
        [SerializeField] StageItemDirector director;
        [Tooltip("Optional: shows which mode is active.")]
        [SerializeField] TMP_Text label;
        [Header("Original Item Behaviour labels")]
        [SerializeField] string originalText = "Items: ORIGINAL (testing) — live where placed";
        [SerializeField] string gddText = "Items: GDD rules — asleep, woken by attack shockwaves";
        [Header("Original Shockwaves labels")]
        [SerializeField] string nearestText = "Shockwaves: ORIGINAL — hit rolls once, nearest item wakes";
        [SerializeField] string radiusText = "Shockwaves: RADIUS — every item in reach rolls its own chance";

        Toggle toggle;

        void Awake()
        {
            toggle = GetComponent<Toggle>();
            if (!director) director = FindFirstObjectByType<StageItemDirector>();
            toggle.onValueChanged.AddListener(OnChanged);
        }

        void OnEnable() => Refresh(); // the menu opens: show the current mode

        void OnDestroy()
        {
            if (toggle) toggle.onValueChanged.RemoveListener(OnChanged);
        }

        void OnChanged(bool on)
        {
            if (director)
            {
                if (setting == Setting.OriginalItemBehaviour) director.SetOriginalItemBehaviour(on);
                else director.Shockwaves = on ? StageItemDirector.ShockwaveMode.Nearest : StageItemDirector.ShockwaveMode.Radius;
            }
            Refresh();
        }

        void Refresh()
        {
            if (!toggle) return;
            bool on = director && (setting == Setting.OriginalItemBehaviour
                ? director.OriginalItemBehaviour
                : director.Shockwaves == StageItemDirector.ShockwaveMode.Nearest);
            toggle.SetIsOnWithoutNotify(on);
            toggle.interactable = director;
            if (!label) return;
            if (!director) { label.text = "Items: no Stage Items director in this scene"; return; }
            label.text = setting == Setting.OriginalItemBehaviour
                ? (on ? originalText : gddText)
                : (on ? nearestText : radiusText);
        }
    }
}
