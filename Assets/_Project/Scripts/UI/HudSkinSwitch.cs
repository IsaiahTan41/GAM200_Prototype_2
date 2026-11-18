using UnityEngine;

namespace KungFuCrashout.UI
{
    /// <summary>
    /// Switches the fight HUD between the Concept 27 skin (portraits, slanted bars, coin round counters, status banners)
    /// and the Prototype 2 HUD it replaced. Nothing is deleted: the old widgets sit, disabled, under "Legacy HUD".
    /// Tick / untick Use Concept Skin in the Inspector (edit or play mode). Built by
    /// Window → KungFu Crashout → GDD Build → Apply Fight HUD Skin (Concept 27). Place in: Assets/_Project/Scripts/UI/HudSkinSwitch.cs
    /// </summary>
    public class HudSkinSwitch : MonoBehaviour
    {
        [SerializeField] bool useConceptSkin = true;
        [SerializeField] GameObject conceptHud;
        [SerializeField] GameObject legacyHud;

        public bool UseConceptSkin
        {
            get => useConceptSkin;
            set { useConceptSkin = value; Apply(); }
        }

        void Awake() => Apply();

        public void Apply()
        {
            if (conceptHud && conceptHud.activeSelf != useConceptSkin) conceptHud.SetActive(useConceptSkin);
            if (legacyHud && legacyHud.activeSelf == useConceptSkin) legacyHud.SetActive(!useConceptSkin);
        }

#if UNITY_EDITOR
        void OnValidate()
        {
            // SetActive isn't allowed inside OnValidate: do it right after.
            UnityEditor.EditorApplication.delayCall += () => { if (this) Apply(); };
        }
#endif
    }
}
