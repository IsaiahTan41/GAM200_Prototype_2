using UnityEngine;
using UnityEngine.Serialization;
using KungFuCrashout.Fighters;

namespace KungFuCrashout.Feedback
{
    /// <summary>
    /// Plays a fighter's painted effects (VfxSet) from the Fighter's own events. No combat code changes.
    ///   Attack  — strike at the hitbox + dust at the feet, on the attack's first ACTIVE frame (throws excluded)
    ///   Hit     — burst at the contact point (heavy / special / item hits use the heavy version); KO adds a bigger one
    ///   Block   — blue lotus at the defender's feet          Parry — yellow lotus + starburst at the contact point
    ///   Dash / jump / land / knocked down — dust at the feet
    /// Sizes follow the active Visual's scale; sorting follows its sprite. Effects freeze in hitstop (VfxPlayer).
    ///
    /// Setup: on the Player and the Opponent next to Fighter (VFX → 2. Add VFX To Open Scene does it).
    /// Place in: Assets/_Project/Scripts/Feedback/FighterVfx.cs
    /// </summary>
    [RequireComponent(typeof(Fighter))]
    public class FighterVfx : MonoBehaviour
    {
        [Tooltip("This character's effects, e.g. VfxSet_Monk (the original single-frame effects).")]
        [FormerlySerializedAs("set")]
        [SerializeField] VfxSet staticSet;
        [Tooltip("The animated (sprite sheet) effects, e.g. VfxSet_Monk_Animated. Used while VfxPlayer → Use Animated VFX " +
                 "is on; empty = always the set above.")]
        [SerializeField] VfxSet animatedSet;
        [Tooltip("Animated lotus: tint while the parry window is open (the animated set has no separate parry lotus).")]
        [SerializeField] Color parryWindowTint = new Color(1f, 0.85f, 0.45f, 1f);

        /// <summary>The set in use now: the animated one while VfxPlayer → Use Animated VFX is on.</summary>
        VfxSet set => VfxPlayer.AnimatedVfxOn && animatedSet ? animatedSet : staticSet;

        [Header("Which effects play")]
        [SerializeField] bool attacks = true;
        [SerializeField] bool hits = true;
        [SerializeField] bool blocks = true;
        [SerializeField] bool parries = true;
        [SerializeField] bool movement = true;
        [SerializeField] bool knockdowns = true;
        [SerializeField] bool knockout = true;
        [Tooltip("Log every attack effect (which attack, which cue, where) to the Console, for checking.")]
        [SerializeField] bool logEffects = false;

        [Header("Guard lotus (replaces the capsule shield)")]
        [Tooltip("ON: while blocking, the Block lotus (blue) stands at the feet for as long as block is held, turning into the " +
                 "Parry lotus (yellow) during the parry window, and replaces FighterPoseView's coloured capsule shield. " +
                 "A blocked hit punches it and flashes; a parry flashes yellow with the starburst. OFF: one-shot lotus per blocked / parried hit.")]
        [SerializeField] bool holdLotusWhileBlocking = true;
        [Tooltip("Turn off FighterPoseView's capsule shield and perfect-block flash while this is on (play time only; its settings are untouched).")]
        [SerializeField] bool hideCapsuleShield = true;
        [Tooltip("Seconds to fade out after letting go of block.")]
        [SerializeField, Min(0.01f)] float lotusFadeOut = 0.12f;
        [Tooltip("Size kick when a hit is blocked / parried (1.15 = 15 % bigger, easing back).")]
        [SerializeField, Min(1f)] float blockPunch = 1.12f;
        [SerializeField, Min(1f)] float parryPunch = 1.25f;
        [SerializeField, Min(0.01f)] float punchTime = 0.18f;
        [Tooltip("After a parry, the lotus stays yellow this long (real seconds), even if the block ends.")]
        [SerializeField, Min(0f)] float parryHold = 0.3f;
        [Tooltip("Blockstun: lotus brightness multiplier (shows you're stuck guarding).")]
        [SerializeField, Range(0.5f, 2f)] float blockstunBrightness = 1.25f;
        [Tooltip("A soft steady light in the lotus colour while blocking (Light colour / radius from the cue, intensity × this). 0 = none.")]
        [SerializeField, Range(0f, 1f)] float holdLightStrength = 0.35f;

