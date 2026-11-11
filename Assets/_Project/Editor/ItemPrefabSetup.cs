#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using KungFuCrashout.Fighters;
using KungFuCrashout.Props;

namespace KungFuCrashout.EditorTools
{
    /// <summary>
    /// Item creation in the editor — two menus under Window → KungFu Crashout → Items:
    ///   • Make Item Prefab → Light / Medium / Heavy — select an item's sprites in Art/Items, pick its weight, done.
    ///   • Add Throw Toss — gives the selected item prefabs the wind-up toss (up over the head, thrown from the ThrowPoint).
    /// Then drag the prefab under Stage Items in FightScene and scale it however the background needs: on pick-up it
    /// grows to its Fight Scale (Carryable → Size), its proper size next to the fighters.
    /// Place in: Assets/_Project/Editor/ItemPrefabSetup.cs
    /// </summary>
    public static class ItemPrefabSetup
    {
        // ───────────── Make Item Prefab ─────────────

        public enum Weight { Light, Medium, Heavy }

        const string ItemPrefabFolder = "Assets/_Project/Prefabs/ItemPrefab";
        const string ItemDataFolder = "Assets/_Project/Data/GDD";
        const string HighlightMaterialPath = "Assets/_Project/Materials/GDD/ItemHighlight.mat";

        /// <summary>
        /// Window → KungFu Crashout → Items → Make Item Prefab → Light / Medium / Heavy
        /// Select an item's sprites in the Project window (e.g. Restaurant_Prop_Container_Idle + _Held, or a whole lantern
        /// set), then choose its weight. One prefab per item, in Prefabs/ItemPrefab, named from the file names:
        ///   …_Idle / …_Ground  → Ground Sprite        …_Held → Held Sprite        …_Hanging / …_Sleeping → Sleeping Sprite
        ///   a sprite with none of these (e.g. Restaurant_Prop_Plate) → Ground and Held
        ///   only a Held sprite (Kettle, Vase) → used as the Ground Sprite too
        /// Several items can be selected at once. Each prefab gets: Sprite Renderer, tap collider fitted to the Ground
        /// Sprite, the Environment layer, Carryable (weight preset), Item Highlight (grab glow + green throwable outline),
        /// Item Durability (weight rules), its OWN hit data (Data/GDD/Item_&lt;Name&gt;_Hit, copied from the weight's template)
        /// and the item throw / break sounds. Add the wind-up toss with Add Throw Toss.
        /// Weight presets (all editable on the prefab afterwards):
        ///   Light  — throw 18, 2 holds, breaks on a block and on a clean hit, carrier moves freely
        ///   Medium — throw 14, 3 holds, breaks on a block, carrier moves freely
        ///   Heavy  — throw 10, 3 holds, survives one block, carrier is ROOTED while holding it
        /// If the prefab already exists you're asked: update its sprites only (keeps all your tuning) or rebuild it.
        /// </summary>
        [MenuItem("Window/KungFu Crashout/Items/Make Item Prefab/Light", priority = 10070)]
        static void MakeLight() => MakeItemPrefabs(Weight.Light);
        [MenuItem("Window/KungFu Crashout/Items/Make Item Prefab/Medium", priority = 10071)]
        static void MakeMedium() => MakeItemPrefabs(Weight.Medium);
        [MenuItem("Window/KungFu Crashout/Items/Make Item Prefab/Heavy", priority = 10072)]
        static void MakeHeavy() => MakeItemPrefabs(Weight.Heavy);

        [MenuItem("Window/KungFu Crashout/Items/Make Item Prefab/Light", true)]
        [MenuItem("Window/KungFu Crashout/Items/Make Item Prefab/Medium", true)]
        [MenuItem("Window/KungFu Crashout/Items/Make Item Prefab/Heavy", true)]
        static bool HasSpriteSelection() => SelectedSprites().Count > 0;

        class ItemSprites { public Sprite ground, held, sleeping, plain; }

