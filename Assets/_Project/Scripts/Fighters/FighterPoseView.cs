using UnityEngine;
using KungFuCrashout.Props;

namespace KungFuCrashout.Fighters
{
    /// <summary>
    /// Shows hand-drawn KEY POSES for a fighter by swapping one sprite based on what the fighter is doing
    /// (pose-to-pose, like a classic 2D fighter). Lives on a child "Visual" object so the Fighter's own
    /// box keeps working as the hurtbox, wall width and debug colour.
    ///
    ///   KO → Downed → Throw (for Throw Pose Time after releasing an item) → Hitstun → Attack phases
    ///   (Wind-up / Attack or Heavy Attack / Recovery) → Block → Dash → Airborne → Carry → Idle
    /// Any empty pose falls back sensibly (Heavy → Attack, Recovery → Attack, everything else → Idle),
    /// so it works with just one drawing and gets richer as poses are added.
    ///
    /// Also: a short red flash and a squash when hit, a smaller squash when blocking, a see-through flicker
    /// while invincible after getting up, and it hides the Fighter's coloured debug box (Debug Box Alpha).
    /// BLOCK SHIELD: while blocking, that box (the capsule) is drawn IN FRONT of the art as a see-through
    /// shield — blue when blocking, gold during the parry window, deeper blue in blockstun, gently pulsing.
    /// A stand-in until there's a block pose / animation.
    ///
    /// Setup: menu KungFu Crashout → Set Up Fighter Poses (imports the drawings and builds Visual on each fighter).
    /// Place in: Assets/_Project/Scripts/Fighters/FighterPoseView.cs
    /// </summary>
    [RequireComponent(typeof(SpriteRenderer))]
    [DefaultExecutionOrder(90)] // after the Fighter has updated its state and debug colour this frame
    public class FighterPoseView : MonoBehaviour
    {
        [Tooltip("Empty = the Fighter on a parent object.")]
        [SerializeField] Fighter fighter;
        [Tooltip("Empty = the ItemHolder on a parent object (for the throw and carry poses).")]
        [SerializeField] ItemHolder holder;

        [Header("Poses (empty = falls back)")]
        [SerializeField] Sprite idle;
        [SerializeField] Sprite block;
        [Tooltip("Attack startup. Empty = Idle.")]
        [SerializeField] Sprite windUp;
        [Tooltip("Attack active frames (the hit).")]
        [SerializeField] Sprite attack;
        [Tooltip("Heavy attack active frames. Empty = Attack.")]
        [SerializeField] Sprite heavyAttack;
        [Tooltip("Side special (Elbow Slide). Empty = Heavy Attack / Attack pose.")]
        [SerializeField] Sprite sideSpecialPose;
        [Tooltip("Up special (Uppercut). Empty = Heavy Attack / Attack pose.")]
        [SerializeField] Sprite upSpecialPose;
        [Tooltip("Down special (Leg Sweep). Empty = Heavy Attack / Attack pose.")]
        [SerializeField] Sprite downSpecialPose;
        [Tooltip("Attack recovery. Empty = the attack pose is held through recovery.")]
        [SerializeField] Sprite recovery;
        [Tooltip("Shown for Throw Pose Time after throwing an item.")]
        [SerializeField] Sprite throwPose;
        [SerializeField] Sprite carry;
        [SerializeField] Sprite hitstun;
        [Tooltip("Lying down after an item hit. When set, consider Fighter → Downed Tip Angle = 0 so it isn't rotated as well.")]
        [SerializeField] Sprite downed;
        [SerializeField] Sprite airborne;
        [SerializeField] Sprite dash;
        [SerializeField] Sprite ko;

        [Header("Timing")]
        [SerializeField, Min(0f)] float throwPoseTime = 0.3f;

        [Header("Facing")]
        [Tooltip("Tick if the drawings face LEFT. The Fighter's own turning flips them for the other side.")]
        [SerializeField] bool artFacesLeft = true;
        [Tooltip("While dashing, the dash pose faces the way you're moving (backdashes and cross-ups included), not the opponent.")]
        [SerializeField] bool dashFacesMovement = true;

