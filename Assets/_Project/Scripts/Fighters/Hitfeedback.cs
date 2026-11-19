using System.Collections;
using UnityEngine;
using KungFuCrashout.Core;
using KungFuCrashout.Fighters;
using KungFuCrashout.Sound;

namespace KungFuCrashout.Feedback
{
    /// <summary>
    /// Game feel (M8): hitstop, screen shake, hit sparks and sounds for every attack, hit, block and parry.
    /// Listens to fighter events only; no combat code changes needed.
    ///
    /// Carried over from the Udemy sample:
    ///   Player1Trigger / Player2Trigger: EmitFX + Particles.Play() + Time.timeScale = PauseSpeed
    ///       → spark at the hit point + a short freeze. The sample slowed time to 0.6 and relied on an
    ///         animation event (ResetTime) to undo it, which could leave the game stuck in slow motion.
    ///         Here the freeze always ends after a fixed real-time duration.
    ///   Player1Actions.PunchWooshSound / KickWooshSound → whoosh when an attack starts (light vs heavy)
    ///   Player1Move.OnTriggerEnter LightPunch / HeavyPunch clips → impact sound by hit weight
    /// New: block and parry sounds/sparks, screen shake, and phone vibration on heavy hits.
    /// Camera hybrid: every screen shake is also announced through ShakeRequested (direction, strength, kind).
    /// When something listens (CinemachineFightRig turns it into a Cinemachine Impulse) and Use External
    /// Shake is on, this script skips its own position jitter, so the two never double up.
    /// Step 3d: thrown-item hits use their Throwable Data's Impact Feel (longer freeze, bigger shake,
    /// optional slow-motion beat); landing Downed and wall splats thud, shake and buzz.
    ///
    /// Setup: put on the Main Camera (next to LandscapeCameraFit). Assign both fighters, one AudioSource
    /// routed to the SFX mixer group, and optional particle systems placed anywhere in the scene.
    /// Place in: Assets/_Project/Scripts/Feedback/HitFeedback.cs
    /// </summary>
    [DefaultExecutionOrder(100)] // after LandscapeCameraFit, so the shake offset isn't overwritten
    public class HitFeedback : MonoBehaviour
    {
        /// <summary>What caused a shake, so a listener can pick a different shake shape for each.</summary>
        public enum ShakeKind { HeavyHit, ItemHit, Parry, Downed, WallSplat }

        /// <summary>
        /// (direction, strength, kind). Direction is the way the hit pushed (world space, may be zero),
        /// strength is the shake setting in world units. Raised for every shake.
        /// </summary>
        public event System.Action<Vector2, float, ShakeKind> ShakeRequested;

        [SerializeField] Fighter[] fighters = new Fighter[2];

        [Header("Hitstop mode")]
        [Tooltip("ON (new): hitstop in FRAMES (60 fps), tiered by what the hit means (Tiered hitstop below), shorter late in " +
                 "combos, and a shake on the fighter who was hit. OFF (original): the real-seconds values just below, no hit " +
                 "shake. Testing switch: flip it any time, even in Play mode.")]
        [SerializeField] bool tieredHitstop = true;

        [Header("Tiered hitstop (frames at 60 fps; Tiered Hitstop on)")]
        [Tooltip("A light / combo hit at neutral (red damage).")]
        [SerializeField, Min(0)] int neutralLightFrames = 6;
        [Tooltip("The last hit of the light combo or the air combo.")]
        [SerializeField, Min(0)] int finisherFrames = 8;
        [Tooltip("Heavy attacks and specials.")]
        [SerializeField, Min(0)] int heavyFrames = 11;
        [Tooltip("Blocked hits freeze this many frames less than the same hit landing.")]
        [SerializeField, Min(0)] int blockReduction = 2;
        [SerializeField, Min(0)] int parryFrames = 12;
        [Tooltip("The parried attacker keeps shaking this many frames after the freeze (the defender-only extra stop).")]
        [SerializeField, Min(0)] int parryVictimExtraFrames = 4;
        [Tooltip("A thrown item's clean hit (real damage, stagger). The item's slow-motion beat follows. " +
                 "An item's Hitstop Frames (≥ 0) overrides this.")]
        [SerializeField, Min(0)] int itemHitFrames = 14;
        [Tooltip("A hit on a Downed fighter (real damage).")]
        [SerializeField, Min(0)] int downedHitFrames = 8;
        [Tooltip("The round-ending hit. The KO slow motion follows.")]
        [SerializeField, Min(0)] int koFrames = 18;
        [SerializeField, Min(1)] int maxFrames = 20;
        [Tooltip("From this hit of a combo on, each light / heavy hit freezes 1 frame less, down to Min Combo Frames.")]
        [SerializeField, Min(2)] int comboDecayFromHit = 3;
        [SerializeField, Min(0)] int minComboFrames = 4;

