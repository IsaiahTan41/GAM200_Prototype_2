using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem.EnhancedTouch;
using KungFuCrashout.Fighters;
using ETouch = UnityEngine.InputSystem.EnhancedTouch.Touch;
using TouchPhase = UnityEngine.InputSystem.TouchPhase;

namespace KungFuCrashout.Controls
{
    /// <summary>
    /// PROTOTYPE alternative control scheme. Use it instead of TouchInputHandler (never both at once;
    /// ControlSchemeSwitcher handles that). Sends the same IFighterCommands, so nothing else changes.
    ///
    ///   TAP anywhere               = attack (repeat taps chain the combo)
    ///   HOLD anywhere              = heavy attack (optional)
    ///   SWIPE DOWN                 = block. With Latch Down Swipe Block ON (default) the block STAYS on
    ///                                after you lift the finger, until your next action: a tap (attack),
    ///                                hold (heavy), sideways swipe (dash) or up swipe (jump) drops the block
    ///                                and does that action. OFF = keep the finger down to block, lift to stop.
    ///   DOUBLE PRESS, keep holding = block; a quick double tap = parry (option)
    ///   TWO FINGERS TOGETHER, hold = block (Block Input = Two Finger Hold / Both). Works like the original
    ///                                left-side press: the block starts the instant both fingers land, so
    ///                                landing them together just before a hit = PARRY; keep holding = block;
    ///                                lift either finger = stop. Any part of the screen.
    ///   Block Input picks the defence: Swipe (down swipe / double press as set by Block Gesture),
    ///   Two Finger Hold, or Both.
    ///   SWIPE sideways             = dash
    ///   SWIPE up-left / up-right   = diagonal jump      SWIPE up = vertical jump
    ///
    /// Touches that start in the HUD band at the top are ignored, like TouchInputHandler.
    /// Place in: Assets/_Project/Scripts/Input/TapAnywhereInputHandler.cs
    /// </summary>
    public class TapAnywhereInputHandler : MonoBehaviour
    {
        public enum BlockGesture
        {
            /// <summary>Swipe down and keep the finger down to block.</summary>
            DownSwipe,
            /// <summary>Press twice quickly and keep the second press down to block.</summary>
            DoublePress,
            /// <summary>Either gesture blocks.</summary>
            Both
        }

        public enum BlockInput
        {
            /// <summary>The swipe-based block above (Block Gesture: down swipe / double press).</summary>
            Swipe,
            /// <summary>Two fingers pressed together and held, anywhere. Timed just before a hit = parry.</summary>
            TwoFingerHold,
            /// <summary>Either.</summary>
            Both
        }

        [SerializeField] Fighter fighter;

        [Header("Scheme options")]
        [Tooltip("Swipe: down swipe / double press (Block Gesture). Two Finger Hold: two fingers together and held, anywhere " +
                 "(press just before a hit = parry, like the original left-side press). Both: either.")]
        [SerializeField] BlockInput blockInput = BlockInput.Swipe;
        [Tooltip("Which gesture starts a block (and a parry, if it lands within the parry window).")]
        [SerializeField] BlockGesture blockGesture = BlockGesture.Both;
        [Tooltip("Holding a finger still anywhere does the heavy attack.")]
        [SerializeField] bool holdForHeavy = true;
        [Tooltip("ON: a down swipe keeps you blocking after the finger lifts, until you attack, dash or jump. OFF: keep the finger down to block. (Double press always needs the finger kept down.)")]
        [SerializeField] bool latchDownSwipeBlock = true;

        [Header("Two-finger block")]
        [Tooltip("The second finger must land within this many seconds of the first to count as pressed together. " +
                 "Smaller = stricter; a single tap's attack only fires on release, so a short window costs no input delay.")]
        [SerializeField, Range(0.03f, 0.3f)] float twoFingerWindow = 0.12f;

