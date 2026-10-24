using System;
using System.Collections.Generic;
using UnityEngine;
using KungFuCrashout.Core;
using KungFuCrashout.Fighters;

namespace KungFuCrashout.Props
{
    /// <summary>
    /// A stage object a fighter can pick up and carry (M5, step 1 of environmental combat).
    ///
    ///   On stage  — sits on the ground. A fighter close enough can pick it up.
    ///   Carried   — hovers in front of the carrier, raised at an angle and tilted, with a slight bob.
    ///               Follows the carrier everywhere (dash, jump) and flips when they turn round.
    ///   Dropped   — placed a little in front of the carrier and falls back to the ground.
    ///   Thrown    — flies from the carry spot in the throw direction at a constant speed, in a straight
    ///               line with no gravity. The flight ends when it reaches the ground, a stage wall,
    ///               its max distance, or touches a fighter other than the thrower; then it drops to the
    ///               ground and can be picked up again.
    ///   Hitting   — touching a fighter mid-flight calls Fighter.ReceiveThrownHit with this item's
    ///               Throwable Data: damage + a launch along the throw (wall bounce included), or chip
    ///               and pushback if blocked from the front, or nothing if parried. The check is swept
    ///               along the whole frame's movement, so a fast item can't skip through a fighter.
    ///               The item then pops up a little and falls. (Parry-catch comes next.)
    /// Moves by transform like the fighters (no Rigidbody2D).
    ///
    /// Setup: a Square sprite on the "Environment" layer with a BoxCollider2D (used for tapping it).
    /// Place in: Assets/_Project/Scripts/Environment/Carryable.cs
    /// </summary>
    [DefaultExecutionOrder(50)] // after fighters move and push apart, so the item never lags a frame
    [RequireComponent(typeof(SpriteRenderer), typeof(Collider2D))]
    public class Carryable : MonoBehaviour
    {
        public enum ItemState { OnStage, Carried, Thrown, Falling }

        [SerializeField] StageBounds stage;

        [Header("Carry pose")]
        [Tooltip("How far from the carrier's centre the item hovers (world units).")]
        [SerializeField, Min(0f)] float carryDistance = 1.0f;
        [Tooltip("Angle of that offset above horizontal, toward the facing side (degrees). 0 = straight ahead, 90 = overhead.")]
        [SerializeField, Range(-180f, 180f)] float carryAngle = 35f;
        [Tooltip("How much the item itself is tilted while carried (degrees, leaning toward the facing side).")]
        [SerializeField, Range(-90f, 90f)] float carryTilt = -20f;
        [Tooltip("Up-and-down hover distance (world units). 0 = no bob.")]
        [SerializeField, Min(0f)] float bobAmount = 0.06f;
        [Tooltip("Hover bobs per second.")]
        [SerializeField, Min(0f)] float bobSpeed = 2f;
        [Tooltip("How quickly the item catches up with the carry spot. Higher = snappier.")]
        [SerializeField, Min(1f)] float followSharpness = 25f;
        [Tooltip("Draw order while carried, so it shows in front of the fighter.")]
        [SerializeField] int carriedSortingOrder = 10;

        [Header("Throw")]
        [Tooltip("Flight speed (world units per second). The same for every throw, however fast the swipe.")]
        [SerializeField, Min(0.1f)] float throwSpeed = 14f;
        [Tooltip("The flight ends after travelling this far (world units).")]
        [SerializeField, Min(0.5f)] float maxThrowDistance = 14f;
        [Tooltip("Damage, knockback and wall-bounce numbers for a hit. Empty = built-in crate defaults (12 damage, heavy).")]
        [SerializeField] ThrowableData hitData;
        [Tooltip("Hit fighters it touches (ending the flight). Off = it flies through them.")]
        [SerializeField] bool hitsFighters = true;
        [Tooltip("Upward speed the item bounces off a fighter with before falling (world units per second).")]
        [SerializeField, Min(0f)] float hitPopSpeed = 4f;

        [Header("Drop")]
        [Tooltip("How far in front of the carrier the item lands when dropped.")]
        [SerializeField, Min(0f)] float dropDistance = 0.8f;
        [SerializeField, Min(0.1f)] float gravity = 30f;

