using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;
using KungFuCrashout.Core;
using KungFuCrashout.Fighters;
using KungFuCrashout.Sound;
using Random = UnityEngine.Random;

namespace KungFuCrashout.Props
{
    /// <summary>
    /// GDD → Environmental System, the stage side of it (10 Nov 2026 rules):
    ///   • Stage Items — every entry is one item at its own spot on the stage. When a round starts they're all laid out
    ///     ASLEEP: visible (dimmed) but not grabbable. They only come into play as the round goes on.
    ///   • Hit shockwaves — when one fighter's attack connects with the other (hit, and blocked if Blocked Hits Count),
    ///     a shockwave goes out around BOTH fighters and wakes ONE sleeping item within its radius (each entry's
    ///     Wake Radius, or Shockwave Radius). The woken item is knocked loose, falls to the floor and can be grabbed.
    ///     Which one, set by the item's element index in Stage Items (Wake Order):
    ///       In List Order         — entry 0 first, then 1, 2…: a hit wakes the NEXT entry only if it's within range
    ///       Lowest Index In Range — a hit wakes the lowest-numbered sleeping entry that's within range
    ///   • Empty stage — when no item is in play (lying, carried, flying; sleeping ones don't count unless Count Sleeping
    ///     Items) for Empty Delay seconds, a random item from Stage Items drops in anywhere within the stage bounds,
    ///     with a shadow marking where it lands.
    /// Prototype 3's fixed round-start layout and heavy-hit sky drops are replaced by these rules.
    /// Item spots: drag an empty Transform into Spot, or use Stage X (0 = left wall, 1 = right wall) + Height above
    /// the floor. Select this object to see every spot, its index and its wake radius in the Scene view.
    ///
    /// Setup: Window → KungFu Crashout → GDD Build → Upgrade Fight Scene. Place in: Assets/_Project/Scripts/Environment/StageItemDirector.cs
    /// </summary>
    public class StageItemDirector : MonoBehaviour
    {
        public enum WakeOrder { InListOrder, LowestIndexInRange }

        [Serializable]
        public class StageItem
        {
            public Carryable prefab;
            [Tooltip("Where it sleeps. Empty = Stage X + Height below.")]
            public Transform spot;
            [Tooltip("0 = left wall, 0.5 = centre, 1 = right wall (used when Spot is empty).")]
            [Range(0f, 1f)] public float stageX = 0.5f;
            [Tooltip("How high above the floor it sleeps (world units), e.g. on a table or shelf. 0 = on the floor.")]
            [Min(0f)] public float height = 0f;
            [Tooltip("How close a hit has to be to wake it (world units). 0 = the director's Shockwave Radius.")]
            [Min(0f)] public float wakeRadius = 0f;
        }

        [SerializeField] StageBounds stage;
        [Tooltip("Fighters whose hits make shockwaves. Empty = every Fighter in the scene.")]
        [SerializeField] Fighter[] fighters = new Fighter[0];

        [Header("Stage items (element index = wake order / priority)")]
        [FormerlySerializedAs("roundStartItems")]
        [SerializeField] List<StageItem> stageItems = new List<StageItem>();
        [Tooltip("Remove every item (including ones placed in the scene by hand) when a round starts.")]
        [SerializeField] bool clearItemsEachRound = true;

        [Header("Hit shockwaves")]
        [SerializeField] bool hitShockwaves = true;
        [Tooltip("TESTING TOGGLE. In List Order: a hit wakes the next entry (0, 1, 2…) only if it's in range. " +
                 "Lowest Index In Range: a hit wakes the lowest-numbered sleeping entry that's in range.")]
        [SerializeField] WakeOrder wakeOrder = WakeOrder.InListOrder;
        [Tooltip("Default wake distance from either fighter (world units). Each entry can override it.")]
        [SerializeField, Min(0.1f)] float shockwaveRadius = 2.5f;
        [Tooltip("Blocked hits also send out a shockwave (parried hits never do).")]
        [SerializeField] bool blockedHitsCount = true;
        [Tooltip("Thrown items hitting a fighter also send out a shockwave. Off = only fighters hitting each other.")]
        [SerializeField] bool itemHitsCount = false;
        [Tooltip("Seconds before another shockwave can wake an item (stops one combo waking everything).")]
        [SerializeField, Min(0f)] float shockwaveCooldown = 0.3f;
        [Tooltip("Played when a shockwave wakes an item (optional).")]
        [SerializeField] SfxCue wakeCue;

