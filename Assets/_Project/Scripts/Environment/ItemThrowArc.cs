using UnityEngine;
using KungFuCrashout.Fighters;

namespace KungFuCrashout.Props
{
    /// <summary>
    /// Throw wind-up toss for an item: when the carrier starts a throw, the item is bounced straight up (vertically, staying
    /// in front of the character) to just above the carrier's head, then falls back down to the carrier's Throw Point
    /// height (ItemHolder → Throw Point, e.g. Visual/ThrowPoint) — arriving exactly on the release frame, when the throw
    /// launches it from there. Timed to the throw wind-up (ItemHolder → Throw Attack's Startup, which is the MONKTHROW clip's
    /// release frame: 18 of 60 = 0.3 s), so it stays in step if the wind-up is retimed.
    ///   0 ────── Peak At ────────── 1 (release)
    ///   rises (fast, easing out) → apex above the head → falls (easing in) → Throw Point height → thrown
    /// Getting hit during the wind-up drops the item as usual and the toss stops.
    ///
    /// Add it to any item prefab (needs a Carryable) — nothing else to set up. Tune per item in the Inspector.
    /// Head height: a child named "HeadPoint" anywhere under the fighter if it has one, else Head Height above the
    /// fighter's feet (its Visual's pivot).
    /// Place in: Assets/_Project/Scripts/Environment/ItemThrowArc.cs
    /// </summary>
    [RequireComponent(typeof(Carryable))]
    [DefaultExecutionOrder(55)] // after Carryable has moved the item this frame (50), before the outline (60)
    public class ItemThrowArc : MonoBehaviour
    {
        [Tooltip("Play the toss during the throw wind-up.")]
        [SerializeField] bool tossOnThrow = true;

        [Header("Shape")]
        [Tooltip("How far above the top of the carrier's head the item goes (world units).")]
        [SerializeField] float apexAboveHead = 0.3f;
        [Tooltip("When it reaches the top, as a fraction of the wind-up (MONKTHROW: the arc peaks around frames 6–9 of 18).")]
        [SerializeField, Range(0.1f, 0.9f)] float peakAt = 0.4f;
        [Tooltip("Turns while in the air (degrees over the whole toss, toward the facing side). 0 = no spin.")]
        [SerializeField] float spinDegrees = 0f;

        [Header("Head height")]
        [Tooltip("Top of the carrier's head above their feet (world units), used when the fighter has no \"HeadPoint\" child. " +
                 "The Monk is about 2.2.")]
        [SerializeField, Min(0.1f)] float headHeight = 2.2f;

        [Header("Timing")]
        [Tooltip("0 = the carrier's wind-up time (Throw Attack's Startup). Set a time (seconds) to override it.")]
        [SerializeField, Min(0f)] float durationOverride = 0f;

        Carryable item;
        bool tossing;
        float started, duration;
        Vector3 startPos, startOffsetFromCarrier;
        ItemHolder holder;
        Fighter carrier;
        Transform headPoint, feet;

        /// <summary>True while the item is mid-toss (wind-up).</summary>
        public bool IsTossing => tossing;

        void Awake() => item = GetComponent<Carryable>();

        void LateUpdate()
        {
            if (!tossOnThrow || item.State != Carryable.ItemState.Carried || !item.Carrier)
            {
                tossing = false;
                return;
            }

            if (item.Carrier != carrier) Cache(item.Carrier);
            bool windingUp = holder && holder.IsWindingUpThrow && holder.Held == item;
            if (!windingUp) { tossing = false; return; }

            if (!tossing) Begin();
            Toss();
        }

        void Cache(Fighter f)
        {
            carrier = f;
            holder = f.GetComponent<ItemHolder>();
            headPoint = FindDeep(f.transform, "HeadPoint");
            var view = f.GetComponentInChildren<FighterPoseView>();
            feet = view ? view.transform : f.transform;
        }

        void Begin()
        {
            tossing = true;
            started = Time.time;
            duration = durationOverride > 0f ? durationOverride : (holder ? holder.ThrowWindupTime : 0.3f);
            if (duration <= 0.01f) duration = 0.3f;
            startPos = transform.position;
            startOffsetFromCarrier = startPos - carrier.transform.position;
        }

        void Toss()
        {
            float t = Mathf.Clamp01((Time.time - started) / duration);

            // Straight up and down: stays at the same place in front of the character (follows them if they move).
            float x = carrier.transform.position.x + startOffsetFromCarrier.x;

            // Ends at the Throw Point's height (where the throw leaves from), else back where it was held.
            float endY = holder.TryGetThrowSpot(out Vector3 throwSpot) ? throwSpot.y
                       : carrier.transform.position.y + startOffsetFromCarrier.y;
            float headY = headPoint ? headPoint.position.y : feet.position.y + headHeight;
            float apexY = Mathf.Max(headY + apexAboveHead, endY);

            float y;
            if (t < peakAt)
            {
                float k = t / peakAt;
                y = Mathf.Lerp(startPos.y, apexY, 1f - (1f - k) * (1f - k)); // fast off the hands, slowing at the top
            }
            else
            {
                float k = (t - peakAt) / (1f - peakAt);
                y = Mathf.Lerp(apexY, endY, k * k);                           // slow at the top, speeding down
            }
            transform.position = new Vector3(x, y, startPos.z);

            if (spinDegrees != 0f) transform.rotation = Quaternion.Euler(0f, 0f, -spinDegrees * t * carrier.Facing);
        }

        static Transform FindDeep(Transform root, string name)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true)) if (t.name == name) return t;
            return null;
        }
    }
}