        static List<Sprite> SelectedSprites()
        {
            var list = new List<Sprite>();
            foreach (var o in Selection.objects)
            {
                if (o is Sprite sp) { list.Add(sp); continue; }
                string path = AssetDatabase.GetAssetPath(o);
                var sprite = string.IsNullOrEmpty(path) ? null : AssetDatabase.LoadAssetAtPath<Sprite>(path);
                if (sprite) list.Add(sprite);
            }
            return list.Distinct().ToList();
        }

        /// <summary>"Restaurant_Prop_Lantern_Red_Hanging" → ("Lantern_Red", "hanging").</summary>
        static (string name, string role) Parse(string fileName)
        {
            string n = fileName;
            int prop = n.IndexOf("Prop_", System.StringComparison.OrdinalIgnoreCase);
            if (prop >= 0) n = n.Substring(prop + 5);
            foreach (var (suffix, role) in new[] { ("_Idle", "ground"), ("_Ground", "ground"), ("_Held", "held"),
                                                   ("_Hanging", "sleeping"), ("_Sleeping", "sleeping") })
                if (n.EndsWith(suffix, System.StringComparison.OrdinalIgnoreCase))
                    return (n.Substring(0, n.Length - suffix.Length), role);
            return (n, "plain");
        }

        public static void MakeItemPrefabs(Weight weight)
        {
            var groups = new SortedDictionary<string, ItemSprites>();
            foreach (var sprite in SelectedSprites())
            {
                var (name, role) = Parse(System.IO.Path.GetFileNameWithoutExtension(AssetDatabase.GetAssetPath(sprite)));
                if (!groups.TryGetValue(name, out var g)) groups[name] = g = new ItemSprites();
                switch (role)
                {
                    case "ground": g.ground = sprite; break;
                    case "held": g.held = sprite; break;
                    case "sleeping": g.sleeping = sprite; break;
                    default: g.plain = sprite; break;
                }
            }
            if (groups.Count == 0)
            {
                EditorUtility.DisplayDialog("Make Item Prefab", "Select the item's sprites in the Project window first " +
                                            "(e.g. everything in Art/Items for one prop).", "OK");
                return;
            }

            Folder(ItemPrefabFolder);
            Folder(ItemDataFolder);
            var log = new List<string>();
            foreach (var kv in groups)
            {
                var g = kv.Value;
                var ground = g.ground ? g.ground : g.plain ? g.plain : g.held ? g.held : g.sleeping;
                var held = g.held ? g.held : ground;
                var sleeping = g.sleeping; // empty = ground
                string path = $"{ItemPrefabFolder}/Item_{kv.Key}.prefab";

                if (AssetDatabase.LoadAssetAtPath<GameObject>(path))
                {
                    int choice = EditorUtility.DisplayDialogComplex("Make Item Prefab",
                        $"Item_{kv.Key} already exists.\n\nUpdate sprites only: keeps all its tuning, components and hit data.\n" +
                        $"Rebuild: starts again from the {weight} preset.", "Update sprites only", "Skip", "Rebuild");
                    if (choice == 1) { log.Add($"Item_{kv.Key}: skipped"); continue; }
                    if (choice == 0)
                    {
                        UpdateSprites(path, ground, held, sleeping);
                        log.Add($"Item_{kv.Key}: sprites updated (ground {Nm(ground)}, held {Nm(held)}, sleeping {Nm(sleeping)})");
                        continue;
                    }
                }

                BuildPrefab(kv.Key, path, weight, ground, held, sleeping);
                log.Add($"Item_{kv.Key}: {weight} (ground {Nm(ground)}, held {Nm(held)}, sleeping {Nm(sleeping)}) → {path}");
            }
            AssetDatabase.SaveAssets();
            Debug.Log("[Items] Make Item Prefab:\n  • " + string.Join("\n  • ", log) +
                      "\nDrag them under Stage Items in FightScene to place them (Hierarchy order = wake order).");
        }

        static string Nm(Sprite s) => s ? s.name : "-";

