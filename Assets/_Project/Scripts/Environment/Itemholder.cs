using System;
using UnityEngine;
using KungFuCrashout.Fighters;

namespace KungFuCrashout.Props
{
    /// <summary>
    /// Lets a fighter pick up, carry, drop and throw one Carryable item (M5).
    /// Works for the player and, later, the AI — it only needs a Fighter.
    ///
    ///   • Pick up: only when the item is on the stage and within Pick Up Range (horizontal distance),
    ///     and the fighter is free (idle, blocking, dashing or airborne — not attacking, in hitstun or KO'd).
    ///   • While carrying: you keep dashing, jumping and attacking; the item hovers in front. An item with Can Move While
    ///     Held OFF roots you instead (no walk, dash or jump) until it's thrown or dropped.
    ///   • Throw: launches the item along a direction (from the swipe), unless the fighter is stunned,
    ///     KO'd or the round isn't live.
    ///   • Getting hit drops the item (optional); being KO'd always drops it.
    ///
    /// Setup: add to the Player (and Opponent later) next to Fighter.
    /// Place in: Assets/_Project/Scripts/Environment/ItemHolder.cs
    /// </summary>
    [RequireComponent(typeof(Fighter))]
    public class ItemHolder : MonoBehaviour
    {
        [Tooltip("Max horizontal distance (world units) between fighter and item to pick it up.")]
        [SerializeField, Min(0f)] float pickUpRange = 1.5f;
        [Tooltip("Drop the item when hit.")]
        [SerializeField] bool dropWhenHit = true;

        [Header("Carry point")]
        [Tooltip("Where this fighter holds items. Empty = a child named \"CarryPoint\" anywhere under the fighter " +
                 "(e.g. Visual/CarryPoint). Put it under Visual so it flips with facing and can be keyed in clips.")]
        [SerializeField] Transform carryPoint;
        [Tooltip("ON: held items sit on the Carry Point. OFF (or no Carry Point): each item's own Carry Distance / Angle.")]
        [SerializeField] bool useCarryPoint = true;

        [Header("Throw wind-up")]
        [Tooltip("Throwing plays a wind-up first and the item only leaves on the release frame (the Throw Attack's " +
                 "Startup, or its clip's HitboxOn event if the FighterAnimator has one). Getting hit during it drops " +
                 "the item. Off, or no Throw Attack = the item leaves instantly, as before.")]
        [SerializeField] bool throwWindup = true;
        [Tooltip("Timing for the wind-up: Startup = time until the item leaves, Recovery = time after. No hitbox is used.")]
        [SerializeField] AttackData throwAttack;

        public Carryable Held { get; private set; }
        public bool IsCarrying => Held;
        public Fighter Fighter => fighter;
        public float PickUpRange => pickUpRange;
        /// <summary>Where held items sit, or null to use the item's own carry pose.</summary>
        public Transform CarryPoint => useCarryPoint ? carryPoint : null;

        public event Action<Carryable> PickedUp;
        public event Action<Carryable> Dropped;
        public event Action<Carryable, Vector2> Threw;
        /// <summary>Caught a thrown item with a parry (also fires PickedUp).</summary>
        public event Action<Carryable> Caught;

        Fighter fighter;
        bool windingUp;
        Vector2 pendingThrowDirection;

        /// <summary>A throw has started its wind-up and the item hasn't left yet.</summary>
        public bool IsWindingUpThrow => windingUp;

        void Awake()
        {
            fighter = GetComponent<Fighter>();
            if (!carryPoint) carryPoint = FindChild(transform, "CarryPoint");
        }

        FighterPoseView poseView;

        /// <summary>
        /// World position of the Carry Point, mirrored across the art's pivot while the art is drawn mirrored (backdash /
        /// dash-through), so the item stays on the same side of the character as it looks. False = no Carry Point.
        /// </summary>
        public bool TryGetCarrySpot(out Vector3 position)
        {
            position = default;
            var point = CarryPoint;
            if (!point) return false;

            position = point.position;
            if (!poseView) poseView = GetComponentInChildren<FighterPoseView>();
            if (poseView && poseView.ArtMirrored)
            {
                float pivotX = poseView.transform.position.x; // flipX mirrors the sprite around its pivot
                position.x = 2f * pivotX - position.x;
            }
            return true;
        }

