using UnityEngine;
using KungFuCrashout.Fighters;

namespace KungFuCrashout.DebugTools
{
    /// <summary>
    /// Makes a fighter a practice target:
    ///  • PRACTICE MODE (off by default): gets back up with full health shortly after being KO'd, and
    ///    refills health after you stop hitting it for a while. OFF = a KO is final (health 0 = dead).
    ///  • ATTACK WHEN CLOSE (toggle): when you're in range it winds up (shown as "!" by DefenseDebugHUD),
    ///    then throws a jab or a short string, so you can test blocking, stamina and parrying yourself.
    ///    It never walks: dash in to it. With Auto Range on it attacks only when its jab can actually
    ///    reach you. If it won't attack, DefenseDebugHUD shows why over its head (and the Console warns
    ///    once about setup problems: no attacks assigned, no Opponent, AIController still on, etc.).
    ///  • M3: can guard, so you can test your attacks being blocked or parried
    ///
    /// The "Random After Hit" guard is the Udemy sample AI's Defend roll (Random.Range after each hit).
    ///
    /// Put it on the Opponent. You can also put it on the Player (Attack Interval = 0, Guard = Never)
    /// so you recover if the dummy KOs you.
    /// Remove it once the AI (M4) and MatchManager (M7) are in the scene.
    /// REPLACES the M2 version of this file.
    /// Place in: Assets/_Project/Scripts/DebugTools/TrainingDummy.cs
    /// </summary>
    [RequireComponent(typeof(Fighter))]
    public class TrainingDummy : MonoBehaviour
    {
        public enum GuardMode
        {
            Never,
            /// <summary>Holds block all the time (tests chip damage, blockstun, pushback).</summary>
            Always,
            /// <summary>After being hit, sometimes blocks for a while (the sample's Defend roll).</summary>
            RandomAfterHit,
            /// <summary>Parries every attack it sees coming (tests parry stun on the player).</summary>
            ParryEverything
        }

        [Tooltip("ON: revive after a KO and refill health when left alone (endless practice). OFF: health 0 = dead, no refill.")]
        [SerializeField] bool practiceMode = false;
        [Tooltip("Practice Mode: seconds after a KO before getting back up.")]
        [SerializeField, Min(0f)] float resetDelayAfterKO = 1.5f;
        [Tooltip("Practice Mode: refill health after this many seconds without being hit. 0 = never.")]
        [SerializeField, Min(0f)] float refillAfterIdle = 3f;
        [Tooltip("Go back to the starting spot when getting up after a KO.")]
        [SerializeField] bool returnToStartOnReset = true;

        [Header("Attack when close (defence testing)")]
        [Tooltip("Attack the opponent whenever they're within Attack Range. Can be toggled while playing.")]
        [SerializeField] bool attackWhenClose = false;
        [Tooltip("Work out the range from the attack's hitbox + lunge and the target's width, so every attack can land.")]
        [SerializeField] bool autoRange = true;
        [Tooltip("Auto Range off: only attack if the target is within this distance (centre to centre, world units).")]
        [SerializeField, Min(0f)] float attackRange = 1.4f;
        [Tooltip("Seconds between attacks (measured from the end of the wind-up).")]
        [SerializeField, Min(0.2f)] float attackInterval = 1.5f;
        [Tooltip("Random ± fraction added to the interval so the rhythm isn't perfectly predictable.")]
        [SerializeField, Range(0f, 0.5f)] float intervalJitter = 0.25f;
        [Tooltip("Warning time before each attack (seconds). The HUD shows \"!\" over the dummy meanwhile. 0 = no warning.")]
        [SerializeField, Min(0f)] float windUpTime = 0.4f;
        [Tooltip("Light hits per attack: 1 = single jab, 3 = the full tap combo.")]
        [SerializeField, Range(1, 3)] int hitsPerString = 1;
        [Tooltip("Use the heavy attack instead of light hits.")]
        [SerializeField] bool useHeavy;

        [Header("Guard (M3 testing)")]
        [SerializeField] GuardMode guard = GuardMode.Never;
        [Tooltip("Random After Hit: chance to start blocking after each hit.")]
        [SerializeField, Range(0f, 1f)] float guardChance = 0.4f;
        [Tooltip("Random After Hit: how long the block is held (seconds).")]
        [SerializeField, Min(0f)] float guardHoldTime = 1.5f;

