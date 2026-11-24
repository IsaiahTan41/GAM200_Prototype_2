using System;
using System.Collections.Generic;
using UnityEngine;

namespace KungFuCrashout.Fighters
{
    /// <summary>Every non-attack drawing FighterPoseView can show. Each can have its own clip (FighterAnimator → Pose Clips).</summary>
    public enum PoseSlot
    {
        None = 0, Idle, Block, Dash, Hitstun, Airborne, Downed, KO, Carry, Throw,
        WindUp, Attack, HeavyAttack, Recovery, SideSpecial, UpSpecial, DownSpecial,
    }

    /// <summary>
    /// Plays whole animation clips for specific attacks (e.g. the light combo → Monk_Attack) on the fighter's Visual.
    ///   • Fighter calls TryPlay when an attack starts. No clip for that attack = nothing happens (sprite poses as before).
    ///   • While a clip plays, FighterPoseView leaves the sprite to the Animator.
    ///   • The clip's Animation Events (HitboxOn / HitboxOff / CancelOpen / AttackEnd, received by FighterAnimationEvents
    ///     on this object) drive the attack's phases when Fighter → Use Animation Timing is on.
    ///   • Hitbox: a child BoxCollider2D whose Offset / Size are keyed in the clip. It stays DISABLED (it's only a shape:
    ///     an enabled collider would count as part of this fighter's hurtbox). Fighter reads its box during Active frames.
    ///   • POSE CLIPS: everything that isn't an attack clip (idle, block, dash, hit, jump, downed, the special poses…) is a
    ///     clip too, one Animator state per pose, shown by the same 60 fps frame counter (speed 0, Play at the frame). A pose
    ///     clip can be one drawing or many (looping, or played once and held); FighterPoseView picks the pose and the clip
    ///     draws it. Built by Window → KungFu Crashout → Animation → Set Up Pose Clips (all sprites). Off = the old sprite swap.
    ///   • FRAME DATA (Fighter → Use Frame Data): instead of playing the clip, the Fighter's 60 fps frame counter decides the
    ///     frame and ShowFrame jumps the clip to it (Animator speed 0), so the drawing on screen always matches the frame the
    ///     game is checking hits on. A slow phone frame can skip a drawing on screen, never a gameplay frame.
    /// Plays states by name (the clip's name) on layer 0, so the controller needs a state per clip plus an "Idle" state.
    /// Setup: Window → KungFu Crashout → Animation → Set Up Monk Attack (Player). Place in: Assets/_Project/Scripts/Fighters/FighterAnimator.cs
    /// </summary>
    [RequireComponent(typeof(Animator))]
    public class FighterAnimator : MonoBehaviour
    {
        [Serializable]
        public class AttackClip
        {
            public AttackData attack;
            [Tooltip("Played whole when this attack starts. The Animator Controller needs a state with this clip's name.")]
            public AnimationClip clip;
        }

        [SerializeField] Animator animator;
        [Tooltip("Which attacks play a clip. Attacks not listed keep the sprite poses and AttackData timing.")]
        [SerializeField] List<AttackClip> attackClips = new List<AttackClip>();
        [Tooltip("Disabled BoxCollider2D (child \"Hitbox\") whose Offset / Size the clips key per frame.")]
        [SerializeField] BoxCollider2D hitbox;
        [Tooltip("Empty state the Animator returns to when no clip is playing (the sprite poses take over).")]
        [SerializeField] string idleState = "Idle";

        [Serializable]
        public class PoseClip
        {
            public PoseSlot pose;
            [Tooltip("One drawing or several. The Animator Controller needs a state with this clip's name.")]
            public AnimationClip clip;
            [Tooltip("Loop while the pose lasts (idle, block, dash). Off = play once and hold the last drawing (hit, downed).")]
            public bool loop = true;
        }

        [Header("Pose clips (everything that isn't an attack)")]
        [Tooltip("ON: idle, block, dash, hit, jump… are shown by their pose clips through the Animator, on the 60 fps frame " +
                 "counter (frozen by hitstop like the attacks). OFF: FighterPoseView swaps the sprite directly (the old way).")]
        [SerializeField] bool usePoseClips = true;
        [SerializeField] List<PoseClip> poseClips = new List<PoseClip>();