        [Header("Hit shake (the fighter who was hit; Tiered Hitstop on)")]
        [SerializeField] bool hitShake = true;
        [Tooltip("Shake size per frame of hitstop (world units). Sideways on the ground, up and down in the air; fades out.")]
        [SerializeField, Min(0f)] float shakePerFrame = 0.008f;
        [SerializeField, Min(0f)] float maxHitShake = 0.15f;
        [Tooltip("Shakes per second.")]
        [SerializeField, Min(1f)] float shakeFrequency = 30f;
        [Tooltip("Blocked hits shake this fraction as much.")]
        [SerializeField, Range(0f, 1f)] float blockShakeScale = 0.5f;

        [Header("Hitstop (original: real seconds the game freezes; Tiered Hitstop off)")]
        [SerializeField] float lightHitstop = 0.04f;
        [SerializeField] float heavyHitstop = 0.09f;
        [SerializeField] float blockHitstop = 0.03f;
        [SerializeField] float parryHitstop = 0.15f;

        [Header("Screen shake (world units)")]
        [Tooltip("When a listener handles shakes (e.g. CinemachineFightRig), don't also jitter the camera here.")]
        [SerializeField] bool useExternalShake = true;
        [SerializeField] float heavyShake = 0.15f;
        [SerializeField] float parryShake = 0.1f;
        [SerializeField] float shakeDuration = 0.15f;

        [Header("Sparks (optional; one ParticleSystem each, Play On Awake off)")]
        [SerializeField] ParticleSystem hitSpark;
        [SerializeField] ParticleSystem blockSpark;
        [SerializeField] ParticleSystem parryFlash;
        [SerializeField] int lightSparkCount = 8;
        [SerializeField] int heavySparkCount = 20;

        [Header("Sound (optional)")]
        [SerializeField] AudioSource sfx;
        [SerializeField] AudioClip lightWhoosh;
        [SerializeField] AudioClip heavyWhoosh;
        [SerializeField] AudioClip lightImpact;
        [SerializeField] AudioClip heavyImpact;
        [SerializeField] AudioClip blockImpact;
        [SerializeField] AudioClip parryClang;
        [Tooltip("Small random pitch change so repeated hits don't sound identical.")]
        [SerializeField, Range(0f, 0.3f)] float pitchJitter = 0.08f;

        [Header("Impact cues (preferred over the impact clips above; need an AudioManager)")]
        [Tooltip("Light hits: jabs and combo hits. Randomised variations, e.g. SFX_PunchLight.")]
        [SerializeField] SfxCue lightImpactCue;
        [Tooltip("Heavy hits, finishers, thrown-item impacts and wall splats, e.g. SFX_PunchHeavy.")]
        [SerializeField] SfxCue heavyImpactCue;

        [Header("Basic attack hits: wooden staff")]
        [Tooltip("ON: every basic attack (light combo) hit plays the next sound of Basic Attack Wood Cue, cycling through " +
                 "Wooden_Stick_Hit_1…5 in order. OFF: the old sounds (Light / Heavy Impact Cue, the punches).")]
        [SerializeField] bool useWoodHitSounds = true;
        [Tooltip("SFX_WoodStickHit (Sequential order = cycles 1 → 5). Built by Window → KungFu Crashout → Audio → Set Up Wood Stick Hits.")]
        [SerializeField] SfxCue basicAttackWoodCue;