        Fighter fighter;
        Health health;
        IFighterCommands commands;
        Vector3 startPosition;
        float lastHitTime;
        float koTime = -1f;
        float nextAttackTime;
        float windUpUntil = -1f;
        bool stringRunning;     // true while a multi-hit string is being chained
        float nextChainTime;
        const float ChainSpacing = 0.1f; // how often to re-press during a string (Fighter buffers one hit ahead)

        /// <summary>Turn close-range attacks on/off (e.g. from a UI button).</summary>
        public bool AttackWhenClose { get => attackWhenClose; set => attackWhenClose = value; }
        /// <summary>True during the warning before an attack.</summary>
        public bool IsWindingUp => windUpUntil >= 0f;
        /// <summary>Centre-to-centre distance the dummy attacks from.</summary>
        public float EffectiveRange => autoRange ? AutoRange() : attackRange;
        /// <summary>Why it isn't attacking right now (null = it is, or will on the next beat). For the debug HUD.</summary>
        public string Status { get; private set; }

        string lastWarning;

        // guard timing
        float guardFrom = -1f, guardUntil = -1f;
        bool holdingBlock;

        void Awake()
        {
            fighter = GetComponent<Fighter>();
            health = GetComponent<Health>();
            commands = fighter;
            startPosition = transform.position;
        }

        void OnEnable()
        {
            fighter.GotHit += OnGotHit;
            health.Died += OnDied;
            if (fighter.Opponent) fighter.Opponent.AttackStarted += OnOpponentAttackStarted;
        }

        void OnDisable()
        {
            fighter.GotHit -= OnGotHit;
            health.Died -= OnDied;
            if (fighter.Opponent) fighter.Opponent.AttackStarted -= OnOpponentAttackStarted;
            SetHolding(false);
        }

        void OnGotHit(Fighter attacker, AttackData attack)
        {
            lastHitTime = Time.time;

            if (guard == GuardMode.RandomAfterHit && Random.value < guardChance)
            {
                guardFrom = Time.time;
                guardUntil = Time.time + attack.hitstun + guardHoldTime;
            }
        }

        void OnOpponentAttackStarted(AttackData attack)
        {
            if (guard != GuardMode.ParryEverything || !attack) return;

            // Tap block just before the hit lands, so it falls inside the parry window.
            float pressAt = Time.time + Mathf.Max(0f, attack.startup - 0.08f);
            guardFrom = pressAt;
            guardUntil = pressAt + 0.1f;
        }

        void OnDied() => koTime = Time.time;

        void Update()
        {
            // Get back up after a KO.
            if (koTime >= 0f)
            {
                SetHolding(false);
                windUpUntil = -1f;
                stringRunning = false;
                if (!practiceMode) return; // dead stays dead
                if (Time.time - koTime >= resetDelayAfterKO)
                {
                    koTime = -1f;
                    fighter.ResetFighter(returnToStartOnReset ? startPosition : (Vector3?)null);
                }
                return;
            }

            // Refill when left alone.
            if (practiceMode && refillAfterIdle > 0f && health.Current < health.Max && Time.time - lastHitTime >= refillAfterIdle)
                health.ResetHealth();

            UpdateGuard();

            if (attackWhenClose) UpdateAttacks();
            else { windUpUntil = -1f; stringRunning = false; Status = null; }
        }

