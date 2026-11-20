using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using KungFuCrashout.Fighters;
using KungFuCrashout.Props;

namespace KungFuCrashout.EditorTools
{
    /// <summary>
    /// Light / Heavy item split (M2D02 tuning base).
    ///
    ///   Window → KungFu Crashout → Items → Apply Light-Heavy Item Base
    ///       Sorts the Restaurant items into Light and Heavy (ItemDurability → Weight), creates two shared hit data
    ///       assets (Data/GDD/Item_Light_Hit, Item_Heavy_Hit), points every item at its class's asset and writes the
    ///       base values below onto the assets and the prefabs. Run it again at any time to return to the base.
    ///       The first run saves every item's previous values to Data/GDD/ItemWeight_PreSplit_Backup.json.
    ///
    ///   Window → KungFu Crashout → Items → Restore Pre-Split Item Values
    ///       Puts every item back on its old per-item hit data and its old prefab values (from the backup).
    ///       The old Item_*_Hit assets are never edited, so this is a full undo. Apply again to switch back.
    ///
    /// Only the values listed in LightBase / HeavyBase are written; every other field (sprites, sounds, launch
    /// angles, bounces) keeps what the asset or prefab already has.
    ///
    /// Place in: Assets/_Project/Editor/ItemWeightBaseSetup.cs
    /// </summary>
    public static class ItemWeightBaseSetup
    {
        const string Menu = "Window/KungFu Crashout/Items/";
        const string PrefabFolder = "Assets/_Project/Prefabs/ItemPrefab";
        const string DataFolder = "Assets/_Project/Data/GDD";
        const string LightHitPath = DataFolder + "/Item_Light_Hit.asset";
        const string HeavyHitPath = DataFolder + "/Item_Heavy_Hit.asset";
        const string BackupPath = DataFolder + "/ItemWeight_PreSplit_Backup.json";

        // Classification (prefab names in Prefabs/ItemPrefab).
        // For now only the Chair is heavy.
        static readonly string[] LightItems = { "Item_Plate", "Item_Bowl", "Item_Kettle", "Item_Lantern_Red", "Item_Lantern_White",
                                                "Item_Pot", "Item_Vase", "Item_Container" };
        static readonly string[] HeavyItems = { "Item_Chair" };

        // New shared assets start as a copy of these (keeps their angles, bounces and sounds).
        const string LightTemplate = DataFolder + "/Item_Plate_Hit.asset";
        const string HeavyTemplate = DataFolder + "/Item_Pot_Hit.asset";

        class ClassBase
        {
            // Hit data (ThrowableData)
            public float damage, hitstun, chipDamage;
            public HitWeight hitWeight;
            public float launchSpeed, launchGravity;
            public float downedTime, getUpInvulnerability;
            public bool? wallSplat;
            public float impactHitstop, impactShake;
            public float slowMotionScale, slowMotionTime;
            // Prefab (Carryable + ItemDurability)
            public float throwSpeed;
            public bool canMoveWhileHeld, breakOnShield;
            public int? maxHolds;
            public float wakeChance; // -1 = by weight (Light 70%, Heavy 30%)
            public ItemDurability.Weight durabilityWeight;
            public int blocksBeforeBreaking;
        }

        static readonly ClassBase LightBase = new ClassBase
        {
            damage = 0f, hitstun = 0.25f, chipDamage = 0f, hitWeight = HitWeight.Light,
            launchSpeed = 4.5f, launchGravity = 18f,
            downedTime = 0.45f, getUpInvulnerability = 0.2f,
            wallSplat = null,
            impactHitstop = 0.08f, impactShake = 0.12f,
            slowMotionScale = 0.43f, slowMotionTime = 0f, // time 0 = no slow motion
            throwSpeed = 21f, canMoveWhileHeld = true, breakOnShield = true, maxHolds = null,
            wakeChance = -1f, durabilityWeight = ItemDurability.Weight.Light, blocksBeforeBreaking = 0,
        };