        public enum WoodLayering
        {
            [Tooltip("The wooden stick hit alone (the original).")] WoodOnly,
            [Tooltip("Punch B layered OVER the wood: one fused hit, the wood's low knock + the punch's crack.")] LayeredOver,
            [Tooltip("Punch B right AFTER the wood: a quick crack-then-thump double hit.")] LayeredAfter,
        }
        [Tooltip("How basic attack hits sound while Use Wood Hit Sounds is on. Wood Only = as before. " +
                 "Layered Over / After add the Punch B layer (Punch Layer Cue), cycling 1 → 5 alongside the wood.")]
        [SerializeField] WoodLayering woodLayering = WoodLayering.LayeredOver;
        [Tooltip("SFX_PunchB_Layer (Punch_B_fx_01…05, Sequential).")]
        [SerializeField] SfxCue punchLayerCue;
        [Header("Layered Over")]
        [Tooltip("Both layers are lowered so their peaks don't clip when summed (the fuller hit still sounds as loud).")]
        [SerializeField, Range(0f, 1f)] float overWoodVolume = 0.62f;
        [SerializeField, Range(0f, 1f)] float overPunchVolume = 0.45f;
        [Tooltip("The punch's peak lands this long after the wood's peak (seconds). Under ~0.015 it still reads as ONE hit; " +
                 "a few ms keeps the two peaks from stacking.")]
        [SerializeField, Range(0f, 0.03f)] float overStagger = 0.006f;
        [Header("Layered After")]
        [SerializeField, Range(0f, 1f)] float afterWoodVolume = 0.9f;
        [SerializeField, Range(0f, 1f)] float afterPunchVolume = 0.7f;
        [Tooltip("The punch starts this long after the wood's peak (seconds). ~0.03–0.06 = crack-thump; longer = two hits.")]
        [SerializeField, Range(0f, 0.15f)] float afterDelay = 0.045f;

        [Header("Punch layer per move (same Wood Layering mode, levels and timing as the basic attack)")]
        [Tooltip("Heavy attack: Punch_A_fx_01.")]
        [SerializeField] SfxCue heavyLayerCue;
        [Tooltip("Side special: Punch_A_fx_02.")]
        [SerializeField] SfxCue sideSpecialLayerCue;
        [Tooltip("Down special: Punch_A_fx_03.")]
        [SerializeField] SfxCue downSpecialLayerCue;
        [Tooltip("Up special: Punch_A_fx_04.")]
        [SerializeField] SfxCue upSpecialLayerCue;
        [Tooltip("Air combo hits. Empty = the basic attack's Punch Layer Cue (Punch B, cycling with it).")]
        [SerializeField] SfxCue airLayerCue;
        [Tooltip("Blocked staff hits: Punch_C_fx_01, layered with the wood INSTEAD of the block sound. " +
                 "Blocked thrown items keep the block sound. Wood Only mode = the block sound as before.")]
        [SerializeField] SfxCue blockLayerCue;
        [SerializeField] bool layerBlockedHits = true;

        [Header("Attack swing whooshes (every Monk swing: basic, heavy, air, specials — hit or miss; not item throws)")]
        [Tooltip("ON: every swing plays the next whoosh of Swing Whoosh Cue (cycling through all of them). OFF: the old " +
                 "Light / Heavy Whoosh clips (empty = silent, as before).")]
        [SerializeField] bool useSwingWhooshes = true;
        [Tooltip("SFX_SwingWhoosh (Sequential). Built by Window → KungFu Crashout → Audio → Set Up Swing Whooshes.")]
        [SerializeField] SfxCue swingWhooshCue;
        [Tooltip("Line each whoosh's loudest moment up with the moment the swing's hitbox turns on (skips the clip's lead-in, " +
                 "or waits if the strike is later). Off = every whoosh plays from its start the moment the swing starts.")]
        [SerializeField] bool alignWhooshToStrike = true;
        [Tooltip("The whoosh peaks this long BEFORE the hitbox turns on (seconds): the rush of air just ahead of the impact.")]
        [SerializeField, Range(0f, 0.15f)] float whooshLead = 0.03f;

        [Header("Mobile")]
        [Tooltip("Vibrate the phone on heavy hits and parries.")]
        [SerializeField] bool vibrate = true;

        [Header("Item hits (Step 3d)")]
        [Tooltip("Use each item's slow-motion beat after the freeze.")]
        [SerializeField] bool itemSlowMotion = true;
        [Tooltip("Played when a launched fighter lands Downed (optional).")]
        [SerializeField] AudioClip downedThud;
        [Tooltip("Knockdown: played when a launched fighter lands Downed (SFX_Knockdown). Empty = Downed Thud above.")]
        [SerializeField] SfxCue downedCue;

        [Header("Parry")]
        [Tooltip("Parry impact (SFX_Parry). Played from its first contact (the silent lead-in is skipped), so the tap lands on " +
                 "the parry frame and the deflect ring fills the parry freeze. Empty = Parry Clang above.")]
        [SerializeField] SfxCue parryCue;
        [Tooltip("Screen shake when a fighter lands Downed or splats on a wall (world units).")]
        [SerializeField] float downedShake = 0.12f;

