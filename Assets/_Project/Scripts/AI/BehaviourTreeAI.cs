using System;
using System.Collections.Generic;
using UnityEngine;
using KungFuCrashout.Core;
using KungFuCrashout.Fighters;
using KungFuCrashout.Props;
using Random = UnityEngine.Random;

namespace KungFuCrashout.AI
{
    /// <summary>
    /// GDD BUILD opponent: a behaviour tree that plays the GDD's rock-paper-scissors loop, in three difficulty tiers.
    ///
    ///   Thrown items beat neutral → it fetches stage items, throws them when the player is open, and RACES for them on Hard
    ///   Block beats thrown items  → it catches (timed block), jumps over or blocks the player's throws
    ///   Heavy / combos beat block → it pressures a turtling player's stamina into a guard break
    ///   Neutral beats melee       → it trusts the auto-guard at neutral and saves manual blocks / parries for heavies and specials
    ///   Punish windows            → a Downed, parry-stunned or whiffing player gets rushed down (melee only damages Downed fighters)
    ///   Movement                  → dashes, gap-closing Elbow Slide, anti-air Uppercut, Leg Sweep against cross-ups,
    ///                               combo breaker dash when it's being comboed
    ///
    /// It drives its Fighter ONLY through IFighterCommands (plus Fighter.MoveX for walking and ItemHolder for items), exactly
    /// like the player's touch input, so every combat rule applies to it unchanged.
    ///
    /// Tiers (Profile below; picked from Settings / the Roster unless Use Saved Difficulty is off):
    ///   EASY   — slow reactions, shows "!" before every attack and throw, rarely defends, never parries or catches
    ///   NORMAL — reacts to throws most of the time, blocks heavies, sometimes parries, uses specials
    ///   HARD   — fast reactions, catches throws, parries, races for items, breaks guards and breaks combos
    ///
    /// The tree (top = highest priority):
    ///   Can't act → Combo breaker → Stunned (wait) → Item incoming (catch / jump / block) → Guarding (hold) →
    ///   Telegraphing (Easy "!") → Busy (own move) → Moving (walk / dash to a goal) →
    ///   DECIDE every Think Interval: Punish → Throw held item → Break guard → Anti-air / anti-cross-up →
    ///                                Fetch item → Attack in range → Close in → Spacing
    ///
    /// Debug: Show Debug Label draws the current branch above its head (also on the phone).
    /// Setup: KungFu Crashout → GDD Build → Upgrade Fight Scene adds it to the Opponent (and turns TrainingDummy off).
    /// Place in: Assets/_Project/Scripts/AI/BehaviourTreeAI.cs
    /// </summary>
    [RequireComponent(typeof(Fighter))]
    public class BehaviourTreeAI : MonoBehaviour
    {
        [Serializable]
        public class Profile
        {
            [Header("Speed of thought")]
            [Tooltip("Seconds between offensive decisions (defence reacts every frame).")]
            [Min(0.05f)] public float thinkInterval = 0.3f;
            [Tooltip("Seconds before it can react to something the player started (an attack, a throw).")]
            [Min(0f)] public float reactionTime = 0.25f;
            [Tooltip("Easy tier: a \"!\" warning this long before each attack or throw. 0 = no warning.")]
            [Min(0f)] public float telegraphTime = 0f;

            [Header("Movement")]
            [Min(0f)] public float walkSpeed = 2.8f;
            [Tooltip("Dash instead of walking when the goal is farther than this (world units).")]
            [Min(0f)] public float dashWhenFarther = 3.5f;
            [Tooltip("Minimum seconds between its own dashes.")]
            [Min(0f)] public float dashPause = 0.7f;
            [Tooltip("Chance per decision to step back / hop when nothing better to do.")]
            [Range(0f, 1f)] public float retreatChance = 0.15f;

            [Header("Offence")]
            [Min(0f)] public float attackCooldown = 0.9f;
            [Range(0f, 1f)] public float comboChance = 0.6f;
            [Tooltip("Chance a neutral attack is the heavy (it staggers if it isn't blocked, but is punishable).")]
            [Range(0f, 1f)] public float heavyChance = 0.25f;
            [Tooltip("Chance to use a special when one fits (gap close, launcher, sweep, combo ender).")]
            [Range(0f, 1f)] public float specialChance = 0.3f;
            [Tooltip("Chance to punish a Downed / parry-stunned / whiffing player.")]
            [Range(0f, 1f)] public float punishChance = 0.75f;
            [Tooltip("Chance to pressure a player who is blocking with low stamina (guard break).")]
            [Range(0f, 1f)] public float guardBreakChance = 0.3f;
            [Tooltip("Chance to Uppercut a player jumping in close.")]
            [Range(0f, 1f)] public float antiAirChance = 0.3f;

