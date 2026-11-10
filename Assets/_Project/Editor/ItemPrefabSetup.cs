#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
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
