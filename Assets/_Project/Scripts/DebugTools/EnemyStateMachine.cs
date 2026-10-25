using System;
using UnityEngine;
using KungFuCrashout.Fighters;
using KungFuCrashout.Props;

namespace KungFuCrashout.DebugTools
{
    /// <summary>
    /// A deliberately SIMPLE, easy-to-beat enemy brain: one enum, one switch. Used by TrainingDummy when its
    /// Behaviour Mode = State Machine (switch back to Dummy any time in the Inspector).
    ///
    ///   Approach  → walk (or dash when far) toward the player. Holding an item and in throw range → Throw.
    ///               A free item nearby (and closer to us than to the player) → FetchItem. In reach → Attack.
    ///   FetchItem → walk / dash to the item and pick it up (same ItemHolder rules as the player) → Approach.
    ///   Attack    → "!" wind-up, then one jab (or sometimes a heavy) → Rest.
    ///   Throw     → "!" wind-up, then throw the item straight across at the player → Rest.
    ///   Rest      → stand still for a moment: the player's opening → Approach.
    ///
    /// Interrupts: while stunned / Downed / KO'd / controls locked it does nothing; a pending wind-up is
    /// cancelled and it goes back to Approach when it can act again. It never blocks on purpose and never parries.
    /// Not a MonoBehaviour: TrainingDummy owns it and ticks it. Place in: Assets/_Project/Scripts/DebugTools/EnemyStateMachine.cs
    /// </summary>
    public class EnemyStateMachine
    {
        public enum State { Approach, FetchItem, Attack, Throw, Rest }

        [Serializable]
        public class Settings
        {
            [Header("Movement")]
            [Tooltip("Walking speed toward the player / an item (world units per second).")]
            [Min(0f)] public float walkSpeed = 2.5f;
            [Tooltip("Dash instead of walking when the target is farther than this (world units). 0 = never dash.")]
            [Min(0f)] public float dashWhenFartherThan = 4f;
            [Tooltip("Minimum seconds between the enemy's dashes, so it can't zip around.")]
            [Min(0f)] public float dashPause = 1f;

            [Header("Attack")]
            [Tooltip("Warning before each attack (seconds). The HUD shows \"!\" over the enemy.")]
            [Min(0f)] public float attackWindUp = 0.5f;
            [Tooltip("Chance an attack is the heavy (which staggers, if Heavy Attack Staggers is on) instead of a jab.")]
            [Range(0f, 1f)] public float heavyChance = 0.2f;

            [Header("Rest (the player's opening)")]
            [Tooltip("Seconds it stands still after attacking or throwing.")]
            [Min(0f)] public float restTime = 0.9f;
            [Tooltip("Random ± fraction on Rest Time.")]
            [Range(0f, 0.5f)] public float restJitter = 0.25f;

            [Header("Items")]
            [Tooltip("Walk over to free items on the stage and pick them up.")]
            public bool fetchItems = true;
            [Tooltip("Only go for items within this distance of the enemy (world units).")]
            [Min(0f)] public float fetchSearchRange = 6f;
            [Tooltip("Only go for an item that is closer to the enemy than to the player (no racing the player for it).")]
            public bool onlyFetchIfCloserThanPlayer = true;
            [Tooltip("Give up on an item it can't reach after this many seconds.")]
            [Min(0.5f)] public float fetchGiveUpTime = 4f;
            [Tooltip("Throw a held item when the player is within this distance (world units). 0 = from anywhere.")]
            [Min(0f)] public float throwRange = 7f;
            [Tooltip("Warning before throwing (seconds). The HUD shows \"!\".")]
            [Min(0f)] public float throwWindUp = 0.6f;

            [Header("Debug")]
            public bool logStateChanges = false;
        }

        public State Current { get; private set; } = State.Approach;
        /// <summary>True during an attack or throw wind-up (the HUD shows "!").</summary>
        public bool IsWindingUp => windUpUntil >= 0f;
        /// <summary>Short readout for the debug HUD.</summary>
        public string Label => $"[State Machine] {Current}";

        readonly Fighter self;
        readonly ItemHolder holder;
        readonly Settings s;
        readonly Func<float> reach;   // centre-to-centre distance its attack connects from

        float stateStart;
        float windUpUntil = -1f;
        float restUntil;
        float nextDashTime;
        float nextItemSearch;
        Carryable targetItem;

        public EnemyStateMachine(Fighter self, ItemHolder holder, Settings settings, Func<float> reach)
        {
            this.self = self;
            this.holder = holder;
            s = settings;
            this.reach = reach;
        }

        /// <summary>Back to Approach with nothing pending (e.g. after a KO or switching modes).</summary>
        public void Reset()
        {
            windUpUntil = -1f;
            targetItem = null;
            Enter(State.Approach);
        }

