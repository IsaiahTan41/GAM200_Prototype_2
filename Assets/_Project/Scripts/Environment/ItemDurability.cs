using UnityEngine;
using KungFuCrashout.Fighters;
using KungFuCrashout.Sound;

namespace KungFuCrashout.Props
{
    /// <summary>
    /// Item weight rules from the Game Feel spec (§8 "Wear"), on top of Carryable's own rules (Max Holds, Break On Shield):
    ///   LIGHT  (plates, lanterns) — Break On Clean Hit: shatters on the fighter it hits
    ///   MEDIUM (chairs, the crate) — no extra rule: breaks when blocked without a catch, or after its last throw (Carryable)
    ///   HEAVY  (pots)              — survives Blocks Before Breaking − 1 blocks (needs Carryable → Break On Shield OFF)
    /// A parry-catch never breaks anything. Carryable.cs is unchanged from Prototype 2; this listens to its HitFighter event.
    ///
    /// Put on the item prefab next to Carryable (the GDD Build setup creates the Restaurant set with it).
    /// Place in: Assets/_Project/Scripts/Environment/ItemDurability.cs
    /// </summary>
    [RequireComponent(typeof(Carryable))]
    public class ItemDurability : MonoBehaviour
    {
        public enum Weight { Light, Medium, Heavy }

        [SerializeField] Weight weight = Weight.Medium;
        [Tooltip("Shatter when it hits a fighter cleanly (light items).")]
        [SerializeField] bool breakOnCleanHit = false;
        [Tooltip("Blocks it takes before breaking (heavy items: 2 = survives one block). 0 = leave blocking to Carryable.")]
        [SerializeField, Min(0)] int blocksBeforeBreaking = 0;
        [Tooltip("Played when this rule breaks the item (e.g. the stage's break / smash cue).")]
        [SerializeField] SfxCue breakCue;

        public Weight ItemWeight => weight;
        public int BlocksTaken { get; private set; }

        Carryable item;

        void Awake() => item = GetComponent<Carryable>();
        void OnEnable() => item.HitFighter += OnHitFighter;
        void OnDisable() => item.HitFighter -= OnHitFighter;

        void OnHitFighter(Fighter target, Fighter thrower, HitResult result)
        {
            if (result == HitResult.Hit && breakOnCleanHit)
            {
                Break("shattered on impact");
                return;
            }

            if (result == HitResult.Blocked && blocksBeforeBreaking > 0)
            {
                BlocksTaken++;
                if (BlocksTaken >= blocksBeforeBreaking) Break($"broke after {BlocksTaken} block(s)");
            }
        }

        void Break(string why)
        {
            Debug.Log($"[{name}] {why}");
            AudioManager.Play(breakCue);
            if (TryGetComponent(out Carryable carryable)) carryable.RaiseBroken(); // break VFX (ItemVfx)
            Destroy(gameObject); // end of frame: the hit, sparks and sound still play this frame
        }
    }
}