        static void UpdateSprites(string path, Sprite ground, Sprite held, Sprite sleeping)
        {
            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                var carry = root.GetComponent<Carryable>();
                if (carry)
                {
                    var so = new SerializedObject(carry);
                    so.FindProperty("groundSprite").objectReferenceValue = ground;
                    so.FindProperty("heldSprite").objectReferenceValue = held;
                    var sl = so.FindProperty("sleepingSprite");
                    if (sl != null) sl.objectReferenceValue = sleeping;
                    so.ApplyModifiedPropertiesWithoutUndo();
                }
                var sr = root.GetComponent<SpriteRenderer>();
                if (sr) sr.sprite = sleeping ? sleeping : ground;
                if (root.TryGetComponent(out BoxCollider2D box) && ground)
                {
                    box.size = ground.bounds.size;
                    box.offset = ground.bounds.center;
                }
                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        static void BuildPrefab(string itemName, string path, Weight weight, Sprite ground, Sprite held, Sprite sleeping)
        {
            // Look and sounds copied from an existing item, so new ones match.
            Carryable reference = null;
            foreach (var n in new[] { "Item_Chair", "Item_Bowl", "Item_Pot" })
            {
                reference = AssetDatabase.LoadAssetAtPath<Carryable>($"{ItemPrefabFolder}/{n}.prefab");
                if (reference) break;
            }
            Object throwCue = null, breakCue = null;
            int sortingLayer = 0, sortingOrder = 0;
            if (reference)
            {
                var rso = new SerializedObject(reference);
                throwCue = rso.FindProperty("throwCue").objectReferenceValue;
                breakCue = rso.FindProperty("breakCue").objectReferenceValue;
                var rsr = reference.GetComponent<SpriteRenderer>();
                if (rsr) { sortingLayer = rsr.sortingLayerID; sortingOrder = rsr.sortingOrder; }
            }
            if (!throwCue) throwCue = FindAssetByName("SFX_ItemThrow");
            if (!breakCue) breakCue = FindAssetByName("SFX_ItemBreak");

            var go = new GameObject($"Item_{itemName}");
            try
            {
                int env = LayerMask.NameToLayer("Environment");
                if (env >= 0) go.layer = env;

                var sr = go.AddComponent<SpriteRenderer>();
                sr.sprite = sleeping ? sleeping : ground;
                sr.sortingLayerID = sortingLayer;
                sr.sortingOrder = sortingOrder;
                var box = go.AddComponent<BoxCollider2D>();
                if (ground) { box.size = ground.bounds.size; box.offset = ground.bounds.center; }

                var carry = go.AddComponent<Carryable>();
                var so = new SerializedObject(carry);
                so.FindProperty("groundSprite").objectReferenceValue = ground;
                so.FindProperty("heldSprite").objectReferenceValue = held;
                var sl = so.FindProperty("sleepingSprite");
                if (sl != null) sl.objectReferenceValue = sleeping;
                so.FindProperty("hitData").objectReferenceValue = HitDataFor(itemName, weight);
                so.FindProperty("throwCue").objectReferenceValue = throwCue;
                so.FindProperty("breakCue").objectReferenceValue = breakCue;
                so.FindProperty("lifetime").floatValue = 300f;
                switch (weight)
                {
                    case Weight.Light:
                        so.FindProperty("throwSpeed").floatValue = 18f;
                        so.FindProperty("maxHolds").intValue = 2;
                        so.FindProperty("breakOnShield").boolValue = true;
                        so.FindProperty("canMoveWhileHeld").boolValue = true;
                        break;
                    case Weight.Medium:
                        so.FindProperty("throwSpeed").floatValue = 14f;
                        so.FindProperty("maxHolds").intValue = 3;
                        so.FindProperty("breakOnShield").boolValue = true;
                        so.FindProperty("canMoveWhileHeld").boolValue = true;
                        break;
                    default:
                        so.FindProperty("throwSpeed").floatValue = 10f;
                        so.FindProperty("maxHolds").intValue = 3;
                        so.FindProperty("breakOnShield").boolValue = false;
                        so.FindProperty("canMoveWhileHeld").boolValue = false;
                        break;
                }
                so.ApplyModifiedPropertiesWithoutUndo();

                var hl = go.AddComponent<ItemHighlight>();
                var hso = new SerializedObject(hl);
                hso.FindProperty("silhouetteMaterial").objectReferenceValue = AssetDatabase.LoadAssetAtPath<Material>(HighlightMaterialPath);
                hso.ApplyModifiedPropertiesWithoutUndo();

                var dur = go.AddComponent<ItemDurability>();
                var dso = new SerializedObject(dur);
                dso.FindProperty("weight").enumValueIndex = (int)weight;
                dso.FindProperty("breakOnCleanHit").boolValue = weight == Weight.Light;
                dso.FindProperty("blocksBeforeBreaking").intValue = weight == Weight.Heavy ? 2 : 0;
                dso.FindProperty("breakCue").objectReferenceValue = breakCue;
                dso.ApplyModifiedPropertiesWithoutUndo();

                PrefabUtility.SaveAsPrefabAsset(go, path);
            }
            finally { Object.DestroyImmediate(go); }
        }

        /// <summary>The item's own hit numbers (Data/GDD/Item_Name_Hit), copied from the weight's template the first time.</summary>
        static ThrowableData HitDataFor(string itemName, Weight weight)
        {
            string path = $"{ItemDataFolder}/Item_{itemName}_Hit.asset";
            var existing = AssetDatabase.LoadAssetAtPath<ThrowableData>(path);
            if (existing) return existing;

            string template = weight == Weight.Light ? "Item_Plate_Hit" : weight == Weight.Medium ? "Item_Chair_Hit" : "Item_Pot_Hit";
            var source = AssetDatabase.LoadAssetAtPath<ThrowableData>($"{ItemDataFolder}/{template}.asset");
            ThrowableData asset;
            if (source) asset = Object.Instantiate(source);
            else { asset = ScriptableObject.CreateInstance<ThrowableData>(); asset.ApplyThrowDefaults(); }
            AssetDatabase.CreateAsset(asset, path);
            return asset;
        }

        static Object FindAssetByName(string name)
        {
            foreach (var guid in AssetDatabase.FindAssets(name))
            {
                string p = AssetDatabase.GUIDToAssetPath(guid);
                if (System.IO.Path.GetFileNameWithoutExtension(p) == name) return AssetDatabase.LoadAssetAtPath<Object>(p);
            }
            return null;
        }

        static void Folder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            int slash = path.LastIndexOf('/');
            Folder(path.Substring(0, slash));
            AssetDatabase.CreateFolder(path.Substring(0, slash), path.Substring(slash + 1));
        }

