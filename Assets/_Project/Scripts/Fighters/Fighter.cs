using System;
using System.Collections.Generic;
using UnityEngine;
using KungFuCrashout.Core;

namespace KungFuCrashout.Fighters
{
    public enum FighterState { Idle, Dashing, Blocking, Attacking, Hitstun, KO, Airborne, Downed }
    public enum AttackPhase { None, Startup, Active, Recovery }

    /// <summary>What happened when an attack touched a fighter.</summary>
    public enum HitResult { Ignored, Hit, Blocked, Parried }

    /// <summary>
    /// A fighter on the stage.
    /// M3 version: everything from M2 (dashing, facing, attacks with hitboxes, 3-hit tap combo,
    /// health, hitstun, knockback, KO, body pushing) plus:
    ///   • Blocking  — chip damage, blockstun and pushback instead of full damage (front only)
    ///   • Parrying  — a block started within the parry window takes no damage and stuns the attacker
    ///   • Control lock — the match manager (M7) freezes input during round intros and KOs
    ///   • HitConnected / Blocked / Parried / GotParried events for the AI, meter, HUD and juice
    ///   • Jumping (Step 1 iteration) — swipe up = straight jump along a set vector; diagonal swipe =
    ///     launch along the swipe's own direction. Both launch for a short time, then code gravity
    ///     takes over until landing. Moves by transform like the dash (no Rigidbody2D).
    ///     You can attack in the air; you can't dash or block in the air.
    ///   • Thrown items (M5) — ReceiveThrownHit: damage, then a launch along the throw direction that can
    ///     bounce off a stage wall. Blockable from the front; a parry deflects it with no damage. Thrown
    ///     hits never stun the thrower (they're far away), unlike a parried melee attack.
    ///   • Downed (Step 3d) — after a thrown-item launch lands (one ground bounce first), the fighter lies
    ///     on the floor, skidding to a stop and flashing, VULNERABLE: can't act or block, takes extra
    ///     damage (Throwable Data → Downed Damage Multiplier), then gets up with a short invincibility.
    ///     Launches use the item's own gravity (floatier) and can wall-splat instead of bouncing.
    ///   • Camera leash — with a following camera (LandscapeCameraFit → Follow Fighters) the two fighters
    ///     can't get further apart than StageBounds.MaxSeparation, so neither leaves the screen.
    ///   • Damage rules (prototype, both toggles on by default):
    ///       Auto Block At Neutral      — a melee hit on an IDLE fighter is blocked automatically: no damage,
    ///                                    pushed back (the attack's knockback), short blockstun. Thrown items
    ///                                    are never auto-blocked, so they always stagger.
    ///       Damage Only When Staggered — melee attacks only take health while the target is Downed
    ///                                    (staggered after an item hit). Otherwise they still cause hitstun
    ///                                    and knockback. Thrown items deal their own Throwable Data damage.
    ///   • Hit bounce — a melee hit on a fighter who is stunned (hitstun, guard break, parry stun) or Downed pops
    ///     them up a little (Hit Bounce settings). A Downed fighter stays Downed and lands back on the floor.
    ///   • Stamina (defence system) — with a Stamina component, holding block drains stamina and each
    ///     blocked hit costs some (damage × Block Stamina Per Damage). A blocked hit deals NO damage
    ///     (unless Chip Damage On Block is on). If a hit costs more than is left, the guard breaks:
    ///     no damage from that hit, but the blocker is stunned. At 0 stamina you can't block until it
    ///     refills (Stamina.Recover At). A parry costs nothing and refunds a little.
    ///
    /// Ideas carried over from the Udemy sample (My Scripts):
    ///   • Block state you can cancel straight into an attack   (Player1Actions "BlockOff" on attack)
    ///   • Stun after a heavy reaction / parry                   (Player2ActionsAI "Dazed")
    ///   • Freeze both fighters while the round is not live      (SaveScript.TimeOut)
    ///   • Facing, push-apart and stage limits were already here (FaceLeft/Right, MoveRestrict, ScreenBounds)
    /// Unlike the sample, blocking never switches colliders off, so you're never invulnerable,
    /// and there are no static flags, so any number of fighters can exist.
    ///
    /// REPLACES the M2 version of this file.
    /// Place in: Assets/_Project/Scripts/Fighters/Fighter.cs
    /// </summary>
    [RequireComponent(typeof(SpriteRenderer), typeof(Health))]
    public class Fighter : MonoBehaviour, IFighterCommands
    {
        [Header("References")]
        [SerializeField] Fighter opponent;
        [SerializeField] StageBounds stage;
        [Tooltip("Shown on the HUD (M7). Leave empty to use the GameObject name.")]
        [SerializeField] string displayName = "";

        [Header("Attacks")]
        [Tooltip("The tap combo, in order. 3 entries = 3-hit combo.")]
        [SerializeField] AttackData[] lightCombo = new AttackData[3];
        [Tooltip("Hold attack. Can also be used to finish a light combo.")]
        [SerializeField] AttackData heavyAttack;
        [Tooltip("Which layers can be hit. Leave on Everything unless you set up layers.")]
        [SerializeField] LayerMask hurtboxLayers = ~0;

        [Header("Body")]
        [Tooltip("Fighters can't stand closer than this, centre to centre (they can still dash through each other).")]
        [SerializeField] float minSeparation = 1f;

        [Header("Dash")]
        [SerializeField] float dashDistance = 2.5f;
        [SerializeField] float dashDuration = 0.18f;
        [SerializeField] float dashCooldown = 0.15f;
        [Tooltip("How the dash moves over time. Default is fast start, soft stop.")]
        [SerializeField] AnimationCurve dashCurve =
            new AnimationCurve(new Keyframe(0f, 0f, 0f, 2f), new Keyframe(1f, 1f, 0f, 0f));

        [Header("Jump")]
        [Tooltip("Direction of the straight jump (swipe up). (0, 1) = straight up. Tilt it, e.g. (0.2, 1), for a slight forward drift (x is flipped to the facing side).")]
        [SerializeField] Vector2 verticalJumpVector = new Vector2(0f, 1f);
        [Tooltip("Speed along the jump direction at take-off (world units per second).")]
        [SerializeField, Min(0f)] float jumpLaunchSpeed = 8f;
        [Tooltip("Seconds the fighter travels along the swipe direction before gravity takes over.")]
        [SerializeField, Min(0f)] float jumpLaunchTime = 0.1f;
        [Tooltip("Downward acceleration once the launch ends (world units per second²).")]
        [SerializeField, Min(0.1f)] float gravity = 30f;
        [Tooltip("Fastest the fighter can fall (world units per second).")]
        [SerializeField, Min(0.1f)] float maxFallSpeed = 20f;
        [Tooltip("Diagonal jumps are kept between these angles above horizontal (degrees), so a shallow swipe still leaves the ground and a steep one still travels.")]
        [SerializeField, Range(5f, 85f)] float minDiagonalAngle = 25f;
        [SerializeField, Range(5f, 85f)] float maxDiagonalAngle = 70f;
        [Tooltip("Seconds after landing before you can jump again.")]
        [SerializeField, Min(0f)] float jumpCooldown = 0.1f;

