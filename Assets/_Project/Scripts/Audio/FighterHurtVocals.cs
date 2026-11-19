using UnityEngine;
using KungFuCrashout.Fighters;

namespace KungFuCrashout.Sound
{
    /// <summary>
    /// Hurt vocals: this fighter's pained voice now and then when they get hit (the same system as FighterHitVocals).
    /// Every clean hit counts (melee and thrown items); blocks and parries don't.
    /// Spacing so it never gets constant:
    ///   • after a vocal, none for Min Interval + a random 0…Random Extra seconds (real time)
    ///   • once that has passed, each hit taken has Chance to trigger one; big hits (heavy, specials, the last hit of a
    ///     combo, thrown items, the KO hit) always do if Big Hits Always is on
    ///   • the cue plays its clips shuffled, so the same vocal never plays twice in a row
    /// KO: when this fighter's health runs out (every round they lose by KO), KO Cue plays instead, always, once per round;
    /// the ordinary hurt vocal skips that hit so the two never overlap. A round lost on time has no KO cry.
    /// Delay is a little longer than the hit vocals' by default, so the attacker's shout and this one don't land together.
    /// Setup: Window → KungFu Crashout → Audio → Set Up Hurt Vocals. Place in: Assets/_Project/Scripts/Audio/FighterHurtVocals.cs
    /// </summary>
    [RequireComponent(typeof(Fighter))]
    public class FighterHurtVocals : MonoBehaviour
    {
        [SerializeField] bool playVocals = true;
        [Tooltip("SFX_HurtVocals (Voice_DEMO clips, shuffled).")]
        [SerializeField] SfxCue vocalCue;

        [Header("Spacing")]
        [Tooltip("Shortest time between two vocals (real seconds).")]
        [SerializeField, Min(0f)] float minInterval = 2.5f;
        [Tooltip("A random 0…this many seconds is added to Min Interval after each vocal, so the rhythm isn't predictable.")]
        [SerializeField, Min(0f)] float randomExtra = 1.5f;
        [Tooltip("Once the interval has passed: the chance each hit taken plays a vocal (0–1).")]
        [SerializeField, Range(0f, 1f)] float chance = 0.6f;
        [Tooltip("Heavy attack, specials, the last hit of a combo, thrown items and the KO hit always play one (once the interval has passed).")]
        [SerializeField] bool bigHitsAlways = true;
        [Tooltip("Wait after the hit before the vocal (seconds). A bit later than the attacker's shout, so they don't mask each other.")]
        [SerializeField, Range(0f, 0.4f)] float delay = 0.12f;

        [Header("KO")]
        [Tooltip("SFX_KOVocal (Voice_DEMO_08): played when this fighter's health is depleted, once per round. Empty = none.")]
        [SerializeField] SfxCue koCue;
        [Tooltip("Wait after the KO hit before the cry (seconds).")]
        [SerializeField, Range(0f, 0.5f)] float koDelay = 0.1f;

        Fighter fighter;
        float nextAllowed;
        Health health;
        bool koPlayed;

        public bool PlayVocals { get => playVocals; set => playVocals = value; }

        void Awake()
        {
            fighter = GetComponent<Fighter>();
            health = GetComponent<Health>();
        }

        void OnEnable()
        {
            fighter.GotHit += OnGotHit;
            if (health) { health.Died += OnDied; health.Changed += OnHealthChanged; }
        }

        void OnDisable()
        {
            fighter.GotHit -= OnGotHit;
            if (health) { health.Died -= OnDied; health.Changed -= OnHealthChanged; }
        }

        void OnDied()
        {
            if (koPlayed || !playVocals || !koCue || !koCue.HasSound || !AudioManager.Instance) return;
            koPlayed = true;
            nextAllowed = Time.unscaledTime + minInterval; // no hurt cry right after it (e.g. a juggle on the way down)
            var clip = koCue.NextClip();
            if (!clip) { AudioManager.Play(koCue); return; }
            AudioManager.Instance.PlayClip(koCue, clip, koCue.NextVolume(), koCue.NextPitch(), 0f, koDelay, false);
        }

        // New round (health refilled): the KO cry can play again.
        void OnHealthChanged()
        {
            if (health && !health.IsDead) koPlayed = false;
        }

        void OnGotHit(Fighter attacker, AttackData attack)
        {
            if (!playVocals || !vocalCue || !vocalCue.HasSound || !AudioManager.Instance) return;
            if (koCue && health && health.IsDead) return; // the KO cry covers this hit

            float now = Time.unscaledTime;
            if (now < nextAllowed) return;

            if (!(bigHitsAlways && IsBigHit(attacker, attack)) && Random.value > chance) return; // the next hit rolls again

            nextAllowed = now + minInterval + Random.Range(0f, randomExtra);
            var clip = vocalCue.NextClip();
            if (!clip) { AudioManager.Play(vocalCue); return; }
            AudioManager.Instance.PlayClip(vocalCue, clip, vocalCue.NextVolume(), vocalCue.NextPitch(), 0f, delay, false);
        }

        bool IsBigHit(Fighter attacker, AttackData attack)
        {
            if (attack is ThrowableData) return true;
            if (fighter.Health && fighter.Health.IsDead) return true;
            if (attack && attack.weight == HitWeight.Heavy) return true;
            if (!attacker) return false;
            if (attack == attacker.HeavyAttackData || attacker.IsDoingSpecial) return true;
            if (attacker.IsAirAttacking) return attacker.AirComboStep >= 3;
            int step = attacker.ComboStep;
            return step >= 1 && attacker.LightCombo != null && step >= attacker.LightCombo.Count;
        }
    }
}