        Coroutine hitstopRoutine;
        float resumeScale = 1f;   // the speed to go back to after a hitstop (normally 1)
        float lastSetScale = 1f;  // what we last set Time.timeScale to

        // No dependency on MatchManager: the game counts as paused when time is stopped and we didn't stop it.
        bool ExternallyPaused => Time.timeScale == 0f && hitstopRoutine == null;
        // Something else (e.g. a pause menu) changed the time scale during our hitstop: leave it alone.
        bool TimeTouchedByOthers => !Mathf.Approximately(Time.timeScale, lastSetScale);
        float shakeTimeLeft, shakeStrength;

        void OnEnable()
        {
            Active = this;
            foreach (var f in fighters)
            {
                if (!f) continue;
                if (!swingHandlers.ContainsKey(f))
                {
                    var fighter = f;
                    swingHandlers[f] = attack => OnAttackStarted(fighter, attack);
                }
                f.AttackStarted += swingHandlers[f];
                f.HitConnected += OnHitConnected;
                f.Downed += OnDowned;
                f.WallSplatted += OnWallSplatted;
            }
        }

        void OnDisable()
        {
            if (Active == this) Active = null;
            foreach (var f in fighters)
            {
                if (!f) continue;
                if (swingHandlers.TryGetValue(f, out var h)) f.AttackStarted -= h;
                f.HitConnected -= OnHitConnected;
                f.Downed -= OnDowned;
                f.WallSplatted -= OnWallSplatted;
            }
            if (hitstopRoutine != null) { StopCoroutine(hitstopRoutine); RestoreTime(); }
        }

        readonly System.Collections.Generic.Dictionary<Fighter, System.Action<AttackData>> swingHandlers =
            new System.Collections.Generic.Dictionary<Fighter, System.Action<AttackData>>();

        void OnAttackStarted(Fighter fighter, AttackData attack)
        {
            if (useSwingWhooshes && swingWhooshCue && swingWhooshCue.HasSound && AudioManager.Instance)
            {
                if (fighter && !fighter.IsThrowing && !(attack is ThrowableData)) PlaySwing(fighter, attack);
                return;
            }
            Play(attack.weight == HitWeight.Heavy ? heavyWhoosh : lightWhoosh);
        }

        /// <summary>The next whoosh, its peak timed to land Whoosh Lead before this swing's hitbox turns on.</summary>
        void PlaySwing(Fighter fighter, AttackData attack)
        {
            var clip = swingWhooshCue.NextClip();
            if (!clip) { AudioManager.Play(swingWhooshCue); return; }
            float pitch = swingWhooshCue.NextPitch();
            float volume = swingWhooshCue.NextVolume();
            float startAt = 0f;
            double delay = 0d;
            if (alignWhooshToStrike)
            {
                var timing = ClipTiming(clip, 0.01f);               // whooshes swell: a 10 ms envelope finds the real peak
                float peak = timing.y / pitch;                       // seconds of real time into the clip
                float strike = Mathf.Max(0f, TimeToStrike(fighter, attack) - whooshLead);
                if (peak > strike) startAt = (peak - strike) * pitch; // skip the lead-in
                else delay = strike - peak;                           // slow wind-up: wait
            }
            AudioManager.Instance.PlayClip(swingWhooshCue, clip, volume, pitch, startAt, delay);
        }

        /// <summary>Play the cue's next clip from its onset (a few ms before), skipping any silent lead-in. False = no cue / AudioManager.</summary>
        bool PlayFromOnset(SfxCue cue)
        {
            if (!cue || !cue.HasSound || !AudioManager.Instance) return false;
            var clip = cue.NextClip();
            if (!clip) { AudioManager.Play(cue); return true; }
            float start = Mathf.Max(0f, ClipTiming(clip).x - 0.005f);
            AudioManager.Instance.PlayClip(cue, clip, cue.NextVolume(), cue.NextPitch(), start);
            return true;
        }

        /// <summary>Seconds from the start of this swing to its first active (hitbox) frame.</summary>
        static float TimeToStrike(Fighter fighter, AttackData attack)
        {
            bool plainHeavy = attack == fighter.HeavyAttackData && !fighter.HeavyUsesAnimation;
            bool frames = !plainHeavy && fighter.UseFrameData && attack.frames != null && attack.frames.IsSet;
            return frames ? attack.frames.activeStart / 60f : attack.startup;
        }