            [Header("Defence")]
            [Tooltip("Chance to manually block a heavy / special it sees coming (lights are auto-guarded at neutral).")]
            [Range(0f, 1f)] public float blockChance = 0.4f;
            [Tooltip("Chance to time a parry instead (any attack it sees coming).")]
            [Range(0f, 1f)] public float parryChance = 0.15f;
            [Tooltip("Chance to dash out of a combo (combo breaker) when it's been hit twice in a row.")]
            [Range(0f, 1f)] public float comboBreakChance = 0.3f;

            [Header("Items")]
            [Tooltip("Chance to go and fetch a free stage item.")]
            [Range(0f, 1f)] public float fetchChance = 0.8f;
            [Tooltip("ON: goes for items even when the player is closer to them (races).")]
            public bool raceForItems = false;
            [Tooltip("Throws a held item when the player is within this distance (world units).")]
            [Min(1f)] public float throwRange = 8f;
            [Tooltip("Reaction to an item thrown at it, rolled once per throw: catch (timed block) / jump over / block. " +
                     "Whatever is left over = it gets hit.")]
            [Range(0f, 1f)] public float catchChance = 0.2f;
            [Range(0f, 1f)] public float jumpOverChance = 0.35f;
            [Range(0f, 1f)] public float itemBlockChance = 0.35f;
        }

        [Header("Difficulty")]
        [Tooltip("Use the difficulty chosen on the Roster / in Settings. Off = Difficulty below (testing).")]
        [SerializeField] bool useSavedDifficulty = true;
        [SerializeField] Difficulty difficulty = Difficulty.Normal;

        [SerializeField] Profile easy = new Profile
        {
            thinkInterval = 0.5f, reactionTime = 0.45f, telegraphTime = 0.45f,
            walkSpeed = 2.2f, dashWhenFarther = 4.5f, dashPause = 1.2f, retreatChance = 0.1f,
            attackCooldown = 1.6f, comboChance = 0.3f, heavyChance = 0.15f, specialChance = 0.1f, punishChance = 0.4f,
            guardBreakChance = 0f, antiAirChance = 0f,
            blockChance = 0.15f, parryChance = 0f, comboBreakChance = 0f,
            fetchChance = 0.5f, raceForItems = false, throwRange = 7f,
            catchChance = 0f, jumpOverChance = 0.15f, itemBlockChance = 0.2f
        };
        [SerializeField] Profile normal = new Profile();
        [SerializeField] Profile hard = new Profile
        {
            thinkInterval = 0.16f, reactionTime = 0.14f, telegraphTime = 0f,
            walkSpeed = 3.2f, dashWhenFarther = 3f, dashPause = 0.35f, retreatChance = 0.2f,
            attackCooldown = 0.4f, comboChance = 0.9f, heavyChance = 0.3f, specialChance = 0.5f, punishChance = 1f,
            guardBreakChance = 0.6f, antiAirChance = 0.6f,
            blockChance = 0.6f, parryChance = 0.4f, comboBreakChance = 0.7f,
            fetchChance = 1f, raceForItems = true, throwRange = 9f,
            catchChance = 0.45f, jumpOverChance = 0.3f, itemBlockChance = 0.2f
        };

        [Header("Tuning")]
        [Tooltip("Extra distance added to an attack's reach when deciding 'in range'.")]
        [SerializeField] float rangePadding = 0.2f;
        [Tooltip("Only go for items within this distance (world units).")]
        [SerializeField, Min(0f)] float fetchSearchRange = 7f;
        [Tooltip("Don't throw from closer than this (it would rather fight up close).")]
        [SerializeField, Min(0f)] float minThrowDistance = 1.6f;
        [Tooltip("Seconds before impact to jump over a thrown item.")]
        [SerializeField, Range(0.1f, 0.5f)] float jumpLead = 0.28f;
        [Tooltip("Seconds before impact to press block for a catch (the thrown-item parry window is ~0.2 s).")]
        [SerializeField, Range(0.02f, 0.2f)] float catchLead = 0.09f;

        [Header("Debug")]
        [Tooltip("Shows the current behaviour-tree branch above the AI (on the phone too).")]
        [SerializeField] bool showDebugLabel = false;
        [SerializeField] bool logDecisions = false;

        // ───────────── Public read-outs (HUD "!" and debug) ─────────────

        /// <summary>True during an Easy-tier warning before an attack or throw (MatchHUD shows "!").</summary>
        public bool IsTelegraphing => Time.time < telegraphUntil;
        /// <summary>The branch of the tree that acted last, e.g. "Root > Decide > Punish > Heavy".</summary>
        public string Intent => ctx.Trace;
        public Difficulty ActiveDifficulty => tier;
        public bool ShowDebugLabel { get => showDebugLabel; set => showDebugLabel = value; }

