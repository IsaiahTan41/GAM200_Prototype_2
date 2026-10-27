using UnityEngine;
using UnityEngine.UI;
using KungFuCrashout.Fighters;

namespace KungFuCrashout.UI
{
    /// <summary>
    /// A small stamina bar that FOLLOWS a fighter, just beneath their feet (a uGUI Slider on a Screen Space Overlay
    /// canvas, positioned from the camera every frame). Colours:
    ///   Normal     — Normal Colour (cyan)
    ///   Low        — Low Colour below Low Fraction (a guard break is near)
    ///   EXHAUSTED  — flashes Exhausted / Exhausted Flash colours until it has refilled enough to block again
    /// Stamina itself refills gradually on its own (Stamina → Regen Per Second after Regen Delay); this only shows it.
    ///
    /// Setup: KungFu Crashout → Add Stamina Bars (builds one per fighter with a Stamina component).
    /// Place in: Assets/_Project/Scripts/UI/FighterStaminaBar.cs
    /// </summary>
    [RequireComponent(typeof(Slider))]
    [DefaultExecutionOrder(200)] // after the camera (framing, Cinemachine, shake) has moved this frame
    public class FighterStaminaBar : MonoBehaviour
    {
        [SerializeField] Fighter fighter;
        [Tooltip("Camera that films the fight. Empty = Main Camera.")]
        [SerializeField] Camera cam;

        [Header("Position")]
        [Tooltip("Follow the fighter on screen. Off = stays where it is placed in the Canvas.")]
        [SerializeField] bool followFighter = true;
        [Tooltip("Where the bar sits relative to the fighter's centre (world units). Negative Y = below.")]
        [SerializeField] Vector2 worldOffset = new Vector2(0f, -1.25f);

        [Header("Value")]
        [Tooltip("How quickly the bar slides to the new value (fraction of the bar per second). 0 = instant.")]
        [SerializeField, Min(0f)] float slideSpeed = 4f;
        [Tooltip("Hide the bar while stamina is full.")]
        [SerializeField] bool hideWhenFull = false;

        [Header("Colours")]
        [SerializeField] Color normalColour = new Color(0.3f, 0.85f, 1f);
        [SerializeField] Color lowColour = new Color(1f, 0.6f, 0.1f);
        [Tooltip("Below this fraction the bar shows Low Colour.")]
        [SerializeField, Range(0f, 1f)] float lowFraction = 0.3f;
        [SerializeField] Color exhaustedColour = new Color(1f, 0.2f, 0.2f);
        [SerializeField] Color exhaustedFlashColour = Color.white;
        [SerializeField, Min(0f)] float exhaustedFlashRate = 8f;

        Slider slider;
        Image fill;
        RectTransform rect;
        Graphic[] graphics;
        Stamina stamina;
        float shown = 1f;

        void Awake()
        {
            slider = GetComponent<Slider>();
            rect = (RectTransform)transform;
            if (slider.fillRect) fill = slider.fillRect.GetComponent<Image>();
            graphics = GetComponentsInChildren<Graphic>(true);

            slider.interactable = false;
            slider.transition = Selectable.Transition.None;
            slider.minValue = 0f;
            slider.maxValue = 1f;
            slider.wholeNumbers = false;
            foreach (var g in graphics) g.raycastTarget = false; // never blocks touches
        }

        void Start()
        {
            stamina = fighter ? fighter.Stamina : null;
            if (!stamina)
            {
                Debug.LogWarning($"[{name}] FighterStaminaBar: {(fighter ? fighter.name + " has no Stamina component" : "no Fighter assigned")}.", this);
                gameObject.SetActive(false);
                return;
            }
            shown = stamina.Normalized;
            slider.value = shown;
        }

        void LateUpdate()
        {
            if (!stamina) return;

            // Value
            float target = stamina.Normalized;
            shown = slideSpeed > 0f ? Mathf.MoveTowards(shown, target, slideSpeed * Time.unscaledDeltaTime) : target;
            slider.value = shown;

            // Colour
            if (fill)
            {
                if (stamina.IsExhausted)
                {
                    bool flash = exhaustedFlashRate > 0f && Mathf.Repeat(Time.unscaledTime * exhaustedFlashRate, 1f) >= 0.5f;
                    fill.color = flash ? exhaustedFlashColour : exhaustedColour;
                }
                else fill.color = target < lowFraction ? lowColour : normalColour;
            }

            bool visible = !(hideWhenFull && target >= 0.999f && shown >= 0.999f) && fighter.State != FighterState.KO;
            foreach (var g in graphics) if (g) g.enabled = visible;

            // Position: under the fighter on screen.
            if (followFighter)
            {
                if (!cam) cam = Camera.main;
                if (cam)
                {
                    Vector3 world = fighter.transform.position + (Vector3)worldOffset;
                    rect.position = cam.WorldToScreenPoint(world);
                }
            }
        }
    }
}
