using UnityEngine;
using KungFuCrashout.Fighters;

namespace KungFuCrashout.Sound
{
    /// <summary>
    /// Dash, jump and landing sounds for one fighter (AudioManager cues). A landing that turns into a knockdown (a launched
    /// fighter landing Downed) stays quiet here: HitFeedback plays the knockdown sound for it, so the two never stack.
    /// Place in: Assets/_Project/Scripts/Audio/FighterMovementSfx.cs
    /// </summary>
    [RequireComponent(typeof(Fighter))]
    public class FighterMovementSfx : MonoBehaviour
    {
        [SerializeField] SfxCue dashCue;
        [SerializeField] SfxCue jumpCue;
        [SerializeField] SfxCue landCue;

        Fighter fighter;

        void Awake() => fighter = GetComponent<Fighter>();

        void OnEnable()
        {
            fighter.StateChanged += OnStateChanged;
            fighter.Jumped += OnJumped;
            fighter.Landed += OnLanded;
            fighter.Downed += OnDowned;
        }

        void OnDisable()
        {
            fighter.StateChanged -= OnStateChanged;
            fighter.Jumped -= OnJumped;
            fighter.Landed -= OnLanded;
            fighter.Downed -= OnDowned;
            landPending = false;
        }

        void OnStateChanged(FighterState previous, FighterState next)
        {
            if (next == FighterState.Dashing) AudioManager.Play(dashCue);
        }

        void OnJumped(Vector2 direction) => AudioManager.Play(jumpCue);
        bool landPending;

        // The fighter raises Landed, then (same frame) Downed if the landing knocks them down: wait for the end of the frame.
        void OnLanded() => landPending = true;
        void OnDowned(float seconds) => landPending = false;

        void LateUpdate()
        {
            if (!landPending) return;
            landPending = false;
            AudioManager.Play(landCue);
        }
    }
}
