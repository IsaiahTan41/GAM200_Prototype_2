using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.EnhancedTouch;
using KungFuCrashout.Controls;
using ETouch = UnityEngine.InputSystem.EnhancedTouch.Touch;
using TouchPhase = UnityEngine.InputSystem.TouchPhase;

namespace KungFuCrashout.Props
{
    /// <summary>
    /// Touch controls for stage items (M5). Works with either control scheme.
    ///
    ///   PICK UP (Pick Up Gesture setting, proximity rules are the same for both):
    ///     Tap                   — TAP an item when your fighter is close
    ///     Two Finger Swipe Down — TWO fingers swipe DOWN together, anywhere on screen: picks up the nearest
    ///                             item within Pick Up Range (the same range and rules as tapping)
    ///     Both                  — either works
    ///   SWIPE starting on the item you're carrying   = throw it in the swipe's direction
    ///   TAP the item you're carrying (no swipe)      = drop it (optional)
    ///
    /// The two-finger swipe is recognised before either finger counts as a fight swipe (smaller threshold), and
    /// both fingers are CLAIMED, so the fight controls drop whatever those fingers started (e.g. the left-side
    /// block press) and no block, dash or attack comes out of it.
    ///
    /// A touch that starts on an item and does one of the above is CLAIMED (TouchClaims), so
    /// TouchInputHandler / TapAnywhereInputHandler ignore it: no attack, block, dash or jump fires.
    /// A tap on an item that's out of range is NOT claimed and works as a normal tap.
    ///
    /// Editor: F = pick up the nearest item in range / drop it.
    ///         G = throw forward (hold W or ↑ while pressing G to throw up-forward at 45°).
    ///
    /// Setup: put on the PlayerInput object. Holder = the Player's ItemHolder.
    /// Item Layers = the "Environment" layer.
    /// Place in: Assets/_Project/Scripts/Environment/ItemTapHandler.cs
    /// </summary>
    [DefaultExecutionOrder(-40)] // runs before the fight input handlers, so its claims are seen first
    public class ItemTapHandler : MonoBehaviour
    {
        [SerializeField] ItemHolder holder;
        [Tooltip("Camera used to turn screen touches into world positions. Empty = Main Camera.")]
        [SerializeField] Camera cam;
        [Tooltip("Layers that carryable items live on (the Environment layer).")]
        [SerializeField] LayerMask itemLayers;
        [Tooltip("Extra forgiveness around an item ON THE STAGE for fat fingers (world units). The carried item uses Held Item Tap Radius.")]
        [SerializeField, Min(0f)] float tapRadius = 0.4f;

        public enum PickUpGesture { Tap, TwoFingerSwipeDown, Both }
        public enum RadiusUnits { WorldUnits, ScreenFraction }

        [Header("Pick up")]
        [Tooltip("Tap: tap the item. Two Finger Swipe Down: two fingers swipe down together anywhere. Both: either.")]
        [SerializeField] PickUpGesture pickUpGesture = PickUpGesture.Tap;
        [Tooltip("Two-finger swipe: how far both fingers must move down, as a fraction of the screen's short side. " +
                 "Keep it below the fight controls' Swipe Threshold (0.08) so it wins before a swipe registers.")]
        [SerializeField, Range(0.02f, 0.2f)] float twoFingerSwipeThreshold = 0.05f;
        [Tooltip("Two-finger swipe: the second finger must land within this many seconds of the first.")]
        [SerializeField, Range(0.05f, 0.5f)] float twoFingerWindow = 0.25f;
        [Tooltip("Two-finger swipe: must be finished within this many seconds of the second finger landing.")]
        [SerializeField, Range(0.1f, 1.5f)] float twoFingerMaxTime = 0.6f;
        [Tooltip("Two-finger swipe: how steeply down each finger must move (degrees below horizontal). 45 = anything downward-ish.")]
        [SerializeField, Range(20f, 89f)] float twoFingerMinDownAngle = 50f;