        /// <summary>A clip is playing: the Animator owns the sprite.</summary>
        public bool IsPlaying { get; private set; }
        /// <summary>The clip is being shown frame by frame by the Fighter (frame data), not playing by itself.</summary>
        public bool IsFrameDriven { get; private set; }

        int currentState;
        AnimationClip currentClip;

        /// <summary>The clip mapped to this attack (null = none).</summary>
        public AnimationClip ClipFor(AttackData attack)
        {
            foreach (var entry in attackClips)
                if (entry != null && entry.attack == attack && entry.clip) return entry.clip;
            return null;
        }

        // ───────────── Pose clips ─────────────

        const float Step = 1f / 60f;
        PoseSlot curPose;
        PoseClip curPoseClip;
        int poseHash, poseFrame, poseShown = -1;
        float poseAcc;
        readonly HashSet<string> warned = new HashSet<string>();

        /// <summary>Pose clips on / off at play time (Inspector: Use Pose Clips).</summary>
        public bool UsePoseClips
        {
            get => usePoseClips;
            set { usePoseClips = value; if (!value) ReleasePose(); }
        }
        /// <summary>The pose clip on screen (None = an attack clip or the plain sprite).</summary>
        public PoseSlot CurrentPose => curPose;
        /// <summary>Frames (60 fps) since the current pose started.</summary>
        public int PoseFrame => poseFrame;

        PoseClip PoseClipFor(PoseSlot pose)
        {
            foreach (var e in poseClips)
                if (e != null && e.pose == pose && e.clip) return e;
            return null;
        }

        /// <summary>
        /// Show this pose through its clip, advancing its own 60 fps frame counter by dt (0 during hitstop). Called by
        /// FighterPoseView every LateUpdate. False = no clip for it (or an attack clip owns the sprite): set the sprite yourself.
        /// </summary>
        public bool ShowPose(PoseSlot pose, float dt)
        {
            if (IsPlaying) { curPose = PoseSlot.None; curPoseClip = null; return false; } // an attack owns the Animator
            var entry = usePoseClips && pose != PoseSlot.None && animator && animator.runtimeAnimatorController ? PoseClipFor(pose) : null;
            if (entry == null) { ReleasePose(); return false; }

            if (pose != curPose || entry != curPoseClip)
            {
                int hash = Animator.StringToHash(entry.clip.name);
                if (!animator.HasState(0, hash))
                {
                    if (warned.Add(entry.clip.name))
                        Debug.LogWarning($"[FighterAnimator] No state named '{entry.clip.name}' in {animator.runtimeAnimatorController.name}. " +
                                         "Run Set Up Pose Clips.", this);
                    ReleasePose();
                    return false;
                }
                curPose = pose; curPoseClip = entry; poseHash = hash;
                poseFrame = 0; poseAcc = 0f; poseShown = -1;
            }
            else
            {
                poseAcc += dt;
                int steps = 0;
                while (poseAcc >= Step && steps < 8) { poseAcc -= Step; poseFrame++; steps++; }
                if (steps >= 8) poseAcc = 0f;
            }

            var clip = entry.clip;
            int total = Mathf.Max(1, Mathf.RoundToInt(clip.length * 60f));
            int f = entry.loop ? poseFrame % total : Mathf.Min(poseFrame, total - 1);
            if (f != poseShown)
            {
                animator.speed = 0f;
                animator.Play(poseHash, 0, Mathf.Clamp((f + 0.05f) / 60f / Mathf.Max(Step, clip.length), 0f, 0.999f));
                animator.Update(0f); // draw it this frame, not next
                poseShown = f;
            }
            return true;
        }

        /// <summary>Start the pose's clip again from its first frame if it's the one on screen (a new hit restarts the hit animation).</summary>
        public void RestartPose(PoseSlot pose)
        {
            if (curPose != pose) return; // a different pose: switching to it starts it from frame 0 anyway
            poseFrame = 0;
            poseAcc = 0f;
            poseShown = -1;
        }

        /// <summary>Length of this pose's clip in 60 fps frames (0 = no clip for it).</summary>
        public int PoseClipFrames(PoseSlot pose)
        {
            var e = PoseClipFor(pose);
            return e != null ? Mathf.Max(1, Mathf.RoundToInt(e.clip.length * 60f)) : 0;
        }