        [Header("Block / Parry")]
        [Tooltip("PARRY (perfect block) WINDOW, in frames: a hit that lands within this many frames of pressing block is a parry. " +
                 "Press too early (the hit lands after the window) = normal block. 9 frames @ 60 fps = 0.15 s.")]
        [SerializeField, Range(1, 30)] int parryWindowFrames = 9;
        [Tooltip("The frame rate the parry frames are counted at (fixed, so the window is the same on every phone, whatever its real fps).")]
        [SerializeField, Range(30, 120)] int parryFrameRate = 60;
        [Tooltip("PARRY WINDOW FOR THROWN ITEMS, in frames (same Parry Frame Rate): a thrown item that reaches you within this " +
                 "many frames of pressing block is parried. Separate from melee, so catching can be tuned on its own.")]
        [SerializeField, Range(1, 30)] int thrownParryWindowFrames = 12;
        [Tooltip("Parrying a thrown item CATCHES it: it goes straight into your hands (needs an ItemHolder, and your hands must be empty). " +
                 "Off = the item is knocked away as before.")]
        [SerializeField] bool catchThrownItemsOnParry = true;
        [Tooltip("A parry (perfect block) costs no stamina: no hold drain during the parry window and nothing for the parried hit.")]
        [SerializeField] bool parryCostsNoStamina = true;
        [Tooltip("If you block again within this many seconds of the last block ending, you get no parry window (stops mashing).")]
        [SerializeField] float parryCooldown = 0.5f;
        [Tooltip("How long an attacker is stunned after being parried (seconds). Long enough to punish with a full combo.")]
        [SerializeField] float parryStun = 0.6f;
        [Tooltip("Parrying a melee attack pushes the attacker back a little (they slide away while parry-stunned).")]
        [SerializeField] bool parryKnocksBack = true;
        [Tooltip("How far the parried attacker slides back (world units). Uses the normal knockback slide time.")]
        [SerializeField, Min(0f)] float parryKnockback = 0.6f;
        [Tooltip("Blocks and parries only work against hits from the front. Turn off to block cross-ups too.")]
        [SerializeField] bool blockOnlyFromFront = true;

        [Header("Damage rules (prototype)")]
        [Tooltip("A melee hit on an idle fighter is blocked automatically: no damage, knockback, short blockstun. " +
                 "Manual block / parry still work. Thrown items are never auto-blocked.")]
        [SerializeField] bool autoBlockAtNeutral = true;
        [Tooltip("Auto-block knockback = the attack's Knockback × this (never less than its Block Pushback).")]
        [SerializeField, Min(0f)] float autoBlockKnockbackScale = 1f;
        [Tooltip("Melee attacks only take health while the target is Downed (staggered by an item). " +
                 "Off = every clean melee hit deals damage (old behaviour).")]
        [SerializeField] bool damageOnlyWhenStaggered = true;

        [Header("Heavy attack stagger")]
        [Tooltip("This fighter's heavy (held) attack staggers like a thrown item: it is NOT auto-blocked at neutral, " +
                 "knocks the target into the air and leaves them Downed (open to damage). Manual block / parry still stop it.")]
        [SerializeField] bool heavyAttackStaggers = true;
        [Tooltip("Launch, Downed time, wall bounce etc. for that stagger. Empty = the same settings as the stage's crate " +
                 "(the first Carryable's Hit Data), so it matches an item hit exactly.")]
        [SerializeField] ThrowableData heavyStaggerData;
        [Tooltip("Damage of the staggering heavy hit. On = the heavy attack's own Damage (Attack Data). Off = no damage " +
                 "(only the stagger; follow-up hits do the damage).")]
        [SerializeField] bool heavyStaggerDealsDamage = true;

        [Header("Hit bounce (stunned / downed)")]
        [Tooltip("A melee hit on a stunned fighter (hitstun, guard break, parry stun) pops them up.")]
        [SerializeField] bool bounceWhenHitStunned = true;
        [Tooltip("Upward speed of that pop (world units/s). Height ≈ speed² ÷ (2 × Bounce Gravity): 3.5 → about 0.2 units.")]
        [SerializeField, Min(0f)] float stunnedBounceSpeed = 3.5f;
        [Tooltip("A melee hit on a Downed (staggered) fighter pops them up. They stay Downed and land back down.")]
        [SerializeField] bool bounceWhenHitDowned = true;
        [Tooltip("Upward speed of that pop (world units/s). 5 → about 0.4 units high with Bounce Gravity 30.")]
        [SerializeField, Min(0f)] float downedBounceSpeed = 5f;
        [Tooltip("Gravity for these pops (world units/s²). Lower = floatier, longer hang time. 0 = the jump's Gravity.")]
        [SerializeField, Min(0f)] float bounceGravity = 30f;

        [Header("Block stamina (needs a Stamina component)")]
        [Tooltip("Stamina drained per second while blocking.")]
        [SerializeField, Min(0f)] float blockHoldStaminaPerSecond = 8f;
        [Tooltip("Stamina a blocked hit costs, per point of its damage. 2 = a 5-damage jab costs 10.")]
        [SerializeField, Min(0f)] float blockStaminaPerDamage = 2f;
        [Tooltip("Stamina given back by a successful parry.")]
        [SerializeField, Min(0f)] float parryStaminaRefund = 10f;
        [Tooltip("How long the guard-broken blocker is stunned (seconds).")]
        [SerializeField, Min(0f)] float guardBreakStun = 0.7f;
        [Tooltip("Off = a block negates ALL damage. On = the attack's Chip Damage fraction still gets through (never lethal).")]
        [SerializeField] bool chipDamageOnBlock = false;

        [Header("Getting hit")]
        [Tooltip("How long the knockback slide takes (seconds).")]
        [SerializeField] float knockbackSlideTime = 0.15f;
        [Tooltip("Knockback multiplier for the hit that KOs.")]
        [SerializeField] float koKnockbackMultiplier = 2f;
        [Tooltip("How far the fighter tips over while Downed (degrees, away from the hit). 0 = stays upright.")]
        [SerializeField, Range(0f, 90f)] float downedTipAngle = 70f;
        [Tooltip("Downed flashes between Idle Colour and Downed Colour this many times per second.")]
        [SerializeField, Min(0f)] float downedFlashRate = 10f;

        [Header("Debug")]
        [Tooltip("Draws a red box on screen (also on the phone) while a hitbox is active.")]
        [SerializeField] bool showHitboxesInGame = true;
        [SerializeField] bool logStateChanges = true;
        [SerializeField] Color idleColour = Color.white;
        [SerializeField] Color dashColour = new Color(0.4f, 1f, 1f);
        [SerializeField] Color airborneColour = new Color(0.55f, 1f, 0.55f);
        [SerializeField] Color blockColour = new Color(0.3f, 0.5f, 1f);
        [SerializeField] Color blockstunColour = new Color(0.15f, 0.3f, 0.8f);
        [SerializeField] Color parryColour = new Color(1f, 0.95f, 0.3f);
        [SerializeField] Color startupColour = new Color(1f, 0.65f, 0.65f);
        [SerializeField] Color activeColour = new Color(1f, 0.15f, 0.15f);
        [SerializeField] Color heavyActiveColour = new Color(1f, 0.55f, 0f);
        [SerializeField] Color recoveryColour = new Color(0.55f, 0.25f, 0.25f);
        [SerializeField] Color hitstunColour = new Color(1f, 0.3f, 1f);
        [SerializeField] Color parryStunColour = new Color(0.6f, 0.3f, 1f);
        [SerializeField] Color guardBreakColour = new Color(1f, 0.5f, 0.1f);
        [SerializeField] Color downedColour = new Color(1f, 0.1f, 0.1f);
        [SerializeField] Color koColour = new Color(0.25f, 0.25f, 0.25f);

        // ───────────── Events (used by HUD, AI, meter, juice) ─────────────

        /// <summary>(previous, next)</summary>
        public event Action<FighterState, FighterState> StateChanged;
        /// <summary>Fired every time an attack starts, including combo follow-ups.</summary>
        public event Action<AttackData> AttackStarted;
        /// <summary>This fighter landed a clean hit: (target, attack). Not fired for blocks or parries.</summary>
        public event Action<Fighter, AttackData> HitLanded;
        /// <summary>This fighter's attack touched someone, whatever the outcome: (target, attack, result).</summary>
        public event Action<Fighter, AttackData, HitResult> HitConnected;
        /// <summary>This fighter was hit: (attacker, attack).</summary>
        public event Action<Fighter, AttackData> GotHit;
        /// <summary>This fighter blocked a hit: (attacker, attack).</summary>
        public event Action<Fighter, AttackData> Blocked;
        /// <summary>This fighter parried a hit: (attacker, attack).</summary>
        public event Action<Fighter, AttackData> Parried;
        /// <summary>This fighter's attack was parried: (defender).</summary>
        public event Action<Fighter> GotParried;
        /// <summary>Left the ground: (launch direction, normalised).</summary>
        public event Action<Vector2> Jumped;
        /// <summary>Touched the ground again after a jump.</summary>
        public event Action Landed;
        /// <summary>Hit by a thrown item: (thrower — may be null, item data, result, throw direction).</summary>
        public event Action<Fighter, ThrowableData, HitResult, Vector2> HitByItem;
        /// <summary>Landed from an item launch and is now Downed and vulnerable: (seconds).</summary>
        public event Action<float> Downed;
        /// <summary>Got back up after being Downed (now briefly invincible).</summary>
        public event Action GotUp;
        /// <summary>Bounced off the floor during an item launch.</summary>
        public event Action GroundBounced;
        /// <summary>Splatted against a stage wall during an item launch: (wall side, -1 left / +1 right).</summary>
        public event Action<int> WallSplatted;
        /// <summary>Our guard broke: a blocked hit cost more stamina than we had.</summary>
        public event Action GuardBroken;
        /// <summary>Tried to block with no stamina, or the block dropped because stamina ran out.</summary>
        public event Action BlockRefused;
        /// <summary>Knocked into a stage wall and bounced off it: (wall side, -1 left / +1 right).</summary>
        public event Action<int> WallBounced;