        public bool UseSwingWhooshes { get => useSwingWhooshes; set => useSwingWhooshes = value; }

        void OnHitConnected(Fighter target, AttackData attack, HitResult result)
        {
            bool heavy = attack.weight == HitWeight.Heavy;
            Vector3 point = ContactPoint(target, attack);

            // Thrown item, clean hit: the item's own, bigger impact.
            if (attack is ThrowableData item && result == HitResult.Hit)
            {
                PlayImpact(heavy: true);
                Burst(hitSpark, target.transform.position, heavySparkCount);
                Hitstop(StopFor(item.impactHitstop, target, attack, result),
                        itemSlowMotion ? item.slowMotionScale : 1f,
                        itemSlowMotion ? item.slowMotionTime : 0f);
                // Already launched by now, so its air velocity is the knockback direction.
                Vector2 launchDir = target.AirVelocity.sqrMagnitude > 0.01f ? target.AirVelocity.normalized : Vector2.zero;
                Shake(item.impactShake, launchDir, ShakeKind.ItemHit);
                Buzz();
                return;
            }

            switch (result)
            {
                case HitResult.Hit:
                    if (!PlayStaffHit(target, attack, false)) PlayImpact(heavy);
                    Burst(hitSpark, point, heavy ? heavySparkCount : lightSparkCount);
                    Hitstop(StopFor(heavy ? heavyHitstop : lightHitstop, target, attack, result));
                    if (heavy) { Shake(heavyShake, AwayFromAttacker(target), ShakeKind.HeavyHit); Buzz(); }
                    break;

                case HitResult.Blocked:
                    if (!PlayStaffHit(target, attack, true)) Play(blockImpact); // else FighterDefenseSfx's block cue plays
                    Burst(blockSpark, point, lightSparkCount);
                    Hitstop(StopFor(blockHitstop, target, attack, result));
                    break;

                case HitResult.Parried:
                    if (!PlayFromOnset(parryCue)) Play(parryClang);
                    Burst(parryFlash, point, heavySparkCount);
                    Hitstop(StopFor(parryHitstop, target, attack, result));
                    Shake(parryShake, Vector2.zero, ShakeKind.Parry);
                    Buzz();
                    break;
            }
        }

        /// <summary>Unit vector pointing from the target's opponent to the target (the way the hit pushes).</summary>
        static Vector2 AwayFromAttacker(Fighter target)
        {
            var attacker = target.Opponent;
            if (!attacker) return Vector2.zero;
            return new Vector2(target.X >= attacker.X ? 1f : -1f, 0f);
        }

        /// <summary>Halfway between the target's centre and the attacker's hitbox, at hitbox height.</summary>
        static Vector3 ContactPoint(Fighter target, AttackData attack)
        {
            var attacker = target.Opponent;
            Vector3 t = target.transform.position;
            if (!attacker) return t;
            float x = Mathf.Lerp(t.x, attacker.X, 0.35f);
            return new Vector3(x, t.y + attack.hitboxOffset.y, t.z - 0.1f);
        }

        // ───────────── Tiered hitstop + hit shake ─────────────

        /// <summary>Real seconds the game freezes for this hit, plus the hit shake (Tiered Hitstop); else the original value.</summary>
        float StopFor(float originalSeconds, Fighter target, AttackData attack, HitResult result)
        {
            if (!tieredHitstop) return originalSeconds;
            int frames = TierFrames(target, attack, result);
            float seconds = frames / 60f;

            if (hitShake && frames > 0)
            {
                // Parry: the parried ATTACKER reels (and keeps shaking a few frames longer); otherwise the fighter who was hit.
                var victim = result == HitResult.Parried ? target.Opponent : target;
                float amp = Mathf.Min(maxHitShake, frames * shakePerFrame) * (result == HitResult.Blocked ? blockShakeScale : 1f);
                float time = seconds + (result == HitResult.Parried ? parryVictimExtraFrames / 60f : 0f);
                if (victim) HitShake.Shake(victim, amp, time, shakeFrequency);
            }
            return seconds;
        }