        public void Tick(float dt)
        {
            var player = self.Opponent;
            if (!player) return;

            // ── Interrupts: can't act → drop whatever was planned. ──
            var fs = self.State;
            if (self.ControlsLocked || fs == FighterState.Hitstun || fs == FighterState.Downed || fs == FighterState.KO)
            {
                if (Current != State.Approach || IsWindingUp) Reset();
                return;
            }
            // Busy finishing its own move: wait.
            if (fs == FighterState.Attacking || fs == FighterState.Dashing || self.IsAirborne) return;

            float dist = Mathf.Abs(player.X - self.X);
            bool holding = holder && holder.IsCarrying;

            switch (Current)
            {
                case State.Approach:
                    if (holding && player.State != FighterState.KO && (s.throwRange <= 0f || dist <= s.throwRange))
                        Enter(State.Throw);
                    else if (!holding && FindItem())
                        Enter(State.FetchItem);
                    else if (dist <= reach() && player.State != FighterState.KO)
                        Enter(State.Attack);
                    else if (player.State != FighterState.KO)
                        MoveToward(player.X, reach() * 0.85f, dt);
                    break;

                case State.FetchItem:
                    if (holding || !targetItem || !targetItem.IsAvailable || Time.time - stateStart > s.fetchGiveUpTime)
                    {
                        targetItem = null;
                        Enter(State.Approach);
                    }
                    else if (holder.CanPickUp(targetItem))
                    {
                        holder.TryPickUp(targetItem);
                        targetItem = null;
                        Enter(State.Approach);
                    }
                    else MoveToward(targetItem.X, 0f, dt);
                    break;

                case State.Attack:
                    if (Time.time < windUpUntil) break;
                    windUpUntil = -1f;
                    if (dist <= reach() * 1.15f)
                    {
                        if (UnityEngine.Random.value < s.heavyChance && self.HeavyAttackData) self.HeavyAttack();
                        else self.Attack();
                        StartRest();
                    }
                    else Enter(State.Approach); // the player stepped away during the wind-up
                    break;

                case State.Throw:
                    if (!holding) { Enter(State.Approach); break; }
                    if (Time.time < windUpUntil) break;
                    windUpUntil = -1f;
                    int dir = player.X >= self.X ? 1 : -1;
                    if (holder.Throw(new Vector2(dir, 0f))) StartRest();
                    else Enter(State.Approach);
                    break;

                case State.Rest:
                    if (Time.time >= restUntil) Enter(State.Approach);
                    break;
            }
        }

        void Enter(State next)
        {
            if (s.logStateChanges && next != Current) Debug.Log($"[{self.name} SM] {Current} → {next}");
            Current = next;
            stateStart = Time.time;
            windUpUntil = next == State.Attack ? Time.time + s.attackWindUp
                        : next == State.Throw ? Time.time + s.throwWindUp
                        : -1f;
        }

        void StartRest()
        {
            float j = 1f + UnityEngine.Random.Range(-s.restJitter, s.restJitter);
            restUntil = Time.time + s.restTime * j;
            Enter(State.Rest);
        }

        /// <summary>Walk toward x, stopping stopDistance short; dash when far (never past the target).</summary>
        void MoveToward(float x, float stopDistance, float dt)
        {
            float dx = x - self.X;
            float gap = Mathf.Abs(dx) - stopDistance;
            if (gap <= 0.02f) return;
            int dir = dx > 0f ? 1 : -1;

            bool farEnough = s.dashWhenFartherThan > 0f && Mathf.Abs(dx) > s.dashWhenFartherThan;
            if (farEnough && Time.time >= nextDashTime && gap >= self.DashDistance)
            {
                self.Dash(dir);
                nextDashTime = Time.time + s.dashPause;
                return;
            }

            if (self.State == FighterState.Idle)
                self.MoveX(dir * Mathf.Min(s.walkSpeed * dt, gap));
        }

        /// <summary>Pick a free item worth fetching (searched a few times a second). True if one is chosen.</summary>
        bool FindItem()
        {
            if (!s.fetchItems || !holder) return false;
            if (Time.time < nextItemSearch) return false;
            nextItemSearch = Time.time + 0.3f;

            var player = self.Opponent;
            Carryable best = null;
            float bestDist = float.MaxValue;
            foreach (var item in UnityEngine.Object.FindObjectsByType<Carryable>(FindObjectsSortMode.None))
            {
                if (!item.IsAvailable) continue;
                float d = Mathf.Abs(item.X - self.X);
                if (d > s.fetchSearchRange || d >= bestDist) continue;
                if (s.onlyFetchIfCloserThanPlayer && player && Mathf.Abs(item.X - player.X) <= d) continue;
                best = item;
                bestDist = d;
            }
            targetItem = best;
            return best;
        }
    }
}