        [Header("Throw")]
        [Tooltip("How far from the CARRIED item a finger can land and still start a throw swipe (or a tap-to-drop), " +
                 "measured from the item's edge. Bigger = easier to grab while it's moving with the fighter.")]
        [SerializeField, Min(0f)] float heldItemTapRadius = 0.8f;
        [Tooltip("Screen Fraction: Held Item Tap Radius is a fraction of the screen's short side (same finger distance on every " +
                 "device and camera zoom). World Units: it's in world units (grows/shrinks on screen with the camera zoom).")]
        [SerializeField] RadiusUnits heldItemRadiusUnits = RadiusUnits.WorldUnits;
        [Tooltip("How far a finger that started on the carried item must move to throw, as a fraction of the screen's short side.")]
        [SerializeField, Range(0.02f, 0.3f)] float throwSwipeThreshold = 0.06f;
        [Tooltip("Snap the throw to set angles (Throw Snap Angle apart, starting from straight right), " +
                 "so a slightly crooked swipe still flies straight across or exactly diagonal. Off = exact swipe direction.")]
        [SerializeField] bool snapThrowDirection = true;
        [Tooltip("Degrees between the allowed throw directions. 45 = 8 directions (across, diagonals, up, down); " +
                 "90 = 4 directions; 30 = 12 directions.")]
        [SerializeField, Range(5f, 180f)] float throwSnapAngle = 45f;
        [Tooltip("Lifting a finger from the carried item without swiping drops it. Turn off if that happens by accident.")]
        [SerializeField] bool tapCarriedItemToDrop = true;

        [Header("Editor")]
        [Tooltip("Picks up the nearest item in range, or drops the carried one. None = off.")]
        [SerializeField] Key pickUpKey = Key.F;
        [Tooltip("Throws the carried item forward (hold W / ↑ to throw up-forward). None = off.")]
        [SerializeField] Key throwKey = Key.G;

        readonly List<Collider2D> hits = new List<Collider2D>();

        // Fingers that started on the carried item: touchId → screen position where they started.
        readonly Dictionary<int, Vector2> onCarriedItem = new Dictionary<int, Vector2>();

        // Free fingers that could be half of a two-finger swipe down.
        class Finger { public Vector2 start, now; public double startTime; }
        readonly Dictionary<int, Finger> free = new Dictionary<int, Finger>();
        readonly List<int> ids = new List<int>();

        bool TapPicksUp => pickUpGesture != PickUpGesture.TwoFingerSwipeDown;
        bool SwipePicksUp => pickUpGesture != PickUpGesture.Tap;
        static double Now => Time.unscaledTimeAsDouble;

        static float ShortSide => Mathf.Min(Screen.width, Screen.height);

        void Reset()
        {
            itemLayers = LayerMask.GetMask("Environment");
        }

        void OnEnable() => EnhancedTouchSupport.Enable();

        void OnDisable()
        {
            EnhancedTouchSupport.Disable();
            TouchClaims.Clear();
            onCarriedItem.Clear();
            free.Clear();
        }

        void Update()
        {
            if (!holder) return;
            if (!cam) cam = Camera.main;

            foreach (var touch in ETouch.activeTouches)
            {
                int id = touch.touchId;
                switch (touch.phase)
                {
                    case TouchPhase.Began:
                        OnTouchBegan(id, touch.screenPosition);
                        if (!TouchClaims.IsClaimed(id))
                            free[id] = new Finger { start = touch.screenPosition, now = touch.screenPosition, startTime = Now };
                        break;

                    case TouchPhase.Moved:
                    case TouchPhase.Stationary:
                        CheckThrowSwipe(id, touch.screenPosition);
                        if (free.TryGetValue(id, out var f)) f.now = touch.screenPosition;
                        break;

                    case TouchPhase.Ended:
                    case TouchPhase.Canceled:
                        CheckThrowSwipe(id, touch.screenPosition); // catch fast flicks that ended this frame
                        if (onCarriedItem.Remove(id) && tapCarriedItemToDrop && touch.phase == TouchPhase.Ended)
                            holder.Drop(); // lifted without swiping = tap = drop
                        TouchClaims.Release(id);
                        free.Remove(id);
                        break;
                }
            }

            if (SwipePicksUp) CheckTwoFingerSwipe();

            KeyboardControls();
        }

