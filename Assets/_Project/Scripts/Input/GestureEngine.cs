using System;
using System.Collections.Generic;
using UnityEngine;
using KungFuCrashout.Fighters;

namespace KungFuCrashout.Controls
{
    /// <summary>Phase of a raw touch sample (mirrors the Input System's TouchPhase, so logs replay without it).</summary>
    public enum RawPhase { Began, Moved, Stationary, Ended, Canceled }

    /// <summary>One finger's sample for one frame, as the finger tracker hands it to the engine (live or from a log).</summary>
    public struct RawTouch
    {
        public int id;
        public RawPhase phase;
        public Vector2 position;       // screen pixels
        public Vector2 startPosition;  // where the finger landed
        public double startTime;       // when the finger landed (Input System clock, seconds)
    }

    /// <summary>Every tunable number of the gesture engine. Shown in the Inspector of GestureArbiterInput.</summary>
    [Serializable]
    public class GestureSettings
    {
        [Header("Arbiter")]
        [Tooltip("When two gestures could still conflict (two fingers together, a tap right after a swipe), wait this long " +
                 "(seconds) for the fingers to show which it is before committing. ~4 frames.")]
        [Range(0f, 0.2f)] public float arbitrationWait = 0.07f;

        [Header("Two-finger block (GDD)")]
        [Tooltip("Two fingers pressed together and held = block. Landing them just before a hit = parry / item catch.")]
        public bool twoFingerBlock = true;
        [Tooltip("The second finger must land within this many seconds of the first (measured from when each finger " +
                 "actually touched, not the frame that noticed it). Both fingers must still be still.")]
        [Range(0.05f, 0.3f)] public float pairWindow = 0.16f;
        [Tooltip("Once the block is confirmed, a finger must move this many times Swipe Min Distance (sideways or up) " +
                 "before it leaves the block and dashes / jumps, so thumb roll doesn't drop the block.")]
        [Range(1f, 6f)] public float blockDriftMultiplier = 3f;

        [Header("Swipes (physical size)")]
        [Tooltip("A finger must move at least this far (millimetres on the glass) to be a swipe.")]
        [Range(1f, 15f)] public float swipeMinMm = 4f;
        [Tooltip("...and move at least this fast (mm per second, averaged since it landed), so slow drift isn't a swipe.")]
        [Range(0f, 300f)] public float swipeMinSpeed = 50f;
        [Tooltip("A move this long is a swipe whatever its speed.")]
        [Range(2f, 30f)] public float swipeSureMm = 10f;
        [Tooltip("Swipes flatter than this (degrees from horizontal) are dashes.")]
        [Range(5f, 60f)] public float diagonalMinAngle = 25f;
        [Tooltip("Upward swipes steeper than this are straight jumps; between the two angles they are diagonal jumps.")]
        [Range(30f, 89f)] public float verticalMinAngle = 65f;
        [Tooltip("After a swipe, the same finger can swipe again once it stops or turns back (dash → dash without lifting).")]
        public bool chainSwipes = true;

        [Header("Taps and holds")]
        [Tooltip("Holding one finger still does the heavy attack.")]
        public bool holdForHeavy = true;
        [Tooltip("Seconds a finger must stay still to count as a hold (longer than Pair Window, so a late second finger " +
                 "still makes a block).")]
        [Range(0.15f, 0.6f)] public float holdTime = 0.3f;

        [Header("Specials (swipe + tap)")]
        public bool directionalSpecials = true;
        [Tooltip("A tap that lands within this many seconds after another finger's swipe = that swipe's special.")]
        [Range(0.05f, 0.4f)] public float chordWindow = 0.2f;
        [Tooltip("Also count a tap as a special while the dash / jump from the swipe is still moving, even after the " +
                 "Chord Window. OFF (recommended): dash then tap = dash then jab, and alternate-thumb dashes work.")]
        public bool tapDuringMotion = false;

        [Header("Screen")]
        [Tooltip("Touches that START above this height (fraction of screen, from the bottom) are ignored (HUD band).")]
        [Range(0.2f, 1f)] public float inputZoneTop = 0.85f;
        [Tooltip("Used when the device doesn't report its dpi (Screen.dpi = 0).")]
        [Min(50f)] public float fallbackDpi = 160f;
    }

