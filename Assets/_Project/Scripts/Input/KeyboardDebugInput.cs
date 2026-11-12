using UnityEngine;
using UnityEngine.InputSystem;
using KungFuCrashout.Fighters;

namespace KungFuCrashout.Controls
{
    /// <summary>
    /// Keyboard controls for testing, so you can test block + attack at the same time (a mouse can only simulate one
    /// finger). Works in the editor, and in Development Builds (e.g. a Windows test build) when Work In Development
    /// Builds is on. Release builds (the phone build you ship) never read the keyboard.
    ///
    ///   A / ←  dash left        D / →  dash right
    ///   W / ↑  jump up          Q / E  jump up-left / up-right (45°)
    ///   J      attack           K      heavy attack
    ///   Space  hold to block (tap for parry)
    ///   U      side special (Elbow Slide, the way you face)   I  up special (Uppercut)   O  down special (Leg Sweep)
    ///   Items (ItemTapHandler): F pick up / drop, hold G + direction keys + release G = aimed throw.
    ///   While the throw key is held, the direction keys aim instead of dashing / jumping.
    ///
    /// Place in: Assets/_Project/Scripts/Input/KeyboardDebugInput.cs
    /// </summary>
    public class KeyboardDebugInput : MonoBehaviour
    {
        [SerializeField] Fighter fighter;
        [Tooltip("Also work in Development Builds (Build Profiles → Development Build). Off = editor only. " +
                 "Release builds never read the keyboard.")]
        [SerializeField] bool workInDevelopmentBuilds = true;
        [Tooltip("Optional: the item handler. While its throw key is held, the direction keys aim the throw instead " +
                 "of moving. Empty = found automatically.")]
        [SerializeField] KungFuCrashout.Props.ItemTapHandler itemKeys;

        IFighterCommands commands;
        bool blockHeld;

        void Awake()
        {
            if (!Application.isEditor && !(workInDevelopmentBuilds && Debug.isDebugBuild)) { enabled = false; return; }
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

            if (kb.jKey.wasPressedThisFrame) commands.Attack();
            if (kb.kKey.wasPressedThisFrame) commands.HeavyAttack();

            if (kb.uKey.wasPressedThisFrame) commands.Special(SpecialMove.Side, fighter ? fighter.Facing : 1);
            if (kb.iKey.wasPressedThisFrame) commands.Special(SpecialMove.Up, 0);
            if (kb.oKey.wasPressedThisFrame) commands.Special(SpecialMove.Down, 0);

            if (kb.spaceKey.wasPressedThisFrame) { commands.BlockStart(); blockHeld = true; }
            if (kb.spaceKey.wasReleasedThisFrame && blockHeld) { commands.BlockEnd(); blockHeld = false; }
        }

        // Switched off while the pause menu is open: never leave the fighter stuck blocking if Space is let go meanwhile.
        void OnDisable()
        {
            if (blockHeld && commands != null) commands.BlockEnd();
            blockHeld = false;
        }
    }
}