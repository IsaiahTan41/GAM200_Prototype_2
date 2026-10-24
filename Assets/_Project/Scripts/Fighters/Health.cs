using System;
using UnityEngine;

namespace KungFuCrashout.Fighters
{
    /// <summary>
    /// Hit points for a fighter. Other systems (HUD, match manager) listen to its events.
    /// Place in: Assets/_Project/Scripts/Fighters/Health.cs
    /// </summary>
    public class Health : MonoBehaviour
    {
        [SerializeField, Min(1f)] float maxHealth = 100f;

        public float Max => maxHealth;
        public float Current { get; private set; }
        public float Normalized => Current / maxHealth;
        public bool IsDead => Current <= 0f;

        /// <summary>Fired when damage is taken, with the amount.</summary>
        public event Action<float> Damaged;
        /// <summary>Fired once when health reaches zero.</summary>
        public event Action Died;
        /// <summary>Fired on any change (damage or reset). Handy for HUD bars.</summary>
        public event Action Changed;

        void Awake() => Current = maxHealth;

        public void TakeDamage(float amount)
        {
            if (IsDead || amount <= 0f) return;

            Current = Mathf.Max(0f, Current - amount);
            Damaged?.Invoke(amount);
            Changed?.Invoke();

            if (IsDead) Died?.Invoke();
        }

        public void ResetHealth()
        {
            Current = maxHealth;
            Changed?.Invoke();
        }
    }
}