        int TierFrames(Fighter target, AttackData attack, HitResult result)
        {
            int frames;
            var attacker = target.Opponent;
            bool ko = result == HitResult.Hit && target.Health && target.Health.IsDead;

            if (result == HitResult.Parried) frames = parryFrames;
            else if (ko) frames = koFrames;
            else if (attack && attack.hitstopFrames >= 0) frames = attack.hitstopFrames;
            else if (attack is ThrowableData) frames = itemHitFrames;
            else if (result == HitResult.Hit && target.IsDowned) frames = downedHitFrames;
            else
            {
                bool heavy = attack && (attack.weight == HitWeight.Heavy || (attacker && attacker.IsDoingSpecial));
                bool finisher = attacker && IsFinisher(attacker);
                frames = heavy ? heavyFrames : finisher ? finisherFrames : neutralLightFrames;

                // Late in a combo each hit freezes a little less, so mashed strings stay snappy.
                int n = target.ComboHitsTaken;
                if (result == HitResult.Hit && n >= comboDecayFromHit)
                    frames = Mathf.Max(Mathf.Min(minComboFrames, frames), frames - (n - comboDecayFromHit + 1));
            }

            if (result == HitResult.Blocked) frames -= blockReduction;
            return Mathf.Clamp(frames, 0, maxFrames);
        }

        /// <summary>The attacker's current hit is the last of its light combo or air combo.</summary>
        static bool IsFinisher(Fighter attacker)
        {
            if (attacker.IsAirAttacking) return attacker.AirComboStep >= 3;
            int step = attacker.ComboStep;
            return step >= 1 && attacker.LightCombo != null && step >= attacker.LightCombo.Count;
        }

        // ───────────── Hitstop ─────────────

        /// <summary>Real time (Time.realtimeSinceStartup) the current hit freeze ends. MatchManager waits for it before the KO slow motion.</summary>
        public static float FrozenUntil { get; private set; }

        void OnDowned(float seconds)
        {
            if (downedCue && AudioManager.Instance) AudioManager.Play(downedCue);
            else Play(downedThud);
            Shake(downedShake, Vector2.down, ShakeKind.Downed);
            Buzz();
        }

        void OnWallSplatted(int side)
        {
            PlayImpact(heavy: true);
            Shake(Mathf.Max(downedShake, heavyShake), new Vector2(side, 0f), ShakeKind.WallSplat);
            Buzz();
        }

        /// <summary>Freeze for seconds, then optionally run at slowScale for slowTime (all real time).</summary>
        void Hitstop(float seconds, float slowScale = 1f, float slowTime = 0f)
        {
            if ((seconds <= 0f && slowTime <= 0f) || ExternallyPaused) return;
            if (hitstopRoutine != null) StopCoroutine(hitstopRoutine);
            else resumeScale = Time.timeScale > 0f ? Time.timeScale : 1f;
            hitstopRoutine = StartCoroutine(HitstopRoutine(seconds, slowScale, slowTime));
            FrozenUntil = Time.realtimeSinceStartup + Mathf.Max(0f, seconds);
        }

        IEnumerator HitstopRoutine(float seconds, float slowScale, float slowTime)
        {
            if (seconds > 0f)
            {
                SetScale(0f);
                yield return new WaitForSecondsRealtime(seconds);
            }
            if (slowTime > 0f && slowScale < 1f && !TimeTouchedByOthers)
            {
                SetScale(slowScale);
                yield return new WaitForSecondsRealtime(slowTime);
            }
            if (!TimeTouchedByOthers) RestoreTime();
            hitstopRoutine = null;
        }

        void SetScale(float scale)
        {
            Time.timeScale = scale;
            lastSetScale = scale;
        }

        void RestoreTime() => SetScale(resumeScale);

        // ───────────── Shake ─────────────

        void Shake(float strength, Vector2 direction, ShakeKind kind)
        {
            if (strength <= 0f) return;
            ShakeRequested?.Invoke(direction, strength, kind);
            if (useExternalShake && ShakeRequested != null) return; // a listener (Cinemachine) shakes instead

            shakeStrength = Mathf.Max(shakeStrength, strength);
            shakeTimeLeft = shakeDuration;
        }

        void LateUpdate()
        {
            if (shakeTimeLeft <= 0f) return;

            shakeTimeLeft -= Time.unscaledDeltaTime;
            float fade = Mathf.Clamp01(shakeTimeLeft / shakeDuration);
            Vector2 offset = Random.insideUnitCircle * shakeStrength * fade;
            transform.position += new Vector3(offset.x, offset.y, 0f);
            if (shakeTimeLeft <= 0f) shakeStrength = 0f;
        }