        [Header("Gesture tuning")]
        [Tooltip("Distance a finger must travel to count as a swipe, as a fraction of the screen's short side.")]
        [SerializeField, Range(0.02f, 0.3f)] float swipeThreshold = 0.08f;
        [Tooltip("Seconds a finger must stay down (without swiping) to count as a hold.")]
        [SerializeField, Range(0.05f, 0.6f)] float holdTime = 0.2f;
        [Tooltip("Touches that START above this height (fraction of screen, from the bottom) are ignored. 0.85 leaves the top 15% for the HUD.")]
        [SerializeField, Range(0.2f, 1f)] float inputZoneTop = 0.85f;
        [Tooltip("Swipes flatter than this (degrees from horizontal) are dashes.")]
        [SerializeField, Range(5f, 60f)] float diagonalMinAngle = 25f;
        [Tooltip("Upward swipes steeper than this are straight jumps; between the two angles they are diagonal jumps.")]
        [SerializeField, Range(30f, 89f)] float verticalMinAngle = 65f;
        [Tooltip("Downward swipes steeper than this (degrees below horizontal) block.")]
        [SerializeField, Range(10f, 89f)] float downSwipeMinAngle = 45f;
        [Tooltip("After a swipe, the same finger can swipe again once it stops or turns back (multiple dashes in a row " +
                 "without lifting). Off = one swipe per touch.")]
        [SerializeField] bool chainSwipes = true;

        [Header("Double press")]
        [Tooltip("Max seconds between the first tap's release and the second press.")]
        [SerializeField, Range(0.1f, 0.5f)] float doublePressWindow = 0.25f;
        [Tooltip("Max distance between the two presses, as a fraction of the screen's short side.")]
        [SerializeField, Range(0.05f, 0.5f)] float doublePressMaxDistance = 0.15f;
        [Tooltip("ON: a single tap's attack waits until the double-press window has passed, so a double press never throws a jab first (costs that much input delay). OFF: the first tap attacks immediately.")]
        [SerializeField] bool waitForDoublePress = false;

        [Header("Directional specials (swipe + tap)")]
        [Tooltip("A tap from another finger right after a swipe turns into that direction's special: sideways = Side (Elbow " +
                 "Slide), up = Up (Uppercut), down = Down (Leg Sweep). The movement happens first; the special takes over from it.")]
        [SerializeField] bool directionalSpecials = true;
        [Tooltip("Seconds after a swipe in which a tap (timed from when the finger touches down) makes the special.")]
        [SerializeField, Range(0.03f, 0.5f)] float chordWindow = 0.15f;
        [Tooltip("Also make the special when tapping any time the movement is still going (during the dash, or the jump), " +
                 "even after the Chord Window. Off = only the Chord Window (taps later in a jump are air attacks).")]
        [SerializeField] bool tapDuringMotion = true;
        [Tooltip("Tap During Motion for jumps only counts while still rising, so an Uppercut never comes out on the way down " +
                 "(a falling tap stays an air attack).")]
        [SerializeField] bool jumpSpecialUntilApex = true;
        [Tooltip("The tap that made a special may also be the first press of a Double Press block (special, then a quick " +
                 "guard). Off = it never starts a double press.")]
        [SerializeField] bool specialTapCanStartDoublePress = false;

        [Header("Editor / debug")]
        [Tooltip("Lets the mouse act as a finger in the Game view. Turn off if you get double inputs in the Simulator view.")]
        [SerializeField] bool simulateTouchInEditor = true;
        [SerializeField] bool showDebugOverlay = true;

        enum GestureState { Pending, Holding, Swiped, Blocking, PairBlock, Ignored }

        class TrackedTouch
        {
            public Vector2 start;
            public double startTime;
            public GestureState state;
            public bool latchedBlock; // this finger's down swipe latched the block
            public Vector2 last;      // position last frame (for Chain Swipes)
            public Vector2 swipeDir;  // direction of this finger's last swipe
            public bool chained;      // re-armed after a swipe: never a tap / hold
            public bool hasSpecial;   // touched down in a special's chord window / during the motion
            public SpecialMove special;
            public int specialDir;
        }

        // The last swipe that can lead into a special (movement first, then the special).
        double lastSwipeTime = -1;
        SpecialMove lastSwipeMove;
        int lastSwipeDir;

        void RecordSwipe(SpecialMove move, int dir)
        {
            lastSwipeTime = Now;
            lastSwipeMove = move;
            lastSwipeDir = dir;
        }

        /// <summary>Is the movement from the last swipe still going (dash, or jump — rising only if Until Apex)?</summary>
        bool MotionStillGoing()
        {
            if (!fighter) return false;
            switch (lastSwipeMove)
            {
                case SpecialMove.Side: return fighter.State == FighterState.Dashing;
                case SpecialMove.Up:   return fighter.IsAirborne && !fighter.IsLaunched &&
                                              (!jumpSpecialUntilApex || fighter.AirVelocity.y > 0f);
                default:               return false;
            }
        }

