using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using KungFuCrashout.Core;
using KungFuCrashout.Fighters;
using KungFuCrashout.Sound;
using Random = UnityEngine.Random;

namespace KungFuCrashout.Props
{
    /// <summary>
    /// GDD → Environmental System, the stage side:
    ///   "At the beginning of the match, there is a set amount of items" — every item placed in the scene (put them under
    ///     this Stage Items object to keep it tidy) is a stage item. Each round they're laid out again at the exact spot
    ///     you placed them, ALL ASLEEP: visible (dimmed / Sleeping Sprite) but not grabbable.
    ///   "Shockwaves make new items drop onto the stage" — ANY attack that connects (light, combo, heavy, special; blocked
    ///     hits too if Blocked Hits Count) sends out a shockwave with a percentage chance to wake ONE sleeping item:
    ///     Wake Chance for light attacks, Heavy Wake Chance for heavies and specials. The sleeping item nearest the hit
    ///     (within Wake Radius; 0 = anywhere on the stage) is knocked loose, falls to the floor and can be grabbed.
    ///   "Ensuring there will always be at least one item available" — if no item is in play (on the floor, held or
    ///     flying) for Empty Delay seconds, the sleeping item nearest the fighters wakes; if none are left asleep, a
    ///     random stage item drops in from above with a shadow marking where it lands.
    /// Holding, throwing, highlighting and breaking are the items' own rules (Carryable, ItemHighlight, ItemDurability).
    ///
    /// Original Item Behaviour (testing): the director stands aside — items placed in the scene are awake and live
    /// from the start, nothing sleeps, nothing is re-laid between rounds, nothing drops in. Can be switched while playing
    /// (pause menu → GAME → "Original item behaviour", ItemModeToggle): the stage is laid out again in the new mode.
    /// The switch lasts until the app closes (scene reloads keep it); the Inspector value is the default at app start.
    ///
    /// Setup: one per fight scene (GDD Build → Upgrade Fight Scene adds it). Add an item: drag its prefab into the
    /// scene where it should sleep (floor, table, shelf). Remove one: delete it (or untick it: inactive items are ignored).
    /// Place in: Assets/_Project/Scripts/Environment/StageItemDirector.cs
    /// </summary>
    public class StageItemDirector : MonoBehaviour
    {
        [SerializeField] StageBounds stage;
        [Tooltip("Fighters whose attacks send out shockwaves. Empty = every Fighter in the scene.")]
        [SerializeField] Fighter[] fighters = new Fighter[0];

        [Header("Testing")]
        [Tooltip("ON: items work as they originally did. Whatever is placed in the scene is live from the start (pick up, " +
                 "throw); no sleeping, no shockwaves, no re-laying between rounds, no drop-ins. OFF: the GDD rules. " +
                 "Default at app start; the pause menu toggle can switch it while playing.")]
        [SerializeField] bool originalItemBehaviour = false;

        [Header("Shockwaves (any attack that connects)")]
        [Tooltip("Chance (%) that a light attack / combo hit wakes a sleeping item.")]
        [SerializeField, Range(0f, 100f)] float wakeChance = 25f;
        [Tooltip("Chance (%) that a heavy attack or a special wakes a sleeping item.")]
        [SerializeField, Range(0f, 100f)] float heavyWakeChance = 60f;
        [Tooltip("Blocked hits also send out a shockwave (parried hits never do).")]
        [SerializeField] bool blockedHitsCount = true;
        [Tooltip("Thrown items hitting a fighter also send out a shockwave.")]
        [SerializeField] bool itemHitsCount = false;
        [Tooltip("Only items this close to the hit can wake (world units). 0 = anywhere on the stage (the nearest one wakes).")]
        [SerializeField, Min(0f)] float wakeRadius = 0f;
        [Tooltip("Seconds after a shockwave before the next one can roll (stops one combo waking everything).")]
        [SerializeField, Min(0f)] float shockwaveCooldown = 0.3f;
        [Tooltip("Played when a shockwave wakes an item (optional).")]
        [SerializeField] SfxCue wakeCue;