        // ───────────── Helpers ─────────────

        void Burst(ParticleSystem ps, Vector3 at, int count)
        {
            if (!ps) return;
            ps.transform.position = at;
            ps.Emit(count);
        }

        /// <summary>The punch layer for this staff hit (null = not a staff hit: items, wall splats…). Wood-only mode still needs a non-null answer.</summary>
        SfxCue LayerFor(Fighter attacker, AttackData attack, bool blocked, out bool isStaffHit)
        {
            isStaffHit = false;
            if (!attacker || !attack || attack is ThrowableData || attacker.State != FighterState.Attacking || attacker.IsThrowing) return null;
            isStaffHit = true;
            if (blocked) return blockLayerCue;
            if (attacker.IsAirAttacking) return airLayerCue ? airLayerCue : punchLayerCue;
            if (attacker.IsDoingSpecial)
            {
                if (attack == attacker.SpecialFor(SpecialMove.Side)) return sideSpecialLayerCue;
                if (attack == attacker.SpecialFor(SpecialMove.Up)) return upSpecialLayerCue;
                if (attack == attacker.SpecialFor(SpecialMove.Down)) return downSpecialLayerCue;
                return heavyLayerCue;
            }
            if (attack == attacker.HeavyAttackData) return heavyLayerCue;
            if (attacker.ComboStep >= 1) return punchLayerCue;   // basic attack (light combo)
            isStaffHit = false;
            return null;
        }

        /// <summary>True while blocked staff hits are played here (wood + Punch C), so FighterDefenseSfx skips its block cue.</summary>
        public static bool PlaysBlockedStaffHits(AttackData attack) =>
            Active && Active.isActiveAndEnabled && !(attack is ThrowableData) && Active.BlockLayerOn;

        bool BlockLayerOn => useWoodHitSounds && layerBlockedHits && blockLayerCue && blockLayerCue.HasSound &&
                             basicAttackWoodCue && woodLayering != WoodLayering.WoodOnly && AudioManager.Instance;

        static HitFeedback Active;

        /// <summary>
        /// Wood sounds on: a Monk staff hit = the wooden stick hit (cycling 1 → 5) + this move's punch layer, layered the same
        /// way for every move (Wood Layering: Wood Only / Layered Over / Layered After, same levels and timing).
        /// Basic attack → Punch B (cycling), air combo → Punch B, heavy → Punch A 01, side / down / up special → Punch A 02 / 03 / 04,
        /// blocked → Punch C. False = not handled (not a staff hit, or wood sounds off): the old sound plays.
        /// </summary>
        bool PlayStaffHit(Fighter target, AttackData attack, bool blocked)
        {
            if (!useWoodHitSounds || !basicAttackWoodCue || !AudioManager.Instance) return false;
            var attacker = target ? target.Opponent : null;
            var layer = LayerFor(attacker, attack, blocked, out bool staff);
            if (!staff) return false;
            if (blocked && !BlockLayerOn) return false;                 // the block sound as before

            if (woodLayering == WoodLayering.WoodOnly || !layer || !layer.HasSound)
            {
                AudioManager.Play(basicAttackWoodCue);
                return true;
            }

            var am = AudioManager.Instance;
            var wood = basicAttackWoodCue.NextClip();
            var punch = layer.NextClip();
            if (!wood) { AudioManager.Play(basicAttackWoodCue); return true; }
            float pitch = basicAttackWoodCue.NextPitch();  // one pitch for both layers, so they fuse
            var wt = ClipTiming(wood);
            var pt = ClipTiming(punch);
            bool over = woodLayering == WoodLayering.LayeredOver;

            // Variation only ever lowers a layer here, so the summed peaks stay under the levels tested for clipping.
            float woodVol = (over ? overWoodVolume : afterWoodVolume) * Mathf.Min(basicAttackWoodCue.volume, basicAttackWoodCue.NextVolume());
            if (!am.PlayClip(basicAttackWoodCue, wood, woodVol, pitch)) return true; // cooldown: a double trigger
            if (!punch) return true;

            float punchVol = (over ? overPunchVolume : afterPunchVolume) * Mathf.Min(layer.volume, layer.NextVolume());
            if (over)
            {
                // Skip the punch's lead-in so its peak lands Over Stagger after the wood's peak.
                float skip = pt.y - wt.y - overStagger * pitch;
                if (skip >= 0f) am.PlayClip(layer, punch, punchVol, pitch, skip, 0d, false);
                else am.PlayClip(layer, punch, punchVol, pitch, 0f, -skip / pitch, false);
            }
            else
            {
                // From the punch's own onset (no lead-in), After Delay after the wood's peak.
                am.PlayClip(layer, punch, punchVol, pitch, pt.x, wt.y / pitch + afterDelay, false);
            }
            return true;
        }