        readonly Dictionary<int, TrackedTouch> tracked = new Dictionary<int, TrackedTouch>();
        IFighterCommands commands;
        int blockingFingers;
        bool pairBlocking;          // two-finger hold block active
        int pairA = -1, pairB = -1; // its two fingers
        bool blockLatched;      // down-swipe block that stays on until the next action

        /// <summary>True while a down-swipe block is latched on (for debug UI).</summary>
        public bool IsBlockLatched => blockLatched;
        public bool LatchDownSwipeBlock => latchDownSwipeBlock;
        string lastGesture = "-";
        GUIStyle style;

        // double press
        double lastTapTime = -1;
        Vector2 lastTapPos;
        bool tapPending;        // waitForDoublePress: an attack waiting for the window to close
        double tapPendingSince;

        bool SwipeBlocks => blockInput != BlockInput.TwoFingerHold;
        bool TwoFingerBlocks => blockInput != BlockInput.Swipe;
        bool DownSwipeBlocks => SwipeBlocks && blockGesture != BlockGesture.DoublePress;
        bool DoublePressBlocks => SwipeBlocks && blockGesture != BlockGesture.DownSwipe;

        /// <summary>Is any block input holding the block right now (fingers, latch or two-finger hold)?</summary>
        bool BlockHeld => blockingFingers > 0 || blockLatched || pairBlocking;

        /// <summary>Send BlockStart / BlockEnd only when the overall held state actually changes.</summary>
        void ApplyBlock(bool wasHeld)
        {
            bool held = BlockHeld;
            if (held && !wasHeld) commands.BlockStart();
            else if (!held && wasHeld) commands.BlockEnd();
        }
        static float ShortSide => Mathf.Min(Screen.width, Screen.height);
        static double Now => Time.unscaledTimeAsDouble;
        static double NowTime => Now;

        void Awake() => commands = fighter;

        void Start()
        {
            // Both schemes on at once = every touch is read twice (e.g. the split-zones scheme turns a two-finger block's
            // right-hand finger into a heavy attack, which cancels the block). Normally ControlSchemeSwitcher prevents
            // this; without one, this scheme wins and the other is switched off.
            if (!enabled || FindFirstObjectByType<ControlSchemeSwitcher>()) return;
            foreach (var other in FindObjectsByType<TouchInputHandler>(FindObjectsSortMode.None))
            {
                if (!other.enabled) continue;
                other.enabled = false;
                Debug.LogWarning($"[TapAnywhere] '{other.name}' TouchInputHandler was also on, so every touch was read twice " +
                                 "(attacks broke the two-finger block). Turned it off for this session. " +
                                 "Fix: KungFu Crashout → Set Up Control Schemes.", other);
            }
        }

        void OnEnable()
        {
            EnhancedTouchSupport.Enable();
#if UNITY_EDITOR
            if (simulateTouchInEditor) TouchSimulation.Enable();
#endif
        }

        void OnDisable()
        {
#if UNITY_EDITOR
            if (simulateTouchInEditor) TouchSimulation.Disable();
#endif
            EnhancedTouchSupport.Disable();

            // Never leave the fighter stuck blocking when the scheme is switched off.
            if (BlockHeld) commands?.BlockEnd();
            blockingFingers = 0;
            blockLatched = false;
            pairBlocking = false;
            pairA = pairB = -1;
            tracked.Clear();
            tapPending = false;
            lastTapTime = -1;
        }

        void Update()
        {
            if (commands == null) return;

            foreach (var touch in ETouch.activeTouches)
            {
                var tt = Track(touch);

                // Another handler took this finger mid-gesture (e.g. a two-finger swipe down to pick up an item):
                // undo what it started and ignore it from now on.
                if (tt.state != GestureState.Ignored && TouchClaims.IsClaimed(touch.touchId)) CancelClaimed(tt);

                switch (touch.phase)
                {
                    case TouchPhase.Moved:
                    case TouchPhase.Stationary:
                        TryRearm(touch.screenPosition, tt);
                        Evaluate(touch, tt);
                        tt.last = touch.screenPosition;
                        break;

                    case TouchPhase.Ended:
                    case TouchPhase.Canceled:
                        Evaluate(touch, tt); // catch very fast flicks that ended this frame
                        Release(tt);
                        tracked.Remove(touch.touchId);
                        break;
                }
            }

            // A delayed single tap whose double-press window has passed: attack now.
            if (tapPending && Now - tapPendingSince > doublePressWindow)
            {
                tapPending = false;
                ReleaseLatch();
                commands.Attack();
                Report("Tap → Attack");
            }
        }