        // ───────────── Public read-only state ─────────────

        public FighterState State { get; private set; } = FighterState.Idle;
        public AttackPhase Phase { get; private set; } = AttackPhase.None;
        public AttackData CurrentAttack { get; private set; }
        /// <summary>+1 = facing right, -1 = facing left.</summary>
        public int Facing { get; private set; } = 1;
        /// <summary>1, 2, 3 during the light combo; 0 otherwise.</summary>
        public int ComboStep => comboIndex + 1;
        /// <summary>How many hits in a row this fighter has taken without escaping hitstun.</summary>
        public int ComboHitsTaken { get; private set; }
        public Health Health => health;
        public Fighter Opponent => opponent;
        public float X => transform.position.x;
        public string DisplayName => string.IsNullOrEmpty(displayName) ? name : displayName;

        /// <summary>The light combo and heavy, read-only. The AI uses these to judge its reach.</summary>
        public IReadOnlyList<AttackData> LightCombo => lightCombo;
        public AttackData HeavyAttackData => heavyAttack;
        /// <summary>How far one dash travels (world units).</summary>
        public float DashDistance => dashDistance;

        /// <summary>True during the parry window at the start of a block.</summary>
        public bool IsParryActive =>
            State == FighterState.Blocking && parryAvailable && Time.time - blockStartTime <= parryWindow;

        /// <summary>Parry window length in seconds (Parry Window Frames ÷ Parry Frame Rate).</summary>
        float parryWindow => parryWindowFrames / (float)Mathf.Max(1, parryFrameRate);
        public int ParryWindowFrames => parryWindowFrames;
        public int ThrownParryWindowFrames => thrownParryWindowFrames;
        /// <summary>Parrying a thrown item catches it (read by Carryable).</summary>
        public bool CatchesParriedItems => catchThrownItemsOnParry;
        /// <summary>Window (frames) the last parry was judged against: melee or thrown.</summary>
        public int LastParryWindowFrames { get; private set; }
        float thrownParryWindow => thrownParryWindowFrames / (float)Mathf.Max(1, parryFrameRate);
        /// <summary>True during the thrown-item parry window at the start of a block.</summary>
        public bool IsThrownParryActive =>
            State == FighterState.Blocking && parryAvailable && Time.time - blockStartTime <= thrownParryWindow;
        public int ParryFrameRate => parryFrameRate;
        /// <summary>Frames since block was pressed (1 = the first frame), counted at Parry Frame Rate.</summary>
        public int BlockFrame => Mathf.FloorToInt((Time.time - blockStartTime) * Mathf.Max(1, parryFrameRate)) + 1;
        /// <summary>On which frame of the window the last parry landed (1..Parry Window Frames).</summary>
        public int LastParryFrame { get; private set; }
        /// <summary>The last MANUAL block that missed the parry: how many frames too EARLY block was pressed (0 = no parry window, e.g. mashing).</summary>
        public int LastBlockFramesEarly { get; private set; }

        /// <summary>True while stuck in a block after blocking a hit.</summary>
        public bool InBlockstun => State == FighterState.Blocking && blockstunTimer > 0f;

        /// <summary>True while stunned from being parried.</summary>
        public bool IsParryStunned { get; private set; }
        /// <summary>Lying on the floor after an item launch: can't act or block, takes extra damage.</summary>
        public bool IsDowned => State == FighterState.Downed;
        public float DownedTimeLeft => IsDowned ? Mathf.Max(0f, downedTimer) : 0f;
        public float DownedDuration { get; private set; }
        /// <summary>The item data behind the current Downed state (null when not Downed).</summary>
        public ThrowableData LastDownedData => downedData;
        /// <summary>Just got up from Downed: hits are ignored.</summary>
        public bool IsInvulnerable => Time.time < invulnerableUntil;
        public bool IsWallSplatted => splatTimer > 0f;
        /// <summary>Damage actually taken by the last clean hit (after multipliers).</summary>
        public float LastDamageTaken { get; private set; }
        /// <summary>Blocking because Auto Block At Neutral kicked in (not a held block).</summary>
        public bool IsAutoBlocking => State == FighterState.Blocking && isAutoBlock;
        /// <summary>Stunned because our guard broke.</summary>
        public bool IsGuardBroken { get; private set; }
        /// <summary>Null if this fighter has no Stamina component (blocking is free).</summary>
        public Stamina Stamina => stamina;
        /// <summary>True if a block could start now stamina-wise (always true without a Stamina component).</summary>
        public bool HasGuardStamina => !stamina || !stamina.IsExhausted;
        /// <summary>The last blocked hit: stamina it cost and damage that still got through (for debug UI).</summary>
        public float LastBlockStaminaCost { get; private set; }
        public float LastBlockDamageTaken { get; private set; }

        /// <summary>While locked, every command is ignored (round intro, KO, match over).</summary>
        public bool ControlsLocked { get; private set; }

        /// <summary>True from take-off until landing, whatever else the fighter is doing (attacking, hitstun…).</summary>
        public bool IsAirborne { get; private set; }
        /// <summary>Current air velocity (world units per second). Zero on the ground.</summary>
        public Vector2 AirVelocity => airVelocity;
        /// <summary>True while flying from a thrown-item hit (until landing).</summary>
        public bool IsLaunched { get; private set; }

        bool CanStartAction =>
            !ControlsLocked &&
            (State == FighterState.Idle || (State == FighterState.Blocking && blockstunTimer <= 0f));

        /// <summary>Attacks can also start mid-jump.</summary>
        bool CanStartAttack => CanStartAction || (!ControlsLocked && State == FighterState.Airborne);

        // ───────────── Private ─────────────

        Health health;
        Stamina stamina;
        SpriteRenderer spriteRenderer;
        float halfWidth;

        // dash
        float dashStartX, dashTargetX, dashElapsed, nextDashTime;

        // jump
        float restY;            // the Y the fighter stands at; landing snaps back to it
        Vector2 airVelocity;
        float launchTimer, nextJumpTime;

        // launched by a thrown item
        int wallBouncesLeft;
        ThrowableData launchData;
        bool groundBounced;
        bool isPopping;     // in the air from a hit bounce (uses Bounce Gravity)
        float splatTimer;
        int splatSide;
        int launchPush = 1;

        // downed (after an item launch lands)
        ThrowableData downedData;
        float downedTimer;
        float skidVx, skidTime, skidLeft;
        float invulnerableUntil;

        // block
        bool blockHeld, parryAvailable;
        float blockStartTime;
        float lastBlockEndTime = -999f;
        float blockstunTimer;
        bool isAutoBlock;   // current block was started automatically (Auto Block At Neutral)

        // attack
        int comboIndex = -1;          // -1 = not in the light combo (or heavy)
        float phaseTimer;
        bool bufferedLight, bufferedHeavy;
        readonly HashSet<Fighter> hitThisAttack = new HashSet<Fighter>();
        readonly List<Collider2D> overlapResults = new List<Collider2D>();
        ContactFilter2D hurtboxFilter;