        [Header("Empty stage")]
        [SerializeField] bool spawnWhenEmpty = true;
        [Tooltip("Seconds with no item in play before a random one drops in.")]
        [FormerlySerializedAs("guaranteeDelay")]
        [SerializeField, Min(0f)] float emptyDelay = 1.5f;
        [Tooltip("Count sleeping items as \"in play\" (then nothing drops in while any are asleep).")]
        [SerializeField] bool countSleepingItems = false;
        [Tooltip("Keep this far from the stage walls (world units).")]
        [SerializeField, Min(0f)] float wallMargin = 0.6f;
        [Tooltip("Try not to land on a fighter: at least this far from both (world units). 0 = anywhere.")]
        [SerializeField, Min(0f)] float minDistanceFromFighters = 1.2f;

        [Header("Dropping in")]
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
        /// <summary>A hit sent out a shockwave (centre between the fighters) that woke an item.</summary>
        public event Action<Vector3> Shockwave;
        /// <summary>(Stage Items index, item) a shockwave woke this entry.</summary>
        public event Action<int, Carryable> ItemWoken;

        public WakeOrder Order { get => wakeOrder; set => wakeOrder = value; }

        Carryable[] sleepers = new Carryable[0];
        int nextIndex;
        int pendingDrops;
        float nextShockwave;
        float emptySince = -1f;
        float nextCheck;
        readonly List<GameObject> dropProxies = new List<GameObject>();