        /// <summary>Start tracking a finger the first time we see it.</summary>
        TrackedTouch Track(ETouch touch)
        {
            if (tracked.TryGetValue(touch.touchId, out var tt)) return tt;

            Vector2 start = touch.startScreenPosition;
            tt = new TrackedTouch
            {
                start = start,
                startTime = Now,
                // Touches claimed by another handler (e.g. tapping a stage item) are not fight input.
                state = TouchClaims.IsClaimed(touch.touchId) || start.y > Screen.height * inputZoneTop
                    ? GestureState.Ignored : GestureState.Pending,
                last = start
            };
            tracked[touch.touchId] = tt;

            // Touched down right after a swipe (or while its movement is still going) = this tap will be a special.
            if (tt.state == GestureState.Pending && directionalSpecials && lastSwipeTime >= 0)
            {
                bool inWindow = Now - lastSwipeTime <= chordWindow;
                bool inMotion = tapDuringMotion && MotionStillGoing();
                if (inWindow || inMotion)
                {
                    tt.hasSpecial = true;
                    tt.special = lastSwipeMove;
                    tt.specialDir = lastSwipeDir;
                }
            }

            // Two fingers landing together = block, starting NOW (so a well-timed press is a parry).
            if (tt.state == GestureState.Pending && TwoFingerBlocks && !pairBlocking)
            {
                foreach (var kv in tracked)
                {
                    if (kv.Key == touch.touchId) continue;
                    var other = kv.Value;
                    if (other.state != GestureState.Pending || Now - other.startTime > twoFingerWindow) continue;

                    bool was = BlockHeld;
                    other.state = GestureState.PairBlock; // its tap no longer attacks on release
                    tt.state = GestureState.PairBlock;
                    pairA = kv.Key;
                    pairB = touch.touchId;
                    pairBlocking = true;
                    tapPending = false;
                    ApplyBlock(was);
                    Report($"Two fingers together → Block (parry window) [{(Now - other.startTime) * 1000:0} ms apart]");
                    break;
                }
            }

            // Checked AFTER the two-finger block, so two fingers landing together always pair up, even right after a tap
            // (before, the second finger could become a one-finger double press and leave the first one free to heavy-attack).
            // Second press soon after a tap, near the same spot = block (instantly, so parry timing stays tight).
            if (tt.state == GestureState.Pending && DoublePressBlocks && lastTapTime >= 0 &&
                Now - lastTapTime <= doublePressWindow &&
                Vector2.Distance(start, lastTapPos) <= ShortSide * doublePressMaxDistance)
            {
                tapPending = false; // the first tap was the start of a double press, not an attack
                lastTapTime = -1;
                StartBlocking(tt);
                Report("Double press → Block (parry window)");
            }

            return tt;
        }

        /// <summary>Check whether a finger has become a swipe or a hold.</summary>
        void Evaluate(ETouch touch, TrackedTouch tt)
        {
            if (tt.state != GestureState.Pending && tt.state != GestureState.Blocking && tt.state != GestureState.PairBlock) return;

            Vector2 delta = touch.screenPosition - tt.start;
            float threshold = ShortSide * swipeThreshold;

            if (delta.magnitude >= threshold)
            {
                // 0° = sideways, +90° = straight up, -90° = straight down.
                float angle = Mathf.Atan2(delta.y, Mathf.Abs(delta.x)) * Mathf.Rad2Deg;
                bool downward = angle <= -downSwipeMinAngle;

                if (tt.state == GestureState.PairBlock)
                {
                    // Same as the original left-side block finger: drifting down keeps blocking;
                    // a real sideways / up swipe leaves the block and dashes / jumps.
                    if (downward || angle < -diagonalMinAngle) return;
                    EndPairBlock();
                    tt.state = GestureState.Swiped;
                    HandleSwipe(delta, angle, false, tt);
                    return;
                }

                if (tt.state == GestureState.Blocking)
                {
                    // Already blocking: sliding further down keeps the block;
                    // swiping sideways or up leaves the block and dashes / jumps.
                    if (downward || angle < -diagonalMinAngle) return;
                    StopBlocking(tt);
                }

                tt.state = GestureState.Swiped;
                tt.swipeDir = delta.normalized;
                HandleSwipe(delta, angle, downward, tt);
                return;
            }

            // Hold: stayed still long enough.
            if (tt.state == GestureState.Pending && holdForHeavy && !tt.chained && Now - tt.startTime >= holdTime)
            {
                tt.state = GestureState.Holding;
                ReleaseLatch();
                commands.HeavyAttack();
                Report("Hold → Heavy attack");
            }
        }

