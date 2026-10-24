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

        [Header("Hitstop (real seconds the game freezes)")]
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

        [Header("Mobile")]
        [Tooltip("Vibrate the phone on heavy hits and parries.")]
        [SerializeField] bool vibrate = true;

        [Header("Item hits (Step 3d)")]
        [Tooltip("Use each item's slow-motion beat after the freeze.")]
        [SerializeField] bool itemSlowMotion = true;
        [Tooltip("Played when a launched fighter lands Downed (optional).")]
        [SerializeField] AudioClip downedThud;
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
            foreach (var f in fighters)
            {
                if (!f) continue;
                f.AttackStarted += OnAttackStarted;
                f.HitConnected += OnHitConnected;
                f.Downed += OnDowned;
                f.WallSplatted += OnWallSplatted;
            }
        }

        void OnDisable()
        {
            foreach (var f in fighters)
            {
                if (!f) continue;
                f.AttackStarted -= OnAttackStarted;
                f.HitConnected -= OnHitConnected;
                f.Downed -= OnDowned;
                f.WallSplatted -= OnWallSplatted;
            }
            if (hitstopRoutine != null) { StopCoroutine(hitstopRoutine); RestoreTime(); }
        }

        void OnAttackStarted(AttackData attack)
        {
            Play(attack.weight == HitWeight.Heavy ? heavyWhoosh : lightWhoosh);
        }

        void OnHitConnected(Fighter target, AttackData attack, HitResult result)
        {
            bool heavy = attack.weight == HitWeight.Heavy;
            Vector3 point = ContactPoint(target, attack);

            // Thrown item, clean hit: the item's own, bigger impact.
            if (attack is ThrowableData item && result == HitResult.Hit)
            {
                PlayImpact(heavy: true);
                Burst(hitSpark, target.transform.position, heavySparkCount);
                Hitstop(item.impactHitstop,
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
                    PlayImpact(heavy);
                    Burst(hitSpark, point, heavy ? heavySparkCount : lightSparkCount);
                    Hitstop(heavy ? heavyHitstop : lightHitstop);
                    if (heavy) { Shake(heavyShake, AwayFromAttacker(target), ShakeKind.HeavyHit); Buzz(); }
                    break;

                case HitResult.Blocked:
                    Play(blockImpact);
                    Burst(blockSpark, point, lightSparkCount);
                    Hitstop(blockHitstop);
                    break;

                case HitResult.Parried:
                    Play(parryClang);
                    Burst(parryFlash, point, heavySparkCount);
                    Hitstop(parryHitstop);
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

        // ───────────── Hitstop ─────────────

        void OnDowned(float seconds)
        {
            Play(downedThud);
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