        /// <summary>Force a tier now (e.g. from the Options menu).</summary>
        public void SetDifficulty(Difficulty d)
        {
            difficulty = d;
            useSavedDifficulty = false;
            tier = d;
            p = ProfileFor(d);
        }

        // ───────────── Private ─────────────

        Fighter self;
        ItemHolder holder;
        IFighterCommands commands;
        Collider2D body;
        Profile p;
        Difficulty tier = Difficulty.Normal;
        BtNode root;
        readonly BtContext ctx = new BtContext();

        float nextThink, nextAttack, nextDash;
        float telegraphUntil = -1f;
        Action telegraphed;

        // guard (block) timing
        float guardFrom = -1f, guardUntil = -1f;
        bool holdingBlock;

        // move goal
        bool hasGoal;
        float goalX, goalStop;
        Carryable fetchTarget;
        float fetchStart;

        // planned follow-ups (combo strings)
        struct Planned { public float time; public Action action; }
        readonly List<Planned> planned = new List<Planned>();

        // thrown-item threat
        enum ItemReaction { None, Catch, Jump, Block }
        Carryable threatItem;
        float threatSeenAt;
        ItemReaction threatReaction;
        bool threatHandled;
        readonly Dictionary<Carryable, Vector2> lastItemPos = new Dictionary<Carryable, Vector2>();
        readonly Dictionary<Carryable, float> itemSpeed = new Dictionary<Carryable, float>();
        readonly List<Carryable> items = new List<Carryable>();
        readonly List<Carryable> staleKeys = new List<Carryable>();
        float nextItemScan;

        // chance nodes that should re-roll on new situations
        Chance breakRoll;

        void Awake()
        {
            self = GetComponent<Fighter>();
            holder = GetComponent<ItemHolder>();
            body = GetComponent<Collider2D>();
            commands = self;
            RefreshTier();
            root = BuildTree();
        }

        /// <summary>Re-read the saved difficulty (Roster / Settings) unless a tier was forced.</summary>
        public void RefreshTier()
        {
            tier = useSavedDifficulty ? GameSettings.Difficulty : difficulty;
            p = ProfileFor(tier);
        }

        void OnEnable()
        {
            RefreshTier();
            self.GotHit += OnGotHit;
            self.HitConnected += OnHitConnected;
            if (self.Opponent) self.Opponent.AttackStarted += OnOpponentAttackStarted;
        }

        void OnDisable()
        {
            self.GotHit -= OnGotHit;
            self.HitConnected -= OnHitConnected;
            if (self.Opponent) self.Opponent.AttackStarted -= OnOpponentAttackStarted;
            ClearPlans();
        }

        Profile ProfileFor(Difficulty d) => d switch
        {
            Difficulty.Easy => easy,
            Difficulty.Hard => hard,
            _ => normal
        };

        void Update()
        {
            if (!self.Opponent) return;
            ScanItems();
            RunPlanned();
            UpdateGuard();
            root.Tick(ctx);
        }

        // ═════════════ The tree ═════════════