        static readonly ClassBase HeavyBase = new ClassBase
        {
            damage = 5f, hitstun = 0.45f, chipDamage = 0.1f, hitWeight = HitWeight.Heavy,
            launchSpeed = 7f, launchGravity = 18f,
            downedTime = 2.75f, getUpInvulnerability = 0.4f,
            wallSplat = true,
            impactHitstop = 0.16f, impactShake = 0.3f,
            slowMotionScale = 0.35f, slowMotionTime = 0.18f,
            throwSpeed = 10f, canMoveWhileHeld = false, breakOnShield = false, maxHolds = 2,
            wakeChance = -1f, durabilityWeight = ItemDurability.Weight.Heavy, blocksBeforeBreaking = 2, // survives one block
        };

        // ---------------------------------------------------------------- Apply

        [MenuItem(Menu + "Apply Light-Heavy Item Base", priority = 10090)]
        static void Apply()
        {
            if (!File.Exists(BackupPath)) SaveBackup();

            var light = HitAsset(LightHitPath, LightTemplate, "Item_Light_Hit");
            var heavy = HitAsset(HeavyHitPath, HeavyTemplate, "Item_Heavy_Hit");
            if (!light || !heavy) return;
            WriteHit(light, LightBase);
            WriteHit(heavy, HeavyBase);

            var log = new List<string>();
            foreach (var n in LightItems) if (WritePrefab(n, light, LightBase)) log.Add($"Light  {n}");
            foreach (var n in HeavyItems) if (WritePrefab(n, heavy, HeavyBase)) log.Add($"Heavy  {n}");

            AssetDatabase.SaveAssets();
            Debug.Log("[Item Base] Light-Heavy base applied:\n  " + string.Join("\n  ", log) +
                      $"\nBackup of the previous values: {BackupPath}");
        }

        static ThrowableData HitAsset(string path, string templatePath, string name)
        {
            var asset = AssetDatabase.LoadAssetAtPath<ThrowableData>(path);
            if (asset) return asset;

            var template = AssetDatabase.LoadAssetAtPath<ThrowableData>(templatePath);
            asset = template ? UnityEngine.Object.Instantiate(template) : ScriptableObject.CreateInstance<ThrowableData>();
            asset.name = name;
            AssetDatabase.CreateAsset(asset, path);
            return asset;
        }

        static void WriteHit(ThrowableData d, ClassBase b)
        {
            Undo.RecordObject(d, "Apply item base");
            d.damage = b.damage;
            d.hitstun = b.hitstun;
            d.chipDamage = b.chipDamage;
            d.weight = b.hitWeight;
            d.launchSpeed = b.launchSpeed;
            d.launchGravity = b.launchGravity;
            d.downedTime = b.downedTime;
            d.getUpInvulnerability = b.getUpInvulnerability;
            if (b.wallSplat.HasValue) d.wallSplat = b.wallSplat.Value;
            d.impactHitstop = b.impactHitstop;
            d.impactShake = b.impactShake;
            d.slowMotionScale = b.slowMotionScale;
            d.slowMotionTime = b.slowMotionTime;
            EditorUtility.SetDirty(d);
        }

        static bool WritePrefab(string itemName, ThrowableData hit, ClassBase b)
        {
            if (!Load(itemName, out var carry, out var durability)) return false;

            var so = new SerializedObject(carry);
            so.FindProperty("hitData").objectReferenceValue = hit;
            so.FindProperty("throwSpeed").floatValue = b.throwSpeed;
            so.FindProperty("canMoveWhileHeld").boolValue = b.canMoveWhileHeld;
            so.FindProperty("breakOnShield").boolValue = b.breakOnShield;
            if (b.maxHolds.HasValue) so.FindProperty("maxHolds").intValue = b.maxHolds.Value;
            so.FindProperty("wakeChance").floatValue = b.wakeChance;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(carry);

            if (durability)
            {
                var dso = new SerializedObject(durability);
                dso.FindProperty("weight").enumValueIndex = (int)b.durabilityWeight;
                dso.FindProperty("blocksBeforeBreaking").intValue = b.blocksBeforeBreaking;
                dso.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(durability);
            }
            else Debug.LogWarning($"[Item Base] {itemName} has no ItemDurability — weight not set.");
            return true;
        }

        // ---------------------------------------------------------------- Backup / restore

        [Serializable] class ItemSnapshot
        {
            public string item;
            public string hitDataGuid;
            public float throwSpeed, wakeChance;
            public bool canMoveWhileHeld, breakOnShield;
            public int maxHolds;
            public bool hasDurability;
            public int durabilityWeight, blocksBeforeBreaking;
        }
        [Serializable] class Backup { public List<ItemSnapshot> items = new List<ItemSnapshot>(); }

