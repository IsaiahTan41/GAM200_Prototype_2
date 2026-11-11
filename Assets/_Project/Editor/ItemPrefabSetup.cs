#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using KungFuCrashout.Fighters;
using KungFuCrashout.Props;

namespace KungFuCrashout.EditorTools
{
    /// <summary>
    /// Window → KungFu Crashout → Items → Fit Item Prefabs To Their Art
    /// For every item prefab (a Carryable) under Assets/_Project/Prefabs that has a Ground Sprite:
    ///   • the SpriteRenderer shows the Ground Sprite (it was still the placeholder square from the crate)
    ///   • the placeholder tint is cleared to white (Prototype 3 tinted crates to fake plates / chairs / pots)
    ///   • the scale is made even (x kept, y matched), so the art isn't squashed by the crate's old stretch
    ///   • the tap collider fits the Ground Sprite
    /// Items without a Ground Sprite are left alone. Safe to re-run. Place in: Assets/_Project/Editor/ItemPrefabSetup.cs
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

        /// <summary>What the renderer shows in the editor: the Sleeping Sprite if set (that's how it sits where placed).</summary>
        static Sprite PreviewSprite(Carryable asset, Sprite ground)
        {
            var sl = new SerializedObject(asset).FindProperty("sleepingSprite")?.objectReferenceValue as Sprite;
            return sl ? sl : ground;
        }

        // ───────────── Scale items to the stage ─────────────

        const string ItemArtFolder = "Assets/_Project/Art/Items";
        /// <summary>
        /// The prop sprites in Art/Items were cut from the restaurant background at HALF its resolution (measured: the
        /// table, stool and jar are all 2.0× bigger in GamConcepts_Restaurant_08_WIP_2). So 1 prop pixel = 2 background
        /// pixels, and the props match the drawn ones when their Pixels Per Unit is half the background's on-screen one.
        /// </summary>
        const float PropToBackgroundResolution = 0.5f;
        // Background_Sprite as set up on 10 Nov (PPU 354.95, scale 0.8326): used if it can't be found in the open scene.
        const float FallbackBackgroundPixelsPerUnit = 354.95f / 0.8326f;

