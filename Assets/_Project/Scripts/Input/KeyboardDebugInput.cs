using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.EnhancedTouch;
using UnityEngine.EventSystems;
using UnityEngine.Serialization;
using KungFuCrashout.Fighters;

namespace KungFuCrashout.Controls
{
    /// <summary>
    /// Keyboard controls for testing, so you can test block + attack at the same time (a mouse can only simulate one
    /// finger). Works in the editor, and in any Windows / Mac / Linux build (release or Development) when Work In PC
    /// Builds is on. Phones never read the keyboard, Development Build or not (PcInput).
    ///
    ///   A / ←  dash left        D / →  dash right
    ///   W / ↑  jump up          Q / E  jump up-left / up-right (45°)
    ///   Left click  attack      Right click  heavy attack   (Mouse Buttons Attack; clicks on UI buttons never attack)
    ///   Space  hold to block (tap for parry)
    ///   U      side special (Elbow Slide, the way you face)   I  up special (Uppercut)   O  down special (Leg Sweep)
    ///   Items (ItemTapHandler): F pick up / drop, hold G + direction keys + release G = aimed throw.
    ///   While the throw key is held, the direction keys aim instead of dashing / jumping.
    ///
    /// Place in: Assets/_Project/Scripts/Input/KeyboardDebugInput.cs
    /// </summary>
    [DefaultExecutionOrder(-100)] // before the touch handlers start, so PcInput.MouseButtonsFight is set when they read it
    public class KeyboardDebugInput : MonoBehaviour
    {
        [SerializeField] Fighter fighter;
        [Tooltip("Also work in Windows / Mac / Linux builds (release or Development). Off = editor only. " +
                 "Phones never read the keyboard.")]
        [FormerlySerializedAs("workInDevelopmentBuilds")]
        [SerializeField] bool workInPcBuilds = true;
        [Tooltip("Optional: the item handler. While its throw key is held, the direction keys aim the throw instead " +
                 "of moving. Empty = found automatically.")]
        [SerializeField] KungFuCrashout.Props.ItemTapHandler itemKeys;
        [Tooltip("Left mouse button = attack, right mouse button = heavy attack. In PC builds the mouse then stops acting " +
                 "as a finger (no double attack from a click). In the editor, while the mouse is simulating a finger, a left " +
                 "click is already a tap (attack), so only the right button is added there.")]
        [SerializeField] bool mouseButtonsAttack = true;

        IFighterCommands commands;
        bool blockHeld;

        void Awake()
        {
            if (!PcInput.KeyboardAllowed(workInPcBuilds)) { enabled = false; return; }
            PcInput.MouseButtonsFight = mouseButtonsAttack;
            commands = fighter;
            if (!itemKeys) itemKeys = FindFirstObjectByType<KungFuCrashout.Props.ItemTapHandler>();
        }

        void Update()
        {
            var kb = Keyboard.current;
            if (kb == null || commands == null) return;

            // Aiming a throw (throw key held): the direction keys belong to the throw, not to movement.
            bool aiming = itemKeys && itemKeys.ThrowKey != Key.None && kb[itemKeys.ThrowKey].isPressed;

            if (!aiming && (kb.aKey.wasPressedThisFrame || kb.leftArrowKey.wasPressedThisFrame)) commands.Dash(-1);
            if (!aiming && (kb.dKey.wasPressedThisFrame || kb.rightArrowKey.wasPressedThisFrame)) commands.Dash(1);

            if (!aiming && (kb.wKey.wasPressedThisFrame || kb.upArrowKey.wasPressedThisFrame)) commands.JumpUp();
            if (!aiming && kb.qKey.wasPressedThisFrame) commands.JumpToward(new Vector2(-1f, 1f));
            if (!aiming && kb.eKey.wasPressedThisFrame) commands.JumpToward(new Vector2(1f, 1f));

            var mouse = Mouse.current;
            if (mouseButtonsAttack && mouse != null && !PointerOnUi())
            {
                // Editor with the mouse simulating a finger: the left click is already a tap → attack, don't add a second.
                bool leftIsFinger = TouchSimulation.instance != null && TouchSimulation.instance.isActiveAndEnabled;
                if (!leftIsFinger && mouse.leftButton.wasPressedThisFrame) commands.Attack();
                if (mouse.rightButton.wasPressedThisFrame) commands.HeavyAttack();
            }

            if (kb.uKey.wasPressedThisFrame) commands.Special(SpecialMove.Side, fighter ? fighter.Facing : 1);
            if (kb.iKey.wasPressedThisFrame) commands.Special(SpecialMove.Up, 0);
            if (kb.oKey.wasPressedThisFrame) commands.Special(SpecialMove.Down, 0);

            if (kb.spaceKey.wasPressedThisFrame) { commands.BlockStart(); blockHeld = true; }
            if (kb.spaceKey.wasReleasedThisFrame && blockHeld) { commands.BlockEnd(); blockHeld = false; }
        }

        /// <summary>A click on a UI button (pause diamond, menus) is for the UI, never an attack.</summary>
        static bool PointerOnUi() => EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();

        void OnDestroy() { PcInput.MouseButtonsFight = false; }

        // Switched off while the pause menu is open: never leave the fighter stuck blocking if Space is let go meanwhile.
        void OnDisable()
        {
            if (blockHeld && commands != null) commands.BlockEnd();
            blockHeld = false;
        }
    }
}