        BtNode BuildTree()
        {
            Func<float> clock = () => Time.time;
            breakRoll = new Chance("roll", () => p.comboBreakChance, 0.5f, clock);

            return new Selector("Root",
                new Sequence("Can't act",
                    new Condition("locked / KO", () => self.ControlsLocked || self.State == FighterState.KO ||
                                                       Opp.State == FighterState.KO),
                    new Act("Idle", IdleLocked)),

                new Sequence("Combo breaker",
                    new Condition("being comboed", () => self.State == FighterState.Hitstun && !self.IsLaunched &&
                                                         self.ComboHitsTaken >= 2),
                    breakRoll,
                    new Act("Dash out", () => { commands.Dash(AwayFromOpp); return NodeStatus.Success; })),

                new Sequence("Stunned",
                    new Condition("stunned / downed", () => self.State == FighterState.Hitstun || self.IsDowned),
                    new Act("Wait", () => { CancelGoal(); return NodeStatus.Running; })),

                new Sequence("Item incoming",
                    new Condition("thrown at me", DetectItemThreat),
                    new Act("React", ReactToItem)),

                new Sequence("Guarding",
                    new Condition("guard planned", () => Time.time < guardUntil),
                    new Act("Hold block", () => NodeStatus.Running)),

                new Sequence("Telegraphing",
                    new Condition("winding up", () => telegraphed != null),
                    new Act("\"!\"", FinishTelegraph)),

                new Sequence("Busy",
                    new Condition("own move", () => self.State == FighterState.Attacking || self.State == FighterState.Dashing ||
                                                    self.IsAirborne || self.InBlockstun),
                    new Act("Wait", () => NodeStatus.Running)),

                new Sequence("Moving",
                    new Condition("has goal", () => hasGoal && Time.time < nextThink),
                    new Act("Step", StepTowardGoal)),

                new Sequence("Decide",
                    new Condition("think tick", () => Time.time >= nextThink),
                    new Act("tick", () => { nextThink = Time.time + p.thinkInterval * Random.Range(0.8f, 1.2f); return NodeStatus.Success; }),
                    new Selector("Choose",
                        new Sequence("Punish",
                            new Condition("player open", PlayerOpen),
                            new Chance("roll", () => p.punishChance, 0.4f, clock),
                            new Act("Rush down", Punish)),

                        new Sequence("Throw item",
                            new Condition("holding", () => holder && holder.IsCarrying),
                            new Selector("Throw or approach",
                                new Sequence("Throw",
                                    new Condition("good throw", GoodThrowNow),
                                    new Act("Throw at player", ThrowHeld)),
                                new Act("Get in range", ApproachForThrow))),

                        new Sequence("Break guard",
                            new Condition("player turtling", PlayerTurtling),
                            new Chance("roll", () => p.guardBreakChance, 0.6f, clock),
                            new Act("Heavy pressure", GuardBreakPressure)),

                        new Sequence("Anti-air / cross-up",
                            new Condition("player jumping or crossing up close", AntiAirChanceSituation),
                            new Chance("roll", () => p.antiAirChance, 0.5f, clock),
                            new Act("Uppercut / Sweep", AntiAir)),

                        new Sequence("Fetch item",
                            new Condition("free item worth it", FindFetchTarget),
                            new Act("Go get it", Fetch)),

                        new Sequence("Attack",
                            new Condition("in range", () => Dist <= JabReach + rangePadding),
                            new Condition("attack ready", () => Time.time >= nextAttack),
                            new Act("Attack", NeutralAttack)),

                        new Sequence("Close in",
                            new Condition("too far", () => Dist > JabReach + rangePadding),
                            new Act("Approach", Approach)),

                        new Act("Spacing", Spacing))));
        }

        // ═════════════ Situation checks ═════════════

        Fighter Opp => self.Opponent;
        float Dist => Mathf.Abs(Opp.X - self.X);
        int TowardOpp => Opp.X >= self.X ? 1 : -1;
        int AwayFromOpp => -TowardOpp;

        bool CanActNow => !self.ControlsLocked &&
                          (self.State == FighterState.Idle || (self.State == FighterState.Blocking && !self.InBlockstun));

        float JabReach
        {
            get
            {
                var combo = self.LightCombo;
                var first = combo != null && combo.Count > 0 ? combo[0] : null;
                return first ? Reach(first) : 1.5f;
            }
        }

        /// <summary>How far in front of the centre an attack can touch someone (centre to centre).</summary>
        static float Reach(AttackData a) =>
            Mathf.Max(0f, a.hitboxOffset.x) + a.hitboxSize.x * 0.5f + a.lunge + a.activeTravel + 0.4f; // 0.4 ≈ half a body

        bool PlayerOpen()
        {
            var o = Opp;
            if (o.IsInvulnerable) return false;
            if (o.IsDowned) return o.DownedTimeLeft > 0.15f;
            if (o.IsParryStunned || o.IsGuardBroken) return true;
            // Whiffed / blocked heavy or special: stuck in recovery.
            return o.State == FighterState.Attacking && o.Phase == AttackPhase.Recovery && o.CurrentAttack &&
                   (o.CurrentAttack == o.HeavyAttackData || o.IsDoingSpecial);
        }

        bool PlayerTurtling()
        {
            var o = Opp;
            return o.State == FighterState.Blocking && !o.IsAutoBlocking && o.Stamina && o.Stamina.Normalized < 0.55f &&
                   Dist <= JabReach + rangePadding + 0.3f;
        }

        bool AntiAirChanceSituation()
        {
            var o = Opp;
            float dx = Mathf.Abs(o.X - self.X);
            bool jumpingIn = o.IsAirborne && !o.IsLaunched && dx < 1.9f;
            bool crossingUp = o.State == FighterState.Dashing && dx < 1.3f;
            return (jumpingIn && self.SpecialFor(SpecialMove.Up)) || (crossingUp && self.SpecialFor(SpecialMove.Down));
        }

        bool GoodThrowNow()
        {
            if (!holder || !holder.CanThrow) return false;
            var o = Opp;
            float d = Dist;
            // Rooted by a heavy item: it can't walk into range, so it throws from wherever it stands.
            if (d < minThrowDistance || (d > p.throwRange && !self.MovementRooted)) return false;
            if (o.IsInvulnerable) return false;
            // A blocked item breaks: Hard waits for the player to drop their guard.
            if (tier == Difficulty.Hard && o.State == FighterState.Blocking) return false;
            return true;
        }

