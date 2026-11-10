using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem.EnhancedTouch;
using UnityEngine.InputSystem.LowLevel;
using KungFuCrashout.Fighters;
using ETouch = UnityEngine.InputSystem.EnhancedTouch.Touch;
using TouchPhase = UnityEngine.InputSystem.TouchPhase;

namespace KungFuCrashout.Controls
{
    /// <summary>
    /// The touch controls, rebuilt around one arbiter (GestureEngine) instead of many rules that each commit at once.
    /// Same gestures as the GDD and the old TapAnywhereInputHandler:
    ///   TAP = light attack (repeat for the combo)       HOLD still = heavy attack
    ///   SWIPE sideways = dash · up = jump · up-diagonal = diagonal jump · down = lead-in for the Down special
    ///   SWIPE + TAP with another finger = directional special (Side / Up / Down)
    ///   TWO FINGERS together + hold = block; landing them just before a hit = parry / item catch
    /// Differences from the old handler: two still fingers start the block at once but a swipe inside the short wait
    /// turns it into a special; distances are in millimetres with a minimum speed; finger timing uses when each finger
    /// actually landed; a resting thumb no longer turns a dash into a special; dash then tap = dash then jab.
    ///
    /// Testing: Show Overlay (editor / development builds) draws each finger's state and the last gestures.
    /// Record Touches writes a touch log to the device (path in the Console); replay it in the editor with
    /// Window → KungFu Crashout → Controls → Replay Touch Log.
    /// Setup: Window → KungFu Crashout → Controls → Use Gesture Arbiter Input. Place in: Assets/_Project/Scripts/Input/GestureArbiterInput.cs
    /// </summary>
    public class GestureArbiterInput : MonoBehaviour
    {
        [SerializeField] Fighter fighter;
        [SerializeField] GestureSettings settings = new GestureSettings();

        [Header("Testing (editor and development builds only)")]
        [Tooltip("In the editor, the mouse acts as one finger.")]
        [SerializeField] bool simulateTouchInEditor = true;
        [Tooltip("Draw each finger's state and the last few gestures on screen.")]
        [SerializeField] bool showOverlay = true;
        [Tooltip("Write every recognised gesture to the Console.")]
        [SerializeField] bool logGestures = false;
        [Tooltip("Record every touch to a log file on the device (Application.persistentDataPath/TouchLogs). " +
                 "Replay it in the editor to see what the arbiter made of it.")]
        [SerializeField] bool recordTouches = false;

        public GestureSettings Settings => settings;
        public string LastGesture => recent.Count > 0 ? recent[recent.Count - 1] : "-";

        GestureEngine engine;
        readonly List<RawTouch> frame = new List<RawTouch>();
        readonly List<string> recent = new List<string>();
        static bool DebugAllowed => Application.isEditor || Debug.isDebugBuild;

        // recording
        StringBuilder log;
        string logPath;
        double lastFlush;

        void Awake()
        {
            engine = new GestureEngine(settings, fighter)
            {
                isClaimed = TouchClaims.IsClaimed,
                motionGoing = MotionGoing,
                report = OnGesture,
            };
        }

        void Start()
        {
            // Two touch handlers on at once = every touch read twice. Without a ControlSchemeSwitcher, this one wins.
            if (!enabled || FindFirstObjectByType<ControlSchemeSwitcher>()) return;
            foreach (var other in FindObjectsByType<TapAnywhereInputHandler>(FindObjectsSortMode.None))
                if (other.enabled) { other.enabled = false; Debug.LogWarning($"[Gestures] Turned off TapAnywhereInputHandler on '{other.name}' (two handlers read every touch twice).", other); }
            foreach (var other in FindObjectsByType<TouchInputHandler>(FindObjectsSortMode.None))
                if (other.enabled) { other.enabled = false; Debug.LogWarning($"[Gestures] Turned off TouchInputHandler on '{other.name}' (two handlers read every touch twice).", other); }
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
            engine?.Reset(); // never leave the fighter stuck blocking
            FlushLog();
        }

        void OnApplicationPause(bool paused)
        {
            if (paused) FlushLog();
        }