        public WoodLayering Layering { get => woodLayering; set => woodLayering = value; }

        // Onset (x) and peak (y) of each clip in seconds, measured once from its samples (2 ms envelope; onset = 10 % of peak).
        static readonly System.Collections.Generic.Dictionary<(AudioClip, int), Vector2> clipTiming =
            new System.Collections.Generic.Dictionary<(AudioClip, int), Vector2>();

        /// <summary>Onset (x) and peak (y) in seconds, from an envelope Window seconds long (2 ms for hits, longer for whooshes).</summary>
        static Vector2 ClipTiming(AudioClip clip, float window = 0.002f)
        {
            if (!clip) return Vector2.zero;
            var key = (clip, Mathf.RoundToInt(window * 10000f));
            if (clipTiming.TryGetValue(key, out var v)) return v;
            v = Vector2.zero;
            if (clip.loadState != AudioDataLoadState.Loaded) clip.LoadAudioData();
            int ch = Mathf.Max(1, clip.channels), frames = clip.samples;
            if (frames > 0 && clip.loadType == AudioClipLoadType.DecompressOnLoad && clip.loadState == AudioDataLoadState.Loaded)
            {
                var data = new float[frames * ch];
                if (clip.GetData(data, 0))
                {
                    int win = Mathf.Max(1, Mathf.RoundToInt(clip.frequency * window));
                    var env = new float[frames];
                    float sum = 0f, max = 0f; int peak = 0;
                    for (int i = 0; i < frames; i++)
                    {
                        float a = 0f;
                        for (int c = 0; c < ch; c++) a += Mathf.Abs(data[i * ch + c]);
                        a /= ch;
                        sum += a;
                        if (i >= win)
                        {
                            float o = 0f;
                            for (int c = 0; c < ch; c++) o += Mathf.Abs(data[(i - win) * ch + c]);
                            sum -= o / ch;
                        }
                        env[i] = sum / win;
                        if (env[i] > max) { max = env[i]; peak = i; }
                    }
                    int onset = 0;
                    for (int i = 0; i < frames; i++) if (env[i] >= max * 0.1f) { onset = i; break; }
                    float half = win * 0.5f; // the window trails the signal
                    v = new Vector2(Mathf.Max(0f, (onset - half) / clip.frequency), Mathf.Max(0f, (peak - half) / clip.frequency));
                }
            }
            clipTiming[key] = v;
            return v;
        }

        void Start()
        {
            // Measure the layer clips now, not on the first hit.
            foreach (var cue in new[] { basicAttackWoodCue, punchLayerCue, heavyLayerCue, sideSpecialLayerCue,
                                        downSpecialLayerCue, upSpecialLayerCue, airLayerCue, blockLayerCue })
                if (cue && cue.clips != null) foreach (var c in cue.clips) ClipTiming(c);
            if (swingWhooshCue && swingWhooshCue.clips != null) foreach (var c in swingWhooshCue.clips) ClipTiming(c, 0.01f);
            if (parryCue && parryCue.clips != null) foreach (var c in parryCue.clips) ClipTiming(c);
        }

        /// <summary>Wood (true) or the old punch sounds (false) for basic attack hits.</summary>
        public bool UseWoodHitSounds { get => useWoodHitSounds; set => useWoodHitSounds = value; }

        /// <summary>Hit sound: the randomised cue through AudioManager if set up, else the single clip.</summary>
        void PlayImpact(bool heavy)
        {
            var cue = heavy ? heavyImpactCue : lightImpactCue;
            if (cue && AudioManager.Instance) AudioManager.Play(cue);
            else Play(heavy ? heavyImpact : lightImpact);
        }

        void Play(AudioClip clip)
        {
            if (!sfx || !clip) return;
            sfx.pitch = 1f + Random.Range(-pitchJitter, pitchJitter);
            sfx.PlayOneShot(clip);
        }

        void Buzz()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (vibrate) Handheld.Vibrate();
#endif
        }
    }
}
