using System;
using UnityEngine;

namespace KungFuCrashout.Fighters
{
    /// <summary>
    /// Hit points for a fighter. Other systems (HUD, match manager) listen to its events.
    ///
    /// Recoverable (red) health — used when Fighter → Recoverable Neutral Damage is on:
    ///   • A hit at neutral turns green health RED instead of removing it (TakeRecoverableDamage). Red still counts as
    ///     health, so a fighter whose health is all red can't be KO'd by neutral hits.
    ///   • Red regrows into green: Recover Delay seconds after the last neutral hit, at Recover Rate per second.
    ///   • A thrown item's clean hit burns all red off for good (LoseRecoverable), then its own damage applies.
    ///   • Real damage (staggered / Downed hits, items) works as before; red never exceeds what's left.
    /// Current = green + red. Green = what a stagger punish can take; Recoverable = the red part.
    /// Place in: Assets/_Project/Scripts/Fighters/Health.cs
    /// </summary>
    public class Health : MonoBehaviour
    {
        [SerializeField, Min(1f)] float maxHealth = 100f;

        [Header("Recoverable (red) health")]
        [Tooltip("Seconds after the last neutral hit before red health starts turning back to green.")]
        [SerializeField, Min(0f)] float recoverDelay = 2.5f;
        [Tooltip("Red health turned back to green per second once recovery starts.")]
        [SerializeField, Min(0f)] float recoverRate = 6f;

        public float Max => maxHealth;
        /// <summary>Total health left, green + red. Zero = KO.</summary>
        public float Current { get; private set; }
        public float Normalized => Current / maxHealth;
        public bool IsDead => Current <= 0f;
        /// <summary>The red (recoverable) part of Current.</summary>
        public float Recoverable { get; private set; }
        /// <summary>The green part: Current minus the red.</summary>
        public float Green => Current - Recoverable;
        public float GreenNormalized => Green / maxHealth;
        public float RecoverDelay { get => recoverDelay; set => recoverDelay = Mathf.Max(0f, value); }
        public float RecoverRate { get => recoverRate; set => recoverRate = Mathf.Max(0f, value); }

        /// <summary>Fired when damage is taken, with the amount.</summary>
        public event Action<float> Damaged;
        /// <summary>Fired when green turns red (a neutral hit), with the amount.</summary>
        public event Action<float> RecoverableDamaged;
        /// <summary>Fired once when health reaches zero.</summary>
        public event Action Died;
        /// <summary>Fired on any change (damage, red, recovery or reset). Handy for HUD bars.</summary>
        public event Action Changed;

        float lastRecoverableHit = -999f;

        void Awake() => Current = maxHealth;

        void Update()
        {
            if (Recoverable <= 0f || IsDead || Time.time - lastRecoverableHit < recoverDelay) return;
            Recoverable = Mathf.MoveTowards(Recoverable, 0f, recoverRate * Time.deltaTime);
            Changed?.Invoke();
        }

        public void TakeDamage(float amount)
        {
            if (IsDead || amount <= 0f) return;

            Current = Mathf.Max(0f, Current - amount);
            Recoverable = Mathf.Min(Recoverable, Current);
            Damaged?.Invoke(amount);
            Changed?.Invoke();

            if (IsDead) Died?.Invoke();
        }

        /// <summary>Neutral hit: turn this much green health red (never more than the green left; can't KO).</summary>
        public void TakeRecoverableDamage(float amount)
        {
            if (IsDead || amount <= 0f) return;
            lastRecoverableHit = Time.time; // recovery waits for the hits to stop
            float add = Mathf.Min(amount, Green);
            if (add <= 0f) return;
            Recoverable += add;
            RecoverableDamaged?.Invoke(add);
            Changed?.Invoke();
        }

        /// <summary>Burn all red health off for good (a thrown item landed). Returns how much was lost.</summary>
        public float LoseRecoverable()
        {
            float red = Recoverable;
            if (red <= 0f || IsDead) return 0f;
            Recoverable = 0f;
            Current = Mathf.Max(0f, Current - red);
            Damaged?.Invoke(red);
            Changed?.Invoke();
            if (IsDead) Died?.Invoke();
            return red;
        }

        public void ResetHealth()
        {
            Current = maxHealth;
            Recoverable = 0f;
            lastRecoverableHit = -999f;
            Changed?.Invoke();
        }
    }
}