        // ═════════════ Actions ═════════════

        NodeStatus IdleLocked()
        {
            ClearPlans();
            return NodeStatus.Success;
        }

        NodeStatus Punish()
        {
            var o = Opp;
            float reach = JabReach + rangePadding;
            if (Dist > reach)
            {
                // Downed with time to spare: rush in.
                if (o.IsDowned && o.DownedTimeLeft < 0.35f) return NodeStatus.Failure;
                SetGoal(o.X, reach * 0.8f);
                return StepTowardGoal();
            }

            CancelGoal();
            if (o.IsDowned)
            {
                // Melee only damages a Downed fighter: heavy if there's time for its wind-up, else the fast string.
                bool heavyFits = self.HeavyAttackData && o.DownedTimeLeft > self.HeavyAttackData.startup + 0.15f;
                if (heavyFits && Random.value < p.heavyChance + 0.25f) DoNow(commands.HeavyAttack, "heavy on downed");
                else DoNow(commands.Attack, "string on downed");
            }
            else
            {
                // Parry-stunned / whiffed: the heavy staggers (not auto-blocked), which opens the real damage window.
                if (self.HeavyAttackData) DoNow(commands.HeavyAttack, "heavy punish");
                else DoNow(commands.Attack, "punish");
            }
            nextAttack = Time.time + p.attackCooldown * 0.5f;
            return NodeStatus.Success;
        }

        NodeStatus ThrowHeld()
        {
            var o = Opp;
            Vector2 dir = new Vector2(TowardOpp, 0f);
            if (o.IsAirborne && o.transform.position.y > self.transform.position.y + 0.8f) dir = new Vector2(TowardOpp, 1f).normalized;

            Telegraph(() =>
            {
                if (holder && holder.CanThrow) holder.Throw(dir);
            }, "throw");
            return NodeStatus.Success;
        }

        NodeStatus ApproachForThrow()
        {
            float d = Dist;
            if (d > p.throwRange) { SetGoal(Opp.X, p.throwRange * 0.8f); return StepTowardGoal(); }
            if (d < minThrowDistance && Random.value < 0.5f)
            {
                // Too close to throw: back off a step (or just fight while holding it).
                TryDash(AwayFromOpp);
                return NodeStatus.Success;
            }
            return NodeStatus.Failure; // fall through to the other options (attack while carrying)
        }

        NodeStatus GuardBreakPressure()
        {
            if (Time.time < nextAttack) return NodeStatus.Failure;
            nextAttack = Time.time + p.attackCooldown;
            if (Random.value < 0.5f && self.HeavyAttackData) Telegraph(commands.HeavyAttack, "heavy into guard");
            else Telegraph(() => { commands.Attack(); PlanString(); }, "string into guard");
            return NodeStatus.Success;
        }

        NodeStatus AntiAir()
        {
            var o = Opp;
            bool jumpingIn = o.IsAirborne && !o.IsLaunched;
            ReleaseBlock();
            bool ok = jumpingIn ? commands.Special(SpecialMove.Up, 0) : commands.Special(SpecialMove.Down, 0);
            if (ok) nextAttack = Time.time + p.attackCooldown;
            Log(jumpingIn ? "anti-air uppercut" : "sweep the cross-up");
            return ok ? NodeStatus.Success : NodeStatus.Failure;
        }

        bool FindFetchTarget()
        {
            if (!holder || holder.IsCarrying) return false;
            if (fetchTarget && fetchTarget.IsAvailable && Time.time - fetchStart < 4f) return true;

            fetchTarget = null;
            if (Random.value > p.fetchChance) return false;

            var o = Opp;
            float best = float.MaxValue;
            foreach (var item in items)
            {
                if (!item || !item.IsAvailable) continue;
                float d = Mathf.Abs(item.X - self.X);
                if (d > fetchSearchRange || d >= best) continue;
                if (!p.raceForItems && Mathf.Abs(item.X - o.X) < d) continue; // the player would get there first
                best = d;
                fetchTarget = item;
            }
            if (fetchTarget) fetchStart = Time.time;
            return fetchTarget;
        }

        NodeStatus Fetch()
        {
            if (!fetchTarget) return NodeStatus.Failure;
            if (holder.CanPickUp(fetchTarget))
            {
                ReleaseBlock();
                holder.TryPickUp(fetchTarget);
                Log($"picked up {fetchTarget.name}");
                fetchTarget = null;
                CancelGoal();
                return NodeStatus.Success;
            }
            SetGoal(fetchTarget.X, holder.PickUpRange * 0.5f);
            return StepTowardGoal();
        }

