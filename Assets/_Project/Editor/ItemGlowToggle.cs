using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using KungFuCrashout.Props;

namespace KungFuCrashout.EditorTools
{
    /// <summary>
    /// Testing switch for the item highlight: soft 2D light glow (white asleep / light blue in play / yellow grab)
    /// or the original outlines (grey / green / white). Flips "Use Light Glow" on every ItemHighlight in the project's
    /// prefabs and in the open scene. Nothing else changes, so it can be flipped back and forth freely.
    /// Place in: Assets/_Project/Editor/ItemGlowToggle.cs
    /// </summary>
    public static class ItemGlowToggle
    {
        [MenuItem("Window/KungFu Crashout/Items/Use Light Glow (2D lights)", priority = 10400)]
        static void GlowOn() => Set(true);

        [MenuItem("Window/KungFu Crashout/Items/Use Outlines (original)", priority = 10401)]
        static void GlowOff() => Set(false);

        static void Set(bool on)
        {
            int prefabs = 0, sceneObjs = 0;
            foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/_Project" }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var root = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (!root) continue;
                foreach (var h in root.GetComponentsInChildren<ItemHighlight>(true))
                    if (Apply(h, on)) { EditorUtility.SetDirty(h); prefabs++; }
            }
            AssetDatabase.SaveAssets();

            foreach (var h in Object.FindObjectsByType<ItemHighlight>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (EditorUtility.IsPersistent(h)) continue;
                var so = new SerializedObject(h);
                var prop = so.FindProperty("useLightGlow");
                if (prop == null) continue;
                if (prop.prefabOverride || !PrefabUtility.IsPartOfPrefabInstance(h))
                {
                    Undo.RecordObject(h, "Item glow toggle");
                    prop.boolValue = on; so.ApplyModifiedProperties(); sceneObjs++;
                }
            }
            if (sceneObjs > 0) EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());

            Debug.Log($"[Item Glow] {(on ? "2D light glow ON" : "original outlines ON")} — {prefabs} prefab(s), {sceneObjs} scene object(s) changed.");
        }

        static bool Apply(ItemHighlight h, bool on)
        {
            var so = new SerializedObject(h);
            var prop = so.FindProperty("useLightGlow");
            if (prop == null || prop.boolValue == on) return false;
            prop.boolValue = on;
            so.ApplyModifiedPropertiesWithoutUndo();
            return true;
        }
    }
}
