using UnityEngine;
using UnityEngine.Rendering.Universal;
using KungFuCrashout.Fighters;
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
        [Tooltip("Throw-area mode (ItemTapHandler → Held Glow Shows Throw Area): where the glow sprite's bright ring sits, as a " +
                 "fraction of the sprite's width / height. The ring is placed on the edge of the area a throw swipe can start from.")]
        [SerializeField] Vector2 glowRimFraction = new Vector2(0.69f, 0.62f);

        [Header("One-shots")]
        [SerializeField] VfxCue breakBurst;
        [SerializeField] VfxCue landDust;

        [Header("Animated (used while VfxPlayer → Use Animated VFX is on and Hold Idle is set)")]
        [Tooltip("Picked up: the aura forms (Hold Start), then Hold Idle loops.")]
        [SerializeField] VfxCue animHoldStart;
        [SerializeField] VfxCue animHoldIdle;
        [Tooltip("While the player's finger rests on the carried item (before the throw swipe).")]
        [SerializeField] VfxCue animHoldAim;
        [Tooltip("Thrown: Throw Start, then Throw Idle loops for the flight, turned the way the item flies.")]
        [SerializeField] VfxCue animThrowStart;
        [SerializeField] VfxCue animThrowIdle;
        [Tooltip("The thrown item hits a fighter or lands (once per throw).")]
        [SerializeField] VfxCue animHit;
        [Tooltip("Aura size: Hold Idle's drawn content (its largest side) = the item's art × this. The other animated " +
                 "effects keep their size relative to Hold Idle, as drawn.")]
        [SerializeField, Min(0f)] float animatedAuraScale = 1.3f;
        [Tooltip("Throw-area mode: where the aura's rim sits, as a fraction of Hold Idle's drawn content.")]
        [SerializeField, Range(0.3f, 1.2f)] float animatedRimFraction = 0.9f;

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
            item.Thrown += OnThrown;
            item.HitFighter += OnItemHitFighter;
        }

        void OnDisable()
        {
            item.Broken -= OnBroken;
            item.FlightEnded -= OnFlightEnded;
            item.Thrown -= OnThrown;
            item.HitFighter -= OnItemHitFighter;
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
            if (item.State != Carryable.ItemState.Carried) PlayThrowHit(); // landed without hitting anyone
        }

        // ───────────── Animated (sprite sheet) effects ─────────────

        enum AnimPhase { None, Hold, Aim, Throw }
        AnimPhase phase;
        float phaseAge, flightAngle;
        bool hitPlayed;
        Vector3 lastPos;

        bool Animated => VfxPlayer.AnimatedVfxOn && animHoldIdle && animHoldIdle.IsAnimated;

        void OnThrown(Fighter thrower, Vector2 direction)
        {
            hitPlayed = false;
            if (direction.sqrMagnitude > 0.0001f) flightAngle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
        }

        void OnItemHitFighter(Fighter thrower, Fighter target, HitResult result) => PlayThrowHit();

        void PlayThrowHit()
        {
            if (hitPlayed || !Animated || !animHit) return;
            hitPlayed = true;
            Vector3 c = art ? art.bounds.center : transform.position;
            VfxPlayer.Play(animHit, c, 1, AuraFit(), art);
        }

        /// <summary>World size per sprite unit that makes Hold Idle's drawn content (largest side) = item art × Aura Scale.
        /// Every animated item effect uses it, so they keep the sizes they were drawn at relative to each other.</summary>
        float AuraFit()
        {
            Vector2 cs = animHoldIdle && animHoldIdle.contentSize != Vector2.zero ? animHoldIdle.contentSize
                       : animHoldIdle && animHoldIdle.FirstSprite ? (Vector2)animHoldIdle.FirstSprite.bounds.size : Vector2.one;
            return ItemSize * animatedAuraScale / Mathf.Max(0.01f, Mathf.Max(cs.x, cs.y));
        }

        /// <summary>Plays First once, then loops Then (age in seconds).</summary>
        static Sprite Chain(VfxCue first, VfxCue then, float age)
        {
            if (first && first.IsAnimated && age < first.Duration) return first.FrameAt(age);
            float t = age - (first && first.IsAnimated ? first.Duration : 0f);
            return then ? then.FrameAt(t) : first ? first.FrameAt(first.Duration) : null;
        }

        void UpdateAnimated()
        {
            var state = item.State;
            bool held = state == Carryable.ItemState.Carried, thrown = state == Carryable.ItemState.Thrown;
            Vector3 centre = art ? art.bounds.center : transform.position;
            float dt = Time.deltaTime;
            if (thrown && dt > 0f)
            {
                Vector3 v = (centre - lastPos) / dt;
                if (v.sqrMagnitude > 0.04f) flightAngle = Mathf.Atan2(v.y, v.x) * Mathf.Rad2Deg; // the way it flies
            }
            lastPos = centre;

            if (!held && !thrown) { phase = AnimPhase.None; ShowGlow(false); return; }

            var tap = held ? ItemTapHandler.For(item.Carrier) : null;
            var next = thrown ? AnimPhase.Throw : (tap && tap.FingerOnHeldItem && animHoldAim ? AnimPhase.Aim : AnimPhase.Hold);
            if (next != phase)
            {
                // Aim ↔ Hold keeps the aura formed (no Hold Start replay); a new pick-up / throw starts from the top.
                bool keepFormed = (next == AnimPhase.Hold && phase == AnimPhase.Aim);
                phaseAge = keepFormed ? 999f : 0f;
                phase = next;
            }
            else phaseAge += dt;
            ShowGlow(true);

            var t = glow.transform;
            float k = AuraFit();
            VfxCue look;
            switch (phase)
            {
                case AnimPhase.Aim:
                    look = animHoldAim;
                    glow.sprite = animHoldAim.FrameAt(phaseAge);
                    break;
                case AnimPhase.Throw:
                    look = animThrowIdle ? animThrowIdle : animThrowStart;
                    glow.sprite = Chain(animThrowStart, animThrowIdle, phaseAge);
                    break;
                default:
                    look = animHoldIdle;
                    glow.sprite = Chain(animHoldStart, animHoldIdle, phaseAge);
                    break;
            }

            t.rotation = Quaternion.identity;
            if (phase == AnimPhase.Throw)
            {
                // The trail's ball sits on the item (its pivot), pointing the way it flies.
                t.position = new Vector3(centre.x, centre.y, centre.z + 0.01f);
                t.localScale = Vector3.one * k * (look ? look.scale : 1f);
                t.rotation = Quaternion.Euler(0f, 0f, flightAngle);
            }
            else if (tap && tap.HeldGlowShowsThrowArea && tap.TryGetThrowArea(item, out var areaCentre, out var areaSize))
            {
                // The aura's rim = where a throw swipe can start (same rule as the single-frame glow).
                Vector2 cs = animHoldIdle.contentSize != Vector2.zero ? animHoldIdle.contentSize : (Vector2)animHoldIdle.FirstSprite.bounds.size;
                float sx = areaSize.x / Mathf.Max(0.001f, cs.x * animatedRimFraction);
                float sy = areaSize.y / Mathf.Max(0.001f, cs.y * animatedRimFraction);
                t.position = new Vector3(areaCentre.x, areaCentre.y, centre.z + 0.01f);
                t.localScale = new Vector3(sx, sy, 1f);
                centre = new Vector3(areaCentre.x, areaCentre.y, centre.z);
            }
            else
            {
                Vector2 off = look ? look.offset : Vector2.zero;
                t.position = new Vector3(centre.x + off.x, centre.y + off.y, centre.z + 0.01f);
                t.localScale = Vector3.one * k * (look ? look.scale : 1f);
            }
            glow.flipX = false;
            glow.sortingLayerID = art ? art.sortingLayerID : 0;
            glow.sortingOrder = (art ? art.sortingOrder : 0) + (look ? look.sortingOffset : -2);
            var c = look ? look.tint : Color.white; c.a *= look ? look.alpha : 1f; glow.color = c;

            if (glowLight)
            {
                glowLight.transform.position = centre;
                if (heldGlow) glowLight.intensity = heldGlow.lightIntensity;
            }
        }

        // ───────────── Held glow ─────────────

        void LateUpdate()
        {
            if (Animated) { UpdateAnimated(); return; }
            if (phase != AnimPhase.None) { phase = AnimPhase.None; if (glow) glow.transform.rotation = Quaternion.identity; }
            bool held = heldGlow && heldGlow.sprite && item.State == Carryable.ItemState.Carried;
            ShowGlow(held);
            if (!held) return;

            glowAge += Time.deltaTime;
            float pulse = 1f + Mathf.Sin(glowAge * heldGlow.pulseSpeed * Mathf.PI * 2f) * heldGlow.pulseAmount;
            float popIn = heldGlow.ScaleAt(Mathf.Min(glowAge, heldGlow.popTime)); // pops in once, then breathes
            Vector3 centre = art ? art.bounds.center : transform.position;
            float breathe = 1f; // throw-area mode breathes with opacity, so the ring stays on the area's edge

            var t = glow.transform;
            var tap = ItemTapHandler.For(item.Carrier);
            if (tap && tap.HeldGlowShowsThrowArea && tap.TryGetThrowArea(item, out var areaCentre, out var areaSize))
            {
                // The ring = where a throw swipe can start (the item's touch collider + Held Item Tap Radius).
                var sb = heldGlow.sprite.bounds.size;
                float sx = areaSize.x / Mathf.Max(0.001f, sb.x * glowRimFraction.x);
                float sy = areaSize.y / Mathf.Max(0.001f, sb.y * glowRimFraction.y);
                t.position = new Vector3(areaCentre.x, areaCentre.y, centre.z + 0.01f);
                t.localScale = new Vector3(sx * popIn, sy * popIn, 1f);
                breathe = 0.8f + 0.2f * Mathf.Clamp01((pulse - 1f) / Mathf.Max(0.0001f, heldGlow.pulseAmount) * 0.5f + 0.5f);
                centre = new Vector3(areaCentre.x, areaCentre.y, centre.z);
            }
            else
            {
                // As before: the glow fits the item's art.
                t.position = new Vector3(centre.x + heldGlow.offset.x, centre.y + heldGlow.offset.y, centre.z + 0.01f);
                t.localScale = Vector3.one * Fit(heldGlow) * heldGlow.scale * popIn * pulse;
            }
            glow.sortingLayerID = art ? art.sortingLayerID : 0;
            glow.sortingOrder = (art ? art.sortingOrder : 0) + heldGlow.sortingOffset;
            var c = heldGlow.tint; c.a *= heldGlow.alpha * breathe * Mathf.Clamp01(glowAge / Mathf.Max(0.01f, heldGlow.popTime)); glow.color = c;

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
            if (glow.gameObject.activeSelf != show) glow.gameObject.SetActive(show);
            if (glowLight) glowLight.gameObject.SetActive(show && heldLight && heldGlow && heldGlow.light);
        }

        void MakeGlow()
        {
            // Not parented to the item: the item flips and resizes, the glow sizes itself from the item's bounds instead.
            var go = new GameObject(name + " HeldGlow");
            glow = go.AddComponent<SpriteRenderer>();
            glow.sprite = heldGlow ? heldGlow.sprite : null;
            var player = VfxPlayer.Instance;
            if (player && player.SpriteMaterial) glow.sharedMaterial = player.SpriteMaterial;
            go.SetActive(false);

            if (heldLight && heldGlow && heldGlow.light)
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
