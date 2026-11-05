using System;
using System.Collections.Generic;
using UnityEngine;

namespace KungFuCrashout.Fighters
{
    /// <summary>
    /// Plays whole animation clips for specific attacks (e.g. the light combo → Monk_Attack) on the fighter's Visual.
    ///   • Fighter calls TryPlay when an attack starts. No clip for that attack = nothing happens (sprite poses as before).
    ///   • While a clip plays, FighterPoseView leaves the sprite to the Animator.
    ///   • The clip's Animation Events (HitboxOn / HitboxOff / CancelOpen / AttackEnd, received by FighterAnimationEvents
    ///     on this object) drive the attack's phases when Fighter → Use Animation Timing is on.
    ///   • Hitbox: a child BoxCollider2D whose Offset / Size are keyed in the clip. It stays DISABLED (it's only a shape:
    ///     an enabled collider would count as part of this fighter's hurtbox). Fighter reads its box during Active frames.
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

        /// <summary>A clip is playing: the Animator owns the sprite.</summary>
        public bool IsPlaying { get; private set; }
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