        void Update()
        {
            if (engine == null || fighter == null) return;
            engine.dpi = Screen.dpi > 0f ? Screen.dpi : settings.fallbackDpi;
            engine.screenHeight = Screen.height;

            frame.Clear();
            foreach (var t in ETouch.activeTouches)
            {
                frame.Add(new RawTouch
                {
                    id = t.touchId,
                    phase = Convert(t.phase),
                    position = t.screenPosition,
                    startPosition = t.startScreenPosition,
                    startTime = t.startTime,
                });
            }

            double now = InputState.currentTime; // the same clock as each touch's startTime
            if (recordTouches && DebugAllowed) Record(now);
            engine.Process(frame, now);
        }

        static RawPhase Convert(TouchPhase p) => p switch
        {
            TouchPhase.Began => RawPhase.Began,
            TouchPhase.Moved => RawPhase.Moved,
            TouchPhase.Ended => RawPhase.Ended,
            TouchPhase.Canceled => RawPhase.Canceled,
            _ => RawPhase.Stationary,
        };

        /// <summary>Is the dash / jump from the last swipe still going? Only used with Tap During Motion on.</summary>
        bool MotionGoing(SpecialMove move)
        {
            if (!fighter) return false;
            return move switch
            {
                SpecialMove.Side => fighter.State == FighterState.Dashing,
                SpecialMove.Up => fighter.IsAirborne && !fighter.IsLaunched && fighter.AirVelocity.y > 0f,
                _ => false,
            };
        }

        void OnGesture(string gesture)
        {
            recent.Add($"{gesture}");
            if (recent.Count > 4) recent.RemoveAt(0);
            if (logGestures && DebugAllowed) Debug.Log($"[Gestures] {gesture}");
        }

        // ───────────── Touch log ─────────────

        void Record(double now)
        {
            if (log == null)
            {
                string dir = Path.Combine(Application.persistentDataPath, "TouchLogs");
                Directory.CreateDirectory(dir);
                logPath = Path.Combine(dir, $"touches_{DateTime.Now:yyyyMMdd_HHmmss}.csv");
                log = new StringBuilder();
                float dpi = Screen.dpi > 0f ? Screen.dpi : settings.fallbackDpi;
                log.AppendLine(FormattableString.Invariant($"# KungFu Crashout touch log v1,dpi={dpi},width={Screen.width},height={Screen.height}"));
                File.WriteAllText(logPath, log.ToString());
                log.Clear();
                Debug.Log($"[Gestures] Recording touches to {logPath}");
            }
            if (frame.Count == 0) return;
            log.AppendLine(FormattableString.Invariant($"F,{now:R}"));
            foreach (var t in frame)
                log.AppendLine(FormattableString.Invariant(
                    $"T,{t.id},{(int)t.phase},{t.position.x:0.##},{t.position.y:0.##},{t.startPosition.x:0.##},{t.startPosition.y:0.##},{t.startTime:R}"));
            if (now - lastFlush > 2.0) FlushLog();
        }

        void FlushLog()
        {
            if (log == null || log.Length == 0 || string.IsNullOrEmpty(logPath)) return;
            try { File.AppendAllText(logPath, log.ToString()); }
            catch (Exception e) { Debug.LogWarning($"[Gestures] Couldn't write the touch log: {e.Message}"); }
            log.Clear();
            lastFlush = InputState.currentTime;
        }

        // ───────────── Overlay ─────────────

        GUIStyle style;

        void OnGUI()
        {
            if (!showOverlay || !DebugAllowed || engine == null) return;
            if (style == null)
            {
                style = new GUIStyle(GUI.skin.label) { fontSize = Mathf.RoundToInt(Mathf.Min(Screen.width, Screen.height) * 0.028f) };
                style.normal.textColor = Color.white;
            }

            float size = Mathf.Min(Screen.width, Screen.height) * 0.08f;
            foreach (var (pos, label) in engine.DebugFingers())
            {
                var r = new Rect(pos.x - size / 2f, Screen.height - pos.y - size / 2f, size, size);
                var old = GUI.color;
                GUI.color = new Color(1f, 1f, 1f, 0.25f);
                GUI.DrawTexture(r, Texture2D.whiteTexture);
                GUI.color = old;
                GUI.Label(new Rect(r.x, r.yMax, size * 3f, style.fontSize * 1.5f), label, style);
            }

            float line = style.fontSize * 1.35f;
            float y = Screen.height * (1f - settings.inputZoneTop) + 8f;
            for (int i = recent.Count - 1; i >= 0; i--, y += line)
                GUI.Label(new Rect(16f, y, Screen.width * 0.6f, line), recent[i], style);
            if (recordTouches) GUI.Label(new Rect(16f, y, Screen.width * 0.6f, line), "● recording touches", style);
        }
    }
}
