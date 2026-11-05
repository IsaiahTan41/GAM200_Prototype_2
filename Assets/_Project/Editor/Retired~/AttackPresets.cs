#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using KungFuCrashout.Fighters;
using Object = UnityEngine.Object;

namespace KungFuCrashout.EditorTools
{
    /// <summary>
    /// Adds "KungFu Crashout → Create Default Attacks + Assign to Fighters".
    /// Creates 4 starter AttackData assets and plugs them into every Fighter in the open scene.
    /// Running it again never overwrites the numbers you've already tuned — it only re-assigns them
    /// and fills in the M3 fields (weight, chip, blockstun, pushback) on attacks made before M3.
    /// REPLACES the M2 version of this file.
    /// Place in: Assets/_Project/Editor/AttackPresets.cs
    /// </summary>
    public static class AttackPresets
    {
        const string Folder = "Assets/_Project/Data/Attacks";

        [MenuItem("KungFu Crashout/Create Default Attacks + Assign to Fighters", priority = 40)]
        public static void CreateAndAssign()
        {
            EnsureFolder(Folder);

            // Light 1 — fast jab, starts the combo.
            var light1 = GetOrCreate("Light1_Jab", a =>
            {
                a.damage = 4f;
                a.startup = 0.07f; a.active = 0.07f; a.recovery = 0.15f;
                a.hitboxOffset = new Vector2(0.75f, 0.2f); a.hitboxSize = new Vector2(0.9f, 0.6f);
                a.knockback = 0.3f; a.hitstun = 0.3f; a.lunge = 0.15f;
            });

            // Light 2 — follow-up.
            var light2 = GetOrCreate("Light2_Strike", a =>
            {
                a.damage = 5f;
                a.startup = 0.08f; a.active = 0.08f; a.recovery = 0.17f;
                a.hitboxOffset = new Vector2(0.8f, 0f); a.hitboxSize = new Vector2(1f, 0.7f);
                a.knockback = 0.35f; a.hitstun = 0.32f; a.lunge = 0.2f;
            });

            // Light 3 — finisher with big knockback.
            var light3 = GetOrCreate("Light3_Finisher", a =>
            {
                a.damage = 9f;
                a.startup = 0.12f; a.active = 0.1f; a.recovery = 0.35f;
                a.hitboxOffset = new Vector2(0.9f, 0.1f); a.hitboxSize = new Vector2(1.2f, 0.9f);
                a.knockback = 1.8f; a.hitstun = 0.5f; a.lunge = 0.3f;
            });

            // Heavy — slow, strong, longer reach. Also usable as a combo finisher.
            var heavy = GetOrCreate("Heavy_PalmStrike", a =>
            {
                a.damage = 14f;
                a.startup = 0.25f; a.active = 0.1f; a.recovery = 0.4f;
                a.hitboxOffset = new Vector2(1f, 0.1f); a.hitboxSize = new Vector2(1.4f, 0.9f);
                a.knockback = 2.5f; a.hitstun = 0.6f; a.lunge = 0.4f;
            });

            // M3 fields. Weight is re-applied every run; the block numbers only fill in
            // values that are still at their defaults, so your tuning is kept.
            ApplyM3(light1, HitWeight.Light, chip: 0.1f, blockstun: 0.18f, pushback: 0.2f);
            ApplyM3(light2, HitWeight.Light, chip: 0.1f, blockstun: 0.24f, pushback: 0.25f);
            ApplyM3(light3, HitWeight.Heavy, chip: 0.15f, blockstun: 0.3f, pushback: 0.6f);
            ApplyM3(heavy, HitWeight.Heavy, chip: 0.2f, blockstun: 0.35f, pushback: 0.8f);

            AssetDatabase.SaveAssets();

            int count = 0;
            foreach (var fighter in Object.FindObjectsByType<Fighter>(FindObjectsSortMode.None))
            {
                var so = new SerializedObject(fighter);

                var combo = so.FindProperty("lightCombo");
                combo.arraySize = 3;
                combo.GetArrayElementAtIndex(0).objectReferenceValue = light1;
                combo.GetArrayElementAtIndex(1).objectReferenceValue = light2;
                combo.GetArrayElementAtIndex(2).objectReferenceValue = light3;
                so.FindProperty("heavyAttack").objectReferenceValue = heavy;

                so.ApplyModifiedProperties();
                count++;
            }

            if (count > 0) EditorSceneManager.MarkAllScenesDirty();
            Debug.Log($"[AttackPresets] Attacks ready in {Folder}. Assigned to {count} fighter(s). Remember to save the scene.");
        }

        static void ApplyM3(AttackData a, HitWeight weight, float chip, float blockstun, float pushback)
        {
            a.weight = weight;
            // Field defaults from AttackData: chipDamage 0.15, blockstun 0 (auto), blockPushback 0.25.
            if (Mathf.Approximately(a.chipDamage, 0.15f)) a.chipDamage = chip;
            if (a.blockstun <= 0f) a.blockstun = blockstun;
            if (Mathf.Approximately(a.blockPushback, 0.25f)) a.blockPushback = pushback;
            EditorUtility.SetDirty(a);
        }

        static AttackData GetOrCreate(string assetName, Action<AttackData> configure)
        {
            string path = $"{Folder}/{assetName}.asset";
            var existing = AssetDatabase.LoadAssetAtPath<AttackData>(path);
            if (existing) return existing; // keep your tuning

            var attack = ScriptableObject.CreateInstance<AttackData>();
            configure(attack);
            AssetDatabase.CreateAsset(attack, path);
            return attack;
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            int slash = path.LastIndexOf('/');
            string parent = path.Substring(0, slash);
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, path.Substring(slash + 1));
        }
    }
}
#endif