        void Awake()
        {
            if (!stage) stage = FindFirstObjectByType<StageBounds>();
            if (fighters == null || fighters.Length == 0) fighters = FindObjectsByType<Fighter>(FindObjectsSortMode.None);
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

        void Update()
        {
            if (!spawnWhenEmpty || Time.time < nextCheck) return;
            nextCheck = Time.time + 0.25f;

            int inPlay = CountItemsInPlay() + pendingDrops;
            if (inPlay > 0) { emptySince = -1f; return; }
            if (emptySince < 0f) { emptySince = Time.time; return; }
            if (Time.time - emptySince < emptyDelay) return;

            emptySince = -1f;
            DropRandom();
        }

        // ───────────── Round reset (MatchManager) ─────────────

        /// <summary>Clear the stage and lay every Stage Items entry out asleep at its spot.</summary>
        public void ResetForRound()
        {
            StopAllCoroutines();
            foreach (var p in dropProxies) if (p) Destroy(p);
            dropProxies.Clear();
            pendingDrops = 0;
            emptySince = -1f;
            nextShockwave = 0f;
            nextIndex = 0;

            if (clearItemsEachRound)
            {
                foreach (var holder in FindObjectsByType<ItemHolder>(FindObjectsSortMode.None)) holder.Drop();
                foreach (var item in FindObjectsByType<Carryable>(FindObjectsSortMode.None)) Destroy(item.gameObject);
            }

            sleepers = new Carryable[stageItems.Count];
            for (int i = 0; i < stageItems.Count; i++)
            {
                var entry = stageItems[i];
                if (entry == null || !entry.prefab || !stage) continue;
                var item = Spawn(entry.prefab, SpotOf(entry, entry.prefab));
                item.name = $"{entry.prefab.name} #{i}";
                item.MakeDormant();
                sleepers[i] = item;
            }
        }

        // ───────────── Shockwaves ─────────────

        void OnHitConnected(Fighter target, AttackData attack, HitResult result)
        {
            if (!hitShockwaves || !target) return;
            if (attack is ThrowableData && !itemHitsCount) return;
            bool counts = result == HitResult.Hit || (result == HitResult.Blocked && blockedHitsCount);
            if (!counts || Time.time < nextShockwave) return;

            var attacker = target.Opponent;
            int index = PickToWake(target, attacker);
            if (index < 0) return;

            var item = sleepers[index];
            if (!item.Wake()) return;
            sleepers[index] = null;
            if (wakeOrder == WakeOrder.InListOrder) nextIndex = index + 1;
            nextShockwave = Time.time + shockwaveCooldown;

            Vector3 centre = attacker ? (attacker.transform.position + target.transform.position) * 0.5f : target.transform.position;
            AudioManager.Play(wakeCue);
            Shockwave?.Invoke(centre);
            ItemWoken?.Invoke(index, item);
            if (logWakes) Debug.Log($"[Stage Items] Shockwave ({wakeOrder}) woke #{index} {item.name}", this);
        }

        /// <summary>The Stage Items index this hit wakes, or -1.</summary>
        int PickToWake(Fighter a, Fighter b)
        {
            if (wakeOrder == WakeOrder.InListOrder)
            {
                // Skip entries that are gone or already awake (destroyed items, empty slots).
                while (nextIndex < sleepers.Length && !IsAsleep(nextIndex)) nextIndex++;
                if (nextIndex >= sleepers.Length) return -1;
                return InRange(nextIndex, a, b) ? nextIndex : -1;
            }

            for (int i = 0; i < sleepers.Length; i++)
                if (IsAsleep(i) && InRange(i, a, b)) return i;
            return -1;
        }

        bool IsAsleep(int i) => sleepers[i] && sleepers[i].IsDormant;

        bool InRange(int i, Fighter a, Fighter b)
        {
            float r = RadiusOf(i);
            Vector2 p = sleepers[i].transform.position;
            return (a && Vector2.Distance(p, a.transform.position) <= r) ||
                   (b && Vector2.Distance(p, b.transform.position) <= r);
        }

        float RadiusOf(int i) =>
            i < stageItems.Count && stageItems[i] != null && stageItems[i].wakeRadius > 0f ? stageItems[i].wakeRadius : shockwaveRadius;

        // ───────────── Empty stage ─────────────

        /// <summary>Drop a random Stage Items prefab in anywhere within the stage bounds.</summary>
        public void DropRandom()
        {
            var prefab = PickRandomPrefab();
            if (!prefab || !stage) return;
            StartCoroutine(DropIn(prefab, PickLandingX()));
        }

        Carryable PickRandomPrefab()
        {
            var pool = new List<Carryable>();
            foreach (var e in stageItems) if (e != null && e.prefab) pool.Add(e.prefab);
            return pool.Count > 0 ? pool[Random.Range(0, pool.Count)] : null;
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

        int CountItemsInPlay()
        {
            int n = 0;
            foreach (var c in FindObjectsByType<Carryable>(FindObjectsSortMode.None))
                if (countSleepingItems || !c.IsDormant) n++;
            return n;
        }

        // ───────────── Spawning ─────────────

        Vector3 SpotOf(StageItem e, Carryable prefab)
        {
            if (e.spot) return e.spot.position;
            float x = Mathf.Lerp(stage.MinX, stage.MaxX, e.stageX);
            return new Vector3(x, stage.GroundY + HalfHeight(prefab) + e.height, 0f);
        }

        static float HalfHeight(Carryable prefab)
        {
            var sprite = prefab.GroundSprite;
            return sprite ? sprite.bounds.extents.y * Mathf.Abs(prefab.transform.localScale.y) : 0.25f;
        }

        Carryable Spawn(Carryable prefab, Vector3 position)
        {
            var item = Instantiate(prefab, position, Quaternion.identity);
            item.name = prefab.name;
            return item;
        }

        /// <summary>A stand-in sprite falls with a shadow below it; the real (awake) item appears where it lands.</summary>
        IEnumerator DropIn(Carryable prefab, float x)
        {
            pendingDrops++;
            var prefabSr = prefab.GetComponent<SpriteRenderer>();
            Vector3 scale = prefab.transform.localScale;
            float restY = stage.GroundY + HalfHeight(prefab);

            var proxy = new GameObject($"{prefab.name} (dropping)");
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

                // Shadow grows and darkens as the item comes down.
                float t = 1f - Mathf.Clamp01((y - restY) / dropHeight);
                shadow.transform.localScale = new Vector3(scale.x * Mathf.Lerp(0.4f, 1.1f, t), scale.y * 0.18f, 1f);
                var c = shadowColour; c.a *= t;
                ssr.color = c;
                yield return null;
            }

            dropProxies.Remove(proxy);
            dropProxies.Remove(shadow);
            Destroy(proxy);
            Destroy(shadow);
            pendingDrops--;

            Spawn(prefab, new Vector3(x, restY, 0f));
            AudioManager.Play(landCue);
        }

        void OnDrawGizmosSelected()
        {
            if (!stage) stage = FindFirstObjectByType<StageBounds>();
            if (!stage) return;
            for (int i = 0; i < stageItems.Count; i++)
            {
                var e = stageItems[i];
                if (e == null) continue;
                Vector3 p = e.spot ? e.spot.position
                                   : new Vector3(Mathf.Lerp(stage.MinX, stage.MaxX, e.stageX),
                                                 stage.GroundY + (e.prefab ? HalfHeight(e.prefab) : 0.3f) + e.height, 0f);
                Gizmos.color = new Color(1f, 0.8f, 0.2f, 0.9f);
                Gizmos.DrawWireCube(p, new Vector3(0.5f, 0.5f, 0f));
                Gizmos.color = new Color(1f, 0.8f, 0.2f, 0.25f);
                DrawCircle(p, e.wakeRadius > 0f ? e.wakeRadius : shockwaveRadius);
#if UNITY_EDITOR
                UnityEditor.Handles.Label(p + Vector3.up * 0.45f, $"#{i} {(e.prefab ? e.prefab.name : "(empty)")}");
#endif
            }
        }

        static void DrawCircle(Vector3 c, float r)
        {
            const int n = 40;
            Vector3 prev = c + new Vector3(r, 0f, 0f);
            for (int k = 1; k <= n; k++)
            {
                float a = k * Mathf.PI * 2f / n;
                Vector3 next = c + new Vector3(Mathf.Cos(a) * r, Mathf.Sin(a) * r, 0f);
                Gizmos.DrawLine(prev, next);
                prev = next;
            }
        }
    }
}