        static Transform FindChild(Transform parent, string childName)
        {
            foreach (Transform c in parent)
            {
                if (c.name == childName) return c;
                var deeper = FindChild(c, childName);
                if (deeper) return deeper;
            }
            return null;
        }

        void OnEnable()
        {
            fighter.GotHit += OnGotHit;
            fighter.StateChanged += OnStateChanged;
            fighter.ThrowReleased += OnThrowReleased;
        }

        void OnDisable()
        {
            fighter.GotHit -= OnGotHit;
            fighter.StateChanged -= OnStateChanged;
            fighter.ThrowReleased -= OnThrowReleased;
            windingUp = false;
            Drop();
            UpdateRoot();
        }

        /// <summary>True if this fighter could pick the item up right now.</summary>
        public bool CanPickUp(Carryable item)
        {
            if (!item || IsCarrying || !item.IsAvailable || fighter.ControlsLocked) return false;

            var s = fighter.State;
            bool free = s == FighterState.Idle || s == FighterState.Blocking ||
                        s == FighterState.Dashing || s == FighterState.Airborne;
            return free && Mathf.Abs(item.X - fighter.X) <= pickUpRange;
        }

        public bool TryPickUp(Carryable item)
        {
            if (!CanPickUp(item) || !item.PickUp(fighter)) return false;

            Held = item;
            UpdateRoot();
            PickedUp?.Invoke(item);
            Debug.Log($"[{name}] picked up {item.name}");
            return true;
        }

        /// <summary>Catch an item out of the air (perfect block). Ignores range and state, but needs empty hands.</summary>
        public bool TryCatch(Carryable item)
        {
            if (!item || IsCarrying || fighter.State == FighterState.KO || !item.Catch(fighter)) return false;

            Held = item;
            UpdateRoot();
            PickedUp?.Invoke(item);
            Caught?.Invoke(item);
            return true;
        }

        public void Drop()
        {
            if (!Held) return;

            var item = Held;
            Held = null;
            UpdateRoot();
            item.Drop();
            Dropped?.Invoke(item);
            Debug.Log($"[{name}] dropped {item.name}");
        }

        /// <summary>True if the held item can be thrown right now.</summary>
        public bool CanThrow =>
            IsCarrying && !windingUp && !fighter.ControlsLocked &&
            fighter.State != FighterState.Hitstun && fighter.State != FighterState.KO &&
            fighter.State != FighterState.Downed;

        /// <summary>
        /// Throw the held item along direction (world space). With Throw Wind-up this starts the wind-up and the item
        /// leaves on the release frame. Returns false if nothing was thrown / started.
        /// </summary>
        public bool Throw(Vector2 direction)
        {
            if (!CanThrow) return false;

            if (throwWindup && throwAttack)
            {
                if (!fighter.StartThrow(throwAttack)) return false; // busy (attacking, stunned…)
                windingUp = true;
                pendingThrowDirection = direction;
                return true;
            }
            return Release(direction);
        }

        /// <summary>The wind-up reached its release frame: let go now.</summary>
        void OnThrowReleased()
        {
            if (!windingUp) return;
            windingUp = false;
            if (Held) Release(pendingThrowDirection);
        }

        bool Release(Vector2 direction)
        {
            var item = Held;
            if (!item || !item.Throw(direction, fighter)) return false;

            Held = null;
            UpdateRoot();
            Threw?.Invoke(item, direction.normalized);
            Debug.Log($"[{name}] threw {item.name} toward {direction.normalized}");
            return true;
        }

        /// <summary>Holding an item with Can Move While Held OFF roots the fighter.</summary>
        void UpdateRoot()
        {
            if (fighter) fighter.MovementRooted = Held && !Held.CanMoveWhileHeld;
        }

        void LateUpdate()
        {
            // The item can vanish while held (lifetime, broken): never leave the fighter rooted.
            if (!Held && fighter && fighter.MovementRooted) UpdateRoot();
        }

        void OnGotHit(Fighter attacker, AttackData attack)
        {
            if (dropWhenHit) Drop();
        }

        void OnStateChanged(FighterState previous, FighterState next)
        {
            // Interrupted before the release frame (hit, KO…): the throw is off.
            if (windingUp && previous == FighterState.Attacking && next != FighterState.Attacking) windingUp = false;

            // A KO always drops the item.
            if (next == FighterState.KO) Drop();
        }
    }
}