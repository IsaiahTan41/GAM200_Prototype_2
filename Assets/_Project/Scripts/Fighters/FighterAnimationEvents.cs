using System;
using UnityEngine;

namespace KungFuCrashout.Fighters
{
    /// <summary>
    /// Receives the Animation Events placed on attack clips (e.g. Monk_Attack) and re-broadcasts them as C# events.
    /// Put it on the same GameObject as the Animator (the fighter's "Visual" child): Unity only delivers Animation Events
    /// to components on the Animator's own GameObject, and logs "has no receiver" errors if nothing there has the method.
    ///   HitboxOn   — first active frame (the hit can land from here)
    ///   HitboxOff  — last active frame (recovery starts)
    ///   CancelOpen — from here the move can be cancelled (specials / combo follow-up)
    ///   AttackEnd  — last recovery frame (back to Idle)
    ///   Travel(float) — optional: move forward this many units on this frame
    /// Nothing listens yet: Fighter keeps its own timing until the frame-driven timing step is added.
    /// Place in: Assets/_Project/Scripts/Fighters/FighterAnimationEvents.cs
    /// </summary>
    public class FighterAnimationEvents : MonoBehaviour
    {
        [Tooltip("Log each event to the Console (for checking frame timing).")]
        [SerializeField] bool logEvents = false;

        public event Action HitboxOnReached;
        public event Action HitboxOffReached;
        public event Action CancelOpened;
        public event Action AttackEnded;
        public event Action<float> TravelRequested;

        // Called by Animation Events (names must match the clip's Function names exactly).
        public void HitboxOn()   { Log(nameof(HitboxOn));   HitboxOnReached?.Invoke(); }
        public void HitboxOff()  { Log(nameof(HitboxOff));  HitboxOffReached?.Invoke(); }
        public void CancelOpen() { Log(nameof(CancelOpen)); CancelOpened?.Invoke(); }
        public void AttackEnd()  { Log(nameof(AttackEnd));  AttackEnded?.Invoke(); }
        public void Travel(float distance) { Log($"Travel {distance}"); TravelRequested?.Invoke(distance); }

        void Log(string what)
        {
            if (logEvents) Debug.Log($"[{transform.root.name}] anim event: {what} (t={Time.time:0.000})");
        }
    }
}