        /// <summary>
        /// Window → KungFu Crashout → Items → Add Throw Toss (selected items, or Item_Chair)
        /// Adds ItemThrowArc (the wind-up toss: up above the head, down into the arms, thrown on the release frame) to
        /// the item prefabs selected in the Project window, or to Item_Chair if none are selected. Already there = left as is.
        /// </summary>
        [MenuItem("Window/KungFu Crashout/Items/Add Throw Toss (selected items)", priority = 10080)]
        public static void AddThrowToss()
        {
            var paths = new List<string>();
            foreach (var o in Selection.objects)
            {
                string path = AssetDatabase.GetAssetPath(o);
                if (path.EndsWith(".prefab") && AssetDatabase.LoadAssetAtPath<Carryable>(path)) paths.Add(path);
            }
            if (paths.Count == 0)
                foreach (var guid in AssetDatabase.FindAssets("Item_Chair t:Prefab", new[] { "Assets/_Project/Prefabs" }))
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    if (System.IO.Path.GetFileNameWithoutExtension(path) == "Item_Chair") paths.Add(path);
                }
            if (paths.Count == 0) { EditorUtility.DisplayDialog("Throw Toss", "Select item prefabs (with a Carryable) first.", "OK"); return; }

            var done = new List<string>();
            foreach (var path in paths)
            {
                var root = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    if (root.GetComponent<ItemThrowArc>()) { done.Add($"{root.name} (already had it)"); continue; }
                    root.AddComponent<ItemThrowArc>();
                    PrefabUtility.SaveAsPrefabAsset(root, path);
                    done.Add(root.name);
                }
                finally { PrefabUtility.UnloadPrefabContents(root); }
            }
            Debug.Log("[Items] Throw toss on: " + string.Join(", ", done) + ". Tune it on the prefab's Item Throw Arc component.");
        }
    }
}
#endif