        public ItemState State { get; private set; } = ItemState.OnStage;
        public Fighter Carrier { get; private set; }
        public bool IsAvailable => State == ItemState.OnStage;
        public float X => transform.position.x;

        /// <summary>(carrier) picked it up.</summary>
        public event Action<Fighter> PickedUp;
        /// <summary>(previous carrier) let go of it.</summary>
        public event Action<Fighter> Dropped;
        /// <summary>(thrower, direction) the item left the carrier's hands.</summary>
        public event Action<Fighter, Vector2> Thrown;
        /// <summary>(fighter touched, thrower, result) the flying item reached a fighter.</summary>
        public event Action<Fighter, Fighter, HitResult> HitFighter;
        /// <summary>The flight ended (ground, wall, max distance or a fighter).</summary>
        public event Action FlightEnded;

        /// <summary>Who threw it while it's flying; null otherwise.</summary>
        public Fighter Thrower { get; private set; }
        public Vector2 ThrowDirection { get; private set; }
        public ThrowableData HitData => hitData;

        SpriteRenderer spriteRenderer;
        int restSortingOrder;
        float groundY;          // the Y the item rests at on the stage
        float halfWidth;
        float fallSpeed;
        float travelled;
        readonly List<Collider2D> overlaps = new List<Collider2D>();
        readonly HashSet<Fighter> ignoredThisFlight = new HashSet<Fighter>(); // e.g. already KO'd

        void Awake()
        {
            spriteRenderer = GetComponent<SpriteRenderer>();
            restSortingOrder = spriteRenderer.sortingOrder;
            halfWidth = spriteRenderer.bounds.extents.x;

            // Rest on the stage ground if we know it, otherwise wherever it was placed.
            groundY = stage ? stage.GroundY + spriteRenderer.bounds.extents.y : transform.position.y;

            if (!hitData)
            {
                hitData = ScriptableObject.CreateInstance<ThrowableData>();
                hitData.ApplyThrowDefaults();
                hitData.name = name + " (default throw)";
            }
        }

        void LateUpdate()
        {
            switch (State)
            {
                case ItemState.Carried: FollowCarrier(Time.deltaTime); break;
                case ItemState.Thrown:  Fly(Time.deltaTime); break;
                case ItemState.Falling: Fall(Time.deltaTime); break;
            }
        }

        // ───────────── Called by ItemHolder ─────────────

        public bool PickUp(Fighter carrier)
        {
            if (!IsAvailable || !carrier) return false;

            Carrier = carrier;
            State = ItemState.Carried;
            spriteRenderer.sortingOrder = carriedSortingOrder;
            PickedUp?.Invoke(carrier);
            return true;
        }

        public void Drop()
        {
            if (State != ItemState.Carried) return;

            var previous = Carrier;
            int facing = previous ? previous.Facing : 1;

            // Land a little in front of the carrier, inside the stage.
            var p = transform.position;
            if (previous) p.x = previous.X + facing * dropDistance;
            if (stage) p.x = stage.ClampX(p.x, halfWidth);
            transform.position = p;
            transform.rotation = Quaternion.identity;

            Carrier = null;
            fallSpeed = 0f;
            State = ItemState.Falling;
            spriteRenderer.sortingOrder = restSortingOrder;
            Dropped?.Invoke(previous);
        }

        /// <summary>Launch from where it hovers, in a straight line along direction (world space).</summary>
        public bool Throw(Vector2 direction, Fighter thrower)
        {
            if (State != ItemState.Carried || direction.sqrMagnitude < 0.0001f) return false;

            Carrier = null;
            Thrower = thrower;
            ThrowDirection = direction.normalized;
            travelled = 0f;
            ignoredThisFlight.Clear();
            transform.rotation = Quaternion.identity;
            spriteRenderer.sortingOrder = carriedSortingOrder; // stay in front while flying
            State = ItemState.Thrown;
            Thrown?.Invoke(thrower, ThrowDirection);
            return true;
        }

        // ───────────── Motion ─────────────