        void HandleSwipe(Vector2 delta, float angle, bool downward, TrackedTouch tt)
        {
            // Any swipe that isn't a block drops a latched block first.
            bool blockSwipe = downward && DownSwipeBlocks;
            if (!blockSwipe && angle > -diagonalMinAngle) ReleaseLatch();

            if (angle >= verticalMinAngle)
            {
                commands.JumpUp();
                RecordSwipe(SpecialMove.Up, 0);
                Report("Swipe up → Jump");
            }
            else if (angle >= diagonalMinAngle)
            {
                commands.JumpToward(delta.normalized);
                RecordSwipe(SpecialMove.Up, 0);
                Report(delta.x > 0f ? "Swipe up-right → Jump right" : "Swipe up-left → Jump left");
            }
            else if (angle > -diagonalMinAngle)
            {
                int dir = delta.x > 0f ? 1 : -1;
                commands.Dash(dir);
                RecordSwipe(SpecialMove.Side, dir);
                Report(dir > 0 ? "Swipe → Dash right" : "Swipe → Dash left");
            }
            else if (blockSwipe && latchDownSwipeBlock)
            {
                RecordSwipe(SpecialMove.Down, 0);
                // Latched: the finger no longer matters; the block stays until the next action.
                tt.state = GestureState.Swiped;
                if (blockLatched) { Report("Swipe down → still blocking"); return; }
                bool was = BlockHeld;
                blockLatched = true;
                tt.latchedBlock = true;
                ApplyBlock(was);
                Report("Swipe down → Block LATCHED (parry window) — attack, dash or jump to drop it");
            }
            else if (blockSwipe)
            {
                RecordSwipe(SpecialMove.Down, 0);
                StartBlocking(tt);
                Report("Swipe down → Block (parry window)");
            }
            else
            {
                // Down swipe that doesn't block (e.g. Block Gesture = Double Press): only the lead-in to the Down special.
                RecordSwipe(SpecialMove.Down, 0);
                Report(directionalSpecials ? "Swipe down → (tap for Down special)" : "Swipe down → (nothing)");
            }
        }

        /// <summary>A finger lifted. Anything still Pending was a tap.</summary>
        void Release(TrackedTouch tt)
        {
            if (tt.state == GestureState.PairBlock)
            {
                EndPairBlock();
                Report("Finger lifted → Two-finger block end");
                return;
            }
            if (tt.state == GestureState.Blocking)
            {
                StopBlocking(tt);
                Report("Release → Block end");
                return;
            }
            if (tt.state != GestureState.Pending || tt.chained) return;

            // Swipe + tap = the special for that swipe's direction (the movement already happened; this takes over).
            if (tt.hasSpecial && directionalSpecials)
            {
                ReleaseLatch();
                if (commands.Special(tt.special, tt.specialDir))
                {
                    tapPending = false;
                    lastSwipeTime = -1; // one special per swipe
                    if (specialTapCanStartDoublePress) { lastTapTime = Now; lastTapPos = tt.start; }
                    Report($"Swipe + tap → {tt.special} special");
                    return;
                }
                // Couldn't start (no special set / not allowed right now): it's a normal tap.
            }

            lastTapTime = Now;
            lastTapPos = tt.start;

            if (waitForDoublePress && DoublePressBlocks)
            {
                tapPending = true;      // attack fires in Update unless a second press arrives
                tapPendingSince = Now;
                return;
            }

            ReleaseLatch();
            commands.Attack();
            Report("Tap → Attack");
        }

        void CancelClaimed(TrackedTouch tt)
        {
            if (tt.state == GestureState.Blocking) StopBlocking(tt);
            if (tt.state == GestureState.PairBlock) EndPairBlock();
            if (tt.latchedBlock && blockLatched) ReleaseLatch();
            tt.state = GestureState.Ignored;
            Report("Finger taken by item gesture");
        }