        /// <summary>
        /// Window → KungFu Crashout → Items → Scale Items To The Stage (open FightScene)
        ///   • every texture in Art/Items gets the same Pixels Per Unit, worked out from the background, so items are
        ///     exactly as big as the same props drawn in the restaurant (and new art dropped in that folder can be fitted
        ///     by running this again)
        ///   • item prefabs go back to Scale 1 (the size now comes from the art), tap collider fitted
        ///   • items placed in the open scene: Scale back to the prefab's, and they keep standing where they stood
        ///     (same bottom edge: a pot on a table stays on the table)
        /// Safe to re-run. Save the scene afterwards.
        /// </summary>
        [MenuItem("Window/KungFu Crashout/Items/Scale Items To The Stage (open scene)", priority = 10061)]
        public static void ScaleToStage()
        {
            float bgPpu = BackgroundPixelsPerUnit(out string bgSource);
            float itemPpu = bgPpu * PropToBackgroundResolution;
            var log = new List<string> { $"Background: {bgPpu:0.##} px per unit ({bgSource}) → items {itemPpu:0.##} px per unit" };

            // 1. Remember where each placed item's bottom edge is, before anything changes size.
            var placed = Object.FindObjectsByType<Carryable>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                               .Where(c => !EditorUtility.IsPersistent(c)).ToList();
            var bottoms = new Dictionary<Carryable, float>();
            foreach (var c in placed)
            {
                var sr = c.GetComponent<SpriteRenderer>();
                if (sr && sr.sprite) bottoms[c] = c.transform.position.y - sr.sprite.bounds.extents.y * Mathf.Abs(c.transform.lossyScale.y);
            }

            // 2. Item art: one Pixels Per Unit for all of it.
            int textures = 0;
            foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { ItemArtFolder }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!(AssetImporter.GetAtPath(path) is TextureImporter ti) || ti.textureType != TextureImporterType.Sprite) continue;
                if (Mathf.Abs(ti.spritePixelsPerUnit - itemPpu) < 0.01f) continue;
                ti.spritePixelsPerUnit = itemPpu;
                ti.SaveAndReimport();
                textures++;
            }
            log.Add($"{textures} item texture(s) in {ItemArtFolder} set to {itemPpu:0.##} Pixels Per Unit");

            // 3. Item prefabs: Scale 1, collider fitted to the ground sprite.
            foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/_Project/Prefabs" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var asset = AssetDatabase.LoadAssetAtPath<Carryable>(path);
                if (!asset) continue;
                var ground = new SerializedObject(asset).FindProperty("groundSprite")?.objectReferenceValue as Sprite;
                if (!ground || !AssetDatabase.GetAssetPath(ground).StartsWith(ItemArtFolder)) continue; // placeholder crates etc.

                var root = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    root.transform.localScale = Vector3.one;
                    var sr = root.GetComponent<SpriteRenderer>();
                    if (sr) sr.sprite = PreviewSprite(asset, ground);
                    if (root.TryGetComponent(out BoxCollider2D box))
                    {
                        box.size = ground.bounds.size;
                        box.offset = ground.bounds.center;
                    }
                    PrefabUtility.SaveAsPrefabAsset(root, path);
                }
                finally { PrefabUtility.UnloadPrefabContents(root); }
                log.Add($"{System.IO.Path.GetFileNameWithoutExtension(path)}: Scale 1, about {ground.bounds.size.x:0.##} × {ground.bounds.size.y:0.##} units");
            }

            // 4. Placed items: prefab scale, same bottom edge.
            int moved = 0;
            foreach (var c in placed)
            {
                if (!c) continue;
                var sr = c.GetComponent<SpriteRenderer>();
                if (!sr || !sr.sprite || !AssetDatabase.GetAssetPath(sr.sprite.texture).StartsWith(ItemArtFolder)) continue;
                Undo.RecordObject(c.transform, "Scale Items To The Stage");
                if (PrefabUtility.IsPartOfPrefabInstance(c.transform))
                {
                    var scale = new SerializedObject(c.transform).FindProperty("m_LocalScale");
                    PrefabUtility.RevertPropertyOverride(scale, InteractionMode.UserAction);
                }
                else c.transform.localScale = Vector3.one;

                if (bottoms.TryGetValue(c, out float bottom))
                {
                    var p = c.transform.position;
                    p.y = bottom + sr.sprite.bounds.extents.y * Mathf.Abs(c.transform.lossyScale.y);
                    c.transform.position = p;
                }
                moved++;
            }
            if (moved > 0)
            {
                EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
                log.Add($"{moved} placed item(s) in the open scene: prefab scale, same bottom edge (save the scene)");
            }

            Debug.Log("[Items] Scaled to the stage:\n  • " + string.Join("\n  • ", log));
        }

        /// <summary>
        /// Window → KungFu Crashout → Items → Add Throw Toss (selected items, or Item_Chair)
        /// Adds ItemThrowArc (the wind-up toss: up above the head, down into the arms, thrown on the release frame) to
        /// the item prefabs selected in the Project window, or to Item_Chair if none are selected. Already there = left as is.
        /// </summary>
        [MenuItem("Window/KungFu Crashout/Items/Add Throw Toss (selected items, or Item_Chair)", priority = 10062)]
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

        /// <summary>The background's on-screen pixels per world unit (its sprite's PPU ÷ its scale).</summary>
        static float BackgroundPixelsPerUnit(out string source)
        {
            foreach (var sr in Object.FindObjectsByType<SpriteRenderer>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (!sr.sprite) continue;
                bool isBackground = sr.name == "Background_Sprite" || sr.sprite.texture.name.StartsWith("GamConcepts_Restaurant");
                if (!isBackground) continue;
                float scale = Mathf.Abs(sr.transform.lossyScale.x);
                if (scale < 0.0001f) continue;
                source = $"'{sr.name}': {sr.sprite.pixelsPerUnit:0.##} PPU at scale {scale:0.####}";
                return sr.sprite.pixelsPerUnit / scale;
            }
            source = "Background_Sprite not in the open scene, used the 10 Nov set-up";
            return FallbackBackgroundPixelsPerUnit;
        }

        [MenuItem("Window/KungFu Crashout/Items/Fit Item Prefabs To Their Art", priority = 10060)]
        public static void FitAll()
        {
            var done = new List<string>();
            foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/_Project/Prefabs" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var asset = AssetDatabase.LoadAssetAtPath<Carryable>(path);
                if (!asset) continue;
                var ground = new SerializedObject(asset).FindProperty("groundSprite")?.objectReferenceValue as Sprite;
                if (!ground) continue;

                var root = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    var sr = root.GetComponent<SpriteRenderer>();
                    if (sr)
                    {
                        sr.sprite = PreviewSprite(asset, ground);
                        sr.color = Color.white;
                    }
                    var s = root.transform.localScale;
                    root.transform.localScale = new Vector3(s.x, Mathf.Abs(s.x) * Mathf.Sign(s.y == 0f ? 1f : s.y), s.z);
                    if (root.TryGetComponent(out BoxCollider2D box))
                    {
                        box.size = ground.bounds.size;
                        box.offset = ground.bounds.center;
                    }
                    PrefabUtility.SaveAsPrefabAsset(root, path);
                    Vector3 size = Vector3.Scale(ground.bounds.size, root.transform.localScale);
                    done.Add($"{System.IO.Path.GetFileNameWithoutExtension(path)}: {ground.name}, white, scale {root.transform.localScale.x:0.###} " +
                             $"(about {size.x:0.##} × {size.y:0.##} world units)");
                }
                finally { PrefabUtility.UnloadPrefabContents(root); }
            }

            Debug.Log(done.Count == 0
                ? "[Items] No item prefab with a Ground Sprite found under Assets/_Project/Prefabs."
                : "[Items] Fitted to their art:\n  • " + string.Join("\n  • ", done) +
                  "\nChange an item's size with its prefab's Scale (keep X and Y equal).");
        }
    }
}
#endif