        // hitstun / knockback / pushback slide
        float hitstunTimer;
        float kbStartX, kbTargetX;
        float kbElapsed = float.MaxValue; // no slide running

        // debug hitbox
        SpriteRenderer hitboxVisual;

        // ───────────── Unity ─────────────

        void Awake()
        {
            spriteRenderer = GetComponent<SpriteRenderer>();
            health = GetComponent<Health>();
            if (!health) health = gameObject.AddComponent<Health>();
            stamina = GetComponent<Stamina>();

            halfWidth = spriteRenderer.bounds.extents.x;
            restY = transform.position.y;

            hurtboxFilter = new ContactFilter2D { useTriggers = true };
            hurtboxFilter.SetLayerMask(hurtboxLayers);

            if (showHitboxesInGame) CreateHitboxVisual();
        }

        void Start()
        {
            MoveX(0f); // clamps inside the stage
            FaceOpponent();
        }

        void Update()
        {
            float dt = Time.deltaTime;

            // Air motion runs alongside every state, so you can attack or get hit mid-jump.
            if (IsAirborne) UpdateAir(dt);

            switch (State)
            {
                case FighterState.Dashing:   UpdateDash(dt); break;
                case FighterState.Attacking: UpdateAttack(dt); break;
                case FighterState.Hitstun:   UpdateHitstun(dt); break;
                case FighterState.KO:        UpdateSlide(dt); UpdateSkid(dt); break;
                case FighterState.Downed:    UpdateDowned(dt); break;
                case FighterState.Blocking:  UpdateBlock(dt); break;
            }

            // Block still held after another action finished → go back to blocking (no parry this time).
            if (State == FighterState.Idle && blockHeld && !ControlsLocked && HasGuardStamina) EnterBlock(allowParry: false);

            // Only turn around when neutral, never mid-attack / mid-dash / mid-hitstun.
            if (State == FighterState.Idle || State == FighterState.Blocking) FaceOpponent();

            UpdateColour();
            UpdateHitboxVisual();
        }

        void LateUpdate() => ResolveSeparation();

        // ───────────── IFighterCommands ─────────────

        public void Dash(int direction)
        {
            if (direction == 0 || !CanStartAction) return;
            if (Time.time < nextDashTime) return;

            if (State == FighterState.Blocking) EndBlock();

            dashStartX = X;
            dashTargetX = ClampToStage(dashStartX + Mathf.Sign(direction) * dashDistance);
            dashElapsed = 0f;
            SetState(FighterState.Dashing);
        }

        public void Attack()
        {
            if (ControlsLocked || lightCombo == null || lightCombo.Length == 0) return;

            // Already attacking → try to chain the next combo hit.
            if (State == FighterState.Attacking)
            {
                bool inLightCombo = comboIndex >= 0;
                bool hasNext = comboIndex + 1 < lightCombo.Length && lightCombo[comboIndex + 1];
                if (!inLightCombo || !hasNext) return;

                if (Phase == AttackPhase.Recovery) StartAttack(lightCombo[comboIndex + 1], comboIndex + 1);
                else bufferedLight = true; // remembered, fires as soon as recovery starts
                return;
            }

            if (!CanStartAttack) return;
            StartAttack(lightCombo[0], 0);
        }

        public void HeavyAttack()
        {
            if (ControlsLocked || !heavyAttack) return;

            // Heavy can finish a light combo.
            if (State == FighterState.Attacking)
            {
                if (comboIndex < 0) return; // already doing the heavy

                if (Phase == AttackPhase.Recovery) StartAttack(heavyAttack, -1);
                else bufferedHeavy = true;
                return;
            }

            if (!CanStartAttack) return;
            StartAttack(heavyAttack, -1);
        }

        public void BlockStart()
        {
            if (ControlsLocked) return;
            blockHeld = true;
            if (State != FighterState.Idle) return;

            if (!HasGuardStamina)
            {
                BlockRefused?.Invoke();
                if (logStateChanges) Debug.Log($"[{name}] can't block: no stamina");
                return;
            }
            EnterBlock(allowParry: Time.time - lastBlockEndTime >= parryCooldown);
        }

        public void BlockEnd()
        {
            blockHeld = false;
        }

        /// <summary>Swipe up: jump along the set vertical vector (its x is flipped to the facing side).</summary>
        public void JumpUp()
        {
            var v = verticalJumpVector.sqrMagnitude > 0.0001f ? verticalJumpVector.normalized : Vector2.up;
            StartJump(new Vector2(v.x * Facing, Mathf.Abs(v.y)));
        }

        /// <summary>Diagonal swipe: jump along the swipe's own direction (world space), angle kept in range.</summary>
        public void JumpToward(Vector2 direction)
        {
            if (direction.sqrMagnitude < 0.0001f) return;

            float side = direction.x >= 0f ? 1f : -1f;
            float angle = Mathf.Atan2(Mathf.Max(direction.y, 0f), Mathf.Abs(direction.x)) * Mathf.Rad2Deg;
            angle = Mathf.Clamp(angle, minDiagonalAngle, maxDiagonalAngle) * Mathf.Deg2Rad;

            StartJump(new Vector2(Mathf.Cos(angle) * side, Mathf.Sin(angle)));
        }

        // ───────────── Match control (M7) ─────────────

        /// <summary>Freeze or unfreeze this fighter's commands. Releases any held block when locking.</summary>
        public void SetControlsLocked(bool locked)
        {
            ControlsLocked = locked;
            if (!locked) return;

            blockHeld = false;
            bufferedLight = bufferedHeavy = false;
        }

        // ───────────── Being hit ─────────────

        /// <summary>Called by the attacker when its hitbox touches this fighter. Returns what happened.</summary>
        public HitResult ReceiveHit(Fighter attacker, AttackData attack)
        {
            if (State == FighterState.KO || IsInvulnerable) return HitResult.Ignored;

            int awayFromAttacker = attacker.X < X ? 1 : -1;

            // ── Downed: can't block, takes extra damage. ──
            if (IsDowned) return HitWhileDowned(attacker, attack, awayFromAttacker);
            bool attackerInFront = !blockOnlyFromFront || awayFromAttacker == -Facing;

            // ── Parry: block pressed just before the hit. No damage, the attacker is stunned. ──
            if (attackerInFront && IsParryActive)
            {
                LastParryFrame = Mathf.Clamp(BlockFrame, 1, parryWindowFrames);
                LastParryWindowFrames = parryWindowFrames;
                parryAvailable = false; // one parry per press; later hits in this block are normal blocks
                if (stamina) stamina.Restore(parryStaminaRefund);
                attacker.OnParried(this);
                Parried?.Invoke(attacker, attack);
                if (logStateChanges) Debug.Log($"[{name}] PERFECT BLOCK (parry) vs {attacker.name}'s {attack.name} on frame {LastParryFrame}/{parryWindowFrames}");
                return HitResult.Parried;
            }

            // ── Block: no damage (or chip), costs stamina, blockstun, small pushback. ──
            if (attackerInFront && State == FighterState.Blocking)
            {
                RecordMissedParry(parryWindowFrames);
                return BlockHit(attacker, attack, awayFromAttacker);
            }

            // ── Heavy attack that staggers: lands like a thrown item (never auto-blocked). ──
            var staggerData = attacker.HeavyStaggerFor(attack);
            if (staggerData) return HeavyStaggerHit(attacker, attack, staggerData, awayFromAttacker);

            // ── Auto-block at neutral: an idle fighter guards by itself (no damage, knocked back). ──
            if (autoBlockAtNeutral && State == FighterState.Idle && !ControlsLocked && HasGuardStamina)
            {
                EnterBlock(allowParry: false, automatic: true);
                float push = Mathf.Max(attack.blockPushback, attack.knockback * autoBlockKnockbackScale);
                return BlockHit(attacker, attack, awayFromAttacker, push);
            }

            // ── Clean hit ──
            bool wasStunned = State == FighterState.Hitstun; // hitstun, guard break or parry stun (before this hit)
            // Still reeling from the last hit? Then this one is part of a combo.
            // A parried attacker starts a fresh combo count when punished.
            bool comboContinues = State == FighterState.Hitstun && !IsParryStunned;
            ComboHitsTaken = comboContinues ? ComboHitsTaken + 1 : 1;
            IsParryStunned = false;
            IsGuardBroken = false;

            // Not staggered: the hit still lands (hitstun + knockback) but takes no health.
            float damage = damageOnlyWhenStaggered ? 0f : attack.damage;
            LastDamageTaken = damage;
            health.TakeDamage(damage);
            GotHit?.Invoke(attacker, attack);
            if (logStateChanges)
                Debug.Log($"[{name}] hit by {attack.name} for {damage}" + (damageOnlyWhenStaggered ? " (not staggered: no damage)" : "") +
                          $" (HP {health.Current}/{health.Max}, combo {ComboHitsTaken})");

            CancelAttack();
            blockstunTimer = 0f; // a held block resumes (without parry) once hitstun ends
            StopAirDrift(); // hit mid-jump: knockback takes over X, gravity keeps pulling down

            bool ko = health.IsDead;
            StartSlide(awayFromAttacker * attack.knockback * (ko ? koKnockbackMultiplier : 1f));
            if (wasStunned && bounceWhenHitStunned) HitBounce(stunnedBounceSpeed);

            if (ko)
            {
                SetState(FighterState.KO);
                return HitResult.Hit;
            }

            hitstunTimer = attack.hitstun;
            SetState(FighterState.Hitstun);
            return HitResult.Hit;
        }

