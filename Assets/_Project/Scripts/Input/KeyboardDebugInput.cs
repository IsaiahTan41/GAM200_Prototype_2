using UnityEngine;
using UnityEngine.InputSystem;
using KungFuCrashout.Fighters;

namespace KungFuCrashout.Controls
{
    /// <summary>
    /// Editor-only keyboard controls, so you can test block + attack at the same time
    /// (a mouse can only simulate one finger). Switches itself off on the phone.
    ///
    ///   A / ←  dash left        D / →  dash right
    ///   W / ↑  jump up          Q / E  jump up-left / up-right (45°)
    ///   J      attack           K      heavy attack
    ///   Space  hold to block (tap for parry)
    ///   U      side special (Elbow Slide, the way you face)   I  up special (Uppercut)   O  down special (Leg Sweep)
    ///
    /// Place in: Assets/_Project/Scripts/Input/KeyboardDebugInput.cs
    /// </summary>
    public class KeyboardDebugInput : MonoBehaviour
    {
        [SerializeField] Fighter fighter;

        IFighterCommands commands;

        void Awake()
        {
            if (!Application.isEditor) { enabled = false; return; }
            commands = fighter;
        }

        void Update()
        {
            var kb = Keyboard.current;
            if (kb == null || commands == null) return;

            if (kb.aKey.wasPressedThisFrame || kb.leftArrowKey.wasPressedThisFrame) commands.Dash(-1);
            if (kb.dKey.wasPressedThisFrame || kb.rightArrowKey.wasPressedThisFrame) commands.Dash(1);

            if (kb.wKey.wasPressedThisFrame || kb.upArrowKey.wasPressedThisFrame) commands.JumpUp();
            if (kb.qKey.wasPressedThisFrame) commands.JumpToward(new Vector2(-1f, 1f));
            if (kb.eKey.wasPressedThisFrame) commands.JumpToward(new Vector2(1f, 1f));

            if (kb.jKey.wasPressedThisFrame) commands.Attack();
            if (kb.kKey.wasPressedThisFrame) commands.HeavyAttack();

            if (kb.uKey.wasPressedThisFrame) commands.Special(SpecialMove.Side, fighter ? fighter.Facing : 1);
            if (kb.iKey.wasPressedThisFrame) commands.Special(SpecialMove.Up, 0);
            if (kb.oKey.wasPressedThisFrame) commands.Special(SpecialMove.Down, 0);

            if (kb.spaceKey.wasPressedThisFrame) commands.BlockStart();
            if (kb.spaceKey.wasReleasedThisFrame) commands.BlockEnd();
        }
    }
}