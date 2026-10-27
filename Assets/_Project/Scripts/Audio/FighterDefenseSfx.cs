using UnityEngine;
using KungFuCrashout.Fighters;
using KungFuCrashout.Props;

namespace KungFuCrashout.Sound
{
    /// <summary>
    /// Defence sounds for one fighter, played through AudioManager (same cue pipeline as the punches):
    ///   Block Cue — every blocked hit: manual hold-to-block, the automatic block at neutral, and blocked thrown items
    ///   Catch Cue — catching a thrown item with a perfect block (needs an ItemHolder)
    ///   Stagger Cue — the big stinger when this fighter is staggered: a guard break (stamina ran out, from a blocked hit
    ///                 or from holding block) and/or a clean thrown-item hit that launches them
    /// Empty slot = silent. Setup: KungFu Crashout → Set Up Block Audio.
    /// Place in: Assets/_Project/Scripts/Audio/FighterDefenseSfx.cs
    /// </summary>
    [RequireComponent(typeof(Fighter))]
    public class FighterDefenseSfx : MonoBehaviour
    {
        [SerializeField] SfxCue blockCue;
        [SerializeField] SfxCue catchCue;

        [Header("Stagger")]
        [SerializeField] SfxCue staggerCue;
        [Tooltip("Play the stagger stinger on a guard break (stamina ran out).")]
        [SerializeField] bool onGuardBreak = true;
        [Tooltip("Play the stagger stinger when a thrown item hits cleanly and launches this fighter.")]
        [SerializeField] bool onItemStagger = true;

        Fighter fighter;
        ItemHolder holder;

        void Awake() => fighter = GetComponent<Fighter>();

        void OnEnable()
        {
            fighter.Blocked += OnBlocked;
            fighter.GuardBroken += OnGuardBroken;
            fighter.HitByItem += OnHitByItem;
            HookHolder();
        }

        // The training dummy may add its ItemHolder at runtime, after OnEnable.
        void Start() => HookHolder();

        void OnDisable()
        {
            fighter.Blocked -= OnBlocked;
            fighter.GuardBroken -= OnGuardBroken;
            fighter.HitByItem -= OnHitByItem;
            if (holder) holder.Caught -= OnCaught;
            holder = null;
        }

        void HookHolder()
        {
            if (holder) return;
            holder = GetComponent<ItemHolder>();
            if (holder) holder.Caught += OnCaught;
        }

        void OnBlocked(Fighter attacker, AttackData attack) => AudioManager.Play(blockCue);
        void OnCaught(Carryable item) => AudioManager.Play(catchCue);

        void OnGuardBroken()
        {
            if (onGuardBreak) AudioManager.Play(staggerCue);
        }

        void OnHitByItem(Fighter thrower, ThrowableData data, HitResult result, Vector2 direction)
        {
            // A clean hit that launched us = a new stagger (a hit while already Downed doesn't count).
            if (onItemStagger && result == HitResult.Hit && fighter.IsLaunched) AudioManager.Play(staggerCue);
        }
    }
}