        void StartBlocking(TrackedTouch tt)
        {
            bool was = BlockHeld;
            tt.state = GestureState.Blocking;
            blockingFingers++;
            ApplyBlock(was);
        }

        void StopBlocking(TrackedTouch tt)
        {
            bool was = BlockHeld;
            tt.state = GestureState.Swiped;
            blockingFingers = Mathf.Max(0, blockingFingers - 1);
            ApplyBlock(was);
        }

        /// <summary>Either finger of the two-finger block lifted (or was taken): stop, and ignore the other finger.</summary>
        void EndPairBlock()
        {
            if (!pairBlocking) return;
            bool was = BlockHeld;
            pairBlocking = false;
            if (tracked.TryGetValue(pairA, out var a) && a.state == GestureState.PairBlock) a.state = GestureState.Ignored;
            if (tracked.TryGetValue(pairB, out var b) && b.state == GestureState.PairBlock) b.state = GestureState.Ignored;
            pairA = pairB = -1;
            ApplyBlock(was);
        }

        /// <summary>Drop a latched down-swipe block, just before another action is sent.</summary>
        void ReleaseLatch()
        {
            if (!blockLatched) return;
            bool was = BlockHeld;
            blockLatched = false;
            ApplyBlock(was);
            Debug.Log("[TapAnywhere] Latched block dropped");
        }

        /// <summary>
        /// Chain Swipes: after a swipe, the same finger can swipe again once it stops or turns back
        /// (dash → dash, dash → jump) without lifting. A re-armed finger never becomes a tap or hold.
        /// </summary>
        void TryRearm(Vector2 pos, TrackedTouch tt)
        {
            if (!chainSwipes || tt.state != GestureState.Swiped) return;
            Vector2 frame = pos - tt.last;
            float still = ShortSide * 0.004f;
            bool stopped = frame.magnitude < still;
            bool turnedBack = !stopped && Vector2.Dot(frame, tt.swipeDir) < 0f;
            if (!stopped && !turnedBack) return;

            tt.start = pos;
            tt.startTime = NowTime;
            tt.state = GestureState.Pending;
            tt.chained = true;
        }

        void Report(string gesture)
        {
            lastGesture = gesture;
            Debug.Log($"[TapAnywhere] {gesture}");
        }

        // ───────────── Debug overlay ─────────────

        void OnGUI()
        {
            if (!showDebugOverlay) return;

            if (style == null)
            {
                style = new GUIStyle(GUI.skin.label) { fontSize = Mathf.RoundToInt(ShortSide * 0.03f) };
                style.normal.textColor = Color.white;
            }

            float zoneTopY = Screen.height * (1f - inputZoneTop);
            float line = style.fontSize * 1.4f;

            DrawRect(new Rect(0, zoneTopY, Screen.width, Screen.height - zoneTopY), new Color(0.6f, 0.3f, 1f, 0.08f));

            string swipeBlock = blockGesture switch
            {
                BlockGesture.DownSwipe => latchDownSwipeBlock ? "swipe down = block (stays on)" : "swipe down + hold = block",
                BlockGesture.DoublePress => "double press + hold = block",
                _ => latchDownSwipeBlock ? "swipe down = block (stays on), double press + hold = block"
                                         : "swipe down or double press + hold = block"
            };
            string block = blockInput switch
            {
                BlockInput.TwoFingerHold => "two fingers together + hold = block (timed = parry)",
                BlockInput.Both => swipeBlock + ", two fingers + hold = block",
                _ => swipeBlock
            };
            GUI.Label(new Rect(20, Screen.height - line * 3.4f, Screen.width - 40, line * 3.2f),
                $"TAP ANYWHERE scheme\ntap = attack{(holdForHeavy ? ", hold = heavy" : "")}, {block}\nswipe sideways = dash, up / up-diagonal = jump", style);

            string state = fighter ? fighter.State.ToString() : "-";
            if (blockLatched) state += "  (block latched)";
            GUI.Label(new Rect(Screen.width * 0.5f - 200, zoneTopY + 10, 400, line * 2.2f),
                $"Last: {lastGesture}\nState: {state}", style);
        }

        static void DrawRect(Rect r, Color c)
        {
            var old = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = old;
        }
    }
}