        // ───────────── Touch ─────────────

        void OnTouchBegan(int id, Vector2 screenPos)
        {
            // Near the item we're carrying (within Held Item Tap Radius): claim it for a throw swipe / tap-to-drop.
            if (holder.Held && NearHeldItem(screenPos))
            {
                onCarriedItem[id] = screenPos;
                TouchClaims.Claim(id);
                return;
            }

            var item = ItemUnder(screenPos);
            if (!item) return;

            // On the item we're carrying: claim it and wait to see if it becomes a swipe (throw) or a tap (drop).
            if (holder.Held == item)
            {
                onCarriedItem[id] = screenPos;
                TouchClaims.Claim(id);
                return;
            }

            // On an item on the stage: pick it up if we're close enough. Out of range = normal tap.
            if (TapPicksUp && holder.TryPickUp(item)) TouchClaims.Claim(id);
        }

        void CheckThrowSwipe(int id, Vector2 screenPos)
        {
            if (!onCarriedItem.TryGetValue(id, out var start)) return;

            Vector2 delta = screenPos - start;
            if (delta.magnitude < ShortSide * throwSwipeThreshold) return;

            onCarriedItem.Remove(id); // it's a swipe now, not a tap; the claim stays until the finger lifts

            // The camera is orthographic and unrotated, so screen direction = world direction.
            holder.Throw(SnapThrow(delta.normalized));
        }

        /// <summary>Round a throw direction to the nearest multiple of Throw Snap Angle (when snapping is on).</summary>
        Vector2 SnapThrow(Vector2 dir)
        {
            if (!snapThrowDirection || throwSnapAngle <= 0f) return dir;
            float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
            float snapped = Mathf.Round(angle / throwSnapAngle) * throwSnapAngle * Mathf.Deg2Rad;
            return new Vector2(Mathf.Cos(snapped), Mathf.Sin(snapped));
        }

        // ───────────── Two-finger swipe down ─────────────

        /// <summary>
        /// Two free fingers that landed close together in time and have both moved down far enough:
        /// claim both (so the fight controls ignore and undo them) and pick up the nearest item in range.
        /// </summary>
        void CheckTwoFingerSwipe()
        {
            if (free.Count < 2) return;

            ids.Clear();
            ids.AddRange(free.Keys);
            float need = Mathf.Min(Screen.width, Screen.height) * twoFingerSwipeThreshold;

            for (int i = 0; i < ids.Count; i++)
            for (int j = i + 1; j < ids.Count; j++)
            {
                var a = free[ids[i]];
                var b = free[ids[j]];
                if (System.Math.Abs(a.startTime - b.startTime) > twoFingerWindow) continue;
                if (Now - System.Math.Max(a.startTime, b.startTime) > twoFingerMaxTime) continue;
                if (!SwipedDown(a, need) || !SwipedDown(b, need)) continue;

                // It's the gesture: both fingers belong to us now.
                TouchClaims.Claim(ids[i]);
                TouchClaims.Claim(ids[j]);
                free.Remove(ids[i]);
                free.Remove(ids[j]);

                bool picked = !holder.IsCarrying && TryPickUpNearest();
                Debug.Log(picked ? "[Items] Two-finger swipe down → picked up"
                                 : holder.IsCarrying ? "[Items] Two-finger swipe down → already carrying"
                                                     : "[Items] Two-finger swipe down → nothing in range");
                return;
            }
        }

        bool SwipedDown(Finger f, float need)
        {
            Vector2 d = f.now - f.start;
            if (-d.y < need) return false;
            float angleBelow = Mathf.Atan2(-d.y, Mathf.Abs(d.x)) * Mathf.Rad2Deg;
            return angleBelow >= twoFingerMinDownAngle;
        }

