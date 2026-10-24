using System.Collections.Generic;

namespace KungFuCrashout.Controls
{
    /// <summary>
    /// Lets one input script take ownership of a finger so the others ignore it.
    /// ItemTapHandler claims a touch that lands on a stage item; TouchInputHandler and
    /// TapAnywhereInputHandler then treat that finger as Ignored, so tapping an item
    /// doesn't also throw an attack or start a block.
    /// No GameObject needed; it's a static class.
    /// Place in: Assets/_Project/Scripts/Input/TouchClaims.cs
    /// </summary>
    public static class TouchClaims
    {
        static readonly HashSet<int> claimed = new HashSet<int>();

        public static void Claim(int touchId) => claimed.Add(touchId);
        public static bool IsClaimed(int touchId) => claimed.Contains(touchId);
        public static void Release(int touchId) => claimed.Remove(touchId);
        public static void Clear() => claimed.Clear();
    }
}