        [Header("Always one item available")]
        [SerializeField] bool keepOneItemAvailable = true;
        [Tooltip("Seconds with no item in play (sleeping ones don't count) before one is supplied.")]
        [SerializeField, Min(0f)] float emptyDelay = 1.5f;

        [Header("Dropping in (when nothing is left asleep)")]
        [Tooltip("Keep this far from the stage walls (world units).")]
        [SerializeField, Min(0f)] float wallMargin = 0.6f;
        [Tooltip("Try not to land on a fighter: at least this far from both (world units). 0 = anywhere.")]
        [SerializeField, Min(0f)] float minDistanceFromFighters = 1.2f;
        [Tooltip("Dropped-in items arrive at their Fight Scale (they land in the fight, in the foreground). " +
                 "OFF: at the size they were placed on the stage.")]
        [SerializeField] bool dropInsAtFightSize = true;
        [Tooltip("How high above the floor dropped items start (world units).")]
        [SerializeField, Min(0.5f)] float dropHeight = 6f;
        [SerializeField, Min(1f)] float dropGravity = 25f;
        [Tooltip("Played when a dropped item lands (optional).")]
        [SerializeField] SfxCue landCue;
        [SerializeField] Color shadowColour = new Color(0f, 0f, 0f, 0.35f);

        [Header("Debug")]
        [SerializeField] bool logWakes = false;

        /// <summary>An item has started dropping in at this landing point.</summary>
        public event Action<Vector3> DropStarted;
        /// <summary>A shockwave (centre between the fighters) woke an item.</summary>
        public event Action<Vector3> Shockwave;
        /// <summary>A shockwave or the empty-stage rule woke this item.</summary>
        public event Action<Carryable> ItemWoken;

        /// <summary>Testing: items work as originally placed (no sleeping, re-laying or drop-ins). Setting it re-lays the stage.</summary>
        public bool OriginalItemBehaviour
        {
            get => originalItemBehaviour;
            set => SetOriginalItemBehaviour(value);
        }

        /// <summary>Raised when the mode is switched (pause menu toggle).</summary>
        public event Action<bool> ModeChanged;

        /// <summary>The pause menu's choice for this app session (null = use the Inspector value). Survives scene reloads.</summary>
        static bool? sessionOriginal;

        /// <summary>
        /// Switch between the GDD rules and the original behaviour while playing. The stage is cleared and every placed item
        /// laid out again at its spot: asleep (GDD rules) or awake and grabbable (original).
        /// </summary>
        public void SetOriginalItemBehaviour(bool on)
        {
            sessionOriginal = on;
            if (on == originalItemBehaviour) return;
            originalItemBehaviour = on;
            LayOut();
            ModeChanged?.Invoke(on);
            if (logWakes) Debug.Log($"[Stage Items] Mode: {(on ? "original item behaviour (testing)" : "GDD rules")}", this);
        }

        /// <summary>A placed item, hidden at scene start and copied (asleep) at its spot every round.</summary>
        struct Template
        {
            public Carryable source;
            public Transform parent;
            public Vector3 position, localScale;
            public Quaternion rotation;
            public string name;
        }

        readonly List<Template> templates = new List<Template>();
        readonly List<Carryable> sleepers = new List<Carryable>();
        readonly List<GameObject> dropProxies = new List<GameObject>();
        bool templatesRead;
        int pendingDrops;
        float nextShockwave;
        float emptySince = -1f;
        float nextCheck;

        void Awake()
        {
            if (sessionOriginal.HasValue) originalItemBehaviour = sessionOriginal.Value;
            if (!stage) stage = FindFirstObjectByType<StageBounds>();
            if (fighters == null || fighters.Length == 0) fighters = FindObjectsByType<Fighter>(FindObjectsSortMode.None);
            ReadTemplates();
        }