        [Header("Fighter's debug box")]
        [Tooltip("Opacity of the Fighter's coloured box behind the art. 0 = hidden, ~0.25 = faint state-colour overlay for debugging.")]
        [SerializeField, Range(0f, 1f)] float debugBoxAlpha = 0f;

        [Header("Block shield (stand-in for a block animation)")]
        [Tooltip("While blocking, show the Fighter's capsule over the art as a shield.")]
        [SerializeField] bool showBlockShield = true;
        [SerializeField] Color shieldColour = new Color(0.35f, 0.65f, 1f, 0.45f);
        [Tooltip("During the parry window (the first moments of a block).")]
        [SerializeField] Color parryShieldColour = new Color(1f, 0.85f, 0.25f, 0.6f);
        [Tooltip("While stuck in blockstun after blocking a hit.")]
        [SerializeField] Color blockstunShieldColour = new Color(0.25f, 0.45f, 1f, 0.65f);
        [Tooltip("Opacity pulse, so the shield reads as active (0 = steady).")]
        [SerializeField, Range(0f, 0.5f)] float shieldPulse = 0.15f;
        [SerializeField, Min(0f)] float shieldPulseSpeed = 6f;
        [Tooltip("PERFECT BLOCK (parry): the shield flashes bright and the fighter pops, fading over Perfect Block Flash Time.")]
        [SerializeField] bool showPerfectBlockFlash = true;
        [SerializeField] Color perfectBlockColour = new Color(1f, 0.97f, 0.7f, 0.95f);
        [Tooltip("Real seconds (plays through the parry freeze).")]
        [SerializeField, Min(0.05f)] float perfectBlockFlashTime = 0.35f;
        [SerializeField] Vector2 perfectBlockPop = new Vector2(1.12f, 1.12f);

        [Header("Hit reaction")]
        [SerializeField] Color hitFlashColour = new Color(1f, 0.45f, 0.45f);
        [SerializeField, Min(0f)] float hitFlashTime = 0.08f;
        [Tooltip("Scale (x, y) right after a hit, easing back to normal.")]
        [SerializeField] Vector2 hitSquash = new Vector2(1.12f, 0.88f);
        [SerializeField] Vector2 blockSquash = new Vector2(1.05f, 0.95f);
        [SerializeField, Min(0.01f)] float squashTime = 0.12f;
        [Tooltip("Opacity flicker range while invincible after getting up.")]
        [SerializeField, Range(0f, 1f)] float invincibleAlpha = 0.4f;

        SpriteRenderer art;
        SpriteRenderer box;
        Vector3 baseScale;
        int boxBaseOrder;
        float throwUntil = -1f;
        float flashUntil = -1f;
        float squashStart = -1f;
        float perfectStart = -1f;
        Vector2 squashAmount = Vector2.one;

        /// <summary>
        /// Capsule block shield + perfect-block flash on / off at play time. FighterVfx turns it off when its
        /// guard lotus (the Block / Parry VFX) replaces it; the Inspector settings are left as they are.
        /// </summary>
        public bool UseBlockShield
        {
            get => showBlockShield;
            set { showBlockShield = value; showPerfectBlockFlash = value; }
        }

        /// <summary>The pose sprite currently shown (for debug UI).</summary>
        public Sprite CurrentPose => art ? art.sprite : null;

        void Awake()
        {
            art = GetComponent<SpriteRenderer>();
            if (!fighter) fighter = GetComponentInParent<Fighter>();
            if (!holder) holder = GetComponentInParent<ItemHolder>();
            box = fighter ? fighter.GetComponent<SpriteRenderer>() : null;
            baseScale = transform.localScale;
            animator = GetComponent<FighterAnimator>();

            // Draw in front of the box (the box comes back in front as the block shield).
            if (box)
            {
                boxBaseOrder = box.sortingOrder;
                art.sortingLayerID = box.sortingLayerID;
                art.sortingOrder = boxBaseOrder + 1;
            }
        }