        /// <summary>The stagger settings if this attack is our heavy and Heavy Attack Staggers is on, else null.</summary>
        ThrowableData HeavyStaggerFor(AttackData attack)
        {
            if (!heavyAttackStaggers || !attack || attack != heavyAttack) return null;
            if (!heavyStaggerData)
            {
                // Match the crate: borrow the first stage item's hit settings (looked up once).
                foreach (var item in FindObjectsByType<KungFuCrashout.Props.Carryable>(FindObjectsSortMode.None))
                    if (item.HitData) { heavyStaggerData = item.HitData; break; }
                if (!heavyStaggerData)
                {
                    heavyStaggerData = ScriptableObject.CreateInstance<ThrowableData>();
                    heavyStaggerData.ApplyThrowDefaults();
                    heavyStaggerData.name = "Heavy stagger (crate defaults)";
                }
            }
            return heavyStaggerData;
        }

        /// <summary>
        /// A clean staggering heavy hit: the same as a thrown item's clean hit (launch → land → Downed), pushed away
        /// from the attacker. Damage = the heavy attack's (or none, with Heavy Stagger Deals Damage off).
        /// </summary>
        HitResult HeavyStaggerHit(Fighter attacker, AttackData attack, ThrowableData data, int push)
        {
            bool comboContinues = State == FighterState.Hitstun && !IsParryStunned;
            ComboHitsTaken = comboContinues ? ComboHitsTaken + 1 : 1;
            IsParryStunned = false;
            IsGuardBroken = false;

            float damage = attacker.heavyStaggerDealsDamage ? attack.damage : 0f;
            LastDamageTaken = damage;
            health.TakeDamage(damage);
            GotHit?.Invoke(attacker, attack);
            if (logStateChanges)
                Debug.Log($"[{name}] STAGGERED by {attacker.name}'s heavy {attack.name} for {damage} (HP {health.Current}/{health.Max})");

            CancelAttack();
            blockstunTimer = 0f;
            kbElapsed = float.MaxValue; // no slide; the launch moves us

            bool ko = health.IsDead;
            Launch(data.LaunchVelocity(new Vector2(push, 0f), push, ko), data);

            if (ko) { SetState(FighterState.KO); return HitResult.Hit; }

            hitstunTimer = data.hitstun;
            SetState(FighterState.Hitstun);
            return HitResult.Hit;
        }

        void RecordMissedParry(int windowFrames)
        {
            if (isAutoBlock) return;
            LastBlockFramesEarly = parryAvailable ? Mathf.Max(1, BlockFrame - windowFrames) : 0;
            if (logStateChanges)
                Debug.Log(parryAvailable ? $"[{name}] blocked on frame {BlockFrame}: {LastBlockFramesEarly} frame(s) too early for a parry " +
                                           $"(window = first {windowFrames})"
                                         : $"[{name}] blocked: no parry window (pressed again within Parry Cooldown)");
        }

        /// <summary>Our attack was parried: drop it and stand stunned (the sample's "Dazed").</summary>
        void OnParried(Fighter defender)
        {
            CancelAttack();
            kbElapsed = float.MaxValue;
            StopAirDrift();
            hitstunTimer = parryStun;
            IsParryStunned = true;

            // The parrier's settings decide the push: a short slide away from them.
            if (defender && defender.parryKnocksBack && defender.parryKnockback > 0f)
                StartSlide((defender.X < X ? 1 : -1) * defender.parryKnockback);

            IsGuardBroken = false;
            ComboHitsTaken = 0;
            SetState(FighterState.Hitstun);
            GotParried?.Invoke(defender);
        }

        /// <summary>
        /// Called by a thrown Carryable when it touches this fighter (M5). Returns what happened.
        /// thrower may be null (or far away); it is never stunned by a parry here.
        /// </summary>
        public HitResult ReceiveThrownHit(Fighter thrower, ThrowableData data, Vector2 direction)
        {
            if (!data || State == FighterState.KO || thrower == this || IsInvulnerable) return HitResult.Ignored;

            // Which way the item pushes us. A near-vertical throw pushes away from the thrower.
            int push = Mathf.Abs(direction.x) > 0.05f ? (direction.x > 0f ? 1 : -1)
                     : thrower ? (thrower.X < X ? 1 : -1) : -Facing;
            // The item comes from the side opposite its push, so it's "in front" when we face that side.
            bool fromFront = !blockOnlyFromFront || push == -Facing;

            // ── Downed: extra damage, no new launch. ──
            if (IsDowned)
            {
                var r = HitWhileDowned(thrower, data, push);
                HitByItem?.Invoke(thrower, data, r, direction);
                return r;
            }

            // ── Parry: deflected, no damage. The thrower is NOT stunned (they're across the stage). ──
            if (fromFront && IsThrownParryActive)
            {
                LastParryFrame = Mathf.Clamp(BlockFrame, 1, thrownParryWindowFrames);
                LastParryWindowFrames = thrownParryWindowFrames;
                parryAvailable = false;
                if (stamina) stamina.Restore(parryStaminaRefund);
                Parried?.Invoke(thrower, data);
                HitByItem?.Invoke(thrower, data, HitResult.Parried, direction);
                if (logStateChanges) Debug.Log($"[{name}] PERFECT BLOCK (parry) vs a thrown {data.name} on frame {LastParryFrame}/{thrownParryWindowFrames}");
                return HitResult.Parried;
            }

            // ── Block: no damage (or chip), costs stamina, blockstun, pushback along the throw. ──
            if (fromFront && State == FighterState.Blocking)
            {
                RecordMissedParry(thrownParryWindowFrames);
                var blocked = BlockHit(thrower, data, push);
                HitByItem?.Invoke(thrower, data, blocked, direction);
                return blocked;
            }

            // ── Clean hit: damage, then launch along the throw. ──
            bool comboContinues = State == FighterState.Hitstun && !IsParryStunned;
            ComboHitsTaken = comboContinues ? ComboHitsTaken + 1 : 1;
            IsParryStunned = false;
            IsGuardBroken = false;

            LastDamageTaken = data.damage;
            health.TakeDamage(data.damage);
            GotHit?.Invoke(thrower, data);
            if (logStateChanges)
                Debug.Log($"[{name}] hit by a thrown {data.name} for {data.damage} (HP {health.Current}/{health.Max})");

            CancelAttack();
            blockstunTimer = 0f;
            kbElapsed = float.MaxValue; // no slide; the launch moves us

            bool ko = health.IsDead;
            Launch(data.LaunchVelocity(direction, push, ko), data);

            HitByItem?.Invoke(thrower, data, HitResult.Hit, direction);

            if (ko) { SetState(FighterState.KO); return HitResult.Hit; }

            hitstunTimer = data.hitstun;
            SetState(FighterState.Hitstun);
            return HitResult.Hit;
        }