        [Header("Positioning (world units; X = forward, flips with facing)")]
        [Tooltip("Dust, block / parry lotus, dash, jump, landing and knockdown: offset from the fighter's feet " +
                 "(ground level: where the Visual's sprite pivot stands, not the Fighter object, which sits higher).")]
        [SerializeField] Vector2 feetOffset;
        [Tooltip("Attack slash: offset from the hitbox centre.")]
        [SerializeField] Vector2 strikeOffset;
        [Tooltip("Hit burst and parry starburst: offset from the contact point. X points the way the hit pushes.")]
        [SerializeField] Vector2 hitOffset;
        [Tooltip("KO burst: offset from the middle of the fighter's sprite.")]
        [SerializeField] Vector2 bodyOffset;
        [Tooltip("Draw draggable anchor dots for these in the Scene view (select the fighter).")]
        [SerializeField] bool showAnchors = true;

        [Header("Size")]
        [Tooltip("Multiplies every effect on this fighter.")]
        [SerializeField, Min(0f)] float sizeMultiplier = 1f;

        Fighter fighter;
        Transform visual;
        SpriteRenderer art;

        public VfxSet Set { get => staticSet; set => staticSet = value; }
        public VfxSet AnimatedSet { get => animatedSet; set => animatedSet = value; }
        /// <summary>Where this fighter's feet effects go (other fighters' impacts land here).</summary>
        public Vector3 FeetPoint => Feet;

        float groundDrop;       // how far the feet are below the Fighter object (the Visual child's offset)
        bool groundMeasured;

        void Awake() => fighter = GetComponent<Fighter>();

        /// <summary>
        /// The Fighter object sits above the floor (e.g. y = 1) and its Visual child is moved down to stand on it (y = −1),
        /// with the sprite's pivot at the feet. Measured once from the Visual's resting offset, so animation can't move it.
        /// </summary>
        void MeasureGround()
        {
            if (groundMeasured && Application.isPlaying) return;
            FindVisual();
            groundDrop = visual && visual != transform && visual.IsChildOf(transform)
                ? transform.InverseTransformPoint(visual.position).y * Mathf.Abs(transform.lossyScale.y)
                : 0f;
            groundMeasured = true;
        }



        void OnEnable()
        {
            fighter.AttackWentActive += OnAttackWentActive;
            fighter.HitConnected += OnHitConnected;
            fighter.Blocked += OnBlocked;
            fighter.Parried += OnParried;
            fighter.GuardBroken += OnGuardBroken;
            fighter.StateChanged += OnStateChanged;
            fighter.Jumped += OnJumped;
            fighter.Landed += OnLanded;
            fighter.Downed += OnDowned;
            fighter.GroundBounced += OnGroundBounced;
        }

        void OnDisable()
        {
            fighter.AttackWentActive -= OnAttackWentActive;
            fighter.HitConnected -= OnHitConnected;
            fighter.Blocked -= OnBlocked;
            fighter.Parried -= OnParried;
            fighter.GuardBroken -= OnGuardBroken;
            fighter.StateChanged -= OnStateChanged;
            fighter.Jumped -= OnJumped;
            fighter.Landed -= OnLanded;
            fighter.Downed -= OnDowned;
            fighter.GroundBounced -= OnGroundBounced;
            HideLotusNow();
        }

        // ───────────── Attacks: first active frame ─────────────

        void LateUpdate()
        {
            UpdateGuardLotus();
        }

        // ───────────── Guard lotus (held while blocking) ─────────────

        SpriteRenderer lotus;
        UnityEngine.Rendering.Universal.Light2D lotusLight;
        float lotusShownAt = -1f, lotusAlpha, punchStart = -10f, punchAmount = 1f, parryUntil = -1f;
        VfxCue lotusCue;
        float lotusAge;

        void Start()
        {
            MeasureGround();
            if (holdLotusWhileBlocking && hideCapsuleShield)
                foreach (var pv in GetComponentsInChildren<FighterPoseView>(true)) pv.UseBlockShield = false;
        }

