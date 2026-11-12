using TMPro;
using UnityEngine;
using UnityEngine.UI;
using KungFuCrashout.Props;

namespace KungFuCrashout.UI
{
    /// <summary>
    /// Playtesting switch in the pause menu (GAME page): ticked = Original item behaviour (items live where placed),
    /// unticked = the GDD stage-item rules (all asleep, woken by attack shockwaves). Switching lays the stage out again
    /// in the new mode straight away. The choice lasts until the app closes (see StageItemDirector).
    ///
    /// Setup: Window → KungFu Crashout → GDD Build → Add Item Mode Toggle (pause menu).
    /// Place in: Assets/_Project/Scripts/UI/ItemModeToggle.cs
    /// </summary>
    [RequireComponent(typeof(Toggle))]
    public class ItemModeToggle : MonoBehaviour
    {
        [Tooltip("Empty = the one in the scene.")]
        [SerializeField] StageItemDirector director;
        [Tooltip("Optional: shows which mode is active.")]
        [SerializeField] TMP_Text label;
        [SerializeField] string originalText = "Items: ORIGINAL (testing) — live where placed";
        [SerializeField] string gddText = "Items: GDD rules — asleep, woken by attack shockwaves";

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

        void OnChanged(bool original)
        {
            if (director) director.SetOriginalItemBehaviour(original);
            Refresh();
        }

        void Refresh()
        {
            if (!toggle) return;
            bool on = director && director.OriginalItemBehaviour;
            toggle.SetIsOnWithoutNotify(on);
            toggle.interactable = director;
            if (label) label.text = !director ? "Items: no Stage Items director in this scene" : on ? originalText : gddText;
        }
    }
}
