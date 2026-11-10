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
    /// GDD → Environmental System, the stage side of it:
    ///   • "At the beginning of the match, there is a set amount of items" — Round Start Items are laid out at the start of
    ///     EVERY round (MatchManager calls ResetForRound), so each round opens with the race for the items
    ///   • "Occasionally, heavy attacks will cause a shockwave that will make new items drop onto the stage" — a heavy that
    ///     connects (hit or blocked) has Shockwave Chance to shake loose Drops Per Shockwave items, which fall from above
    ///     with a shadow marking where they'll land
    ///   • "…ensuring there will always be at least one item available" — when no item is in play (lying, carried or flying)
    ///     for Guarantee Delay seconds, one drops in
    /// Items are the thematic Restaurant set (plates / chairs / pots, created by the GDD Build setup); anything with a
    /// Carryable works. Falling items are a stand-in sprite until they land, so nobody can grab one mid-air.
    ///
    /// Setup: KungFu Crashout → GDD Build → Upgrade Fight Scene. Place in: Assets/_Project/Scripts/Environment/StageItemDirector.cs
    /// </summary>
    public class StageItemDirector : MonoBehaviour
    {
        [Serializable]
        public class Placement
        {
            public Carryable prefab;
            [Tooltip("Where on the stage: 0 = left wall, 0.5 = centre, 1 = right wall.")]
            [Range(0f, 1f)] public float stageX = 0.5f;
        }

        [SerializeField] StageBounds stage;
        [Tooltip("Fighters whose heavy attacks can cause a shockwave. Empty = every Fighter in the scene.")]
        [SerializeField] Fighter[] fighters = new Fighter[0];

        [Header("Round start")]
        [SerializeField] List<Placement> roundStartItems = new List<Placement>();
        [Tooltip("Remove every item (including ones placed in the scene by hand) when a round starts.")]
        [SerializeField] bool clearItemsEachRound = true;

        [Header("Heavy-attack shockwave")]
        [SerializeField] bool heavyShockwave = true;
        [Tooltip("Items a shockwave can shake loose (one is picked at random per drop).")]
        [SerializeField] Carryable[] dropPool = new Carryable[0];
        [SerializeField, Range(0f, 1f)] float shockwaveChance = 0.45f;
        [SerializeField, Min(1)] int dropsPerShockwave = 1;
        [Tooltip("Seconds before another shockwave can drop items.")]
        [SerializeField, Min(0f)] float shockwaveCooldown = 2.5f;
        [Tooltip("No shockwave drops while this many items (or more) are already in play.")]
        [SerializeField, Min(1)] int maxItemsInPlay = 6;

        [Header("Always one item")]
        [SerializeField] bool guaranteeOneItem = true;
        [Tooltip("Seconds with no item in play before one drops in.")]
        [SerializeField, Min(0f)] float guaranteeDelay = 1.5f;

        [Header("Dropping in")]
        [Tooltip("How high above the floor dropped items start (world units).")]
        [SerializeField, Min(0.5f)] float dropHeight = 6f;
        [SerializeField, Min(1f)] float dropGravity = 25f;
        [Tooltip("Dropped items land at least this far from both fighters (world units), inside the camera view.")]
        [SerializeField, Min(0f)] float minDistanceFromFighters = 1.2f;
        [Tooltip("Played when a dropped item lands (optional).")]
        [SerializeField] SfxCue landCue;
        [SerializeField] Color shadowColour = new Color(0f, 0f, 0f, 0.35f);

        /// <summary>An item has started dropping in at this landing point.</summary>
        public event Action<Vector3> DropStarted;
        /// <summary>A shockwave from this fighter's heavy attack shook items loose.</summary>
        public event Action<Fighter> Shockwave;

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
            // No MatchManager (e.g. a test scene): lay out the round-start set once.
            if (!FindFirstObjectByType<MatchManager>()) ResetForRound();
        }

        void Update()
        {
            if (!guaranteeOneItem || Time.time < nextCheck) return;
            nextCheck = Time.time + 0.25f;

            int inPlay = CountItems() + pendingDrops;
            if (inPlay > 0) { emptySince = -1f; return; }
            if (emptySince < 0f) { emptySince = Time.time; return; }
            if (Time.time - emptySince < guaranteeDelay) return;

            emptySince = -1f;
            DropRandom();
        }

        // ───────────── Round reset (MatchManager) ─────────────

        /// <summary>Clear the stage and lay out the round-start items.</summary>
        public void ResetForRound()
        {
            StopAllCoroutines();
            foreach (var p in dropProxies) if (p) Destroy(p);
            dropProxies.Clear();
            pendingDrops = 0;
            emptySince = -1f;
            nextShockwave = Time.time + shockwaveCooldown;

            if (clearItemsEachRound)
            {
                foreach (var holder in FindObjectsByType<ItemHolder>(FindObjectsSortMode.None)) holder.Drop();
                foreach (var item in FindObjectsByType<Carryable>(FindObjectsSortMode.None)) Destroy(item.gameObject);
            }

            foreach (var placement in roundStartItems)
            {
                if (placement == null || !placement.prefab || !stage) continue;
                float x = Mathf.Lerp(stage.MinX, stage.MaxX, placement.stageX);
                SpawnOnFloor(placement.prefab, x);
            }
        }

        // ───────────── Shockwave ─────────────

        void OnHitConnected(Fighter target, AttackData attack, HitResult result)
        {
            if (!heavyShockwave || result == HitResult.Ignored || result == HitResult.Parried) return;
            var attacker = target ? target.Opponent : null;
            if (!attacker || attack != attacker.HeavyAttackData) return;
            if (Time.time < nextShockwave || Random.value > shockwaveChance) return;
            if (CountItems() + pendingDrops >= maxItemsInPlay) return;

            nextShockwave = Time.time + shockwaveCooldown;
            for (int i = 0; i < dropsPerShockwave; i++) DropRandom();
            Shockwave?.Invoke(attacker);
        }

        // ───────────── Spawning ─────────────

        /// <summary>Drop a random pool item (or the first round-start item) at a free spot in view.</summary>
        public void DropRandom()
        {
            var prefab = PickDropPrefab();
            if (!prefab || !stage) return;
            StartCoroutine(DropIn(prefab, PickLandingX()));
        }

        Carryable PickDropPrefab()
        {
            if (dropPool != null && dropPool.Length > 0)
            {
                var p = dropPool[Random.Range(0, dropPool.Length)];
                if (p) return p;
            }
            foreach (var placement in roundStartItems) if (placement != null && placement.prefab) return placement.prefab;
            return null;
        }

        float PickLandingX()
        {
            float min = stage.MinX + 0.6f, max = stage.MaxX - 0.6f;
            var cam = Camera.main;
            if (cam && cam.orthographic)
            {
                float halfW = cam.orthographicSize * cam.aspect;
                min = Mathf.Max(min, cam.transform.position.x - halfW + 0.8f);
                max = Mathf.Min(max, cam.transform.position.x + halfW - 0.8f);
                if (min > max) { min = stage.MinX + 0.6f; max = stage.MaxX - 0.6f; }
            }

            float best = (min + max) * 0.5f, bestGap = -1f;
            for (int i = 0; i < 10; i++)
            {
                float x = Random.Range(min, max);
                float gap = float.MaxValue;
                foreach (var f in fighters) if (f) gap = Mathf.Min(gap, Mathf.Abs(f.X - x));
                if (gap >= minDistanceFromFighters) return x;
                if (gap > bestGap) { bestGap = gap; best = x; }
            }
            return best;
        }

        Carryable SpawnOnFloor(Carryable prefab, float x)
        {
            var item = Instantiate(prefab, new Vector3(x, stage.GroundY, 0f), Quaternion.identity);
            item.name = prefab.name;
            var sr = item.GetComponent<SpriteRenderer>();
            float halfHeight = sr ? sr.bounds.extents.y : 0.25f;
            var p = item.transform.position;
            p.y = stage.GroundY + halfHeight;
            item.transform.position = p;
            return item;
        }

        /// <summary>A stand-in sprite falls with a shadow below it; the real item appears where it lands.</summary>
        IEnumerator DropIn(Carryable prefab, float x)
        {
            pendingDrops++;
            var prefabSr = prefab.GetComponent<SpriteRenderer>();
            Vector3 scale = prefab.transform.localScale;
            float halfHeight = prefabSr && prefabSr.sprite ? prefabSr.sprite.bounds.extents.y * Mathf.Abs(scale.y) : 0.25f;
            float restY = stage.GroundY + halfHeight;

            var proxy = new GameObject($"{prefab.name} (dropping)");
            proxy.transform.position = new Vector3(x, restY + dropHeight, 0f);
            proxy.transform.localScale = scale;
            var psr = proxy.AddComponent<SpriteRenderer>();
            if (prefabSr)
            {
                psr.sprite = prefabSr.sprite;
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

            SpawnOnFloor(prefab, x);
            AudioManager.Play(landCue);
        }

        static int CountItems() => FindObjectsByType<Carryable>(FindObjectsSortMode.None).Length;

        void OnDrawGizmosSelected()
        {
            if (!stage) return;
            Gizmos.color = new Color(1f, 0.8f, 0.2f, 0.9f);
            foreach (var placement in roundStartItems)
            {
                if (placement == null) continue;
                float x = Mathf.Lerp(stage.MinX, stage.MaxX, placement.stageX);
                Gizmos.DrawWireCube(new Vector3(x, stage.GroundY + 0.3f, 0f), new Vector3(0.5f, 0.6f, 0f));
            }
        }
    }
}