        void FlashAtFeet(VfxCue cue)
        {
            if (!cue) return;
            FindVisual();
            float size = (visual ? Mathf.Abs(visual.lossyScale.y) : 1f) * sizeMultiplier;
            VfxPlayer.FlashOnly(cue, Feet, fighter.Facing, size);
        }

        void UpdateGuardLotus()
        {
            if (!holdLotusWhileBlocking || !set) { HideLotusNow(); return; }

            bool parryLook = Time.unscaledTime < parryUntil || (fighter.State == FighterState.Blocking && fighter.IsParryActive);
            bool blocking = fighter.State == FighterState.Blocking && blocks;
            bool show = (blocking || Time.unscaledTime < parryUntil) && (parryLook ? parries : blocks);
            // Animated lotus: Block Start forms it, then Block (Idle) loops; the parry window tints it.
            bool animLotus = set.block && set.block.IsAnimated;
            VfxCue cue = animLotus ? set.block : parryLook ? (set.parry ? set.parry : set.block) : set.block;
            if (!cue || !cue.HasArt) { HideLotusNow(); return; }

            float dt = Time.unscaledDeltaTime;
            if (show)
            {
                if (!lotus) MakeLotus();
                if (lotusShownAt < 0f) { lotusShownAt = Time.unscaledTime; lotusAge = 0f; }
                else lotusAge += Time.deltaTime; // scaled: freezes in hitstop like the fighters
                lotusAlpha = 1f;
            }
            else
            {
                lotusShownAt = -1f;
                lotusAlpha = Mathf.MoveTowards(lotusAlpha, 0f, dt / lotusFadeOut);
                if (!lotus || lotusAlpha <= 0f) { HideLotusNow(); return; }
            }

            if (animLotus)
            {
                var start = set.blockStart && set.blockStart.IsAnimated ? set.blockStart : null;
                float startLen = start ? start.Duration : 0f;
                lotus.sprite = start && lotusAge < startLen ? start.FrameAt(lotusAge) : cue.FrameAt(lotusAge - startLen);
                lotusCue = null; // the single-frame look re-applies its sprite if the toggle is switched off
            }
            else if (cue != lotusCue) { lotusCue = cue; lotus.sprite = cue.sprite; }

            FindVisual();
            float size = (visual ? Mathf.Abs(visual.lossyScale.y) : 1f) * sizeMultiplier * cue.scale;
            float age = lotusShownAt < 0f ? cue.popTime : Time.unscaledTime - lotusShownAt;
            float pop = animLotus ? 1f : age < cue.popTime ? Mathf.Lerp(cue.startScale, cue.peakScale, age / cue.popTime) : 1f;
            float breathe = animLotus ? 1f : 1f + Mathf.Sin(Time.unscaledTime * cue.pulseSpeed * Mathf.PI * 2f) * cue.pulseAmount * 0.5f;
            float pt = Mathf.Clamp01((Time.unscaledTime - punchStart) / punchTime);
            float punch = Mathf.Lerp(punchAmount, 1f, 1f - (1f - pt) * (1f - pt));

            var t = lotus.transform;
            Vector3 feet = Feet;
            t.position = new Vector3(feet.x + cue.offset.x * size, feet.y + cue.offset.y * size, feet.z - 0.05f);
            t.localScale = Vector3.one * size * pop * breathe * punch;
            lotus.flipX = false;
            lotus.sortingLayerID = art ? art.sortingLayerID : 0;
            lotus.sortingOrder = (art ? art.sortingOrder : 0) + cue.sortingOffset;

            float bright = fighter.InBlockstun ? blockstunBrightness : 1f;
            Color c = cue.tint * bright;
            if (animLotus && parryLook) c *= parryWindowTint;
            c.a = cue.tint.a * cue.alpha * lotusAlpha;
            lotus.color = c;
            lotus.gameObject.SetActive(true);

            if (lotusLight)
            {
                bool lit = cue.light && holdLightStrength > 0f;
                lotusLight.gameObject.SetActive(lit);
                if (lit)
                {
                    lotusLight.color = cue.lightColour;
                    lotusLight.intensity = cue.lightIntensity * holdLightStrength * lotusAlpha * breathe;
                    lotusLight.pointLightOuterRadius = cue.lightRadius * size / Mathf.Max(0.01f, cue.scale);
                    lotusLight.pointLightInnerRadius = lotusLight.pointLightOuterRadius * 0.2f;
                    lotusLight.transform.position = feet + new Vector3(cue.lightOffset.x, cue.lightOffset.y, 0f) * size / Mathf.Max(0.01f, cue.scale);
                }
            }
        }