        NodeStatus NeutralAttack()
        {
            nextAttack = Time.time + p.attackCooldown * Random.Range(0.8f, 1.2f);
            float r = Random.value;
            if (r < p.heavyChance && self.HeavyAttackData)
                Telegraph(commands.HeavyAttack, "heavy");
            else
                Telegraph(() => { commands.Attack(); PlanString(); }, "jab string");
            return NodeStatus.Success;
        }

        NodeStatus Approach()
        {
            float reach = JabReach + rangePadding;
            float d = Dist;

            // Gap closer: the Elbow Slide covers mid range with its hitbox out the whole way.
            var side = self.SpecialFor(SpecialMove.Side);
            if (side && d > reach + 0.5f && d < reach + side.activeTravel + 0.6f &&
                Time.time >= nextAttack && Random.value < p.specialChance * 0.6f)
            {
                nextAttack = Time.time + p.attackCooldown;
                Telegraph(() => commands.Special(SpecialMove.Side, TowardOpp), "elbow slide gap close");
                return NodeStatus.Success;
            }

            SetGoal(Opp.X, reach * 0.85f);
            return StepTowardGoal();
        }

        NodeStatus Spacing()
        {
            if (Random.value < p.retreatChance)
            {
                if (Random.value < 0.25f) { ReleaseBlock(); commands.JumpToward(new Vector2(AwayFromOpp, 1f)); Log("hop back"); }
                else TryDash(AwayFromOpp);
                return NodeStatus.Success;
            }
            return NodeStatus.Running; // hold position
        }

        // ═════════════ Thrown-item defence ═════════════

        void ScanItems()
        {
            if (Time.time >= nextItemScan)
            {
                nextItemScan = Time.time + 0.25f;
                items.Clear();
                items.AddRange(FindObjectsByType<Carryable>(FindObjectsSortMode.None));
                // Forget destroyed items.
                staleKeys.Clear();
                foreach (var k in lastItemPos.Keys) if (!k) staleKeys.Add(k);
                foreach (var k in staleKeys) { lastItemPos.Remove(k); itemSpeed.Remove(k); }
            }

            // Measure flight speed (works for every item, whatever its Throw Speed).
            float dt = Time.deltaTime;
            foreach (var c in items)
            {
                if (!c) continue;
                Vector2 pos = c.transform.position;
                if (c.State == Carryable.ItemState.Thrown && dt > 0f && lastItemPos.TryGetValue(c, out var last))
                {
                    float v = (pos - last).magnitude / dt;
                    if (v > 0.5f) itemSpeed[c] = itemSpeed.TryGetValue(c, out var old) ? Mathf.Lerp(old, v, 0.5f) : v;
                }
                lastItemPos[c] = pos;
            }
        }

        /// <summary>Is an item flying at us? Picks the reaction once per throw. True while the threat lasts.</summary>
        bool DetectItemThreat()
        {
            Carryable nearest = null;
            float bestT = float.MaxValue;
            foreach (var c in items)
            {
                if (!c || c.State != Carryable.ItemState.Thrown || c.Thrower == self) continue;
                if (TimeToImpact(c, out float t) && t < bestT) { bestT = t; nearest = c; }
            }

            if (!nearest)
            {
                threatItem = null;
                return false;
            }

            if (nearest != threatItem)
            {
                threatItem = nearest;
                threatSeenAt = Time.time;
                threatHandled = false;
                threatReaction = RollItemReaction();
                Log($"item incoming ({bestT:0.00}s): {threatReaction}");
            }
            threatImpactIn = bestT;
            return threatReaction != ItemReaction.None;
        }

        float threatImpactIn;

        ItemReaction RollItemReaction()
        {
            float r = Random.value;
            bool handsFree = holder && !holder.IsCarrying && self.CatchesParriedItems;
            if (handsFree && r < p.catchChance) return ItemReaction.Catch;
            r -= handsFree ? p.catchChance : 0f;
            if (r < p.jumpOverChance) return ItemReaction.Jump;
            r -= p.jumpOverChance;
            if (r < p.itemBlockChance) return ItemReaction.Block;
            return ItemReaction.None;
        }

        NodeStatus ReactToItem()
        {
            if (threatHandled) return NodeStatus.Running;
            bool reacted = Time.time - threatSeenAt >= p.reactionTime;
            if (!reacted) return NodeStatus.Running; // still "seeing" it

            CancelGoal();
            switch (threatReaction)
            {
                case ItemReaction.Catch:
                    if (threatImpactIn > catchLead) return NodeStatus.Running;
                    // Press block now: the item arrives inside the thrown-item parry window = catch.
                    ReleaseBlock();
                    SetGuard(Time.time, Time.time + 0.3f);
                    threatHandled = true;
                    Log("catch attempt");
                    return NodeStatus.Success;

                case ItemReaction.Jump:
                    if (threatImpactIn > jumpLead) return NodeStatus.Running;
                    if (CanActNow)
                    {
                        ReleaseBlock();
                        commands.JumpUp();
                        Log("jump over the item");
                    }
                    threatHandled = true;
                    return NodeStatus.Success;

                case ItemReaction.Block:
                    // Block early and hold until it has passed. (Late = a parry → catch, which is fine too.)
                    SetGuard(Time.time, Time.time + threatImpactIn + 0.25f);
                    threatHandled = true;
                    Log("block the item");
                    return NodeStatus.Success;
            }
            return NodeStatus.Failure;
        }

