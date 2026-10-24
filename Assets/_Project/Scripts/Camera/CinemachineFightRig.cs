using System.Collections.Generic;
using UnityEngine;
using Unity.Cinemachine;
using KungFuCrashout.Core;
using KungFuCrashout.Fighters;

namespace KungFuCrashout.Feedback
{
    /// <summary>
    /// CAMERA HYBRID — the only script that talks to Cinemachine (3.1+). Add it AFTER installing
    /// com.unity.cinemachine; everything else compiles and works without it.
    ///
    /// Who does what:
    ///   LandscapeCameraFit (Output = Values Only) decides WHERE the camera goes and HOW WIDE it is
    ///     (follow fighters, zoom, stage clamp, ground line, phone aspect, fighter leash).
    ///   This rig copies that onto the Fight Camera (a CinemachineCamera with Position Control = None),
    ///     then updates the Cinemachine Brain itself (Update Method = Manual Update) so the order is
    ///     always: fighters move → framing → Cinemachine → HitFeedback. No lag, no jitter.
    ///   HitFeedback's shakes (ShakeRequested) become Cinemachine IMPULSES: directional, shaped per kind
    ///     (item hit, heavy hit, parry, downed, wall splat), layered, and played on the Fight Camera by its
    ///     Cinemachine Impulse Listener.
    ///   PUNCH-IN: on chosen events (item hit / downed / KO / perfect block) the Punch-In Camera — a tighter
    ///     CinemachineCamera kept disabled — is switched on for a moment, centred on the fighter that was
    ///     hit. The Brain blends to it and back using its Default Blend.
    ///
    /// ON/OFF SWITCH: while this component is enabled the Brain drives the Main Camera; disable it and the
    /// Brain is switched off and LandscapeCameraFit moves the camera directly again (Output = Camera).
    /// Runs in the editor too, so the Game view is framed correctly outside Play mode.
    ///
    /// Setup: menu KungFu Crashout → Set Up Cinemachine Camera (builds and wires everything).
    /// Place in: Assets/_Project/Scripts/Camera/CinemachineFightRig.cs
    /// </summary>
    [ExecuteAlways]
    [DefaultExecutionOrder(80)] // after LandscapeCameraFit (60), before HitFeedback (100)
    public class CinemachineFightRig : MonoBehaviour
    {
        [Header("References")]
        [Tooltip("The framing script on the Main Camera. Its Output is switched to Values Only while this rig is on.")]
        [SerializeField] LandscapeCameraFit framing;
        [Tooltip("The Cinemachine Brain on the Main Camera.")]
        [SerializeField] CinemachineBrain brain;
        [Tooltip("The main fight CinemachineCamera (Position Control = None, Rotation Control = None, with a Cinemachine Impulse Listener).")]
        [SerializeField] CinemachineCamera fightCamera;
        [Tooltip("For shakes. Usually the HitFeedback on the Main Camera.")]
        [SerializeField] HitFeedback hitFeedback;

        [Header("Time")]
        [Tooltip("Play impulses in real time, so a shake fired on a hit keeps moving through the hit freeze.")]
        [SerializeField] bool impulsesIgnoreTimeScale = true;

        [Header("Impulse shake")]
        [Tooltip("Used for any shake kind without its own source below. Impulse Type = Uniform is simplest for a single-screen fighter.")]
        [SerializeField] CinemachineImpulseSource defaultImpulse;
        [SerializeField] CinemachineImpulseSource heavyHitImpulse;
        [SerializeField] CinemachineImpulseSource itemHitImpulse;
        [SerializeField] CinemachineImpulseSource parryImpulse;
        [SerializeField] CinemachineImpulseSource downedImpulse;
        [SerializeField] CinemachineImpulseSource wallSplatImpulse;
        [Tooltip("Impulse strength = HitFeedback shake (world units) × this. Tune the overall shake here.")]
        [SerializeField, Min(0f)] float impulseGain = 1f;
        [Tooltip("Shakes with no direction (e.g. parry) use a random direction within this many degrees of straight up.")]
        [SerializeField, Range(0f, 180f)] float undirectedSpread = 60f;

        [Header("Punch-in camera (optional)")]
        [Tooltip("A second CinemachineCamera (Position Control = None, Priority higher than the Fight Camera), " +
                 "left DISABLED in the scene. Empty = no punch-in.")]
        [SerializeField] CinemachineCamera punchInCamera;
        [Tooltip("The fighters to watch for punch-in events.")]
        [SerializeField] Fighter[] fighters = new Fighter[2];
        [SerializeField] bool punchInOnItemHit = false;
        [SerializeField] bool punchInOnDowned = true;
        [SerializeField] bool punchInOnKO = true;
        [Tooltip("PERFECT BLOCK (parry): the same punch-in as a heavy / item stagger, on the fighter who parried.")]
        [SerializeField] bool punchInOnParry = true;
        [Tooltip("Punch-in view size as a fraction of the normal view. 0.7 = 30% closer.")]
        [SerializeField, Range(0.3f, 1f)] float punchInZoom = 0.7f;
        [Tooltip("How long the punch-in camera stays on (game seconds) before blending back.")]
        [SerializeField, Min(0.05f)] float punchInTime = 0.4f;
        [Tooltip("Shift the punch-in up from the fighter's centre (world units). Raise it to show more air above them.")]
        [SerializeField] float punchInHeight = 0.3f;

        Fighter punchTarget;
        float punchUntil = -1f;
        readonly List<System.Action> unsubscribe = new List<System.Action>();

        // ───────────── Unity ─────────────