        void MakeLotus()
        {
            var go = new GameObject(name + " GuardLotus");
            lotus = go.AddComponent<SpriteRenderer>();
            var player = VfxPlayer.Instance;
            if (player && player.SpriteMaterial) lotus.sharedMaterial = player.SpriteMaterial;
            if (holdLightStrength > 0f)
            {
                var lgo = new GameObject("GuardLight");
                lgo.transform.SetParent(go.transform, false);
                lotusLight = lgo.AddComponent<UnityEngine.Rendering.Universal.Light2D>();
                lotusLight.shadowsEnabled = false;
                lotusLight.falloffIntensity = 0.7f;
            }
            go.SetActive(false);
        }

        void HideLotusNow()
        {
            lotusAlpha = 0f;
            lotusShownAt = -1f;
            if (lotus && lotus.gameObject.activeSelf) lotus.gameObject.SetActive(false);
        }

        void OnDestroy()
        {
            if (lotus) Destroy(lotus.gameObject);
        }

        /// <summary>Fighter event: the attack's hitbox just went live (light combo, heavy, specials, air combo; not throws).</summary>
        void OnAttackWentActive(AttackData attack)
        {
            if (!attacks || !set || !attack) { if (logEffects) Debug.Log($"[{name}] VFX skipped for {(attack ? attack.name : "null")}: {(!attacks ? "Attacks toggle off" : !set ? "no VFX Set" : "no attack")}", this); return; }
            PlayAttack(attack);
        }

        void PlayAttack(AttackData attack)
        {
            var vfx = set.For(fighter, attack);
            if (logEffects)
                Debug.Log($"[{name}] VFX for {attack.name} (combo step {fighter.ComboStep}, air step {fighter.AirComboStep}): " +
                          $"strike {(vfx.strike ? vfx.strike.name : "none")} at {StrikePoint}, dust {(vfx.dust ? vfx.dust.name : "none")}, scale {vfx.Scale}", this);
            if (vfx.IsEmpty) return;
            float s = vfx.Scale;

            if (vfx.strike)
            {
                if (vfx.strike.anchorToArt) PlayOnArt(vfx.strike);
                else Play(vfx.strike, StrikePoint, fighter.Facing, s, transform);
            }
            if (vfx.dust) Play(vfx.dust, Feet, fighter.Facing, s);
        }

        // ───────────── Hits ─────────────

        void OnHitConnected(Fighter target, AttackData attack, HitResult result)
        {
            if (!set || !hits || result != HitResult.Hit || !target) return;
            bool big = attack && (attack.weight == HitWeight.Heavy || attack is ThrowableData || fighter.IsDoingSpecial);
            int push = target.X >= fighter.X ? 1 : -1;
            Vector3 contact = ContactPoint(target, attack) + Offset(hitOffset, push);

            // Animated impacts: a ring at the target's feet (grounded), a slash at the contact point (airborne),
            // and the slash along the ground for the down special.
            bool downSpecial = attack && fighter.IsDoingSpecial && attack == fighter.SpecialFor(SpecialMove.Down);
            var ground = FeetOf(target);
            if (downSpecial && (set.downSpecialHit || set.hitGround)) Play(set.downSpecialHit ? set.downSpecialHit : set.hitGround, ground, push);
            else if (target.IsAirborne && set.hitAir) Play(set.hitAir, contact, push);
            else if (!target.IsAirborne && set.hitGround) Play(set.hitGround, ground, push);

            var cue = big && set.heavyHit ? set.heavyHit : set.hit;
            if (cue) Play(cue, contact, push);
        }

        // ───────────── Defending (this fighter is the defender) ─────────────

        void OnBlocked(Fighter attacker, AttackData attack)
        {
            if (!set || !blocks) return;
            if (holdLotusWhileBlocking)
            {
                punchAmount = blockPunch; punchStart = Time.unscaledTime;
                FlashAtFeet(set.block);
                return;
            }
            Play(set.block, Feet, fighter.Facing);
        }