        /// <summary>
        /// A hit landed on our block (melee or thrown). Pays stamina; breaks the guard if we can't afford it.
        /// push = which way we slide (+1 right, -1 left).
        /// </summary>
        HitResult BlockHit(Fighter attacker, AttackData attack, int push, float pushDistance = -1f)
        {
            float cost = attack.damage * blockStaminaPerDamage;
            bool paid = !stamina || stamina.Spend(cost);

            // Off by default: a block negates all damage. Chip, when on, can never KO.
            float taken = chipDamageOnBlock
                ? Mathf.Min(attack.damage * attack.chipDamage, Mathf.Max(0f, health.Current - 1f))
                : 0f;
            health.TakeDamage(taken);
            LastBlockStaminaCost = stamina ? cost : 0f;
            LastBlockDamageTaken = taken;

            blockstunTimer = Mathf.Max(blockstunTimer, attack.Blockstun);
            StartSlide(push * (pushDistance >= 0f ? pushDistance : attack.blockPushback));

            Blocked?.Invoke(attacker, attack);
            if (logStateChanges)
                Debug.Log($"[{name}] blocked {attack.name}: {taken:0.#} damage, -{LastBlockStaminaCost:0.#} stamina" +
                          (stamina ? $" ({stamina.Current:0}/{stamina.Max:0} left)" : ""));

            if (!paid) GuardBreak();
            return HitResult.Blocked;
        }

        /// <summary>Out of stamina mid-block: the block drops and we're stunned, open to punishment.</summary>
        void GuardBreak()
        {
            blockstunTimer = 0f;
            parryAvailable = false;
            lastBlockEndTime = Time.time;
            hitstunTimer = guardBreakStun;
            IsGuardBroken = true;
            ComboHitsTaken = 0;
            SetState(FighterState.Hitstun);
            GuardBroken?.Invoke();
            if (logStateChanges) Debug.Log($"[{name}] GUARD BREAK");
        }

        /// <summary>
        /// Lets a thrower's own events (HitConnected / HitLanded) report a thrown-item hit, so hit sparks,
        /// hitstop and sounds play exactly as for a melee hit. Called by Carryable.
        /// </summary>
        public void ReportThrownHit(Fighter target, ThrowableData data, HitResult result)
        {
            if (result == HitResult.Ignored) return;
            HitConnected?.Invoke(target, data, result);
            if (result == HitResult.Hit) HitLanded?.Invoke(target, data);
        }

        /// <summary>Knocked into the air by a thrown item. Uses the jump's air motion and gravity.</summary>
        void Launch(Vector2 velocity, ThrowableData data)
        {
            if (!IsAirborne) restY = transform.position.y; // land back where we stood
            IsAirborne = true;
            IsLaunched = true;
            airVelocity = velocity;
            launchTimer = 0f;          // gravity starts straight away
            launchData = data;
            wallBouncesLeft = data.maxWallBounces;
            groundBounced = false;
            splatTimer = 0f;
            if (Mathf.Abs(velocity.x) > 0.01f) launchPush = velocity.x > 0f ? 1 : -1;
        }

        // ───────────── Downed (Step 3d) ─────────────

        /// <summary>A hit while lying on the floor: always lands, extra damage, small push, stays Downed.</summary>
        HitResult HitWhileDowned(Fighter attacker, AttackData attack, int push)
        {
            float mult = downedData ? downedData.downedDamageMultiplier : 1f;
            float dmg = attack.damage * mult;
            ComboHitsTaken++;
            LastDamageTaken = dmg;
            health.TakeDamage(dmg);
            GotHit?.Invoke(attacker, attack);
            if (logStateChanges)
                Debug.Log($"[{name}] hit while DOWNED by {attack.name} for {dmg:0.#} (x{mult:0.##}) (HP {health.Current}/{health.Max})");

            StartSlide(push * attack.knockback * 0.5f);
            if (bounceWhenHitDowned) HitBounce(downedBounceSpeed); // stays Downed; lands back on the floor
            if (health.IsDead) SetState(FighterState.KO); // stays lying down
            return HitResult.Hit;
        }

        /// <summary>
        /// Pop the fighter up a little (vertical only — the knockback slide handles sideways). Uses the jump's
        /// air motion; their state (Hitstun / Downed) is unchanged. Already in the air: pops them up again.
        /// </summary>
        void HitBounce(float upSpeed)
        {
            if (upSpeed <= 0f) return;

            if (IsLaunched)
            {
                // Mid item-launch: keep that flight (its gravity, walls, landing), just lift it.
                airVelocity.y = Mathf.Max(airVelocity.y, upSpeed);
                return;
            }

            if (!IsAirborne) restY = transform.position.y; // land back where they lie / stand
            IsAirborne = true;
            isPopping = true;
            launchTimer = 0f;
            airVelocity = new Vector2(0f, Mathf.Max(airVelocity.y, upSpeed));
        }

        void EnterDowned(ThrowableData data, float landingVx)
        {
            downedData = data;
            downedTimer = DownedDuration = data.downedTime;
            hitstunTimer = 0f;
            StartSkid(landingVx, data.skidTime);
            if (downedTipAngle > 0f) transform.rotation = Quaternion.Euler(0f, 0f, -downedTipAngle * launchPush);

            SetState(FighterState.Downed);
            Downed?.Invoke(data.downedTime);
            if (logStateChanges) Debug.Log($"[{name}] DOWNED for {data.downedTime:0.##}s (x{data.downedDamageMultiplier:0.##} damage)");
        }

        void UpdateDowned(float dt)
        {
            UpdateSkid(dt);
            UpdateSlide(dt);
            downedTimer -= dt;
            if (downedTimer > 0f) return;

            // Get up, briefly invincible so it can't be looped.
            transform.rotation = Quaternion.identity;
            invulnerableUntil = Time.time + (downedData ? downedData.getUpInvulnerability : 0f);
            downedData = null;
            ComboHitsTaken = 0;
            ReturnToNeutral();
            GotUp?.Invoke();
        }

        void StartSkid(float vx, float time)
        {
            skidVx = vx;
            skidTime = skidLeft = time;
        }

        /// <summary>Slide along the floor, slowing evenly to a stop.</summary>
        void UpdateSkid(float dt)
        {
            if (skidLeft <= 0f || skidTime <= 0f) return;
            skidLeft -= dt;
            MoveX(skidVx * Mathf.Clamp01(skidLeft / skidTime) * dt);
        }

        /// <summary>Back to full health and neutral. Used by the training dummy and round resets (M7).</summary>
        public void ResetFighter(Vector3? position = null)
        {
            CancelAttack();
            blockHeld = false;
            blockstunTimer = 0f;
            hitstunTimer = 0f;
            kbElapsed = float.MaxValue;
            IsParryStunned = false;
            ComboHitsTaken = 0;
            IsAirborne = false;
            airVelocity = Vector2.zero;
            launchTimer = 0f;
            IsLaunched = false;
            launchData = null;
            isPopping = false;
            IsGuardBroken = false;
            downedData = null;
            downedTimer = 0f;
            splatTimer = 0f;
            skidLeft = 0f;
            invulnerableUntil = 0f;
            transform.rotation = Quaternion.identity;
            health.ResetHealth();
            if (stamina) stamina.Refill();
            if (position.HasValue)
            {
                transform.position = position.Value;
                restY = position.Value.y;
            }
            else
            {
                var p = transform.position;
                p.y = restY;
                transform.position = p;
            }
            SetState(FighterState.Idle);
            FaceOpponent();
        }

        // ───────────── Attacks ─────────────

        void StartAttack(AttackData attack, int index)
        {
            if (!attack) return;
            if (State == FighterState.Blocking) EndBlock();

            CurrentAttack = attack;
            comboIndex = index;
            bufferedLight = bufferedHeavy = false;
            hitThisAttack.Clear();

            FaceOpponent();
            Phase = AttackPhase.Startup;
            phaseTimer = attack.startup;

            SetState(FighterState.Attacking);
            AttackStarted?.Invoke(attack);
            if (logStateChanges) Debug.Log($"[{name}] starts {attack.name}");
        }