        /// <summary>Every active item in the scene becomes a stage item: remembered, then hidden (copies are used in play).</summary>
        void ReadTemplates()
        {
            if (templatesRead) return;
            templatesRead = true;
            templates.Clear();

            foreach (var item in FindObjectsByType<Carryable>(FindObjectsInactive.Exclude, FindObjectsSortMode.InstanceID))
            {
                var t = item.transform;
                templates.Add(new Template
                {
                    source = item, parent = t.parent, position = t.position, rotation = t.rotation,
                    localScale = t.localScale, name = item.name,
                });
                item.gameObject.SetActive(false);
            }
            if (templates.Count == 0)
                Debug.LogWarning("[Stage Items] No items placed in the scene. Drag item prefabs under 'Stage Items' where they should sleep.", this);
        }

        void OnEnable()
        {
            foreach (var f in fighters) if (f) f.HitConnected += OnHitConnected;
        }

        void OnDisable()
        {
            foreach (var f in fighters) if (f) f.HitConnected -= OnHitConnected;
        }

        void Start()
        {
            // No MatchManager (e.g. a test scene): lay the stage out once.
            if (!FindFirstObjectByType<MatchManager>()) ResetForRound();
        }

        // ───────────── Round start (MatchManager) ─────────────

        /// <summary>
        /// New round: clear the stage and lay every stage item out again, asleep, where it was placed.
        /// Original behaviour: the items are laid out awake once, then left alone between rounds.
        /// </summary>
        public void ResetForRound()
        {
            if (originalItemBehaviour && laidOut) return; // testing: leave the items exactly as they are
            LayOut();
        }

        bool laidOut;

        /// <summary>Clear the stage and lay every placed item out at its spot: asleep (GDD rules) or awake (original).</summary>
        void LayOut()
        {
            StopAllCoroutines();
            laidOut = true;

            foreach (var p in dropProxies) if (p) Destroy(p);
            dropProxies.Clear();
            pendingDrops = 0;
            emptySince = -1f;
            nextShockwave = 0f;

            foreach (var holder in FindObjectsByType<ItemHolder>(FindObjectsSortMode.None)) holder.Drop();
            foreach (var item in FindObjectsByType<Carryable>(FindObjectsSortMode.None)) Destroy(item.gameObject);

            ReadTemplates();
            sleepers.Clear();
            foreach (var t in templates)
            {
                if (!t.source) continue;
                var item = Spawn(t, t.position, t.rotation);
                if (originalItemBehaviour) continue; // awake and grabbable where it was placed
                item.MakeDormant();
                sleepers.Add(item);
            }
        }

        // ───────────── Shockwaves ─────────────

        void OnHitConnected(Fighter target, AttackData attack, HitResult result)
        {
            if (originalItemBehaviour || !target) return;
            if (attack is ThrowableData && !itemHitsCount) return;
            bool counts = result == HitResult.Hit || (result == HitResult.Blocked && blockedHitsCount);
            if (!counts || Time.time < nextShockwave) return;
            nextShockwave = Time.time + shockwaveCooldown;

            var attacker = target.Opponent;
            bool heavy = attack && (attack.weight == HitWeight.Heavy || (attacker && attacker.IsDoingSpecial));
            float chance = heavy ? heavyWakeChance : wakeChance;
            float roll = Random.value * 100f;
            if (roll >= chance)
            {
                if (logWakes) Debug.Log($"[Stage Items] Shockwave rolled {roll:0} vs {chance:0}% ({(heavy ? "heavy" : "light")}): nothing woke.", this);
                return;
            }

            Vector3 centre = attacker ? (attacker.transform.position + target.transform.position) * 0.5f : target.transform.position;
            var item = NearestSleeper(centre, wakeRadius);
            if (!item) return;
            if (WakeItem(item))
            {
                AudioManager.Play(wakeCue);
                Shockwave?.Invoke(centre);
                if (logWakes) Debug.Log($"[Stage Items] Shockwave ({(heavy ? "heavy" : "light")}, {chance:0}%) woke {item.name}", this);
            }
        }