        void OnParried(Fighter attacker, AttackData attack)
        {
            if (!set || !parries) return;
            if (holdLotusWhileBlocking)
            {
                punchAmount = parryPunch; punchStart = Time.unscaledTime;
                parryUntil = Time.unscaledTime + parryHold;
                if (set.parry && set.parry.IsAnimated) Play(set.parry, Feet, fighter.Facing); // the animated parry burst
                else FlashAtFeet(set.parry);
            }
            else Play(set.parry, Feet, fighter.Facing);
            if (set.parryImpact)
            {
                Vector3 at = attacker ? ContactPoint(fighter, attack, attacker) : transform.position;
                int push = attacker ? (attacker.X >= fighter.X ? 1 : -1) : fighter.Facing;
                Play(set.parryImpact, at + Offset(hitOffset, push), fighter.Facing);
            }
        }

        void OnGuardBroken()
        {
            if (!set || !blocks || !set.guardBreak) return;
            if (set.guardBreak.IsAnimated) HideLotusNow(); // the lotus breaks apart instead of fading
            Play(set.guardBreak, Feet, fighter.Facing);
        }

        // ───────────── Movement and state ─────────────

        void OnStateChanged(FighterState previous, FighterState next)
        {
            if (!set) return;
            if (next == FighterState.Dashing && movement)
            {
                int dir = fighter.DashDirection != 0 ? fighter.DashDirection : fighter.Facing;
                Play(set.dash, Feet, dir);
            }
            else if (next == FighterState.KO && knockout && set.ko)
            {
                // The finishing hit: a big burst on the loser (this fighter), pointing away from the winner.
                var other = fighter.Opponent;
                int away = other ? (fighter.X >= other.X ? 1 : -1) : -fighter.Facing;
                Play(set.ko, Body + Offset(bodyOffset, away), away);
            }
        }

        void OnJumped(Vector2 direction)
        {
            if (!set || !movement) return;
            int dir = Mathf.Abs(direction.x) > 0.1f ? (direction.x > 0f ? 1 : -1) : fighter.Facing;
            Play(set.jump, Feet, dir);
        }

        void OnLanded()
        {
            if (!set || !movement) return;
            Play(set.land, Feet, fighter.Facing);
        }

        void OnDowned(float seconds)
        {
            if (!set || !knockdowns) return;
            Play(set.knockdown ? set.knockdown : set.land, Feet, fighter.Facing);
        }

        void OnGroundBounced()
        {
            if (!set || !knockdowns) return;
            Play(set.knockdown ? set.knockdown : set.land, Feet, fighter.Facing, 0.8f);
        }

        // ───────────── Preview & anchors (FighterVfxEditor) ─────────────

        public enum Preview { Strike1, Strike2, HeavyStrike, Hit, HeavyHit, Block, Parry, Dash, Jump, Land, Knockdown, KO,
                              ImpactGround, ImpactAir, DownSpecialImpact, BlockForm, GuardBreak }

