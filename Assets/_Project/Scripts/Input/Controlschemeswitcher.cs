using UnityEngine;
using UnityEngine.InputSystem;

namespace KungFuCrashout.Controls
{
    /// <summary>
    /// PROTOTYPE switch between touch control schemes, for testing. Keeps exactly one scheme active:
    ///   Split Zones  = TouchInputHandler (left half block/parry, right half attack) — the original
    ///   Tap Anywhere = TapAnywhereInputHandler (tap = attack, swipe down / double press = block)
    ///
    /// Change the Scheme dropdown in the Inspector — it also switches live while playing — or press
    /// the Toggle Key (Tab) in the editor, or hook a UI Button to Next() for testing on the phone.
    /// KeyboardDebugInput is not affected.
    ///
    /// Setup: put this on the PlayerInput object next to both handlers and assign them.
    /// Place in: Assets/_Project/Scripts/Input/ControlSchemeSwitcher.cs
    /// </summary>
    [DefaultExecutionOrder(-50)] // decides which handler is on before they start reading touches
    public class ControlSchemeSwitcher : MonoBehaviour
    {
        public enum Scheme { SplitZones, TapAnywhere }

        [SerializeField] Scheme scheme = Scheme.SplitZones;

        [Header("Handlers (both on this object)")]
        [SerializeField] TouchInputHandler splitZones;
        [SerializeField] TapAnywhereInputHandler tapAnywhere;

        [Header("Testing")]
        [Tooltip("Editor keyboard key that swaps schemes while playing. None = off.")]
        [SerializeField] Key toggleKey = Key.Tab;
        [Tooltip("Shows the active scheme in the top-left corner.")]
        [SerializeField] bool showSchemeLabel = true;

        public Scheme Current => scheme;

        bool applyPending;
        GUIStyle style;

        void Reset()
        {
            splitZones = GetComponent<TouchInputHandler>();
            tapAnywhere = GetComponent<TapAnywhereInputHandler>();
        }

        void Awake() => Apply();

        // Changing the dropdown during Play applies on the next frame
        // (Unity doesn't allow enabling components from inside OnValidate).
        void OnValidate()
        {
            if (Application.isPlaying) applyPending = true;
        }

        void Update()
        {
            if (applyPending) { applyPending = false; Apply(); }

            var kb = Keyboard.current;
            if (kb != null && toggleKey != Key.None && kb[toggleKey].wasPressedThisFrame) Next();
        }

        /// <summary>Switch to the other scheme. Hook a UI Button here to swap on the phone.</summary>
        public void Next() => SetScheme(scheme == Scheme.SplitZones ? Scheme.TapAnywhere : Scheme.SplitZones);

        public void SetScheme(Scheme next)
        {
            scheme = next;
            Apply();
        }

        void Apply()
        {
            // Turn the old one off before the new one on, so their touch setup doesn't overlap.
            bool tap = scheme == Scheme.TapAnywhere;
            if (tap)
            {
                if (splitZones) splitZones.enabled = false;
                if (tapAnywhere) tapAnywhere.enabled = true;
            }
            else
            {
                if (tapAnywhere) tapAnywhere.enabled = false;
                if (splitZones) splitZones.enabled = true;
            }
            Debug.Log($"[Controls] Scheme: {scheme}");
        }

        void OnGUI()
        {
            if (!showSchemeLabel) return;
            if (style == null)
            {
                style = new GUIStyle(GUI.skin.label) { fontSize = Mathf.RoundToInt(Mathf.Min(Screen.width, Screen.height) * 0.028f) };
                style.normal.textColor = Color.yellow;
            }
            GUI.Label(new Rect(20, 10, 600, style.fontSize * 1.6f), $"Controls: {scheme}", style);
        }
    }
}