        /// <summary>The sleeping item nearest a point (within radius; 0 = any distance), or null.</summary>
        Carryable NearestSleeper(Vector3 from, float radius)
        {
            sleepers.RemoveAll(s => !s || !s.IsDormant);
            Carryable best = null;
            float bestDist = float.MaxValue;
            foreach (var s in sleepers)
            {
                float d = Vector2.Distance(s.transform.position, from);
                if (radius > 0f && d > radius) continue;
                if (d < bestDist) { bestDist = d; best = s; }
            }
            return best;
        }

        bool WakeItem(Carryable item)
        {
            if (!item.Wake()) return false;
            sleepers.Remove(item);
            ItemWoken?.Invoke(item);
            return true;
        }

        // ───────────── Always one item available ─────────────

        void Update()
        {
            if (originalItemBehaviour || !keepOneItemAvailable || Time.time < nextCheck) return;
            nextCheck = Time.time + 0.25f;

            if (CountItemsInPlay() + pendingDrops > 0) { emptySince = -1f; return; }
            if (emptySince < 0f) { emptySince = Time.time; return; }
            if (Time.time - emptySince < emptyDelay) return;
            emptySince = -1f;

            // Prefer knocking a sleeping item loose (it falls onto the stage); otherwise drop a new one in.
            var item = NearestSleeper(FightersCentre(), 0f);
            if (item && WakeItem(item))
            {
                if (logWakes) Debug.Log($"[Stage Items] Stage empty: woke {item.name}", this);
                return;
            }
            DropRandom();
        }

        int CountItemsInPlay()
        {
            int n = 0;
            foreach (var c in FindObjectsByType<Carryable>(FindObjectsSortMode.None))
                if (!c.IsDormant) n++;
            return n;
        }

        Vector3 FightersCentre()
        {
            Vector3 sum = Vector3.zero; int n = 0;
            foreach (var f in fighters) if (f) { sum += f.transform.position; n++; }
            return n > 0 ? sum / n : transform.position;
        }

        /// <summary>Drop a random stage item in anywhere within the stage bounds.</summary>
        public void DropRandom()
        {
            ReadTemplates();
            var pool = templates.FindAll(t => t.source);
            if (pool.Count == 0 || !stage) return;
            var pick = pool[Random.Range(0, pool.Count)];
            StartCoroutine(DropIn(pick, PickLandingX()));
            if (logWakes) Debug.Log($"[Stage Items] Stage empty, nothing asleep: dropping in {pick.name}", this);
        }

        float PickLandingX()
        {
            float min = stage.MinX + wallMargin, max = stage.MaxX - wallMargin;
            if (min > max) { min = stage.MinX; max = stage.MaxX; }
            if (minDistanceFromFighters <= 0f) return Random.Range(min, max);

            float best = (min + max) * 0.5f, bestGap = -1f;
            for (int i = 0; i < 12; i++)
            {
                float x = Random.Range(min, max);
                float gap = float.MaxValue;
                foreach (var f in fighters) if (f) gap = Mathf.Min(gap, Mathf.Abs(f.X - x));
                if (gap >= minDistanceFromFighters) return x;
                if (gap > bestGap) { bestGap = gap; best = x; }
            }
            return best;
        }

        // ───────────── Spawning ─────────────

        /// <summary>A live copy of a stage item, under the same parent at the same scale.</summary>
        Carryable Spawn(Template t, Vector3 position, Quaternion rotation)
        {
            var item = Instantiate(t.source, t.parent); // inactive, like its template
            item.transform.SetPositionAndRotation(position, rotation);
            item.transform.localScale = t.localScale;
            // The template's outline object (made by ItemHighlight at scene start) would otherwise be copied twice.
            var oldGlow = item.transform.Find("Highlight");
            if (oldGlow) DestroyImmediate(oldGlow.gameObject);
            item.name = t.name;
            item.gameObject.SetActive(true);
            return item;
        }

        static float HalfHeight(Sprite sprite, Vector3 scale) =>
            sprite ? sprite.bounds.extents.y * Mathf.Abs(scale.y) : 0.25f;

