using System;
using System.Collections.Generic;
using UnityEngine;
using KungFuCrashout.Core;
using KungFuCrashout.Fighters;
using KungFuCrashout.Sound;

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
    ///   Sleeping  — (Dormant) placed by the StageItemDirector at its stage spot: visible (dimmed) but can't be grabbed,
    ///               and its Lifetime hasn't started. A hit shockwave nearby wakes it: it's knocked loose, falls to the
    ///               floor and becomes a normal item.
    /// Sprites: Ground Sprite on the stage and while falling, Held Sprite while carried and in flight, Sleeping Sprite
    /// while asleep (e.g. a lantern hanging from the eaves; empty = the Ground Sprite).
    /// Can Move While Held OFF roots the carrier (no walk, dash or jump) until it's thrown or dropped.
    /// Size: place and scale the item however you like on the stage (background size). The moment a fighter takes it
    /// (pick-up or catch) it grows to its Fight Scale — its proper foreground size next to the fighters — and keeps that
    /// size for the rest of its life (thrown, dropped, lying on the floor), unless Back To Stage Size When Dropped is on.
    /// Moves by transform like the fighters (no Rigidbody2D).
    ///
    /// Setup: a Square sprite on the "Environment" layer with a BoxCollider2D (used for tapping it).
    /// Place in: Assets/_Project/Scripts/Environment/Carryable.cs
    /// </summary>
    [DefaultExecutionOrder(50)] // after fighters move and push apart, so the item never lags a frame
    [RequireComponent(typeof(SpriteRenderer), typeof(Collider2D))]
    public class Carryable : MonoBehaviour
    {
        public enum ItemState { OnStage, Carried, Thrown, Falling, Dormant }

        [SerializeField] StageBounds stage;

        [Header("Sprites")]
        [Tooltip("Shown on the ground (and while sleeping or falling). Empty = the SpriteRenderer's sprite.")]
        [SerializeField] Sprite groundSprite;
        [Tooltip("Shown while a fighter holds it and while it flies after a throw. Empty = the Ground Sprite.")]
        [SerializeField] Sprite heldSprite;
        [Tooltip("Shown while it's asleep at its stage spot, e.g. a lantern HANGING. When a shockwave wakes it, it falls and " +
                 "switches to the Ground Sprite. Empty = the Ground Sprite.")]
        [SerializeField] Sprite sleepingSprite;

        [Header("Size")]
        [Tooltip("Scale once a fighter has taken it (its foreground size next to the fighters). The scale you give it on " +
                 "the stage is kept until then, so stage items can be placed as small as the background needs.")]
        [SerializeField, Min(0.01f)] float fightScale = 1f;
        [Tooltip("Seconds to grow to Fight Scale (or shrink back). 0 = instant.")]
        [SerializeField, Min(0f)] float resizeTime = 0.12f;
        [Tooltip("ON: when it's let go (dropped, or a throw lands) it shrinks back to the size it had on the stage. " +
                 "OFF: it stays at Fight Scale once taken.")]
        [SerializeField] bool backToStageSizeWhenDropped = false;

        [Header("Holding")]
        [Tooltip("ON: the carrier can walk, dash and jump while holding this. OFF: the carrier is rooted (no walk, dash or " +
                 "jump) until it's thrown or dropped. Attacks, blocks and the throw still work.")]
        [SerializeField] bool canMoveWhileHeld = true;

        [Header("Sleeping (stage items before a shockwave wakes them)")]
        [Tooltip("Darken the item while it's asleep, so it reads as \"not grabbable yet\".")]
        [SerializeField] bool dimWhileDormant = true;
        [SerializeField, Range(0.2f, 1f)] float dormantBrightness = 0.65f;
        [Tooltip("Upward speed it's knocked loose with when a shockwave wakes it (world units per second).")]
        [SerializeField, Min(0f)] float wakePopSpeed = 3f;

        [Header("Carry pose")]
        [Tooltip("This item's offset from where it's held (the carrier's CarryPoint, or the distance / angle below if the " +
                 "carrier has none), in world units. X = forward (toward the way the carrier faces, flips with them), " +
                 "Y = up. Use it to fit each item's art in the hands.")]
        [SerializeField] Vector2 carryOffset = Vector2.zero;
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
        [Tooltip("ON: if the carrier's ItemHolder has a Carry Point (e.g. Visual/CarryPoint), sit there instead of using " +
                 "Carry Distance / Angle above. The character decides where it's held.")]
        [SerializeField] bool useCarrierCarryPoint = true;
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

        [Header("Sound")]
        [Tooltip("Whoosh played (through AudioManager) the moment this item is thrown. Empty = silent throw. " +
                 "Set by KungFu Crashout → Set Up Item Throw Audio.")]
        [SerializeField] SfxCue throwCue;
        [Tooltip("Smash played when the item BREAKS: hits a block without being caught, or its last throw (Max Holds) ends. " +
                 "Never on pick-up or catch. Set by KungFu Crashout → Set Up Item Audio.")]
        [SerializeField] SfxCue breakCue;
        [Tooltip("Also play the break sound when the item simply runs out of Lifetime. Off = it vanishes quietly.")]
        [SerializeField] bool breakSoundOnLifetimeEnd = false;

        [Header("Lifetime")]
        [Tooltip("Destroyed this many seconds after it appears (spawn / scene start). If someone is holding it then, it's dropped first; " +
                 "if it's flying, it goes when the flight ends. 0 = lasts forever.")]
        [SerializeField, Min(0f)] float lifetime = 40f;
        [Tooltip("After being held this many times (by any fighter: pick-ups and catches), the NEXT throw is its last: " +
                 "it's destroyed when that throw ends. 0 = no limit.")]
        [SerializeField, Min(0)] int maxHolds = 3;
        [Tooltip("Thrown into a block (shield) and NOT caught = destroyed on impact: a normal block, or a parry that " +
                 "couldn't catch it (hands full / catching off). Off = it bounces off and stays in play.")]
        [SerializeField] bool breakOnShield = true;

        public ItemState State
        {
            get => state;
            private set { state = value; ApplyLook(); }
        }
        ItemState state = ItemState.OnStage;

        /// <summary>Carrier can walk / dash / jump while holding it (off = rooted).</summary>
        public bool CanMoveWhileHeld => canMoveWhileHeld;
        /// <summary>Asleep at its stage spot: visible, not grabbable, waiting for a shockwave.</summary>
        public bool IsDormant => State == ItemState.Dormant;
        /// <summary>The sprite it shows on the ground (prefabs too: used for the drop-in stand-in).</summary>
        public Sprite GroundSprite => groundSprite ? groundSprite : GetComponent<SpriteRenderer>().sprite;
        /// <summary>The sprite while asleep (Sleeping Sprite, else the Ground Sprite).</summary>
        public Sprite SleepingSprite => sleepingSprite ? sleepingSprite : GroundSprite;
        /// <summary>A shockwave woke it (it's now falling to the floor).</summary>
        public event Action Woken;
        /// <summary>The scale it has once a fighter has taken it.</summary>
        public float FightScale => fightScale;
        /// <summary>How many times it has been picked up or caught.</summary>
        public int TimesHeld { get; private set; }
        /// <summary>Seconds until the lifetime runs out (infinity when Lifetime = 0).</summary>
        public float TimeLeft => lifetime > 0f ? Mathf.Max(0f, lifetime - (Time.time - bornTime)) : float.PositiveInfinity;

        float bornTime;
        bool lastThrow;         // held Max Holds times: this throw is the final one
        bool destroyWhenLanded;
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
        Fighter parriedBy;      // set for the frame a fighter parries this item (for the catch)
        bool hitShield;         // set for the frame it hits a block or parry
        readonly HashSet<Fighter> ignoredThisFlight = new HashSet<Fighter>(); // e.g. already KO'd

        Color baseColour;

        void Awake()
        {
            spriteRenderer = GetComponent<SpriteRenderer>();
            if (!groundSprite) groundSprite = spriteRenderer.sprite;
            if (!heldSprite) heldSprite = groundSprite;
            baseColour = spriteRenderer.color;
            ApplyLook(); // ground sprite before measuring, so it rests on the floor by its ground art
            restSortingOrder = spriteRenderer.sortingOrder;

            // Spawned from a prefab (which can't reference scene objects): find the stage ourselves.
            if (!stage) stage = FindFirstObjectByType<StageBounds>();

            stageScale = transform.localScale; // as placed / spawned: the stage (background) size
            targetScale = stageScale;
            noStageY = transform.position.y;
            Measure(stageScale);

            if (!hitData)
            {
                hitData = ScriptableObject.CreateInstance<ThrowableData>();
                hitData.ApplyThrowDefaults();
                hitData.name = name + " (default throw)";
            }
        }

        void Start() => bornTime = Time.time;

        // ───────────── Size (stage → fight) ─────────────

        Vector3 stageScale, targetScale;
        float noStageY;

        /// <summary>Floor height and wall distance for a given scale (from the Ground Sprite, which it rests on).</summary>
        void Measure(Vector3 scale)
        {
            var sprite = groundSprite ? groundSprite : spriteRenderer.sprite;
            Vector3 parent = transform.parent ? transform.parent.lossyScale : Vector3.one;
            Vector2 ext = sprite ? (Vector2)sprite.bounds.extents : new Vector2(0.25f, 0.25f);
            halfWidth = ext.x * Mathf.Abs(scale.x * parent.x);
            // Rest on the stage ground if we know it, otherwise wherever it was placed.
            groundY = stage ? stage.GroundY + ext.y * Mathf.Abs(scale.y * parent.y) : noStageY;
        }

        void ResizeTo(Vector3 scale, bool instant = false)
        {
            targetScale = scale;
            Measure(scale);
            if (instant || resizeTime <= 0f) transform.localScale = scale;
        }

        /// <summary>Straight to Fight Scale (e.g. an item that drops into the fight rather than sitting on the stage).</summary>
        public void UseFightSize() => ResizeTo(Vector3.one * fightScale, instant: true);

        void UpdateSize(float dt)
        {
            if (transform.localScale == targetScale) return;
            float speed = Vector3.Distance(stageScale, Vector3.one * fightScale) / Mathf.Max(0.01f, resizeTime);
            transform.localScale = Vector3.MoveTowards(transform.localScale, targetScale, Mathf.Max(speed, 0.01f) * dt);
        }

        /// <summary>Ground sprite on the stage / asleep / falling, held sprite while carried or flying; dimmed asleep.</summary>
        void ApplyLook()
        {
            if (!spriteRenderer) return;
            bool inHand = state == ItemState.Carried || state == ItemState.Thrown;
            var sprite = inHand ? heldSprite
                       : state == ItemState.Dormant && sleepingSprite ? sleepingSprite
                       : groundSprite;
            if (sprite && spriteRenderer.sprite != sprite) spriteRenderer.sprite = sprite;

            var c = baseColour;
            if (state == ItemState.Dormant && dimWhileDormant)
                c = new Color(c.r * dormantBrightness, c.g * dormantBrightness, c.b * dormantBrightness, c.a);
            spriteRenderer.color = c;
        }

        // ───────────── Called by StageItemDirector ─────────────

        /// <summary>Put it to sleep where it stands (visible, not grabbable, lifetime paused).</summary>
        public void MakeDormant()
        {
            if (State == ItemState.Carried || State == ItemState.Thrown) return;
            fallSpeed = 0f;
            transform.rotation = Quaternion.identity;
            State = ItemState.Dormant;
        }

        /// <summary>A shockwave reached it: knocked loose, it falls to the floor and becomes a normal item.</summary>
        public bool Wake()
        {
            if (State != ItemState.Dormant) return false;
            bornTime = Time.time; // Lifetime starts now
            fallSpeed = -wakePopSpeed; // negative = up; Fall() adds gravity
            State = ItemState.Falling;
            Woken?.Invoke();
            return true;
        }

        void LateUpdate()
        {
            UpdateSize(Time.deltaTime);
            if (CheckExpired()) return;

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
            TimesHeld++;
            ResizeTo(Vector3.one * fightScale); // taken from the stage into the fight: foreground size
            spriteRenderer.sortingOrder = carriedSortingOrder;
            PickedUp?.Invoke(carrier);
            return true;
        }

        /// <summary>Caught out of the air (parry): straight into the catcher's hands, from any state but Carried.</summary>
        public bool Catch(Fighter catcher)
        {
            if (State == ItemState.Carried || !catcher) return false;

            Thrower = null;
            fallSpeed = 0f;
            Carrier = catcher;
            State = ItemState.Carried;
            TimesHeld++;
            ResizeTo(Vector3.one * fightScale);
            destroyWhenLanded = false; // caught mid-air: it lives on for one more throw
            spriteRenderer.sortingOrder = carriedSortingOrder;
            PickedUp?.Invoke(catcher);
            return true;
        }

        public void Drop()
        {
            if (State != ItemState.Carried) return;

            var previous = Carrier;
            int facing = previous ? previous.Facing : 1;
            if (backToStageSizeWhenDropped) ResizeTo(stageScale);

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
            lastThrow = maxHolds > 0 && TimesHeld >= maxHolds;
            travelled = 0f;
            ignoredThisFlight.Clear();
            transform.rotation = Quaternion.identity;
            spriteRenderer.sortingOrder = carriedSortingOrder; // stay in front while flying
            State = ItemState.Thrown;
            AudioManager.Play(throwCue); // safe with no cue / no AudioManager
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
                var catcher = parriedBy;
                bool shield = hitShield;
                parriedBy = null;
                hitShield = false;
                EndFlight(popUp: true);
                // Perfect block on a thrown item = CATCH: straight into the parrier's hands.
                if (catcher && catcher.CatchesParriedItems && catcher.TryGetComponent(out ItemHolder h) && h.TryCatch(this))
                {
                    Debug.Log($"[{catcher.name}] CAUGHT {name} with a parry");
                    return;
                }
                // Hit a shield and wasn't caught: it breaks.
                if (shield && breakOnShield)
                {
                    Debug.Log($"[{name}] broke on a block");
                    Break(withSound: true);
                }
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
            if (result == HitResult.Parried) parriedBy = target;
            hitShield = result == HitResult.Blocked || result == HitResult.Parried;
            if (result == HitResult.Ignored)
            {
                ignoredThisFlight.Add(target); // e.g. already KO'd: fly on through
                return false;
            }

            if (thrower) thrower.ReportThrownHit(target, hitData, result); // sparks, hitstop, sound
            HitFighter?.Invoke(target, thrower, result);
            return true;
        }

        /// <summary>Remove the item for good, with the break sound when asked (never used for pick-ups or catches).</summary>
        void Break(bool withSound)
        {
            if (withSound) AudioManager.Play(breakCue);
            Destroy(gameObject);
        }

        /// <summary>Lifetime over or last throw finished: remove the item. True if it's being destroyed.</summary>
        bool CheckExpired()
        {
            if (State == ItemState.Dormant) return false; // asleep: lifetime hasn't started
            bool timeUp = lifetime > 0f && Time.time - bornTime >= lifetime;
            bool finished = destroyWhenLanded && State != ItemState.Carried;
            if (!timeUp && !finished) return false;

            if (State == ItemState.Thrown) return false; // let the flight (and its hit) finish first
            if (State == ItemState.Carried)
            {
                if (!timeUp) return false;
                // Let go properly so the holder isn't left "carrying" a destroyed item.
                var holder = Carrier ? Carrier.GetComponent<ItemHolder>() : null;
                if (holder && holder.Held == this) holder.Drop(); else Drop();
            }

            Debug.Log($"[{name}] destroyed ({(timeUp ? "lifetime over" : $"held {TimesHeld}x, last throw done")})");
            Break(withSound: !timeUp || breakSoundOnLifetimeEnd);
            return true;
        }

        void EndFlight(bool popUp)
        {
            if (lastThrow) destroyWhenLanded = true; // removed next frame unless it was caught this frame
            Thrower = null;
            fallSpeed = popUp ? -hitPopSpeed : 0f; // negative = moving up; Fall() adds gravity
            spriteRenderer.sortingOrder = restSortingOrder;
            if (backToStageSizeWhenDropped) ResizeTo(stageScale);
            State = !popUp && transform.position.y <= groundY ? ItemState.OnStage : ItemState.Falling;
            FlightEnded?.Invoke();
        }

        Fighter cachedCarrier;
        ItemHolder carrierHolder;

        /// <summary>The carrier's Carry Point position (mirrored with its art), if it has one and this item uses it.</summary>
        bool TryGetCarrierPoint(out Vector3 position)
        {
            position = default;
            if (!useCarrierCarryPoint || !Carrier) return false;
            if (Carrier != cachedCarrier)
            {
                cachedCarrier = Carrier;
                carrierHolder = Carrier.GetComponent<ItemHolder>();
            }
            return carrierHolder && carrierHolder.TryGetCarrySpot(out position);
        }

        Vector3 CarrySpot()
        {
            int facing = Carrier.Facing;
            float rad = carryAngle * Mathf.Deg2Rad;
            float bob = bobAmount > 0f ? Mathf.Sin(Time.time * bobSpeed * Mathf.PI * 2f) * bobAmount : 0f;

            // This item's own nudge from that spot: X forward (flips with facing), Y up.
            float offX = carryOffset.x * facing, offY = carryOffset.y;

            // The character decides: sit on its Carry Point (flips with facing, follows any animation of the point).
            if (TryGetCarrierPoint(out Vector3 p))
                return new Vector3(p.x + offX, p.y + offY + bob, transform.position.z);

            Vector3 c = Carrier.transform.position;
            return new Vector3(
                c.x + Mathf.Cos(rad) * carryDistance * facing + offX,
                c.y + Mathf.Sin(rad) * carryDistance + offY + bob,
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

#if UNITY_EDITOR
        // Editor preview: the SpriteRenderer shows the Sleeping Sprite if there is one (that's how it sits where you place
        // it), else the Ground Sprite; the tap collider fits the Ground Sprite. Done a moment later: changing a sprite
        // inside OnValidate warns.
        void OnValidate()
        {
            if (Application.isPlaying || !groundSprite) return;
            UnityEditor.EditorApplication.delayCall += SyncEditorPreview;
        }

        void SyncEditorPreview()
        {
            if (!this || !groundSprite) return;
            var sr = GetComponent<SpriteRenderer>();
            var preview = sleepingSprite ? sleepingSprite : groundSprite;
            if (!sr || sr.sprite == preview) return;
            UnityEditor.Undo.RecordObject(sr, "Item Ground Sprite");
            sr.sprite = preview;
            UnityEditor.EditorUtility.SetDirty(sr);
            if (TryGetComponent(out BoxCollider2D box))
            {
                UnityEditor.Undo.RecordObject(box, "Item Ground Sprite");
                box.size = groundSprite.bounds.size;
                box.offset = groundSprite.bounds.center;
                UnityEditor.EditorUtility.SetDirty(box);
            }
        }
#endif

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