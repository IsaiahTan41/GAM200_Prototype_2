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
    ///   • While carrying: you keep dashing, jumping and attacking; the item hovers in front.
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

        public Carryable Held { get; private set; }
        public bool IsCarrying => Held;
        public Fighter Fighter => fighter;
        public float PickUpRange => pickUpRange;

        public event Action<Carryable> PickedUp;
        public event Action<Carryable> Dropped;
        public event Action<Carryable, Vector2> Threw;
        /// <summary>Caught a thrown item with a parry (also fires PickedUp).</summary>
        public event Action<Carryable> Caught;

        Fighter fighter;

        void Awake() => fighter = GetComponent<Fighter>();

        void OnEnable()
        {
            fighter.GotHit += OnGotHit;
            fighter.StateChanged += OnStateChanged;
        }

        void OnDisable()
        {
            fighter.GotHit -= OnGotHit;
            fighter.StateChanged -= OnStateChanged;
            Drop();
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
            PickedUp?.Invoke(item);
            Debug.Log($"[{name}] picked up {item.name}");
            return true;
        }

        /// <summary>Catch an item out of the air (perfect block). Ignores range and state, but needs empty hands.</summary>
        public bool TryCatch(Carryable item)
        {
            if (!item || IsCarrying || fighter.State == FighterState.KO || !item.Catch(fighter)) return false;

            Held = item;
            PickedUp?.Invoke(item);
            Caught?.Invoke(item);
            return true;
        }

        public void Drop()
        {
            if (!Held) return;

            var item = Held;
            Held = null;
            item.Drop();
            Dropped?.Invoke(item);
            Debug.Log($"[{name}] dropped {item.name}");
        }

        /// <summary>True if the held item can be thrown right now.</summary>
        public bool CanThrow =>
            IsCarrying && !fighter.ControlsLocked &&
            fighter.State != FighterState.Hitstun && fighter.State != FighterState.KO &&
            fighter.State != FighterState.Downed;

        /// <summary>Throw the held item along direction (world space). Returns false if nothing was thrown.</summary>
        public bool Throw(Vector2 direction)
        {
            if (!CanThrow) return false;

            var item = Held;
            if (!item.Throw(direction, fighter)) return false;

            Held = null;
            Threw?.Invoke(item, direction.normalized);
            Debug.Log($"[{name}] threw {item.name} toward {direction.normalized}");
            return true;
        }

        void OnGotHit(Fighter attacker, AttackData attack)
        {
            if (dropWhenHit) Drop();
        }

        void OnStateChanged(FighterState previous, FighterState next)
        {
            // A KO always drops the item.
            if (next == FighterState.KO) Drop();
        }
    }
}