        /// <summary>Plays one effect where it would appear now (Play mode). Used by the Inspector's preview buttons.</summary>
        public void PlayPreview(Preview what)
        {
            if (!set) { Debug.LogWarning($"[{name}] FighterVfx has no VFX Set."); return; }
            if (!fighter) fighter = GetComponent<Fighter>();
            int f = Facing;
            Vector3 hitAt = StrikePoint - Offset(strikeOffset, f) + Offset(hitOffset, f);
            switch (what)
            {
                case Preview.Strike1: PreviewAttack(set.lightCombo != null && set.lightCombo.Length > 0 ? set.lightCombo[0] : default); break;
                case Preview.Strike2: PreviewAttack(set.lightCombo != null && set.lightCombo.Length > 1 ? set.lightCombo[1] : default); break;
                case Preview.HeavyStrike: PreviewAttack(set.heavy); break;
                case Preview.Hit: Play(set.hit, hitAt, f); break;
                case Preview.HeavyHit: Play(set.heavyHit ? set.heavyHit : set.hit, hitAt, f); break;
                case Preview.Block: Play(set.block, Feet, f); break;
                case Preview.Parry: Play(set.parry, Feet, f); Play(set.parryImpact, hitAt, f); break;
                case Preview.Dash: Play(set.dash, Feet, f); break;
                case Preview.Jump: Play(set.jump, Feet, f); break;
                case Preview.Land: Play(set.land, Feet, f); break;
                case Preview.Knockdown: Play(set.knockdown ? set.knockdown : set.land, Feet, f); break;
                case Preview.KO: Play(set.ko, Body + Offset(bodyOffset, -f), -f); break;
                case Preview.ImpactGround: Play(set.hitGround, new Vector3(hitAt.x, Feet.y, Feet.z), f); break;
                case Preview.ImpactAir: Play(set.hitAir, hitAt, f); break;
                case Preview.DownSpecialImpact: Play(set.downSpecialHit ? set.downSpecialHit : set.hitGround, new Vector3(hitAt.x, Feet.y, Feet.z), f); break;
                case Preview.BlockForm: Play(set.blockStart ? set.blockStart : set.block, Feet, f); break;
                case Preview.GuardBreak: Play(set.guardBreak, Feet, f); break;
            }
        }

        /// <summary>The main cue a preview plays (the Inspector shows it for live editing).</summary>
        public VfxCue PreviewCue(Preview what)
        {
            var s = set;
            if (!s) return null;
            VfxCue Or(VfxCue a, VfxCue b) => a ? a : b;
            switch (what)
            {
                case Preview.Strike1: return s.lightCombo != null && s.lightCombo.Length > 0 ? s.lightCombo[0].strike : null;
                case Preview.Strike2: return s.lightCombo != null && s.lightCombo.Length > 1 ? s.lightCombo[1].strike : null;
                case Preview.HeavyStrike: return s.heavy.strike;
                case Preview.Hit: return s.hit;
                case Preview.HeavyHit: return Or(s.heavyHit, s.hit);
                case Preview.Block: return s.block;
                case Preview.Parry: return s.parry;
                case Preview.Dash: return s.dash;
                case Preview.Jump: return s.jump;
                case Preview.Land: return s.land;
                case Preview.Knockdown: return Or(s.knockdown, s.land);
                case Preview.KO: return s.ko;
                case Preview.ImpactGround: return s.hitGround;
                case Preview.ImpactAir: return s.hitAir;
                case Preview.DownSpecialImpact: return Or(s.downSpecialHit, s.hitGround);
                case Preview.BlockForm: return Or(s.blockStart, s.block);
                case Preview.GuardBreak: return s.guardBreak;
                default: return null;
            }
        }

        void PreviewAttack(VfxSet.AttackVfx vfx)
        {
            if (vfx.strike) Play(vfx.strike, StrikePoint, Facing, vfx.Scale, transform);
            if (vfx.dust) Play(vfx.dust, Feet, Facing, vfx.Scale);
        }

        /// <summary>Another fighter's feet (its own FighterVfx, so its Feet offset counts), else its position.</summary>
        static Vector3 FeetOf(Fighter target)
        {
            var tv = target.GetComponent<FighterVfx>();
            return tv ? tv.FeetPoint : target.transform.position;
        }

        public bool ShowAnchors => showAnchors;
        public enum Anchor { Feet, Strike, Hit, Body }

        /// <summary>World position of an anchor (offset included), for the Scene view handles.</summary>
        public Vector3 AnchorPosition(Anchor a)
        {
            if (!fighter) fighter = GetComponent<Fighter>();
            int f = Facing;
            switch (a)
            {
                case Anchor.Feet: return Feet;
                case Anchor.Strike: return StrikePoint;
                case Anchor.Hit: return StrikePoint - Offset(strikeOffset, f) + Offset(hitOffset, f);
                default: return Body + Offset(bodyOffset, f);
            }
        }

        /// <summary>Sets an anchor's offset so it sits at a world position (dragging a handle).</summary>
        public void SetAnchorPosition(Anchor a, Vector3 world)
        {
            int f = Facing;
            Vector3 zeroed = AnchorPosition(a) - Offset(GetOffset(a), f);
            Vector3 d = world - zeroed;
            Vector2 o = new Vector2(d.x * f, d.y);
            switch (a)
            {
                case Anchor.Feet: feetOffset = o; break;
                case Anchor.Strike: strikeOffset = o; break;
                case Anchor.Hit: hitOffset = o; break;
                default: bodyOffset = o; break;
            }
        }