        /// <summary>Pick up the nearest item the fighter is allowed to (same range and state rules as tapping).</summary>
        bool TryPickUpNearest()
        {
            Carryable nearest = null;
            float best = float.MaxValue;
            foreach (var item in FindObjectsByType<Carryable>(FindObjectsSortMode.None))
            {
                float d = Mathf.Abs(item.X - holder.Fighter.X);
                if (d < best && holder.CanPickUp(item)) { best = d; nearest = item; }
            }
            return nearest && holder.TryPickUp(nearest);
        }

        /// <summary>Is this screen position within Held Item Tap Radius of the carried item's edge?</summary>
        bool NearHeldItem(Vector2 screenPos)
        {
            if (!cam || !holder.Held) return false;
            Vector2 world = cam.ScreenToWorldPoint(new Vector3(screenPos.x, screenPos.y, 0f));
            var item = holder.Held;

            var col = item.GetComponentInChildren<Collider2D>();
            Vector2 closest = col && col.enabled ? col.ClosestPoint(world) : (Vector2)item.transform.position;
            float dist = Vector2.Distance(world, closest);

            return dist <= HeldRadiusWorld();
        }

        /// <summary>Held Item Tap Radius in world units (converts from a screen fraction when set to that).</summary>
        float HeldRadiusWorld()
        {
            if (heldItemRadiusUnits == RadiusUnits.WorldUnits || !cam) return heldItemTapRadius;
            // Orthographic: the screen's height is 2 × orthographicSize world units.
            float worldPerPixel = 2f * cam.orthographicSize / Screen.height;
            return heldItemTapRadius * ShortSide * worldPerPixel;
        }

        void OnDrawGizmos()
        {
            // Play mode: shows the grab area around the carried item (cyan ring) so you can tune Held Item Tap Radius.
            if (!Application.isPlaying || !holder || !holder.Held) return;
            if (!cam) cam = Camera.main;
            Gizmos.color = Color.cyan;
            var col = holder.Held.GetComponentInChildren<Collider2D>();
            float r = HeldRadiusWorld() + (col ? Mathf.Max(col.bounds.extents.x, col.bounds.extents.y) : 0f);
            Gizmos.DrawWireSphere(holder.Held.transform.position, r);
        }

        /// <summary>The carryable item under a screen position, if any.</summary>
        Carryable ItemUnder(Vector2 screenPos)
        {
            if (!cam) return null;

            Vector2 world = cam.ScreenToWorldPoint(new Vector3(screenPos.x, screenPos.y, 0f));
            var filter = new ContactFilter2D { useTriggers = true };
            filter.SetLayerMask(itemLayers.value != 0 ? itemLayers : (LayerMask)Physics2D.AllLayers);

            hits.Clear();
            Physics2D.OverlapCircle(world, tapRadius, filter, hits);

            Carryable fallback = null;
            foreach (var hit in hits)
            {
                var item = hit ? hit.GetComponentInParent<Carryable>() : null;
                if (!item) continue;
                if (item == holder.Held) return item;   // the carried item wins if both are under the finger
                if (!fallback) fallback = item;
            }
            return fallback;
        }

        // ───────────── Editor keys ─────────────

        void KeyboardControls()
        {
            if (!Application.isEditor) return;
            var kb = Keyboard.current;
            if (kb == null) return;

            if (pickUpKey != Key.None && kb[pickUpKey].wasPressedThisFrame) KeyboardPickUpOrDrop();

            if (throwKey != Key.None && kb[throwKey].wasPressedThisFrame && holder.IsCarrying)
            {
                int facing = holder.Fighter.Facing;
                bool up = kb.wKey.isPressed || kb.upArrowKey.isPressed;
                holder.Throw(up ? new Vector2(facing, 1f) : new Vector2(facing, 0f));
            }
        }

        void KeyboardPickUpOrDrop()
        {
            if (holder.IsCarrying) { holder.Drop(); return; }
            TryPickUpNearest();
        }
    }
}