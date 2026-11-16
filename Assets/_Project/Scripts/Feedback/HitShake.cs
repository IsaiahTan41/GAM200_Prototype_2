using UnityEngine;
using KungFuCrashout.Fighters;

namespace KungFuCrashout.Feedback
{
    /// <summary>
    /// Shakes a fighter's art (the active Visual) during a hit freeze: sideways on the ground, up and down in the air,
    /// fading out. Visual only: the fighter, its hurtbox and hitboxes never move. Runs on real time, so it plays through
    /// the hitstop (when the rest of the game is frozen).
    /// Added to the Visual automatically by HitFeedback (Tiered Hitstop → Hit Shake). Nothing to set up.
    /// Place in: Assets/_Project/Scripts/Feedback/HitShake.cs
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(95)] // after FighterPoseView (90)
    public class HitShake : MonoBehaviour
    {
        Vector3 basePosition;
        float amplitude, duration, frequency, age;
        bool active, vertical;

        /// <summary>Shake this fighter's art: amplitude in world units, duration in real seconds.</summary>
        public static void Shake(Fighter fighter, float amplitude, float duration, float frequency)
        {
            if (!fighter || amplitude <= 0f || duration <= 0f) return;
            var pose = fighter.GetComponentInChildren<FighterPoseView>(false);
            if (!pose) return;
            if (!pose.TryGetComponent(out HitShake shake)) shake = pose.gameObject.AddComponent<HitShake>();
            shake.Begin(amplitude, duration, frequency, fighter.IsAirborne);
        }

        void Begin(float amp, float time, float freq, bool inAir)
        {
            if (!active) basePosition = transform.localPosition; // a new hit mid-shake keeps the original rest position
            active = true;
            amplitude = amp;
            duration = time;
            frequency = freq;
            vertical = inAir;
            age = 0f;
        }

        void LateUpdate()
        {
            if (!active) return;
            age += Time.unscaledDeltaTime;
            if (age >= duration) { Stop(); return; }

            float fade = 1f - age / duration;
            float offset = Mathf.Sin(age * frequency * Mathf.PI * 2f) * amplitude * fade;
            // Offset in world units, converted into the Visual's parent space.
            var parent = transform.parent;
            float scale = parent ? Mathf.Abs(vertical ? parent.lossyScale.y : parent.lossyScale.x) : 1f;
            offset /= Mathf.Max(0.0001f, scale);
            transform.localPosition = basePosition + (vertical ? new Vector3(0f, offset, 0f) : new Vector3(offset, 0f, 0f));
        }

        void Stop()
        {
            transform.localPosition = basePosition;
            active = false;
        }

        void OnDisable()
        {
            if (active) Stop();
        }
    }
}