        static void SaveBackup()
        {
            var backup = new Backup();
            foreach (var n in AllItems())
            {
                if (!Load(n, out var carry, out var durability)) continue;
                var so = new SerializedObject(carry);
                var hit = so.FindProperty("hitData").objectReferenceValue;
                var s = new ItemSnapshot
                {
                    item = n,
                    hitDataGuid = hit ? AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(hit)) : "",
                    throwSpeed = so.FindProperty("throwSpeed").floatValue,
                    wakeChance = so.FindProperty("wakeChance").floatValue,
                    canMoveWhileHeld = so.FindProperty("canMoveWhileHeld").boolValue,
                    breakOnShield = so.FindProperty("breakOnShield").boolValue,
                    maxHolds = so.FindProperty("maxHolds").intValue,
                    hasDurability = durability,
                };
                if (durability)
                {
                    var dso = new SerializedObject(durability);
                    s.durabilityWeight = dso.FindProperty("weight").enumValueIndex;
                    s.blocksBeforeBreaking = dso.FindProperty("blocksBeforeBreaking").intValue;
                }
                backup.items.Add(s);
            }
            File.WriteAllText(BackupPath, JsonUtility.ToJson(backup, true));
            AssetDatabase.ImportAsset(BackupPath);
        }

        [MenuItem(Menu + "Restore Pre-Split Item Values", priority = 10091)]
        static void Restore()
        {
            var backup = JsonUtility.FromJson<Backup>(File.ReadAllText(BackupPath));
            var log = new List<string>();
            foreach (var s in backup.items)
            {
                if (!Load(s.item, out var carry, out var durability)) continue;
                var so = new SerializedObject(carry);
                var hitPath = AssetDatabase.GUIDToAssetPath(s.hitDataGuid);
                so.FindProperty("hitData").objectReferenceValue =
                    string.IsNullOrEmpty(hitPath) ? null : AssetDatabase.LoadAssetAtPath<ThrowableData>(hitPath);
                so.FindProperty("throwSpeed").floatValue = s.throwSpeed;
                so.FindProperty("wakeChance").floatValue = s.wakeChance;
                so.FindProperty("canMoveWhileHeld").boolValue = s.canMoveWhileHeld;
                so.FindProperty("breakOnShield").boolValue = s.breakOnShield;
                so.FindProperty("maxHolds").intValue = s.maxHolds;
                so.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(carry);

                if (durability && s.hasDurability)
                {
                    var dso = new SerializedObject(durability);
                    dso.FindProperty("weight").enumValueIndex = s.durabilityWeight;
                    dso.FindProperty("blocksBeforeBreaking").intValue = s.blocksBeforeBreaking;
                    dso.ApplyModifiedPropertiesWithoutUndo();
                    EditorUtility.SetDirty(durability);
                }
                log.Add($"{s.item} → {(string.IsNullOrEmpty(hitPath) ? "(no hit data)" : Path.GetFileNameWithoutExtension(hitPath))}");
            }
            AssetDatabase.SaveAssets();
            Debug.Log("[Item Base] Restored the pre-split item values:\n  " + string.Join("\n  ", log));
        }

        [MenuItem(Menu + "Restore Pre-Split Item Values", true)]
        static bool CanRestore() => File.Exists(BackupPath);

        // ---------------------------------------------------------------- Helpers

        static IEnumerable<string> AllItems()
        {
            foreach (var n in LightItems) yield return n;
            foreach (var n in HeavyItems) yield return n;
        }

        static bool Load(string itemName, out Carryable carry, out ItemDurability durability)
        {
            durability = null;
            carry = null;
            var go = AssetDatabase.LoadAssetAtPath<GameObject>($"{PrefabFolder}/{itemName}.prefab");
            if (!go) { Debug.LogWarning($"[Item Base] Prefab not found: {PrefabFolder}/{itemName}.prefab"); return false; }
            carry = go.GetComponentInChildren<Carryable>(true);
            if (!carry) { Debug.LogWarning($"[Item Base] {itemName} has no Carryable."); return false; }
            durability = carry.GetComponent<ItemDurability>();
            return true;
        }
    }
}