        /// <summary>Seconds until a flying item reaches our body, or false if it will miss / is moving away.</summary>
        bool TimeToImpact(Carryable c, out float t)
        {
            t = float.MaxValue;
            Vector2 pos = c.transform.position;
            Vector2 dir = c.ThrowDirection;
            if (Mathf.Abs(dir.x) < 0.05f) return false; // straight up / down: ignore

            float dx = self.X - pos.x;
            if (Mathf.Sign(dx) != Mathf.Sign(dir.x)) return false; // flying away from us

            float halfW = body ? body.bounds.extents.x : 0.4f;
            float distX = Mathf.Max(0f, Mathf.Abs(dx) - halfW);
            float speed = itemSpeed.TryGetValue(c, out var v) ? v : 14f;
            t = distX / Mathf.Max(0.1f, Mathf.Abs(dir.x) * speed);

            // Height of the item when it reaches us (it flies in a straight line).
            float yAtUs = pos.y + dir.y * (Mathf.Abs(dx) / Mathf.Abs(dir.x));
            float bottom = body ? body.bounds.min.y : self.transform.position.y - 1f;
            float top = body ? body.bounds.max.y : self.transform.position.y + 1f;
            return yAtUs >= bottom - 0.3f && yAtUs <= top + 0.3f;
        }

        // ═════════════ Melee defence (event driven, so it reacts between think ticks) ═════════════

        void OnOpponentAttackStarted(AttackData attack)
        {
            if (!attack || self.ControlsLocked || attack is ThrowableData) return;
            if (Opp.IsThrowing) return; // item wind-up: handled by the item threat when it flies
            if (Dist > Reach(attack) + 0.6f) return;

            bool heavyOrSpecial = attack == Opp.HeavyAttackData || Opp.IsDoingSpecial || attack.weight == HitWeight.Heavy;
            float now = Time.time;
            float reactAt = now + p.reactionTime;
            float hitAt = now + attack.startup;

            // Parry: press just before the hit lands so it falls in the window.
            if (Random.value < p.parryChance)
            {
                float pressAt = Mathf.Max(reactAt, hitAt - 0.07f);
                if (pressAt < hitAt + attack.active)
                {
                    PlanGuard(pressAt, pressAt + 0.12f);
                    Log($"parry attempt on {attack.name}");
                    return;
                }
            }

            // Lights are auto-guarded at neutral; manual blocks are for heavies and specials (which stagger / launch).
            if (heavyOrSpecial && Random.value < p.blockChance && reactAt < hitAt + attack.active)
            {
                PlanGuard(reactAt, now + attack.TotalDuration + 0.1f);
                Log($"block {attack.name}");
            }
        }

        void PlanGuard(float from, float until)
        {
            // Only if we're free to block then (not mid-attack); Fighter queues the block if we're briefly busy.
            if (self.State == FighterState.Attacking && self.Phase != AttackPhase.Recovery) return;
            CancelGoal();
            SetGuard(from, until);
        }

        void OnGotHit(Fighter attacker, AttackData attack)
        {
            planned.Clear();
            telegraphed = null;
            breakRoll?.Reset();
        }

        /// <summary>Hit-confirm: keep the string going only when the last hit actually landed.</summary>
        void OnHitConnected(Fighter target, AttackData attack, HitResult result)
        {
            if (result != HitResult.Hit || attack is ThrowableData || self.ComboStep <= 0 || self.IsDoingSpecial) return;
            var combo = self.LightCombo;
            if (combo == null) return;

            if (self.ComboStep < combo.Count)
            {
                if (Random.value < p.comboChance) Plan(0.05f, commands.Attack);
            }
            else if (Random.value < p.specialChance && self.SpecialFor(SpecialMove.Up))
            {
                Plan(0.05f, () => commands.Special(SpecialMove.Up, 0)); // combo → launcher
            }
            else if (Random.value < p.heavyChance)
            {
                Plan(0.05f, commands.HeavyAttack);
            }
        }

        // ═════════════ Helpers ═════════════

        void SetGoal(float x, float stopDistance)
        {
            hasGoal = true;
            goalX = x;
            goalStop = stopDistance;
        }

        void CancelGoal() => hasGoal = false;