        /// <summary>A stand-in sprite falls with a shadow below it; the real (awake) item appears where it lands.</summary>
        IEnumerator DropIn(Template t, float x)
        {
            pendingDrops++;
            var prefab = t.source;
            var prefabSr = prefab.GetComponent<SpriteRenderer>();
            Vector3 parentScale = t.parent ? t.parent.lossyScale : Vector3.one;
            Vector3 scale = dropInsAtFightSize
                ? Vector3.Scale(Vector3.one * prefab.FightScale, parentScale)
                : Vector3.Scale(t.localScale, parentScale);
            float restY = stage.GroundY + HalfHeight(prefab.GroundSprite, scale);

            var proxy = new GameObject($"{t.name} (dropping)");
            proxy.transform.position = new Vector3(x, restY + dropHeight, 0f);
            proxy.transform.localScale = scale;
            var psr = proxy.AddComponent<SpriteRenderer>();
            psr.sprite = prefab.GroundSprite;
            if (prefabSr)
            {
                psr.color = prefabSr.color;
                psr.sortingLayerID = prefabSr.sortingLayerID;
                psr.sortingOrder = prefabSr.sortingOrder + 5;
            }
            dropProxies.Add(proxy);

            var shadow = new GameObject("Drop shadow");
            shadow.transform.position = new Vector3(x, stage.GroundY + 0.02f, 0f);
            var ssr = shadow.AddComponent<SpriteRenderer>();
            ssr.sprite = psr.sprite;
            ssr.color = new Color(shadowColour.r, shadowColour.g, shadowColour.b, 0f);
            ssr.sortingLayerID = psr.sortingLayerID;
            ssr.sortingOrder = psr.sortingOrder - 6;
            dropProxies.Add(shadow);

            DropStarted?.Invoke(new Vector3(x, stage.GroundY, 0f));

            float y = restY + dropHeight, v = 0f;
            float spin = Random.Range(-240f, 240f), angle = 0f;
            while (y > restY)
            {
                float dt = Time.deltaTime;
                v += dropGravity * dt;
                y = Mathf.Max(restY, y - v * dt);
                angle += spin * dt;
                if (!proxy) { pendingDrops--; yield break; }
                proxy.transform.position = new Vector3(x, y, 0f);
                proxy.transform.rotation = Quaternion.Euler(0f, 0f, angle);

                // The shadow grows and darkens as the item comes down.
                float k = 1f - Mathf.Clamp01((y - restY) / dropHeight);
                shadow.transform.localScale = new Vector3(scale.x * Mathf.Lerp(0.4f, 1.1f, k), scale.y * 0.18f, 1f);
                var c = shadowColour; c.a *= k;
                ssr.color = c;
                yield return null;
            }

            dropProxies.Remove(proxy);
            dropProxies.Remove(shadow);
            Destroy(proxy);
            Destroy(shadow);
            pendingDrops--;

            var landed = Spawn(t, new Vector3(x, restY, 0f), Quaternion.identity);
            if (dropInsAtFightSize) landed.UseFightSize();
            AudioManager.Play(landCue);
        }

        // ───────────── Scene view ─────────────

        void OnDrawGizmosSelected()
        {
            if (wakeRadius <= 0f) return;
            Gizmos.color = new Color(1f, 0.8f, 0.2f, 0.6f);
            foreach (var f in fighters)
                if (f) DrawCircle(f.transform.position, wakeRadius);
        }

        static void DrawCircle(Vector3 c, float r)
        {
            const int steps = 40;
            Vector3 prev = c + new Vector3(r, 0f, 0f);
            for (int i = 1; i <= steps; i++)
            {
                float a = i * Mathf.PI * 2f / steps;
                Vector3 next = c + new Vector3(Mathf.Cos(a) * r, Mathf.Sin(a) * r, 0f);
                Gizmos.DrawLine(prev, next);
                prev = next;
            }
        }
    }
}