        void OnEnable()
        {
            if (framing) framing.Output = LandscapeCameraFit.OutputMode.ValuesOnly;
            if (brain)
            {
                brain.enabled = true;
                brain.UpdateMethod = CinemachineBrain.UpdateMethods.ManualUpdate;
                brain.IgnoreTimeScale = false; // the framing already follows game time (holds during hitstop)
            }
            CinemachineImpulseManager.Instance.IgnoreTimeScale = impulsesIgnoreTimeScale;
            if (punchInCamera) punchInCamera.gameObject.SetActive(false);

            if (hitFeedback) hitFeedback.ShakeRequested += OnShakeRequested;
            SubscribeFighters();
        }

        void OnDisable()
        {
            if (hitFeedback) hitFeedback.ShakeRequested -= OnShakeRequested;
            foreach (var u in unsubscribe) u();
            unsubscribe.Clear();

            if (punchInCamera) punchInCamera.gameObject.SetActive(false);
            // Back to the plain code camera: the Brain lets go, LandscapeCameraFit moves the camera itself.
            if (framing) framing.Output = LandscapeCameraFit.OutputMode.Camera;
            if (brain) brain.enabled = false;
        }

        void LateUpdate()
        {
            if (!framing || !fightCamera) return;
            if (framing.Output != LandscapeCameraFit.OutputMode.ValuesOnly) framing.Output = LandscapeCameraFit.OutputMode.ValuesOnly;
            if (brain && brain.UpdateMethod != CinemachineBrain.UpdateMethods.ManualUpdate)
                brain.UpdateMethod = CinemachineBrain.UpdateMethods.ManualUpdate;

            // 1. Copy the code framing onto the fight camera.
            fightCamera.transform.position = framing.TargetPosition;
            SetOrthoSize(fightCamera, framing.TargetOrthoSize);

            // 2. Keep the punch-in camera on its fighter, or switch it off when its time is up.
            UpdatePunchIn();

            // 3. Let Cinemachine place the Main Camera now (blends + impulse shake).
            if (brain && brain.isActiveAndEnabled) brain.ManualUpdate();
        }

        // ───────────── Impulse shake ─────────────

        void OnShakeRequested(Vector2 direction, float strength, HitFeedback.ShakeKind kind)
        {
            var source = SourceFor(kind);
            if (!source || strength <= 0f) return;

            if (direction.sqrMagnitude < 0.0001f)
            {
                float angle = (90f + Random.Range(-undirectedSpread, undirectedSpread) * 0.5f) * Mathf.Deg2Rad;
                direction = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
            }

            // 2D: keep it in the screen plane. Velocity length = impulse strength.
            Vector3 velocity = new Vector3(direction.x, direction.y, 0f).normalized * strength * impulseGain;
            source.GenerateImpulseWithVelocity(velocity);

            if (kind == HitFeedback.ShakeKind.ItemHit && punchInOnItemHit) PunchInOnStrongestReaction();
        }

        CinemachineImpulseSource SourceFor(HitFeedback.ShakeKind kind)
        {
            CinemachineImpulseSource specific = kind switch
            {
                HitFeedback.ShakeKind.HeavyHit => heavyHitImpulse,
                HitFeedback.ShakeKind.ItemHit => itemHitImpulse,
                HitFeedback.ShakeKind.Parry => parryImpulse,
                HitFeedback.ShakeKind.Downed => downedImpulse,
                HitFeedback.ShakeKind.WallSplat => wallSplatImpulse,
                _ => null
            };
            return specific ? specific : defaultImpulse;
        }

        // ───────────── Punch-in ─────────────

        void SubscribeFighters()
        {
            foreach (var f in fighters)
            {
                if (!f) continue;
                var fighter = f;

                System.Action<float> onDowned = _ => { if (punchInOnDowned) PunchIn(fighter); };
                System.Action<FighterState, FighterState> onState = (prev, next) =>
                {
                    if (next == FighterState.KO && punchInOnKO) PunchIn(fighter);
                };

                System.Action<Fighter, AttackData> onParried = (attacker, attack) => { if (punchInOnParry) PunchIn(fighter); };

                fighter.Downed += onDowned;
                fighter.StateChanged += onState;
                fighter.Parried += onParried;
                unsubscribe.Add(() =>
                {
                    fighter.Downed -= onDowned;
                    fighter.StateChanged -= onState;
                    fighter.Parried -= onParried;
                });
            }
        }

        /// <summary>For an item hit (no fighter passed with the shake): punch in on whoever was launched.</summary>
        void PunchInOnStrongestReaction()
        {
            foreach (var f in fighters)
                if (f && f.IsLaunched) { PunchIn(f); return; }
        }

        public void PunchIn(Fighter target)
        {
            if (!punchInCamera || !target) return;
            punchTarget = target;
            punchUntil = Time.time + punchInTime;
            PlacePunchIn(); // in position before it goes live, so the blend starts from the right shot
            punchInCamera.gameObject.SetActive(true);
        }

        void UpdatePunchIn()
        {
            if (!punchInCamera || !punchInCamera.gameObject.activeSelf) return;

            if (Time.time >= punchUntil || !punchTarget)
            {
                punchInCamera.gameObject.SetActive(false); // the Brain blends back to the fight camera
                punchTarget = null;
                return;
            }
            PlacePunchIn();
        }

        void PlacePunchIn()
        {
            float size = framing.TargetOrthoSize * punchInZoom;
            Vector3 p = punchTarget.transform.position;
            float z = framing.TargetPosition.z;
            punchInCamera.transform.position = new Vector3(p.x, p.y + punchInHeight, z);
            SetOrthoSize(punchInCamera, size);
        }

        static void SetOrthoSize(CinemachineCamera cam, float size)
        {
            var lens = cam.Lens;
            lens.OrthographicSize = size;
            cam.Lens = lens;
        }
    }
}
