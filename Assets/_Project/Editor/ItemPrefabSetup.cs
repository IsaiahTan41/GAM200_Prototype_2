#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
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
                    if (sr) sr.sprite = ground;
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
                        sr.sprite = ground;
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