        Vector2 GetOffset(Anchor a) =>
            a == Anchor.Feet ? feetOffset : a == Anchor.Strike ? strikeOffset : a == Anchor.Hit ? hitOffset : bodyOffset;

        // ───────────── Helpers ─────────────

        /// <summary>Anchor To Art cues: centred on a point of the drawing, sized 1:1 with it (follows the fighter).</summary>
        void PlayOnArt(VfxCue cue)
        {
            FindVisual();
            if (!visual) { Play(cue, StrikePoint, fighter.Facing, 1f, transform); return; }
            Vector3 at = visual.TransformPoint(new Vector3(cue.artPosition.x, cue.artPosition.y, 0f)); // root scale mirrors it with facing
            float size = Mathf.Abs(visual.lossyScale.y) / VfxPlayer.GlobalScale;
            VfxPlayer.Play(cue, at, fighter.Facing, size, art, transform);
        }

        void Play(VfxCue cue, Vector3 at, int facing, float extraScale = 1f, Transform follow = null)
        {
            if (!cue) return;
            FindVisual();
            float size = (visual ? Mathf.Abs(visual.lossyScale.y) : 1f) * sizeMultiplier * extraScale;
            VfxPlayer.Play(cue, at, facing, size, art, follow);
        }

        /// <summary>The active art (Visual / Visual_Monk / Visual_Rushdown): its scale sizes the effects, its sprite sorts them.</summary>
        void FindVisual()
        {
            if (visual && visual.gameObject.activeInHierarchy) return;
            var pose = GetComponentInChildren<FighterPoseView>(false);
            if (pose)
            {
                visual = pose.transform;
                art = pose.GetComponent<SpriteRenderer>();
            }
            else
            {
                art = GetComponentInChildren<SpriteRenderer>(false);
                visual = art ? art.transform : transform;
            }
        }

        Vector3 Feet
        {
            get
            {
                MeasureGround();
                return transform.position + new Vector3(0f, groundDrop, 0f) + Offset(feetOffset, Facing);
            }
        }

        /// <summary>The fighter's facing (+1 before Play mode, when Fighter hasn't run yet).</summary>
        int Facing => fighter && fighter.Facing != 0 ? fighter.Facing : 1;

        static Vector3 Offset(Vector2 o, int dir) => new Vector3(o.x * (dir < 0 ? -1f : 1f), o.y, 0f);

        /// <summary>Where the slash goes: the live hitbox while attacking, else the first light attack's box (for previews).</summary>
        Vector3 StrikePoint
        {
            get
            {
                Vector3 c;
                if (fighter && fighter.TryGetCurrentHitbox(out Vector2 centre, out _)) c = new Vector3(centre.x, centre.y, transform.position.z);
                else
                {
                    AttackData first = null;
                    if (fighter && fighter.LightCombo != null) foreach (var a in fighter.LightCombo) if (a) { first = a; break; }
                    c = transform.position + (first ? Offset(first.hitboxOffset, Facing) : new Vector3(0.8f * Facing, 1f, 0f));
                }
                return c + Offset(strikeOffset, Facing);
            }
        }

        Vector3 Body
        {
            get
            {
                FindVisual();
                return art && art.sprite ? art.bounds.center : transform.position + Vector3.up;
            }
        }

        /// <summary>Between the target and the attacker, at the attack's height (same rule as HitFeedback's sparks).</summary>
        Vector3 ContactPoint(Fighter target, AttackData attack, Fighter attacker = null)
        {
            if (!attacker) attacker = fighter;
            Vector3 t = target.transform.position;
            float x = Mathf.Lerp(t.x, attacker.X, 0.35f);
            float y = t.y + (attack ? attack.hitboxOffset.y : 0.5f);
            if (attacker.TryGetCurrentHitbox(out Vector2 centre, out _)) y = centre.y;
            return new Vector3(x, y, t.z);
        }
    }
}
