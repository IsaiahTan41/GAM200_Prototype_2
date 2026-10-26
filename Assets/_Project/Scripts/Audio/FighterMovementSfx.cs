using UnityEngine;
using KungFuCrashout.Fighters;

namespace KungFuCrashout.Sound
{
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
        }

        void OnDisable()
        {
            fighter.StateChanged -= OnStateChanged;
            fighter.Jumped -= OnJumped;
            fighter.Landed -= OnLanded;
        }

        void OnStateChanged(FighterState previous, FighterState next)
        {
            if (next == FighterState.Dashing) AudioManager.Play(dashCue);
        }

        void OnJumped(Vector2 direction) => AudioManager.Play(jumpCue);
        void OnLanded() => AudioManager.Play(landCue);
    }
}