        void UpdateAttack(float dt)
        {
            var attack = CurrentAttack;
            phaseTimer -= dt;

            switch (Phase)
            {
                case AttackPhase.Startup:
                    if (attack.startup > 0f) MoveX(Facing * attack.lunge * dt / attack.startup);
                    if (phaseTimer <= 0f)
                    {
                        Phase = AttackPhase.Active;
                        phaseTimer += attack.active;
                        CheckHits();
                    }
                    break;

                case AttackPhase.Active:
                    CheckHits();
                    if (State != FighterState.Attacking) return; // we got parried mid-swing

                    if (phaseTimer <= 0f)
                    {
                        Phase = AttackPhase.Recovery;
                        phaseTimer += attack.recovery;

                        // Fire anything the player queued up during this attack.
                        if (bufferedHeavy) { StartAttack(heavyAttack, -1); return; }
                        if (bufferedLight) { StartAttack(lightCombo[comboIndex + 1], comboIndex + 1); return; }
                    }
                    break;

                case AttackPhase.Recovery:
                    if (phaseTimer <= 0f)
                    {
                        CancelAttack();
                        ReturnToNeutral(); // still in the air → Airborne, else Idle
                    }
                    break;
            }
        }

        void CheckHits()
        {
            var attack = CurrentAttack;
            if (!attack) return;

            // Make sure colliders are at this frame's positions before querying.
            Physics2D.SyncTransforms();

            overlapResults.Clear();
            Physics2D.OverlapBox(HitboxCentre(attack, Facing), attack.hitboxSize, 0f, hurtboxFilter, overlapResults);

            foreach (var col in overlapResults)
            {
                var target = col.GetComponentInParent<Fighter>();
                if (!target || target == this || hitThisAttack.Contains(target)) continue;

                hitThisAttack.Add(target); // each attack hits a target at most once
                var result = target.ReceiveHit(this, attack);
                if (result == HitResult.Ignored) continue;

                HitConnected?.Invoke(target, attack, result);
                if (result == HitResult.Hit) HitLanded?.Invoke(target, attack);
                if (result == HitResult.Parried) return; // our attack is over
            }
        }

        void CancelAttack()
        {
            CurrentAttack = null;
            comboIndex = -1;
            Phase = AttackPhase.None;
            bufferedLight = bufferedHeavy = false;
        }

        Vector2 HitboxCentre(AttackData attack, int facing) =>
            (Vector2)transform.position + new Vector2(attack.hitboxOffset.x * facing, attack.hitboxOffset.y);

        // ───────────── Hitstun / knockback ─────────────

        void UpdateHitstun(float dt)
        {
            UpdateSlide(dt);
            hitstunTimer -= dt;
            if (hitstunTimer <= 0f && !IsLaunched) // knocked into the air: stunned until we land
            {
                ComboHitsTaken = 0;
                IsParryStunned = false;
                IsGuardBroken = false;
                ReturnToNeutral();
            }
        }

        /// <summary>Starts a short slide along X (knockback or block pushback).</summary>
        void StartSlide(float distance)
        {
            kbStartX = X;
            kbTargetX = ClampToStage(X + distance);
            kbElapsed = 0f;
        }

        void UpdateSlide(float dt)
        {
            if (kbElapsed >= knockbackSlideTime) return;

            kbElapsed += dt;
            float t = Mathf.Clamp01(kbElapsed / knockbackSlideTime);
            float eased = 1f - (1f - t) * (1f - t); // fast then slow

            var p = transform.position;
            p.x = Mathf.Lerp(kbStartX, kbTargetX, eased);
            transform.position = p;
        }

        // ───────────── Jump ─────────────

        void StartJump(Vector2 direction)
        {
            if (!CanStartAction || IsAirborne || Time.time < nextJumpTime) return;
            if (State == FighterState.Blocking) EndBlock();

            restY = transform.position.y;
            IsAirborne = true;
            airVelocity = direction.normalized * jumpLaunchSpeed;
            launchTimer = jumpLaunchTime;

            SetState(FighterState.Airborne);
            Jumped?.Invoke(direction.normalized);
            if (logStateChanges) Debug.Log($"[{name}] jumps toward {direction.normalized}");
        }

        /// <summary>
        /// Launch: travel straight along the jump direction for jumpLaunchTime (no gravity).
        /// Then fall: gravity bends the same velocity into an arc until the fighter is back at rest height.
        /// </summary>
        void UpdateAir(float dt)
        {
            // Wall splat: stuck to the wall for a moment, then slide off and drop.
            if (splatTimer > 0f)
            {
                splatTimer -= dt;
                if (splatTimer <= 0f) airVelocity = new Vector2(-splatSide * 1.5f, 0f);
                return;
            }

            // Item launches use the item's own (usually lower) gravity, for a longer, readable arc.
            float g = IsLaunched && launchData && launchData.launchGravity > 0f ? launchData.launchGravity
                    : isPopping && bounceGravity > 0f ? bounceGravity
                    : gravity;
            if (launchTimer > 0f) launchTimer -= dt;
            else airVelocity.y = Mathf.Max(airVelocity.y - g * dt, -maxFallSpeed);

            float wantedX = X + airVelocity.x * dt;
            MoveX(airVelocity.x * dt); // clamped to the stage walls, like the dash
            if (IsLaunched && Mathf.Abs(X - wantedX) > 0.0001f) HitWall(airVelocity.x > 0f ? 1 : -1);

            var p = transform.position;
            p.y += airVelocity.y * dt;

            if (p.y <= restY && airVelocity.y <= 0f && launchTimer <= 0f)
            {
                p.y = restY;
                transform.position = p;

                // Item launch: bounce off the floor once before landing.
                var d = launchData;
                if (IsLaunched && !groundBounced && d && d.groundBounce > 0f && -airVelocity.y >= d.minGroundBounceSpeed)
                {
                    groundBounced = true;
                    airVelocity.y = -airVelocity.y * d.groundBounce;
                    airVelocity.x *= 0.5f;
                    GroundBounced?.Invoke();
                    if (logStateChanges) Debug.Log($"[{name}] ground bounce");
                    return;
                }

                Land();
                return;
            }
            transform.position = p;
        }

        void Land()
        {
            bool wasLaunched = IsLaunched;
            var data = launchData;
            float landingVx = airVelocity.x;

            IsAirborne = false;
            IsLaunched = false;
            isPopping = false;
            launchData = null;
            airVelocity = Vector2.zero;
            launchTimer = 0f;
            nextJumpTime = Time.time + jumpCooldown;

            if (State == FighterState.Airborne) SetState(FighterState.Idle);
            Landed?.Invoke();

            if (!wasLaunched || !data) return;
            if (State == FighterState.KO) StartSkid(landingVx, data.skidTime); // KO'd mid-air: skid to a stop
            else if (State == FighterState.Hitstun && data.downedTime > 0f) EnterDowned(data, landingVx);
        }

        /// <summary>Launched into a stage wall: bounce back off it, or stop and slide down it.</summary>
        void HitWall(int side)
        {
            var d = launchData;
            bool fastEnough = d && wallBouncesLeft > 0 && Mathf.Abs(airVelocity.x) >= d.minBounceSpeed;

            if (fastEnough && d.wallSplat)
            {
                wallBouncesLeft--;
                splatTimer = d.wallSplatTime;
                splatSide = side;
                airVelocity = Vector2.zero;
                WallSplatted?.Invoke(side);
                if (logStateChanges) Debug.Log($"[{name}] WALL SPLAT ({(side > 0 ? "right" : "left")} wall)");
                return;
            }

            bool bounce = fastEnough && d.wallBounce > 0f;
            if (!bounce)
            {
                airVelocity.x = 0f;
                return;
            }

            wallBouncesLeft--;
            airVelocity.x = -airVelocity.x * d.wallBounce;
            airVelocity.y = Mathf.Max(airVelocity.y, 0f) + d.wallBounceLift;
            WallBounced?.Invoke(side);
            if (logStateChanges) Debug.Log($"[{name}] wall bounce ({(side > 0 ? "right" : "left")} wall)");
        }

