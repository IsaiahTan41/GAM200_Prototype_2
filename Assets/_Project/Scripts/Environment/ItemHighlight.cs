using UnityEngine;
using KungFuCrashout.Core;
using KungFuCrashout.Fighters;

namespace KungFuCrashout.Props
{
    /// <summary>
    /// GDD → "Items will be highlighted when a character is able to pick them up."
    /// A pulsing outline (a solid-colour copy of the item's sprite drawn just behind it, slightly bigger) shows while the
    /// HUMAN player could grab this item right now (in range, hands free, not busy) — the same rule the tap uses
    /// (ItemHolder.CanPickUp), so the glow never lies. UI standard: white = "you can grab this".
    ///
    /// Put on each item prefab next to Carryable (the GDD Build setup does this). Silhouette Material = a material using
    /// "GUI/Text Shader" (solid colour); empty = a tinted copy (fainter).
    /// Place in: Assets/_Project/Scripts/Environment/ItemHighlight.cs
    /// </summary>
    [RequireComponent(typeof(Carryable), typeof(SpriteRenderer))]
    [DefaultExecutionOrder(60)] // after Carryable has moved this frame
    public class ItemHighlight : MonoBehaviour
    {
        [SerializeField] Color colour = Color.white;
        [Tooltip("Rim width (world units).")]
        [SerializeField, Range(0.01f, 0.3f)] float thickness = 0.07f;
        [Tooltip("Pulses per second.")]
        [SerializeField, Min(0f)] float pulseSpeed = 2.5f;
        [Tooltip("Lowest opacity of the pulse (the highest is the colour's alpha).")]
        [SerializeField, Range(0f, 1f)] float pulseMin = 0.35f;
        [SerializeField] Material silhouetteMaterial;

        Carryable item;
        SpriteRenderer art;
        SpriteRenderer glow;
        ItemHolder playerHolder;
        Fighter cachedPlayer;

        void Awake()
        {
            item = GetComponent<Carryable>();
            art = GetComponent<SpriteRenderer>();

            var go = new GameObject("Highlight");
            go.transform.SetParent(transform, false);
            glow = go.AddComponent<SpriteRenderer>();
            if (silhouetteMaterial) glow.sharedMaterial = silhouetteMaterial;
            glow.enabled = false;
        }

        void LateUpdate()
        {
            bool on = art.enabled && art.sprite && PlayerCanGrab();
            glow.enabled = on;
            if (!on) return;

            glow.sprite = art.sprite;
            glow.flipX = art.flipX;
            glow.sortingLayerID = art.sortingLayerID;
            glow.sortingOrder = art.sortingOrder - 1;

            float pulse = pulseSpeed > 0f ? 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * pulseSpeed * Mathf.PI * 2f) : 1f;
            var c = colour;
            c.a *= Mathf.Lerp(pulseMin, 1f, pulse);
            glow.color = c;

            // Scale about the sprite's centre so the rim is even all round (same maths as SpriteOutline).
            var sprite = art.sprite;
            Vector3 size = sprite.bounds.size;
            Vector3 centre = sprite.bounds.center;
            float sx = size.x > 0.0001f ? 1f + 2f * thickness / (size.x * Mathf.Abs(transform.lossyScale.x)) : 1f;
            float sy = size.y > 0.0001f ? 1f + 2f * thickness / (size.y * Mathf.Abs(transform.lossyScale.y)) : 1f;
            glow.transform.localScale = new Vector3(sx, sy, 1f);
            glow.transform.localPosition = new Vector3(centre.x * (1f - sx), centre.y * (1f - sy), 0f);
        }

        bool PlayerCanGrab()
        {
            if (!item.IsAvailable) return false;
            var player = MatchSetup.Player;
            if (!player) player = FindTaggedPlayer();
            if (!player) return false;
            if (player != cachedPlayer)
            {
                cachedPlayer = player;
                playerHolder = player.GetComponent<ItemHolder>();
            }
            return playerHolder && playerHolder.CanPickUp(item);
        }

        static float nextLookup;
        static Fighter lookedUp;

        /// <summary>No MatchManager (test scene): the fighter tagged "Player", looked up at most twice a second.</summary>
        static Fighter FindTaggedPlayer()
        {
            if (lookedUp || Time.unscaledTime < nextLookup) return lookedUp;
            nextLookup = Time.unscaledTime + 0.5f;
            var go = GameObject.FindWithTag("Player");
            lookedUp = go ? go.GetComponent<Fighter>() : null;
            return lookedUp;
        }
    }
}
