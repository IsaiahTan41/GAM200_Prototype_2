using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem.EnhancedTouch;
using KungFuCrashout.Fighters;
using ETouch = UnityEngine.InputSystem.EnhancedTouch.Touch;
using TouchPhase = UnityEngine.InputSystem.TouchPhase;

namespace KungFuCrashout.Controls
{
    /// <summary>
    /// Turns raw touches into fighter commands. Each finger is tracked separately,
    /// so you can block with one thumb and attack with the other.
    ///
    ///   LEFT half   press = block (first moments = parry), release = stop blocking
    ///   RIGHT half  tap = attack, hold = heavy attack
    ///   ANYWHERE    swipe sideways       = dash
    ///               swipe up-left/right  = diagonal jump along the swipe's direction
    ///               swipe straight up    = vertical jump
    ///               (any swipe that starts on the left cancels the block; downward swipes do nothing)
    ///
    /// Landscape layout: the touch zones cover everything below the HUD band at the top.
    /// Touches that start inside the HUD band are ignored. Swipe distance is measured against
    /// the screen's SHORT side, so it feels the same on any phone shape.
    /// Place in: Assets/_Project/Scripts/Input/TouchInputHandler.cs
    /// </summary>
    public class TouchInputHandler : MonoBehaviour
    {
        [SerializeField] Fighter fighter;

        [Header("Gesture tuning")]
        [Tooltip("Horizontal distance a finger must travel to count as a swipe, as a fraction of the screen's short side (its height in landscape).")]
        [SerializeField, Range(0.02f, 0.3f)] float swipeThreshold = 0.08f;
        [Tooltip("Swipes flatter than this angle above/below horizontal (degrees) are dashes. Steeper upward swipes are jumps.")]
        [SerializeField, Range(5f, 60f)] float diagonalMinAngle = 25f;
        [Tooltip("Upward swipes steeper than this angle (degrees) are straight jumps; between the two angles they are diagonal jumps.")]
        [SerializeField, Range(30f, 89f)] float verticalMinAngle = 65f;
        [Tooltip("Seconds a right-side finger must stay down (without swiping) to count as a hold.")]
        [SerializeField, Range(0.05f, 0.6f)] float holdTime = 0.2f;
        [Tooltip("Touches that START above this height (fraction of screen, from the bottom) are ignored. 0.85 leaves the top 15% for the HUD.")]
        [SerializeField, Range(0.2f, 1f)] float inputZoneTop = 0.85f;

        [Header("Editor / debug")]
        [Tooltip("Lets the mouse act as a finger in the Game view. Turn off if you get double inputs in the Simulator view.")]
        [SerializeField] bool simulateTouchInEditor = true;
        [SerializeField] bool showDebugOverlay = true;

        enum Side { Left, Right }
        enum GestureState { Pending, Holding, Swiped, Ignored }

        class TrackedTouch
        {
            public Vector2 start;
            public double startTime;
            public Side side;
            public GestureState state;
        }

        readonly Dictionary<int, TrackedTouch> tracked = new Dictionary<int, TrackedTouch>();
        IFighterCommands commands;
        string lastGesture = "-";
        GUIStyle style;

        /// <summary>The screen's short side in pixels (its height in landscape).</summary>
        static float ShortSide => Mathf.Min(Screen.width, Screen.height);

        void Awake() => commands = fighter;

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
            tracked.Clear();
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
                        Evaluate(touch, tt);
                        break;

