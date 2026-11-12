using UnityEngine;

namespace KungFuCrashout.Props
{
    /// <summary>
    /// Optional, per item placed under the Stage Items object (StageItemDirector → Use Placed Items): stage-item
    /// settings for this one item. Leave it off an item to use the director's defaults.
    ///   Wake Radius — how close a hit has to be to wake THIS item (world units). 0 = the director's Shockwave Radius.
    /// Select the Stage Items object to see each item's radius in the Scene view.
    /// Place in: Assets/_Project/Scripts/Environment/StageItemSettings.cs
    /// </summary>
    [RequireComponent(typeof(Carryable))]
    public class StageItemSettings : MonoBehaviour
    {
        [Tooltip("How close a hit has to be to wake this item (world units). 0 = the director's Shockwave Radius.")]
        [SerializeField, Min(0f)] float wakeRadius = 0f;

        public float WakeRadius => wakeRadius;
    }
}