    /// <summary>
    /// The gesture pipeline, as plain C# so it can run live (GestureArbiterInput) or on a recorded touch log in the
    /// editor (Replay Touch Log). Three stages:
    ///   1. Finger tracker   — one record per finger: start time / position, distance (mm), average speed.
    ///   2. Recogniser       — labels each finger Undecided / Swiping / Holding; commits nothing on its own when a
    ///                         rival gesture is still possible.
    ///   3. Arbiter          — the only place that decides between fingers, by priority:
    ///                         two-finger block → swipe + tap special → swipe movement → hold (heavy) → tap (light).
    ///      • Two still fingers landing within Pair Window → BlockStart at once (parries stay tight). If either finger
    ///        swipes within Arbitration Wait, the block is cancelled and it becomes swipe + tap = special.
    ///      • A finger landing within Chord Window after another finger's swipe waits up to Arbitration Wait: lift or
    ///        stay still = special; swipe = another movement (alternate-thumb dashes).
    ///      • A lone finger: swipe = movement at once; lift before Hold Time = light attack; still for Hold Time = heavy.
    /// Commands go to IFighterCommands; the fighter's move / special buffers cover the short wait.
    /// Place in: Assets/_Project/Scripts/Input/GestureEngine.cs
    /// </summary>
    public class GestureEngine
    {
        enum State { Undecided, Swiping, Holding, Blocking, Consumed, Ignored }

        class Finger
        {
            public int id;
            public State state;
            public Vector2 startPos, pos, last;
            public double startTime;
            public float distMm, speed;      // from startPos; mm / s averaged since startTime
            public Vector2 swipeDir;
            public bool chained;              // re-armed after a swipe: never a tap or hold
            public bool special;              // landed in a chord window: a special candidate
            public SpecialMove specialMove;
            public int specialDir;
            public double decideAt;
        }

        public GestureSettings settings;
        public IFighterCommands commands;
        /// <summary>Optional: is the movement from the last swipe still going? (Tap During Motion.)</summary>
        public Func<SpecialMove, bool> motionGoing;
        /// <summary>Optional: has another script (item taps) claimed this finger?</summary>
        public Func<int, bool> isClaimed;
        /// <summary>Called with every recognised gesture (debug overlay, logs, replay reports).</summary>
        public Action<string> report;

        public float dpi = 160f;
        public float screenHeight = 1080f;

        readonly Dictionary<int, Finger> fingers = new Dictionary<int, Finger>();
        readonly List<int> gone = new List<int>();
        readonly HashSet<int> seen = new HashSet<int>();

        // two-finger block
        bool pairActive, pairConfirmed;
        int pairA = -1, pairB = -1;
        double pairConfirmAt;

        // last swipe, for swipe + tap specials
        double lastSwipeTime = -1;
        int lastSwipeFinger = -1;
        SpecialMove lastSwipeMove;
        int lastSwipeDir;

        public GestureEngine(GestureSettings settings, IFighterCommands commands)
        {
            this.settings = settings;
            this.commands = commands;
        }

        public bool BlockHeld => pairActive;

        /// <summary>Finger states for the debug overlay: id → (screen position, short label).</summary>
        public IEnumerable<(Vector2 pos, string label)> DebugFingers()
        {
            foreach (var f in fingers.Values)
            {
                string label = f.state switch
                {
                    State.Undecided => f.special ? "special?" : f.chained ? "re-armed" : "?",
                    State.Swiping => "swipe",
                    State.Holding => "hold",
                    State.Blocking => pairConfirmed ? "block" : "block?",
                    State.Consumed => "used",
                    _ => "ignored"
                };
                yield return (f.pos, label);
            }
        }

        float Mm(float px) => px / Mathf.Max(1f, dpi) * 25.4f;

        // ───────────── 1. Finger tracker ─────────────

        /// <summary>Feed one frame: every active touch, and the current time on the same clock as startTime.</summary>
        public void Process(List<RawTouch> touches, double now)
        {
            seen.Clear();
            foreach (var t in touches)
            {
                seen.Add(t.id);
                if (!fingers.TryGetValue(t.id, out var f))
                {
                    f = new Finger { id = t.id, startPos = t.startPosition, pos = t.position, last = t.position, startTime = t.startTime };
                    fingers[t.id] = f;
                    bool hud = t.startPosition.y > screenHeight * settings.inputZoneTop;
                    if (hud || (isClaimed != null && isClaimed(t.id))) f.state = State.Ignored;
                    else OnFingerDown(f, now);
                }

                if (f.state != State.Ignored && isClaimed != null && isClaimed(t.id)) Cancel(f, "Finger taken by an item gesture");

                f.pos = t.position;
                Measure(f, now);

                if (t.phase == RawPhase.Ended || t.phase == RawPhase.Canceled)
                {
                    Evaluate(f, now); // a very fast flick that ended this frame
                    OnFingerUp(f, now);
                    gone.Add(t.id);
                }
                else
                {
                    Evaluate(f, now);
                    f.last = t.position;
                }
            }
            // A finger that vanished without an Ended sample (a dropped frame, input paused) counts as lifted.
            foreach (var f in fingers.Values)
                if (!seen.Contains(f.id) && !gone.Contains(f.id)) { OnFingerUp(f, now); gone.Add(f.id); }
            foreach (var id in gone) fingers.Remove(id);
            gone.Clear();

            Tick(now);
        }

