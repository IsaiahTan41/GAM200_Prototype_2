using UnityEngine;

namespace KungFuCrashout.Fighters
{
    /// <summary>
    /// Everything a fighter can be told to do.
    /// The player's touch input and the AI (M4) both talk to fighters ONLY through this,
    /// so every combat feature automatically works for both.
    /// Place in: Assets/_Project/Scripts/Fighters/IFighterCommands.cs
    /// </summary>
    /// <summary>The three directional special moves: swipe direction + tap.</summary>
    public enum SpecialMove { Side, Up, Down }

    public interface IFighterCommands
    {
        /// <summary>Quick dash. direction: -1 = left, +1 = right (world space).</summary>
        void Dash(int direction);

        /// <summary>Straight jump along the fighter's set vertical vector (swipe up).</summary>
        void JumpUp();

        /// <summary>Diagonal jump along a direction in world space, e.g. the swipe's direction (swipe up-left / up-right).</summary>
        void JumpToward(Vector2 direction);

        /// <summary>Basic attack (tap right side). Repeated taps chain the 3-hit combo.</summary>
        void Attack();

        /// <summary>Alternate / heavy attack (hold right side).</summary>
        void HeavyAttack();

        /// <summary>Start blocking (press left side). The first moments of a block are the parry window.</summary>
        void BlockStart();

        /// <summary>Stop blocking (release left side).</summary>
        void BlockEnd();

        /// <summary>
        /// Directional special (swipe + tap): Side = gap closer, Up = launcher, Down = area sweep.
        /// direction: -1 / +1 = which way a Side special goes (0 = the way the fighter faces).
        /// Returns false if it couldn't start now (the caller may fall back to a normal attack).
        /// </summary>
        bool Special(SpecialMove move, int direction);
    }
}