        void OnEnable()
        {
            if (fighter)
            {
                fighter.GotHit += OnGotHit;
                fighter.Blocked += OnBlocked;
                fighter.Parried += OnParried;
            }
            if (holder) holder.Threw += OnThrew;
        }

        void OnDisable()
        {
            if (fighter)
            {
                fighter.GotHit -= OnGotHit;
                fighter.Blocked -= OnBlocked;
                fighter.Parried -= OnParried;
            }
            if (holder) holder.Threw -= OnThrew;
            transform.localScale = baseScale;
            if (box) box.sortingOrder = boxBaseOrder;
        }

        void OnGotHit(Fighter attacker, AttackData attack)
        {
            flashUntil = Time.unscaledTime + hitFlashTime; // real time, so it shows during the hit freeze
            Squash(hitSquash);
        }

        void OnBlocked(Fighter attacker, AttackData attack) => Squash(blockSquash);

        void OnParried(Fighter attacker, AttackData attack)
        {
            if (!showPerfectBlockFlash) return;
            perfectStart = Time.unscaledTime;
            Squash(perfectBlockPop);
        }

        void OnThrew(Carryable item, Vector2 direction) => throwUntil = Time.time + throwPoseTime;

        void Squash(Vector2 amount)
        {
            squashAmount = amount;
            squashStart = Time.unscaledTime;
        }

        FighterAnimator animator;

        /// <summary>The art is drawn mirrored this frame (facing the dash, not the opponent). The carry point follows it.</summary>
        public bool ArtMirrored { get; private set; }

        void LateUpdate()
        {
            if (!fighter) return;

            var slot = PickPose();
            var pose = SpriteFor(slot);
            // Pose clips (FighterAnimator → Use Pose Clips): the Animator draws the pose on its frame counter.
            // While an attack clip plays (e.g. Monk_BasicAttack), the Animator owns the sprite too.
            bool drawn = animator && animator.ShowPose(slot, Time.deltaTime);
            if (!drawn && pose && !(animator && animator.IsPlaying)) art.sprite = pose;
            // The fighter is already turned toward the opponent (root scale = Facing). If the dash goes the other way, mirror the art.
            bool againstDash = dashFacesMovement && fighter.DashDirection != 0 && fighter.DashDirection != fighter.Facing;
            art.flipX = artFacesLeft ^ againstDash;
            ArtMirrored = againstDash;

            // Colour: hit flash, invincibility flicker.
            Color c = Time.unscaledTime < flashUntil ? hitFlashColour : Color.white;
            if (fighter.IsInvulnerable)
                c.a = Mathf.Repeat(Time.time * 12f, 1f) < 0.5f ? invincibleAlpha : 0.85f;
            art.color = c;

            // Squash and ease back.
            float t = squashStart < 0f ? 1f : Mathf.Clamp01((Time.unscaledTime - squashStart) / squashTime);
            Vector2 s = Vector2.Lerp(squashAmount, Vector2.one, 1f - (1f - t) * (1f - t));
            transform.localScale = new Vector3(baseScale.x * s.x, baseScale.y * s.y, baseScale.z);

            // The Fighter re-colours its box every frame; after it has, either show it as the block shield
            // (in front of the art) or fade it to the debug alpha (behind the art).
            float perfectT = perfectStart < 0f ? 1f : (Time.unscaledTime - perfectStart) / perfectBlockFlashTime;
            if (box)
            {
                if (showPerfectBlockFlash && perfectT < 1f)
                {
                    // Perfect block: bright flash over the art, fading back to the normal shield colour.
                    Color target = fighter.State == FighterState.Blocking ? shieldColour : new Color(shieldColour.r, shieldColour.g, shieldColour.b, 0f);
                    box.color = Color.Lerp(perfectBlockColour, target, perfectT * perfectT);
                    box.sortingOrder = art.sortingOrder + 1;
                }
                else if (showBlockShield && fighter.State == FighterState.Blocking)
                {
                    Color sc = fighter.IsParryActive ? parryShieldColour
                             : fighter.InBlockstun ? blockstunShieldColour
                             : shieldColour;
                    if (shieldPulse > 0f)
                        sc.a *= 1f - shieldPulse + shieldPulse * Mathf.Sin(Time.unscaledTime * shieldPulseSpeed);
                    box.color = sc;
                    box.sortingOrder = art.sortingOrder + 1;
                }
                else
                {
                    var bc = box.color;
                    bc.a = debugBoxAlpha;
                    box.color = bc;
                    box.sortingOrder = boxBaseOrder;
                }
            }
        }