        /// <summary>Forget every finger (scheme switched off, menu opened). Releases a held block.</summary>
        public void Reset()
        {
            if (pairActive) commands?.BlockEnd();
            pairActive = pairConfirmed = false;
            pairA = pairB = -1;
            fingers.Clear();
            lastSwipeTime = -1;
        }

        void Measure(Finger f, double now)
        {
            f.distMm = Mm((f.pos - f.startPos).magnitude);
            double age = Math.Max(0.016, now - f.startTime);
            f.speed = (float)(f.distMm / age);
        }

        // ───────────── 2. Recogniser ─────────────

        bool LooksLikeSwipe(Finger f, float minMm) =>
            f.distMm >= minMm && (f.speed >= settings.swipeMinSpeed || f.distMm >= Mathf.Max(minMm, settings.swipeSureMm));

        void Evaluate(Finger f, double now)
        {
            switch (f.state)
            {
                case State.Blocking:
                    EvaluateBlockingFinger(f, now);
                    return;

                case State.Undecided:
                    if (LooksLikeSwipe(f, settings.swipeMinMm))
                    {
                        if (f.special) { f.special = false; Report("Second finger swiped → movement, not a special"); }
                        f.state = State.Swiping;
                        Swipe(f, now);
                    }
                    return;

                case State.Swiping:
                    if (settings.chainSwipes) TryRearm(f, now);
                    return;
            }
        }

        /// <summary>After a swipe, the same finger can swipe again once it stops or turns back.</summary>
        void TryRearm(Finger f, double now)
        {
            Vector2 frame = f.pos - f.last;
            bool stopped = Mm(frame.magnitude) < 0.15f;
            bool turnedBack = !stopped && Vector2.Dot(frame, f.swipeDir) < 0f;
            if (!stopped && !turnedBack) return;
            f.startPos = f.pos;
            f.startTime = now;
            f.distMm = 0f;
            f.speed = 0f;
            f.state = State.Undecided;
            f.chained = true;
        }

        // ───────────── 3. Arbiter ─────────────

        void OnFingerDown(Finger f, double now)
        {
            // Swipe + tap: a finger landing just after ANOTHER finger's swipe is a special candidate (decided shortly).
            bool chord = settings.directionalSpecials && lastSwipeTime >= 0 && lastSwipeFinger != f.id &&
                         (now - lastSwipeTime <= settings.chordWindow ||
                          (settings.tapDuringMotion && motionGoing != null && motionGoing(lastSwipeMove)));
            if (chord)
            {
                f.special = true;
                f.specialMove = lastSwipeMove;
                f.specialDir = lastSwipeDir;
                f.decideAt = now + settings.arbitrationWait;
                return;
            }

            // Two-finger block: another still, undecided finger landed within Pair Window.
            if (settings.twoFingerBlock && !pairActive)
            {
                foreach (var o in fingers.Values)
                {
                    if (o == f || o.state != State.Undecided || o.chained || o.special) continue;
                    if (o.distMm >= settings.swipeMinMm) continue;
                    if (Math.Abs(f.startTime - o.startTime) > settings.pairWindow) continue;
                    StartPair(o, f, now);
                    return;
                }
            }
        }

        void StartPair(Finger a, Finger b, double now)
        {
            a.state = b.state = State.Blocking;
            pairA = a.id;
            pairB = b.id;
            pairActive = true;
            pairConfirmed = false;
            pairConfirmAt = now + settings.arbitrationWait;
            commands.BlockStart(); // at once: the parry window starts on the second finger
            Report("Two fingers → Block (parry window)");
        }

        void EvaluateBlockingFinger(Finger f, double now)
        {
            if (!pairConfirmed)
            {
                // Inside the wait: a real swipe means it was swipe + tap, not a block.
                if (!LooksLikeSwipe(f, settings.swipeMinMm)) return;
                var other = OtherPairFinger(f);
                EndPair(null);
                f.state = State.Swiping;
                Swipe(f, now);
                if (other != null && other.state != State.Ignored)
                {
                    other.state = State.Consumed;
                    FireSpecial(lastSwipeMove, lastSwipeDir, "Block cancelled → swipe + tap special");
                }
                return;
            }

            // Confirmed block: small drift (and any downward slide) keeps blocking; a big sideways / up swipe leaves.
            if (!LooksLikeSwipe(f, settings.swipeMinMm * settings.blockDriftMultiplier)) return;
            if (Angle(f) < -settings.diagonalMinAngle) return;
            EndPair(f);
            f.state = State.Swiping;
            Swipe(f, now);
        }