                    case TouchPhase.Ended:
                    case TouchPhase.Canceled:
                        Evaluate(touch, tt); // catch very fast flicks that ended this frame
                        Release(tt);
                        tracked.Remove(touch.touchId);
                        break;
                }
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
                startTime = Time.unscaledTimeAsDouble,
                side = start.x < Screen.width * 0.5f ? Side.Left : Side.Right,
                // Touches claimed by another handler (e.g. tapping a stage item) are not fight input.
                state = TouchClaims.IsClaimed(touch.touchId) || start.y > Screen.height * inputZoneTop
                    ? GestureState.Ignored : GestureState.Pending
            };
            tracked[touch.touchId] = tt;

            // Left side blocks the instant it's pressed — this keeps parry timing tight.
            if (tt.state == GestureState.Pending && tt.side == Side.Left)
            {
                commands.BlockStart();
                Report("Left press → Block (parry window)");
            }

            return tt;
        }

        /// <summary>Check whether a finger has become a swipe or a hold.</summary>
        void Evaluate(ETouch touch, TrackedTouch tt)
        {
            if (tt.state != GestureState.Pending) return;

            Vector2 delta = touch.screenPosition - tt.start;
            float threshold = ShortSide * swipeThreshold;

            // Swipe: far enough in any direction. Its angle decides dash, diagonal jump or vertical jump.
            if (delta.magnitude >= threshold)
            {
                tt.state = GestureState.Swiped;
                if (tt.side == Side.Left) commands.BlockEnd();

                // 0° = sideways, +90° = straight up, -90° = straight down.
                float angle = Mathf.Atan2(delta.y, Mathf.Abs(delta.x)) * Mathf.Rad2Deg;

                if (angle >= verticalMinAngle)
                {
                    commands.JumpUp();
                    Report("Swipe up → Jump");
                }
                else if (angle >= diagonalMinAngle)
                {
                    commands.JumpToward(delta.normalized);
                    Report(delta.x > 0f ? "Swipe up-right → Jump right" : "Swipe up-left → Jump left");
                }
                else if (angle > -diagonalMinAngle)
                {
                    int dir = delta.x > 0f ? 1 : -1;
                    commands.Dash(dir);
                    Report(dir > 0 ? "Swipe → Dash right" : "Swipe → Dash left");
                }
                else
                {
                    Report("Swipe down → (nothing)");
                }
                return;
            }

            // Hold (right side only): held long enough without drifting into a swipe.
            if (tt.side == Side.Right &&
                Time.unscaledTimeAsDouble - tt.startTime >= holdTime &&
                delta.magnitude < threshold)
            {
                tt.state = GestureState.Holding;
                commands.HeavyAttack();
                Report("Right hold → Heavy attack");
            }
        }

        void CancelClaimed(TrackedTouch tt)
        {
            // A pending left finger is holding the instant block: let it go.
            if (tt.state == GestureState.Pending && tt.side == Side.Left) commands.BlockEnd();
            tt.state = GestureState.Ignored;
            Report("Finger taken by item gesture");
        }

        /// <summary>A finger lifted. Anything still Pending was a tap.</summary>
        void Release(TrackedTouch tt)
        {
            if (tt.state != GestureState.Pending) return;

            if (tt.side == Side.Left)
            {
                commands.BlockEnd();
                Report("Left release → Block end");
            }
            else
            {
                commands.Attack();
                Report("Right tap → Attack");
            }
        }

        void Report(string gesture)
        {
            lastGesture = gesture;
            Debug.Log($"[Touch] {gesture}");
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

            // GUI coordinates are measured from the top of the screen.
            float zoneTopY = Screen.height * (1f - inputZoneTop);
            float zoneHeight = Screen.height - zoneTopY;
            float halfW = Screen.width * 0.5f;
            float line = style.fontSize * 1.4f;

            DrawRect(new Rect(0, zoneTopY, halfW, zoneHeight), new Color(0.2f, 0.4f, 1f, 0.12f));
            DrawRect(new Rect(halfW, zoneTopY, halfW, zoneHeight), new Color(1f, 0.3f, 0.2f, 0.12f));

            // Labels sit at the bottom corners so they don't cover the fighters.
            float labelY = Screen.height - line * 4.5f;
            GUI.Label(new Rect(20, labelY, halfW - 40, line * 3),
                "LEFT\nhold = block\ntap = parry", style);
            GUI.Label(new Rect(halfW + 20, labelY, halfW - 40, line * 3),
                "RIGHT\ntap = attack\nhold = heavy", style);
            GUI.Label(new Rect(20, Screen.height - line * 1.4f, Screen.width - 40, line * 1.4f),
                "Swipe sideways = dash, up = jump, up-left/right = diagonal jump", style);

            string state = fighter ? fighter.State.ToString() : "-";
            GUI.Label(new Rect(halfW - 200, zoneTopY + 10, 400, line * 2.2f),
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