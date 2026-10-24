using UnityEngine;
using UnityEngine.UI;
using KungFuCrashout.Fighters;

namespace KungFuCrashout.UI
{
    /// <summary>
    /// Drives a uGUI Slider as a fighter's health bar and colours its Fill by state:
    ///
    ///   Normal     — the Fill colour set in the scene (green)
    ///   STAGGERED  — flashes between Staggered Colour and Staggered Flash Colour while the fighter is Downed
    ///                (vulnerable after an item hit — the only time melee takes health)
    ///   Launched   — (optional) Staggered Colour, steady, while flying from an item hit, just before the stagger
    ///   Invincible — Normal colour, faded, during the get-up invincibility
    ///   KO         — grey
    ///
    /// The bar slides smoothly to the new value; the slider is made non-interactive so a thumb can't drag it.
    ///
    /// Setup: add to each health Slider (or use KungFu Crashout → Hook Up Health Bars), assign Fighter.
    /// Left empty, it looks for a Fighter by GameObject name: "Enemy…" sliders → "Opponent", others → "Player".
    /// Place in: Assets/_Project/Scripts/UI/FighterHealthBar.cs
    /// </summary>
    [RequireComponent(typeof(Slider))]
    public class FighterHealthBar : MonoBehaviour
    {
        [SerializeField] Fighter fighter;
        [Tooltip("The bar's fill image. Empty = the Slider's Fill Rect.")]
        [SerializeField] Image fill;

        [Header("Value")]
        [Tooltip("How quickly the bar slides to the new health (per second, fraction of the bar). 0 = jump instantly.")]
        [SerializeField, Min(0f)] float slideSpeed = 2.5f;

        [Header("Colours")]
        [Tooltip("Normal fill colour. Empty alpha (0) = keep the colour the Fill has in the scene.")]
        [SerializeField] Color normalColour = new Color(0f, 0f, 0f, 0f);
        [SerializeField] Color staggeredColour = new Color(1f, 0.72f, 0f);      // amber
        [SerializeField] Color staggeredFlashColour = Color.white;
        [Tooltip("Flashes per second while staggered.")]
        [SerializeField, Min(0f)] float staggeredFlashRate = 8f;
        [Tooltip("Also show the staggered colour (steady) while the fighter is flying from an item hit.")]
        [SerializeField] bool showWhileLaunched = true;
        [SerializeField] Color koColour = new Color(0.35f, 0.35f, 0.35f);
        [Tooltip("Fill alpha during the get-up invincibility.")]
        [SerializeField, Range(0f, 1f)] float invincibleAlpha = 0.45f;

        Slider slider;
        Health health;
        float shown = 1f;

        /// <summary>True while the bar is showing the staggered colours (for other UI).</summary>
        public bool ShowingStaggered { get; private set; }

        void Awake()
        {
            slider = GetComponent<Slider>();
            if (!fill && slider.fillRect) fill = slider.fillRect.GetComponent<Image>();
            if (fill && normalColour.a <= 0f) normalColour = fill.color;

            // A display, not a control.
            slider.interactable = false;
            slider.transition = Selectable.Transition.None;
            slider.minValue = 0f;
            slider.maxValue = 1f;
            slider.wholeNumbers = false;
        }

        void Start()
        {
            if (!fighter) fighter = FindFighterByName();
            health = fighter ? fighter.Health : null;
            if (!fighter) Debug.LogWarning($"[{name}] FighterHealthBar: no Fighter assigned or found.", this);

            shown = health ? health.Normalized : 1f;
            slider.value = shown;
        }

        void Update()
        {
            if (!health) return;

            // Value: slide toward the real health (real time, so it keeps moving during hitstop).
            float target = Mathf.Clamp01(health.Normalized);
            shown = slideSpeed > 0f ? Mathf.MoveTowards(shown, target, slideSpeed * Time.unscaledDeltaTime) : target;
            slider.value = shown;

            if (fill) fill.color = CurrentColour();
        }

        Color CurrentColour()
        {
            ShowingStaggered = false;

            if (fighter.State == FighterState.KO) return koColour;

            if (fighter.IsDowned)
            {
                ShowingStaggered = true;
                bool flash = staggeredFlashRate > 0f && Mathf.Repeat(Time.unscaledTime * staggeredFlashRate, 1f) >= 0.5f;
                return flash ? staggeredFlashColour : staggeredColour;
            }

            if (showWhileLaunched && fighter.IsLaunched)
            {
                ShowingStaggered = true;
                return staggeredColour;
            }

            if (fighter.IsInvulnerable)
            {
                var c = normalColour;
                c.a *= invincibleAlpha;
                return c;
            }

            return normalColour;
        }

        /// <summary>Fallback when Fighter is empty: "Enemy…" bars follow "Opponent", others follow "Player".</summary>
        Fighter FindFighterByName()
        {
            string wanted = name.ToLowerInvariant().Contains("enemy") ? "opponent" : "player";
            foreach (var f in FindObjectsByType<Fighter>(FindObjectsSortMode.None))
                if (f.name.ToLowerInvariant().Contains(wanted)) return f;
            return null;
        }
    }
}