        Finger OtherPairFinger(Finger f)
        {
            int otherId = f.id == pairA ? pairB : pairA;
            return fingers.TryGetValue(otherId, out var o) ? o : null;
        }

        /// <summary>End the two-finger block. The finger that isn't 'keep' is used up (lifting it later does nothing).</summary>
        void EndPair(Finger keep)
        {
            if (!pairActive) return;
            foreach (int id in new[] { pairA, pairB })
                if (fingers.TryGetValue(id, out var p) && p != keep && p.state == State.Blocking) p.state = State.Consumed;
            pairActive = pairConfirmed = false;
            pairA = pairB = -1;
            commands.BlockEnd();
        }

        void OnFingerUp(Finger f, double now)
        {
            switch (f.state)
            {
                case State.Blocking:
                    EndPair(null);
                    Report("Finger lifted → Block end");
                    return;

                case State.Undecided:
                    if (f.special)
                    {
                        f.state = State.Consumed;
                        FireSpecial(f.specialMove, f.specialDir, "Swipe + tap");
                        return;
                    }
                    if (f.chained) return; // a re-armed finger never taps
                    commands.Attack();
                    Report("Tap → Attack");
                    return;
            }
        }

        void Tick(double now)
        {
            if (pairActive && !pairConfirmed && now >= pairConfirmAt)
            {
                pairConfirmed = true;
                Report("Block confirmed");
            }

            foreach (var f in fingers.Values)
            {
                if (f.state != State.Undecided) continue;

                // A special candidate that stayed still through the wait: commit the special (held taps count too).
                if (f.special)
                {
                    if (now >= f.decideAt)
                    {
                        f.state = State.Consumed;
                        FireSpecial(f.specialMove, f.specialDir, "Swipe + tap (held)");
                    }
                    continue;
                }

                // Hold → heavy. Hold Time is longer than Pair Window, so a late second finger had its chance to pair.
                if (settings.holdForHeavy && !f.chained && now - f.startTime >= settings.holdTime)
                {
                    f.state = State.Holding;
                    commands.HeavyAttack();
                    Report("Hold → Heavy attack");
                }
            }
        }

        void Cancel(Finger f, string why)
        {
            if (f.state == State.Blocking) EndPair(null);
            f.state = State.Ignored;
            Report(why);
        }

        // ───────────── Commands ─────────────

        float Angle(Finger f)
        {
            Vector2 d = f.pos - f.startPos;
            return Mathf.Atan2(d.y, Mathf.Abs(d.x)) * Mathf.Rad2Deg; // 0 = sideways, +90 = up, -90 = down
        }

        void Swipe(Finger f, double now)
        {
            Vector2 delta = f.pos - f.startPos;
            float angle = Angle(f);
            f.swipeDir = delta.normalized;

            if (angle >= settings.verticalMinAngle)
            {
                commands.JumpUp();
                RecordSwipe(f, SpecialMove.Up, 0, now);
                Report("Swipe up → Jump");
            }
            else if (angle >= settings.diagonalMinAngle)
            {
                commands.JumpToward(delta.normalized);
                RecordSwipe(f, SpecialMove.Up, 0, now);
                Report(delta.x > 0f ? "Swipe up-right → Jump right" : "Swipe up-left → Jump left");
            }
            else if (angle > -settings.diagonalMinAngle)
            {
                int dir = delta.x > 0f ? 1 : -1;
                commands.Dash(dir);
                RecordSwipe(f, SpecialMove.Side, dir, now);
                Report(dir > 0 ? "Swipe → Dash right" : "Swipe → Dash left");
            }
            else
            {
                // Down: no movement of its own; it's the lead-in to the Down special.
                RecordSwipe(f, SpecialMove.Down, 0, now);
                Report("Swipe down (tap for Leg Sweep)");
            }
        }

        void RecordSwipe(Finger f, SpecialMove move, int dir, double now)
        {
            lastSwipeTime = now;
            lastSwipeFinger = f.id;
            lastSwipeMove = move;
            lastSwipeDir = dir;
        }

        void FireSpecial(SpecialMove move, int dir, string how)
        {
            lastSwipeTime = -1; // one special per swipe
            if (settings.directionalSpecials && commands.Special(move, dir))
            {
                Report($"{how} → {move} special");
                return;
            }
            commands.Attack(); // couldn't start: it was a tap after all
            Report($"{how} → no special, Attack");
        }

        void Report(string gesture) => report?.Invoke(gesture);
    }
}