        void ReleasePose()
        {
            if (curPose == PoseSlot.None) return;
            curPose = PoseSlot.None;
            curPoseClip = null;
            if (!animator || !animator.runtimeAnimatorController || IsPlaying) return;
            animator.speed = 1f;
            int idle = Animator.StringToHash(idleState);
            if (animator.HasState(0, idle)) animator.Play(idle, 0, 0f);
        }

        /// <summary>Frame data: take over this attack's clip at frame 0 and hold it there (Animator speed 0). False if it has none.</summary>
        public bool TryPlayFrames(AttackData attack)
        {
            if (!attack || !animator || !animator.runtimeAnimatorController) return false;
            var clip = ClipFor(attack);
            if (!clip) return false;
            int state = Animator.StringToHash(clip.name);
            if (!animator.HasState(0, state))
            {
                Debug.LogWarning($"[FighterAnimator] No state named '{clip.name}' in {animator.runtimeAnimatorController.name}.", this);
                return false;
            }
            currentState = state;
            currentClip = clip;
            animator.speed = 0f;
            animator.Play(state, 0, 0f);
            IsPlaying = true;
            IsFrameDriven = true;
            return true;
        }

        /// <summary>Frame data: show this frame (60 fps) of the current clip.</summary>
        public void ShowFrame(int frame)
        {
            if (!IsFrameDriven || !currentClip || !animator) return;
            float length = Mathf.Max(1f / 60f, currentClip.length);
            float t = Mathf.Clamp((frame + 0.05f) / 60f / length, 0f, 0.999f); // never 1: that would exit to Idle
            animator.Play(currentState, 0, t);
        }
        public BoxCollider2D Hitbox => hitbox;

        void Awake()
        {
            if (!animator) animator = GetComponent<Animator>();
            if (hitbox) hitbox.enabled = false; // a shape only, never a physics collider
        }

        /// <summary>Play the clip for this attack from its first frame. False if it has none.</summary>
        public bool TryPlay(AttackData attack, out float length)
        {
            length = 0f;
            if (!attack || !animator || !animator.runtimeAnimatorController) return false;

            foreach (var entry in attackClips)
            {
                if (entry == null || entry.attack != attack || !entry.clip) continue;

                int state = Animator.StringToHash(entry.clip.name);
                if (!animator.HasState(0, state))
                {
                    Debug.LogWarning($"[FighterAnimator] No state named '{entry.clip.name}' in {animator.runtimeAnimatorController.name}.", this);
                    return false;
                }
                animator.speed = 1f;
                IsFrameDriven = false;
                animator.Play(state, 0, 0f); // restarts it even if it's already playing (combo hits)
                length = entry.clip.length;
                IsPlaying = true;
                return true;
            }
            return false;
        }

        /// <summary>Stop the clip: back to the empty Idle state, the sprite poses take over again.</summary>
        public void Stop()
        {
            if (!IsPlaying) return;
            IsPlaying = false;
            IsFrameDriven = false;
            currentClip = null;
            if (animator) animator.speed = 1f;
            if (!animator || !animator.runtimeAnimatorController) return;
            int idle = Animator.StringToHash(idleState);
            if (animator.HasState(0, idle)) animator.Play(idle, 0, 0f);
        }

        /// <summary>The keyed hitbox in world space (works while the collider is disabled).</summary>
        public bool TryGetHitbox(out Vector2 centre, out Vector2 size)
        {
            centre = size = Vector2.zero;
            if (!hitbox) return false;
            var t = hitbox.transform;
            centre = t.TransformPoint(hitbox.offset);
            Vector3 s = t.lossyScale;
            size = new Vector2(hitbox.size.x * Mathf.Abs(s.x), hitbox.size.y * Mathf.Abs(s.y));
            return size.x > 0f && size.y > 0f;
        }

        void OnDrawGizmosSelected()
        {
            if (!TryGetHitbox(out var c, out var s)) return;
            Gizmos.color = new Color(1f, 0.2f, 0.2f, 0.8f);
            Gizmos.DrawWireCube(c, s);
        }
    }
}
