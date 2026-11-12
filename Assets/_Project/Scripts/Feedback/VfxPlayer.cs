using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace KungFuCrashout.Feedback
{
    /// <summary>
    /// Plays VfxCues from a fixed pool of sprites and 2D lights, so nothing is created or destroyed mid-fight
    /// (Pixel 9a friendly). One per fight scene; made automatically on first use if missing.
    ///
    /// Effects and light flashes run on scaled time: they freeze during hitstop and while paused. While frozen,
    /// an effect still finishes its short pop, so the strike holds at full size on the impact frame.
    ///
    /// Lights: the 2D Renderer turns the whole scene dark as soon as any 2D light exists unless there is a
    /// Global Light 2D, so one is created at intensity 1 if the scene has none (Create Global Light If Missing).
    ///
    /// Setup: Window → KungFu Crashout → VFX → 2. Add VFX To Open Scene.
    /// Place in: Assets/_Project/Scripts/Feedback/VfxPlayer.cs
    /// </summary>
    [DefaultExecutionOrder(200)] // after fighters and cameras
    public class VfxPlayer : MonoBehaviour
    {
        [Tooltip("Master switch for all sprite effects.")]
        [SerializeField] bool effectsOn = true;
        [Tooltip("Material for the effect sprites. Sprite-Unlit-Default keeps the painted colours exactly " +
                 "(the light flashes then only light the stage and fighters). Empty = the default sprite material.")]
        [SerializeField] Material spriteMaterial;
        [Tooltip("How many effects can be on screen at once. The oldest is reused when full.")]
        [SerializeField, Min(1)] int poolSize = 24;
        [Tooltip("Multiplies every cue's size (quick global tune).")]
        [SerializeField, Min(0f)] float globalScale = 1f;

        [Header("Light flashes")]
        [SerializeField] bool lightsOn = true;
        [Tooltip("At most this many flashes at once (the oldest is reused). Keep low on phones.")]
        [SerializeField, Min(0)] int maxLights = 3;
        [Tooltip("Multiplies every cue's light intensity.")]
        [SerializeField, Min(0f)] float lightIntensityScale = 1f;
        [Tooltip("Makes a Global Light 2D (white, intensity below) if the scene has none, so the stage doesn't go dark.")]
        [SerializeField] bool createGlobalLightIfMissing = true;
        [SerializeField, Min(0f)] float globalLightIntensity = 1f;

        static VfxPlayer instance;

        class Fx
        {
            public SpriteRenderer sr;
            public Transform t;
            public VfxCue cue;
            public float age, size, angle;
            public int facing;
            public bool alive;
            public float started;
        }

        class Flash
        {
            public Light2D light;
            public float age, duration, peak;
            public bool alive;
            public float started;
        }

        readonly List<Fx> pool = new List<Fx>();
        readonly List<Flash> flashes = new List<Flash>();

        public static VfxPlayer Instance
        {
            get
            {
                if (!instance)
                {
                    instance = FindAnyObjectByType<VfxPlayer>();
                    if (!instance) instance = new GameObject("VfxPlayer").AddComponent<VfxPlayer>();
                }
                return instance;
            }
        }

        /// <summary>The material used for effect sprites (ItemVfx's held glow uses it too).</summary>
        public Material SpriteMaterial => spriteMaterial;
        public bool EffectsOn { get => effectsOn; set => effectsOn = value; }

        void Awake()
        {
            if (instance && instance != this) { Destroy(gameObject); return; }
            instance = this;

            for (int i = 0; i < poolSize; i++)
            {
                var go = new GameObject("Fx " + i);
                go.transform.SetParent(transform, false);
                var sr = go.AddComponent<SpriteRenderer>();
                if (spriteMaterial) sr.sharedMaterial = spriteMaterial;
                go.SetActive(false);
                pool.Add(new Fx { sr = sr, t = go.transform });
            }

            if (lightsOn && maxLights > 0)
            {
                if (createGlobalLightIfMissing) EnsureGlobalLight();
                for (int i = 0; i < maxLights; i++)
                {
                    var go = new GameObject("Flash " + i);
                    go.transform.SetParent(transform, false);
                    var l = go.AddComponent<Light2D>(); // Point by default, all sorting layers
                    l.shadowsEnabled = false;
                    l.falloffIntensity = 0.6f;
                    go.SetActive(false);
                    flashes.Add(new Flash { light = l });
                }
            }
        }

        void OnDestroy() { if (instance == this) instance = null; }

        void EnsureGlobalLight()
        {
            foreach (var l in FindObjectsByType<Light2D>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
                if (l.lightType == Light2D.LightType.Global) return;

            var go = new GameObject("Global Light 2D (VfxPlayer)");
            var g = go.AddComponent<Light2D>();
            g.lightType = Light2D.LightType.Global;
            g.intensity = globalLightIntensity;
            g.color = Color.white;
            Debug.Log("[VfxPlayer] No Global Light 2D in the scene: made one so the light flashes don't darken the stage. " +
                      "Add one to the scene (VFX → 2. Add VFX To Open Scene) to tune it.");
        }

        /// <summary>
        /// Play a cue. position = the spawn point (before the cue's offset); facing = +1 / −1;
        /// size = the owner's size factor (e.g. the fighter's Visual scale); sorting copied from the owner's sprite.
        /// </summary>
        public static void Play(VfxCue cue, Vector3 position, int facing, float size, int sortingLayerID, int ownerOrder)
        {
            if (!cue || !cue.sprite) return;
            Instance.Spawn(cue, position, facing, size, sortingLayerID, ownerOrder);
        }

        /// <summary>Play with the owner's sprite renderer giving the sorting (null = default layer, order 0).</summary>
        public static void Play(VfxCue cue, Vector3 position, int facing, float size, SpriteRenderer owner)
        {
            Play(cue, position, facing, size, owner ? owner.sortingLayerID : 0, owner ? owner.sortingOrder : 0);
        }

        /// <summary>Just the light flash of a cue (used by looping effects that draw their own sprite).</summary>
        public static void FlashOnly(VfxCue cue, Vector3 position, int facing, float size)
        {
            if (!cue || !cue.light) return;
            Instance.StartFlash(cue, position, facing, size);
        }

        void Spawn(VfxCue cue, Vector3 position, int facing, float size, int layer, int order)
        {
            if (!effectsOn) return;
            if (facing == 0) facing = 1;
            int dir = cue.flipWithFacing ? facing : 1;

            var fx = Take();
            fx.cue = cue;
            fx.age = 0f;
            fx.size = size * cue.scale * globalScale;
            fx.facing = dir;
            fx.angle = (cue.rotation + Random.Range(-cue.randomRotation, cue.randomRotation)) * dir;
            fx.alive = true;
            fx.started = Time.unscaledTime;

            fx.sr.sprite = cue.sprite;
            fx.sr.sortingLayerID = layer;
            fx.sr.sortingOrder = order + cue.sortingOffset;
            fx.sr.flipX = (dir < 0) != cue.paintedFacingLeft;
            var c = cue.tint; c.a *= cue.AlphaAt(0f); fx.sr.color = c;

            Vector2 off = new Vector2(cue.offset.x * dir, cue.offset.y) * size;
            fx.t.position = new Vector3(position.x + off.x, position.y + off.y, position.z - 0.05f);
            fx.t.rotation = Quaternion.Euler(0f, 0f, fx.angle);
            fx.t.localScale = Vector3.one * fx.size * cue.ScaleAt(0f);
            fx.t.gameObject.SetActive(true);

            if (cue.light) StartFlash(cue, position, facing, size);
        }

        Fx Take()
        {
            Fx oldest = null;
            foreach (var f in pool)
            {
                if (!f.alive) return f;
                if (oldest == null || f.started < oldest.started) oldest = f;
            }
            return oldest;
        }

        void StartFlash(VfxCue cue, Vector3 position, int facing, float size)
        {
            if (!lightsOn || flashes.Count == 0) return;
            if (facing == 0) facing = 1;
            int dir = cue.flipWithFacing ? facing : 1;

            Flash f = null;
            foreach (var x in flashes)
            {
                if (!x.alive) { f = x; break; }
                if (f == null || x.started < f.started) f = x;
            }

            f.alive = true;
            f.age = 0f;
            f.duration = cue.lightTime;
            f.peak = cue.lightIntensity * lightIntensityScale;
            f.started = Time.unscaledTime;

            var l = f.light;
            l.color = cue.lightColour;
            l.intensity = f.peak;
            l.pointLightOuterRadius = cue.lightRadius * size;
            l.pointLightInnerRadius = cue.lightRadius * size * 0.15f;
            l.pointLightOuterAngle = cue.lightAngle;
            l.pointLightInnerAngle = cue.lightAngle * 0.6f;
            Vector2 off = new Vector2(cue.lightOffset.x * dir, cue.lightOffset.y) * size;
            l.transform.position = new Vector3(position.x + off.x, position.y + off.y, position.z);
            l.transform.rotation = Quaternion.Euler(0f, 0f, -cue.lightDirection * dir);
            l.gameObject.SetActive(true);
        }

        void LateUpdate()
        {
            float dt = Time.deltaTime; // scaled: hitstop and pause freeze everything

            foreach (var fx in pool)
            {
                if (!fx.alive) continue;
                var cue = fx.cue;
                // Frozen (hitstop / pause): still finish the pop so the effect holds at full size on the impact frame.
                fx.age += dt > 0f ? dt : (cue && fx.age < cue.popTime ? Time.unscaledDeltaTime : 0f);
                if (!cue || fx.age >= cue.lifetime) { Kill(fx); continue; }

                fx.t.localScale = Vector3.one * fx.size * cue.ScaleAt(fx.age);
                if (cue.drift != Vector2.zero)
                    fx.t.position += new Vector3(cue.drift.x * fx.facing, cue.drift.y, 0f) * fx.size * dt;
                var c = cue.tint; c.a *= cue.AlphaAt(fx.age); fx.sr.color = c;
            }

            foreach (var f in flashes)
            {
                if (!f.alive) continue;
                f.age += dt;
                if (f.age >= f.duration) { f.alive = false; f.light.gameObject.SetActive(false); continue; }
                float k = 1f - f.age / f.duration;
                f.light.intensity = f.peak * k * k;
            }
        }

        static void Kill(Fx fx)
        {
            fx.alive = false;
            fx.t.gameObject.SetActive(false);
        }

        /// <summary>Clears everything on screen (e.g. between rounds).</summary>
        public void ClearAll()
        {
            foreach (var fx in pool) if (fx.alive) Kill(fx);
            foreach (var f in flashes) { f.alive = false; f.light.gameObject.SetActive(false); }
        }
    }
}