        /// <summary>Hit or parried in the air: drop sideways drift and the launch, keep falling.</summary>
        void StopAirDrift()
        {
            if (!IsAirborne) return;
            airVelocity.x = 0f;
            launchTimer = 0f;
            airVelocity.y = Mathf.Min(airVelocity.y, 0f);
        }

        void ReturnToNeutral() => SetState(IsAirborne ? FighterState.Airborne : FighterState.Idle);

        // ───────────── Dash / block ─────────────

        void UpdateDash(float dt)
        {
            dashElapsed += dt;
            float t = Mathf.Clamp01(dashElapsed / dashDuration);

            var p = transform.position;
            p.x = Mathf.LerpUnclamped(dashStartX, dashTargetX, dashCurve.Evaluate(t));
            transform.position = p;

            if (t >= 1f)
            {
                nextDashTime = Time.time + dashCooldown;
                SetState(FighterState.Idle);
            }
        }

        void UpdateBlock(float dt)
        {
            UpdateSlide(dt);
            if (blockstunTimer > 0f) blockstunTimer -= dt;

            // Holding block drains stamina. Run dry (outside blockstun) and the block drops.
            if (stamina && blockHoldStaminaPerSecond > 0f && !(parryCostsNoStamina && IsParryActive))
            {
                stamina.Spend(blockHoldStaminaPerSecond * dt);
                if (stamina.IsExhausted && blockstunTimer <= 0f)
                {
                    EndBlock();
                    BlockRefused?.Invoke();
                    if (logStateChanges) Debug.Log($"[{name}] block dropped: out of stamina");
                    return;
                }
            }

            // Released early? Keep blocking until the parry window has played out (so a quick tap
            // still gets a full parry window) and until blockstun is over.
            bool parryWindowOver = Time.time - blockStartTime >= Mathf.Max(parryWindow, thrownParryWindow);
            if (!blockHeld && parryWindowOver && blockstunTimer <= 0f) EndBlock();
        }

        void EnterBlock(bool allowParry, bool automatic = false)
        {
            blockStartTime = Time.time;
            parryAvailable = allowParry;
            isAutoBlock = automatic;
            SetState(FighterState.Blocking);
        }

        void EndBlock()
        {
            // An automatic block doesn't count toward the parry cooldown, so you can still parry right after one.
            if (!isAutoBlock) lastBlockEndTime = Time.time;
            isAutoBlock = false;
            blockstunTimer = 0f;
            SetState(FighterState.Idle);
        }

        // ───────────── Movement helpers ─────────────

        public void MoveX(float delta)
        {
            var p = transform.position;
            p.x = ClampToStage(p.x + delta);
            transform.position = p;
        }

        float ClampToStage(float x)
        {
            if (stage) x = stage.ClampX(x, halfWidth);

            // Camera leash: never further from the opponent than the screen can show (Follow Fighters camera).
            // Like a stage wall, a launched fighter that reaches it bounces off it.
            if (stage && opponent && stage.MaxSeparation > 0f)
                x = Mathf.Clamp(x, opponent.X - stage.MaxSeparation, opponent.X + stage.MaxSeparation);
            return x;
        }

        /// <summary>Stops fighters overlapping. Dashing fighters pass through (cross-ups are allowed).</summary>
        void ResolveSeparation()
        {
            if (!opponent) return;
            // Only one of the pair does the work, so they don't fight each other.
            if (GetInstanceID() > opponent.GetInstanceID()) return;
            if (State == FighterState.Dashing || opponent.State == FighterState.Dashing) return;
            if (IsAirborne || opponent.IsAirborne) return; // jump over each other; landing overlap is fixed next frame

            float dx = X - opponent.X;
            float overlap = minSeparation - Mathf.Abs(dx);
            if (overlap <= 0f) return;

            float dir = Mathf.Abs(dx) > 0.001f ? Mathf.Sign(dx) : -Facing;

            // Each moves half…
            MoveX(dir * overlap * 0.5f);
            opponent.MoveX(-dir * overlap * 0.5f);

            // …and if one is against a wall, the other takes the rest.
            overlap = minSeparation - Mathf.Abs(X - opponent.X);
            if (overlap > 0f)
            {
                MoveX(dir * overlap);
                opponent.MoveX(-dir * overlap);
            }
        }

        void FaceOpponent()
        {
            if (!opponent) return;

            float dx = opponent.X - X;
            if (Mathf.Abs(dx) > 0.01f) Facing = dx > 0f ? 1 : -1;

            var s = transform.localScale;
            s.x = Mathf.Abs(s.x) * Facing;
            transform.localScale = s;
        }

        void SetState(FighterState next)
        {
            if (next == State) return;
            var previous = State;
            State = next;
            if (logStateChanges) Debug.Log($"[{name}] {previous} → {next}");
            StateChanged?.Invoke(previous, next);
        }

        // ───────────── Debug visuals ─────────────

        void UpdateColour()
        {
            Color c = idleColour;
            switch (State)
            {
                case FighterState.Dashing:  c = dashColour; break;
                case FighterState.Airborne: c = airborneColour; break;
                case FighterState.Hitstun:  c = IsGuardBroken ? guardBreakColour : IsParryStunned ? parryStunColour : hitstunColour; break;
                case FighterState.KO:       c = koColour; break;
                case FighterState.Downed:
                    c = Mathf.Repeat(Time.time * downedFlashRate, 1f) < 0.5f ? idleColour : downedColour;
                    break;
                case FighterState.Blocking:
                    c = IsParryActive ? parryColour : (blockstunTimer > 0f ? blockstunColour : blockColour);
                    break;
                case FighterState.Attacking:
                    c = Phase switch
                    {
                        AttackPhase.Startup => startupColour,
                        AttackPhase.Active => comboIndex < 0 ? heavyActiveColour : activeColour,
                        _ => recoveryColour
                    };
                    break;
            }
            if (IsInvulnerable) c.a = Mathf.Repeat(Time.time * 12f, 1f) < 0.5f ? 0.35f : 0.8f; // getting up: see-through flicker
            spriteRenderer.color = c;
        }

        void CreateHitboxVisual()
        {
            var go = new GameObject($"{name}_HitboxDebug");
            hitboxVisual = go.AddComponent<SpriteRenderer>();
            // Texture2D.whiteTexture is 4x4 px → 4 pixels per unit makes a 1x1 unit square.
            hitboxVisual.sprite = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f), 4f);
            hitboxVisual.color = new Color(1f, 0f, 0f, 0.35f);
            hitboxVisual.sortingOrder = 100;
            hitboxVisual.enabled = false;
        }

        void UpdateHitboxVisual()
        {
            if (!hitboxVisual) return;

            bool show = State == FighterState.Attacking && Phase == AttackPhase.Active && CurrentAttack;
            hitboxVisual.enabled = show;
            if (!show) return;

            hitboxVisual.transform.position = HitboxCentre(CurrentAttack, Facing);
            hitboxVisual.transform.localScale = new Vector3(CurrentAttack.hitboxSize.x, CurrentAttack.hitboxSize.y, 1f);
        }

        void OnDestroy()
        {
            if (hitboxVisual) Destroy(hitboxVisual.gameObject);
        }

        /// <summary>Select a fighter in the Scene view to see all its attack ranges.</summary>
        void OnDrawGizmosSelected()
        {
            int facing = Application.isPlaying ? Facing : (transform.localScale.x < 0f ? -1 : 1);

            Gizmos.color = new Color(1f, 0.3f, 0.3f, 0.8f);
            if (lightCombo != null)
                foreach (var a in lightCombo)
                    if (a) Gizmos.DrawWireCube(HitboxCentre(a, facing), a.hitboxSize);

            if (heavyAttack)
            {
                Gizmos.color = new Color(1f, 0.6f, 0f, 0.8f);
                Gizmos.DrawWireCube(HitboxCentre(heavyAttack, facing), heavyAttack.hitboxSize);
            }
        }
    }
}