        void UpdateAttacks()
        {
            string problem = SetupProblem();
            if (problem != null)
            {
                Status = problem;
                windUpUntil = -1f;
                if (problem != lastWarning) Debug.LogWarning($"[{name}] TrainingDummy won't attack: {problem}", this);
                lastWarning = problem;
                return;
            }
            lastWarning = null;

            var target = fighter.Opponent;
            float range = EffectiveRange;
            float dist = Mathf.Abs(target.X - fighter.X);
            bool inRange = target.State != FighterState.KO && dist <= range;

            Status = target.State == FighterState.KO ? "target KO'd"
                   : !inRange ? $"come closer: {dist:0.0} / {range:0.0}"
                   : fighter.ControlsLocked ? "controls locked (round not started?)"
                   : null;

            // Finish a string already started: keep pressing until the combo reaches Hits Per String.
            if (stringRunning && Time.time >= nextChainTime)
            {
                nextChainTime = Time.time + ChainSpacing;
                if (fighter.State != FighterState.Attacking) stringRunning = false;     // done, hit or parried
                else if (fighter.ComboStep < hitsPerString) commands.Attack();       // buffered by Fighter
            }

            // Wind-up finished → attack.
            if (IsWindingUp)
            {
                if (Time.time < windUpUntil) return;
                windUpUntil = -1f;
                if (!inRange || !CanAttackNow()) return;

                if (useHeavy) commands.HeavyAttack();
                else
                {
                    commands.Attack();
                    stringRunning = hitsPerString > 1;
                    nextChainTime = Time.time + ChainSpacing;
                }
                return;
            }

            // Start the next wind-up.
            if (inRange && Time.time >= nextAttackTime && CanAttackNow() && !stringRunning)
            {
                windUpUntil = Time.time + windUpTime;
                float jitter = 1f + Random.Range(-intervalJitter, intervalJitter);
                nextAttackTime = windUpUntil + attackInterval * jitter;
            }
        }

        /// <summary>Things that stop it attacking at all. Null = setup is fine.</summary>
        string SetupProblem()
        {
            var target = fighter.Opponent;
            if (!target) return "Fighter → Opponent is empty (drag the Player in)";

            if (useHeavy)
            {
                if (!fighter.HeavyAttackData) return "Fighter → Heavy Attack is empty (KungFu Crashout → Create Default Attacks)";
            }
            else if (fighter.LightCombo == null || fighter.LightCombo.Count == 0 || !fighter.LightCombo[0])
                return "Fighter → Light Combo is empty (KungFu Crashout → Create Default Attacks)";
            else if (hitsPerString > 1 && (fighter.LightCombo.Count < hitsPerString || !fighter.LightCombo[hitsPerString - 1]))
                return $"Light Combo has fewer than {hitsPerString} attacks";

            // Looked up by name so this script still compiles before the AI (Step 4) is in the project.
            var ai = GetComponent("AIController") as Behaviour;
            if (ai && ai.enabled) return "AIController is on too (disable it while testing)";

            if (!target.GetComponentInChildren<Collider2D>()) return $"{target.name} has no Collider2D, so hits can't touch it";
            return null;
        }

        /// <summary>Hitbox front edge (offset + half size + lunge) plus the target's half width, minus a little margin.</summary>
        float AutoRange()
        {
            var attack = useHeavy ? fighter.HeavyAttackData
                       : fighter.LightCombo != null && fighter.LightCombo.Count > 0 ? fighter.LightCombo[0] : null;
            if (!attack) return attackRange;

            float reach = attack.hitboxOffset.x + attack.hitboxSize.x * 0.5f + attack.lunge;
            var target = fighter.Opponent;
            var sr = target ? target.GetComponent<SpriteRenderer>() : null;
            float targetHalfWidth = sr ? sr.bounds.extents.x : 0.5f;
            return reach + targetHalfWidth - 0.1f;
        }

        void OnDrawGizmosSelected()
        {
            if (!fighter) fighter = GetComponent<Fighter>();
            if (!fighter) return;
            // Attack range either side of the dummy.
            float r = autoRange && Application.isPlaying ? EffectiveRange : attackRange;
            Vector3 c = transform.position;
            Gizmos.color = Color.red;
            Gizmos.DrawLine(c + Vector3.left * r, c + Vector3.right * r);
            Gizmos.DrawLine(c + new Vector3(-r, -0.2f, 0f), c + new Vector3(-r, 0.2f, 0f));
            Gizmos.DrawLine(c + new Vector3(r, -0.2f, 0f), c + new Vector3(r, 0.2f, 0f));
        }

        bool CanAttackNow() =>
            !fighter.ControlsLocked &&
            (fighter.State == FighterState.Idle || fighter.State == FighterState.Blocking);

        void UpdateGuard()
        {
            bool hold = guard switch
            {
                GuardMode.Always => true,
                GuardMode.RandomAfterHit or GuardMode.ParryEverything =>
                    Time.time >= guardFrom && Time.time < guardUntil,
                _ => false
            };
            SetHolding(hold);
        }

        void SetHolding(bool hold)
        {
            if (hold == holdingBlock) return;
            holdingBlock = hold;
            if (hold) commands?.BlockStart();
            else commands?.BlockEnd();
        }
    }
}