        /// <summary>Walk (or dash when far) toward the goal; Success when there.</summary>
        NodeStatus StepTowardGoal()
        {
            if (!hasGoal) return NodeStatus.Failure;
            if (fetchTarget && hasGoal) goalX = fetchTarget.X; // items can be moving (falling, knocked)

            float dx = goalX - self.X;
            float gap = Mathf.Abs(dx) - goalStop;
            if (gap <= 0.03f) { CancelGoal(); return NodeStatus.Success; }
            int dir = dx > 0f ? 1 : -1;

            if (p.dashWhenFarther > 0f && Mathf.Abs(dx) > p.dashWhenFarther && gap >= self.DashDistance * 0.8f && TryDash(dir))
                return NodeStatus.Running;

            if (self.State == FighterState.Blocking && !self.InBlockstun) ReleaseBlock();
            if (self.State == FighterState.Idle && !self.MovementRooted) // rooted by a heavy item: can't walk
                self.MoveX(dir * Mathf.Min(p.walkSpeed * Time.deltaTime, gap));
            return NodeStatus.Running;
        }

        bool TryDash(int dir)
        {
            if (Time.time < nextDash) return false;
            ReleaseBlock();
            commands.Dash(dir);
            nextDash = Time.time + p.dashPause;
            return true;
        }

        /// <summary>Run an action now, or after the Easy-tier "!" warning.</summary>
        void Telegraph(Action action, string what)
        {
            CancelGoal();
            if (p.telegraphTime <= 0f)
            {
                DoNow(action, what);
                return;
            }
            telegraphUntil = Time.time + p.telegraphTime;
            telegraphed = action;
            Log($"telegraph: {what}");
        }

        NodeStatus FinishTelegraph()
        {
            if (Time.time < telegraphUntil) return NodeStatus.Running;
            var a = telegraphed;
            telegraphed = null;
            if (CanActNow) DoNow(a, "after telegraph");
            return NodeStatus.Success;
        }

        void DoNow(Action action, string what)
        {
            ReleaseBlock();
            guardUntil = -1f;
            action?.Invoke();
            Log(what);
        }

        /// <summary>Queue the rest of the light string; hit-confirm (OnHitConnected) decides past the 2nd hit.</summary>
        void PlanString()
        {
            if (Random.value < p.comboChance * 0.5f) Plan(0.12f, commands.Attack);
        }

        void Plan(float delay, Action action) => planned.Add(new Planned { time = Time.time + delay, action = action });

        void RunPlanned()
        {
            for (int i = planned.Count - 1; i >= 0; i--)
            {
                if (i >= planned.Count || Time.time < planned[i].time) continue;
                var a = planned[i].action;
                planned.RemoveAt(i);
                if (!self.ControlsLocked) { ReleaseBlock(); a(); }
            }
        }

        void SetGuard(float from, float until)
        {
            guardFrom = from;
            guardUntil = until;
        }

        void UpdateGuard()
        {
            bool shouldHold = Time.time >= guardFrom && Time.time < guardUntil && !self.ControlsLocked;
            if (shouldHold && !holdingBlock)
            {
                holdingBlock = true;
                commands.BlockStart();
            }
            else if (!shouldHold && holdingBlock) ReleaseBlock();
        }

        void ReleaseBlock()
        {
            if (!holdingBlock) return;
            holdingBlock = false;
            commands.BlockEnd();
        }

        void ClearPlans()
        {
            planned.Clear();
            telegraphed = null;
            telegraphUntil = -1f;
            guardFrom = guardUntil = -1f;
            fetchTarget = null;
            threatItem = null;
            CancelGoal();
            ReleaseBlock();
        }

        void Log(string msg)
        {
            if (logDecisions) Debug.Log($"[BT AI {name} · {ActiveDifficulty}] {msg}   ({ctx.Trace})");
        }

        // ═════════════ Debug label ═════════════

        GUIStyle labelStyle;

        void OnGUI()
        {
            if (!showDebugLabel || !Camera.main) return;
            if (labelStyle == null)
            {
                labelStyle = new GUIStyle(GUI.skin.label)
                {
                    fontSize = Mathf.RoundToInt(Screen.height * 0.022f),
                    alignment = TextAnchor.LowerCenter
                };
                labelStyle.normal.textColor = new Color(1f, 0.85f, 0.4f);
            }
            Vector3 head = transform.position + Vector3.up * 1.6f;
            Vector3 s = Camera.main.WorldToScreenPoint(head);
            if (s.z < 0f) return;
            var r = new Rect(s.x - 300f, Screen.height - s.y - 60f, 600f, 56f);
            GUI.Label(r, $"{ActiveDifficulty}: {ctx.Trace.Replace("Root > ", "")}", labelStyle);
        }
    }
}