        void Fly(float dt)
        {
            Vector3 from = transform.position;
            float step = throwSpeed * dt;
            Vector3 p = from + (Vector3)(ThrowDirection * step);
            travelled += step;

            bool hitGround = p.y <= groundY;
            if (hitGround) p.y = groundY;

            bool hitWall = stage && (p.x <= stage.MinX + halfWidth || p.x >= stage.MaxX - halfWidth);
            if (hitWall) p.x = stage.ClampX(p.x, halfWidth);

            transform.position = p;

            // Fighters first: an item that reaches a fighter and the floor in the same frame still hits.
            if (hitsFighters && TryHitFighter(from, p))
            {
                EndFlight(popUp: true);
                return;
            }

            if (hitGround || hitWall || travelled >= maxThrowDistance) EndFlight(popUp: false);
        }

        /// <summary>
        /// Swept check: a box covering everything the item passed through this frame. The nearest fighter
        /// (other than the thrower) inside it takes the hit. Returns true if the flight should end.
        /// </summary>
        bool TryHitFighter(Vector3 from, Vector3 to)
        {
            Physics2D.SyncTransforms(); // fighters moved this frame by transform

            Vector2 travel = (Vector2)to - (Vector2)from;
            Vector2 size = (Vector2)spriteRenderer.bounds.size + new Vector2(Mathf.Abs(travel.x), Mathf.Abs(travel.y));
            Vector2 centre = ((Vector2)from + (Vector2)to) * 0.5f;

            overlaps.Clear();
            Physics2D.OverlapBox(centre, size, 0f, new ContactFilter2D().NoFilter(), overlaps);

            // Nearest to where the item started this frame = the one it reached first.
            Fighter target = null;
            float best = float.MaxValue;
            foreach (var col in overlaps)
            {
                var f = col ? col.GetComponentInParent<Fighter>() : null;
                if (!f || f == Thrower || ignoredThisFlight.Contains(f)) continue;
                float d = ((Vector2)f.transform.position - (Vector2)from).sqrMagnitude;
                if (d < best) { best = d; target = f; }
            }
            if (!target) return false;

            var thrower = Thrower;
            var result = target.ReceiveThrownHit(thrower, hitData, ThrowDirection);
            if (result == HitResult.Ignored)
            {
                ignoredThisFlight.Add(target); // e.g. already KO'd: fly on through
                return false;
            }

            if (thrower) thrower.ReportThrownHit(target, hitData, result); // sparks, hitstop, sound
            HitFighter?.Invoke(target, thrower, result);
            return true;
        }

        void EndFlight(bool popUp)
        {
            Thrower = null;
            fallSpeed = popUp ? -hitPopSpeed : 0f; // negative = moving up; Fall() adds gravity
            spriteRenderer.sortingOrder = restSortingOrder;
            State = !popUp && transform.position.y <= groundY ? ItemState.OnStage : ItemState.Falling;
            FlightEnded?.Invoke();
        }

        Vector3 CarrySpot()
        {
            int facing = Carrier.Facing;
            float rad = carryAngle * Mathf.Deg2Rad;
            float bob = bobAmount > 0f ? Mathf.Sin(Time.time * bobSpeed * Mathf.PI * 2f) * bobAmount : 0f;

            Vector3 c = Carrier.transform.position;
            return new Vector3(
                c.x + Mathf.Cos(rad) * carryDistance * facing,
                c.y + Mathf.Sin(rad) * carryDistance + bob,
                transform.position.z);
        }

        void FollowCarrier(float dt)
        {
            if (!Carrier) { State = ItemState.Falling; return; }

            // Smooth follow (frame-rate independent), then tilt toward the facing side.
            float t = 1f - Mathf.Exp(-followSharpness * dt);
            transform.position = Vector3.Lerp(transform.position, CarrySpot(), t);
            transform.rotation = Quaternion.Euler(0f, 0f, carryTilt * Carrier.Facing);
        }

        void Fall(float dt)
        {
            fallSpeed += gravity * dt;
            var p = transform.position;
            p.y -= fallSpeed * dt;

            if (p.y <= groundY)
            {
                p.y = groundY;
                State = ItemState.OnStage;
            }
            transform.position = p;
        }

        void OnDrawGizmosSelected()
        {
            // Preview the carry spot for a right-facing carrier standing at the item's position.
            float rad = carryAngle * Mathf.Deg2Rad;
            Gizmos.color = Color.cyan;
            Vector3 from = transform.position;
            Gizmos.DrawLine(from, from + new Vector3(Mathf.Cos(rad), Mathf.Sin(rad), 0f) * carryDistance);
        }
    }
}