using System;
using UnityEngine;

namespace KungFuCrashout.Fighters
{
    /// <summary>
    /// Stamina for defence (M3, defence system). Fighter spends it while blocking:
    ///   • holding block drains it slowly
    ///   • each blocked hit costs some (bigger hits cost more)
    ///   • a parry gives a little back
    /// It refills on its own after a short pause with no spending.
    ///
    /// Running out = EXHAUSTED: the fighter can't block until stamina refills to Recover At.
    /// If a blocked hit costs more than is left, the guard breaks (Fighter stuns the blocker).
    ///
    /// Setup: add to the Player and the Opponent next to Fighter. No Stamina = blocking is free (old behaviour).
    /// Place in: Assets/_Project/Scripts/Fighters/Stamina.cs
    /// </summary>
    public class Stamina : MonoBehaviour
    {
        [SerializeField, Min(1f)] float maxStamina = 100f;
        [Tooltip("Refill speed once it starts refilling (stamina per second).")]
        [SerializeField, Min(0f)] float regenPerSecond = 30f;
        [Tooltip("Seconds without spending before it starts refilling.")]
        [SerializeField, Min(0f)] float regenDelay = 0.6f;
        [Tooltip("After running out, blocking is locked until stamina is back to this fraction of max.")]
        [SerializeField, Range(0f, 1f)] float recoverAt = 0.3f;

        public float Max => maxStamina;
        public float Current { get; private set; }
        public float Normalized => Current / maxStamina;
        /// <summary>Ran out and hasn't refilled to Recover At yet. No blocking while true.</summary>
        public bool IsExhausted { get; private set; }

        public event Action Changed;
        /// <summary>Hit zero.</summary>
        public event Action Exhausted;
        /// <summary>Refilled enough to block again after being exhausted.</summary>
        public event Action Recovered;

        float lastSpendTime = -999f;

        void Awake() => Current = maxStamina;

        void Update()
        {
            if (Current >= maxStamina || Time.time - lastSpendTime < regenDelay) return;

            Current = Mathf.Min(maxStamina, Current + regenPerSecond * Time.deltaTime);
            if (IsExhausted && Normalized >= recoverAt)
            {
                IsExhausted = false;
                Recovered?.Invoke();
            }
            Changed?.Invoke();
        }

        /// <summary>
        /// Spend stamina. Returns false if there wasn't enough (it still drops to 0).
        /// Hitting 0 either way makes the fighter exhausted.
        /// </summary>
        public bool Spend(float amount)
        {
            if (amount <= 0f) return true;

            lastSpendTime = Time.time;
            bool enough = amount <= Current;
            Current = Mathf.Max(0f, Current - amount);
            Changed?.Invoke();

            if (Current <= 0f && !IsExhausted)
            {
                IsExhausted = true;
                Exhausted?.Invoke();
            }
            return enough;
        }

        public void Restore(float amount)
        {
            if (amount <= 0f) return;
            Current = Mathf.Min(maxStamina, Current + amount);
            if (IsExhausted && Normalized >= recoverAt)
            {
                IsExhausted = false;
                Recovered?.Invoke();
            }
            Changed?.Invoke();
        }

        public void Refill()
        {
            Current = maxStamina;
            IsExhausted = false;
            Changed?.Invoke();
        }
    }
}