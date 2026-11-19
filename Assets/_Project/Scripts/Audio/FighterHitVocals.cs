using UnityEngine;
using KungFuCrashout.Fighters;

namespace KungFuCrashout.Sound
{
    /// <summary>
    /// Hit vocals: this fighter's voice (a grunt / shout) now and then when one of their attacks lands on the opponent.
    /// Every attack counts (basic, heavy, air, specials, and thrown items if ticked); blocked or parried hits don't.
    /// Spacing so it never gets constant:
    ///   • after a vocal, none for Min Interval + a random 0…Random Extra seconds (real time)
    ///   • once that has passed, each landed hit has Chance to trigger one; big hits (heavy, specials, the last hit of a
    ///     combo) always do if Big Hits Always is on
    ///   • the cue plays its clips shuffled, so the same vocal never plays twice in a row
    /// Setup: Window → KungFu Crashout → Audio → Set Up Hit Vocals. Place in: Assets/_Project/Scripts/Audio/FighterHitVocals.cs
    /// </summary>
    [RequireComponent(typeof(Fighter))]
    public class FighterHitVocals : MonoBehaviour
    {
        [SerializeField] bool playVocals = true;
        [Tooltip("SFX_HitVocals (Voice_DEMO clips, shuffled).")]
        [SerializeField] SfxCue vocalCue;

        [Header("Spacing")]
        [Tooltip("Shortest time between two vocals (real seconds).")]
        [SerializeField, Min(0f)] float minInterval = 2.5f;
        [Tooltip("A random 0…this many seconds is added to Min Interval after each vocal, so the rhythm isn't predictable.")]
        [SerializeField, Min(0f)] float randomExtra = 1.5f;
        [Tooltip("Once the interval has passed: the chance each landed hit plays a vocal (0–1).")]
        [SerializeField, Range(0f, 1f)] float chance = 0.6f;
        [Tooltip("Heavy attack, specials and the last hit of a light or air combo always play one (once the interval has passed).")]
        [SerializeField] bool bigHitsAlways = true;
        [Tooltip("Thrown items that hit count as this fighter's attacks too.")]
        [SerializeField] bool includeThrownItems = true;
        [Tooltip("Wait after the hit before the vocal (seconds), so the impact lands first.")]
        [SerializeField, Range(0f, 0.3f)] float delay = 0.04f;

        Fighter fighter;
        float nextAllowed;

        public bool PlayVocals { get => playVocals; set => playVocals = value; }

        void Awake() => fighter = GetComponent<Fighter>();
        void OnEnable() => fighter.HitLanded += OnHitLanded;
        void OnDisable() => fighter.HitLanded -= OnHitLanded;

        void OnHitLanded(Fighter target, AttackData attack)
        {
            if (!playVocals || !vocalCue || !vocalCue.HasSound || !AudioManager.Instance || !attack) return;
            bool item = attack is ThrowableData;
            if (item && !includeThrownItems) return;

            float now = Time.unscaledTime;
            if (now < nextAllowed) return;

            bool big = !item && (attack == fighter.HeavyAttackData || fighter.IsDoingSpecial || IsFinisher());
            if (!(bigHitsAlways && big) && Random.value > chance) return; // a miss doesn't restart the wait: the next hit rolls again

            nextAllowed = now + minInterval + Random.Range(0f, randomExtra);
            var clip = vocalCue.NextClip();
            if (!clip) { AudioManager.Play(vocalCue); return; }
            AudioManager.Instance.PlayClip(vocalCue, clip, vocalCue.NextVolume(), vocalCue.NextPitch(), 0f, delay, false);
        }

        bool IsFinisher()
        {
            if (fighter.IsAirAttacking) return fighter.AirComboStep >= 3;
            int step = fighter.ComboStep;
            return step >= 1 && fighter.LightCombo != null && step >= fighter.LightCombo.Count;
        }
    }
}