        /// <summary>The drawing assigned to this pose (null = none).</summary>
        public Sprite SpriteFor(PoseSlot slot) => slot switch
        {
            PoseSlot.Idle => idle,
            PoseSlot.Block => block,
            PoseSlot.Dash => dash,
            PoseSlot.Hitstun => hitstun,
            PoseSlot.Airborne => airborne,
            PoseSlot.Downed => downed,
            PoseSlot.KO => ko,
            PoseSlot.Carry => carry,
            PoseSlot.Throw => throwPose,
            PoseSlot.WindUp => windUp,
            PoseSlot.Attack => attack,
            PoseSlot.HeavyAttack => heavyAttack,
            PoseSlot.Recovery => recovery,
            PoseSlot.SideSpecial => sideSpecialPose,
            PoseSlot.UpSpecial => upSpecialPose,
            PoseSlot.DownSpecial => downSpecialPose,
            _ => null,
        };

        /// <summary>Which pose to show now: the first one in each fallback list that has a drawing.</summary>
        PoseSlot PickPose()
        {
            switch (fighter.State)
            {
                case FighterState.KO:     return First(PoseSlot.KO, PoseSlot.Downed, PoseSlot.Hitstun, PoseSlot.Idle);
                case FighterState.Downed: return First(PoseSlot.Downed, PoseSlot.Hitstun, PoseSlot.Idle);
            }

            if (Time.time < throwUntil) return First(PoseSlot.Throw, PoseSlot.Attack, PoseSlot.Idle);

            switch (fighter.State)
            {
                case FighterState.Hitstun:
                    return First(PoseSlot.Hitstun, PoseSlot.Idle);

                case FighterState.Attacking:
                    // Item throw without a clip: hold the item while winding up, throw pose from the release on.
                    if (fighter.IsThrowing)
                        return fighter.Phase == AttackPhase.Startup ? First(PoseSlot.Carry, PoseSlot.WindUp, PoseSlot.Idle)
                                                                    : First(PoseSlot.Throw, PoseSlot.Attack, PoseSlot.Idle);
                    var cur = fighter.CurrentAttack;
                    bool heavy = cur && cur == fighter.HeavyAttackData;
                    PoseSlot hit = heavy ? First(PoseSlot.HeavyAttack, PoseSlot.Attack) : First(PoseSlot.Attack);
                    if (fighter.IsDoingSpecial)
                    {
                        PoseSlot special = cur == fighter.SpecialFor(SpecialMove.Side) ? PoseSlot.SideSpecial
                                         : cur == fighter.SpecialFor(SpecialMove.Up) ? PoseSlot.UpSpecial
                                         : PoseSlot.DownSpecial;
                        hit = First(special, PoseSlot.HeavyAttack, PoseSlot.Attack);
                    }
                    return fighter.Phase switch
                    {
                        AttackPhase.Startup => First(PoseSlot.WindUp, PoseSlot.Idle),
                        AttackPhase.Recovery => First(PoseSlot.Recovery, hit, PoseSlot.Idle),
                        _ => First(hit, PoseSlot.Idle)
                    };

                case FighterState.Blocking: return First(PoseSlot.Block, PoseSlot.Idle);
                case FighterState.Dashing:  return First(PoseSlot.Dash, PoseSlot.Idle);
            }

            if (fighter.IsAirborne) return First(PoseSlot.Airborne, PoseSlot.Idle);
            if (holder && holder.IsCarrying) return First(PoseSlot.Carry, PoseSlot.Idle);
            return PoseSlot.Idle;
        }

        PoseSlot First(params PoseSlot[] options)
        {
            foreach (var s in options) if (s != PoseSlot.None && SpriteFor(s)) return s;
            return PoseSlot.None;
        }
    }
}
