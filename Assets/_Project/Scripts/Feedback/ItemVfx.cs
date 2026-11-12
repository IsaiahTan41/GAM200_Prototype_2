using UnityEngine;
using UnityEngine.Rendering.Universal;
using KungFuCrashout.Props;

namespace KungFuCrashout.Feedback
{
    /// <summary>
    /// A stage item's effects:
    ///   Held  — a pulsing glow behind the item while a fighter carries it (VFX_Monk_Holding), with a faint light
    ///   Break — a dust burst where it broke (VFX_ObjectBreak_Light): Carryable's last throw / lifetime, or ItemDurability
    ///   Land  — a small dust ring when a throw's flight ends
    /// Sizes follow the item's on-screen size, so a plate and a chair each get a fitting burst.
    ///
    /// Setup: on each item prefab next to Carryable (VFX → 3. Add Item VFX To Item Prefabs; Make Item Prefab adds it too).
    /// Place in: Assets/_Project/Scripts/Feedback/ItemVfx.cs
    /// </summary>
    [RequireComponent(typeof(Carryable))]
    public class ItemVfx : MonoBehaviour
    {
        [Header("Held glow")]
        [SerializeField] VfxCue heldGlow;
        [Tooltip("Also light the fighter and item while held (uses Held Glow's light colour / intensity / radius).")]
        [SerializeField] bool heldLight = true;

        [Header("One-shots")]
        [SerializeField] VfxCue breakBurst;
        [SerializeField] VfxCue landDust;

        Carryable item;
        SpriteRenderer art;
        SpriteRenderer glow;
        Light2D glowLight;
        float glowAge;

        void Awake()
        {
            item = GetComponent<Carryable>();
            art = GetComponent<SpriteRenderer>();
            if (!art) art = GetComponentInChildren<SpriteRenderer>();
        }

        void OnEnable()
        {
            item.Broken += OnBroken;
            item.FlightEnded += OnFlightEnded;
        }

        void OnDisable()
        {
            item.Broken -= OnBroken;
            item.FlightEnded -= OnFlightEnded;
            ShowGlow(false);
        }

        void OnDestroy()
        {
            if (glow) Destroy(glow.gameObject);
        }

        /// <summary>The item's current size on screen (largest side, world units).</summary>
        float ItemSize => art && art.sprite ? Mathf.Max(art.bounds.size.x, art.bounds.size.y) : 1f;

        /// <summary>Scale factor that makes a cue's sprite as wide as the item (then the cue's own Scale applies).</summary>
        float Fit(VfxCue cue)
        {
            if (!cue || !cue.sprite) return 1f;
            var b = cue.sprite.bounds.size;
            return ItemSize / Mathf.Max(0.01f, Mathf.Max(b.x, b.y));
        }

        Vector3 Bottom => art ? new Vector3(art.bounds.center.x, art.bounds.min.y, transform.position.z) : transform.position;

        void OnBroken()
        {
            if (breakBurst) VfxPlayer.Play(breakBurst, Bottom, 1, Fit(breakBurst), art);
        }

        void OnFlightEnded()
        {
            if (landDust && item.State != Carryable.ItemState.Carried) VfxPlayer.Play(landDust, Bottom, 1, Fit(landDust), art);
        }

        // ───────────── Held glow ─────────────

        void LateUpdate()
        {
            bool held = heldGlow && heldGlow.sprite && item.State == Carryable.ItemState.Carried;
            ShowGlow(held);
            if (!held) return;

            glowAge += Time.deltaTime;
            float pulse = 1f + Mathf.Sin(glowAge * heldGlow.pulseSpeed * Mathf.PI * 2f) * heldGlow.pulseAmount;
            float popIn = heldGlow.ScaleAt(Mathf.Min(glowAge, heldGlow.popTime)); // pops in once, then breathes
            Vector3 centre = art ? art.bounds.center : transform.position;

            var t = glow.transform;
            t.position = new Vector3(centre.x + heldGlow.offset.x, centre.y + heldGlow.offset.y, centre.z + 0.01f);
            t.localScale = Vector3.one * Fit(heldGlow) * heldGlow.scale * popIn * pulse;
            glow.sortingLayerID = art ? art.sortingLayerID : 0;
            glow.sortingOrder = (art ? art.sortingOrder : 0) + heldGlow.sortingOffset;
            var c = heldGlow.tint; c.a *= heldGlow.alpha * Mathf.Clamp01(glowAge / Mathf.Max(0.01f, heldGlow.popTime)); glow.color = c;

            if (glowLight)
            {
                glowLight.transform.position = centre;
                glowLight.intensity = heldGlow.lightIntensity * (0.85f + 0.15f * pulse);
            }
        }

        void ShowGlow(bool show)
        {
            if (show && !glow) MakeGlow();
            if (!glow) return;
            if (show && !glow.gameObject.activeSelf) glowAge = 0f;
            glow.gameObject.SetActive(show);
            if (glowLight) glowLight.gameObject.SetActive(show && heldLight && heldGlow && heldGlow.light);
        }

        void MakeGlow()
        {
            // Not parented to the item: the item flips and resizes, the glow sizes itself from the item's bounds instead.
            var go = new GameObject(name + " HeldGlow");
            glow = go.AddComponent<SpriteRenderer>();
            glow.sprite = heldGlow.sprite;
            var player = VfxPlayer.Instance;
            if (player && player.SpriteMaterial) glow.sharedMaterial = player.SpriteMaterial;
            go.SetActive(false);

            if (heldLight && heldGlow.light)
            {
                var lgo = new GameObject(name + " HeldLight");
                lgo.transform.SetParent(go.transform, false);
                glowLight = lgo.AddComponent<Light2D>();
                glowLight.shadowsEnabled = false;
                glowLight.color = heldGlow.lightColour;
                glowLight.intensity = heldGlow.lightIntensity;
                glowLight.pointLightOuterRadius = heldGlow.lightRadius;
                glowLight.pointLightInnerRadius = heldGlow.lightRadius * 0.2f;
                glowLight.falloffIntensity = 0.7f;
            }
        }
    }
}
