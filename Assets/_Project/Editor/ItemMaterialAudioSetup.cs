#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using KungFuCrashout.Props;
using KungFuCrashout.Sound;

namespace KungFuCrashout.EditorTools
{
    /// <summary>
    /// Menu: Window → KungFu Crashout → Audio → Set Up Item Material Audio (Break + Land)
    /// Same pipeline as Set Up Item Audio (Throw + Break): clips in Audio/SFX/Items found by name prefix, given the mobile
    /// import settings (mono, ADPCM, decompress on load, preload), gathered into SfxCues (shuffle, small pitch / volume
    /// variation), then assigned on the item prefabs. Add more variants later (…_2.wav) and re-run: the clip lists refresh,
    /// your later tuning of each cue is kept.
    ///   ItemBreak_Pottery_* → SFX_ItemBreak_Pottery : Bowl, Plate, Pot, Vase, Container, Kettle
    ///   ItemBreak_Wood_*    → SFX_ItemBreak_Wood    : Chair, CrateItem
    ///   ItemBreak_Paper_*   → SFX_ItemBreak_Paper   : Lantern_Red, Lantern_White
    ///   ItemLand_Clatter_*  → SFX_ItemLand          : every item (Carryable → Land Cue) + StageItemDirector → Land Cue
    /// Break cues go on Carryable → Break Cue and ItemDurability → Break Cue (both places an item can break).
    /// Restore: puts SFX_ItemBreak (Smash) back on every item and clears the land cues. Save the scene afterwards.
    /// Place in: Assets/_Project/Editor/ItemMaterialAudioSetup.cs
    /// </summary>
    public static class ItemMaterialAudioSetup
    {
        const string Title = "Item Material Audio";
        const string Folder = "Assets/_Project/Audio/SFX/Items";
        const string PrefabRoot = "Assets/_Project/Prefabs";
        const string OldBreakCue = Folder + "/SFX_ItemBreak.asset";

        enum ItemMat { Pottery, Wood, Paper }

        static readonly Dictionary<string, ItemMat> ItemMaterial = new Dictionary<string, ItemMat>
        {
            { "Item_Bowl", ItemMat.Pottery }, { "Item_Plate", ItemMat.Pottery }, { "Item_Pot", ItemMat.Pottery },
            { "Item_Vase", ItemMat.Pottery }, { "Item_Container", ItemMat.Pottery }, { "Item_Kettle", ItemMat.Pottery },
            { "Item_Chair", ItemMat.Wood }, { "CrateItem", ItemMat.Wood },
            { "Item_Lantern_Red", ItemMat.Paper }, { "Item_Lantern_White", ItemMat.Paper },
        };

        [MenuItem("Window/KungFu Crashout/Audio/Set Up Item Material Audio (Break + Land)", priority = 10078)]
        static void SetUp()
        {
            if (EditorApplication.isPlaying) { EditorUtility.DisplayDialog(Title, "Stop Play mode first.", "OK"); return; }

            var cues = new Dictionary<ItemMat, SfxCue>
            {
                { ItemMat.Pottery, MakeCue("ItemBreak_Pottery_", "SFX_ItemBreak_Pottery", 0.9f, 2, 48) },
                { ItemMat.Wood,    MakeCue("ItemBreak_Wood_",    "SFX_ItemBreak_Wood",    0.95f, 2, 48) },
                { ItemMat.Paper,   MakeCue("ItemBreak_Paper_",   "SFX_ItemBreak_Paper",   1f,   2, 48) },
            };
            var land = MakeCue("ItemLand_Clatter_", "SFX_ItemLand", 0.6f, 3, 100);
            var missing = cues.Where(kv => !kv.Value).Select(kv => kv.Key.ToString()).ToList();
            if (!land) missing.Add("Land (ItemLand_Clatter_*)");
            if (missing.Count > 0)
            {
                EditorUtility.DisplayDialog(Title, $"No clips found in {Folder} for: {string.Join(", ", missing)}.", "OK");
                return;
            }

            string report = "";
            foreach (var kv in ItemMaterial)
                report += Assign(kv.Key, cues[kv.Value], land, $"{kv.Value} break + clatter");

            // Dropped-in items (StageItemDirector) land with the clatter too.
            int directors = 0;
            foreach (var d in Object.FindObjectsByType<StageItemDirector>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                var so = new SerializedObject(d);
                var p = so.FindProperty("landCue");
                if (p == null) continue;
                Undo.RecordObject(d, Title);
                p.objectReferenceValue = land;
                so.ApplyModifiedProperties();
                directors++;
            }
            if (directors > 0) EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            else report += "\n  (No StageItemDirector in the open scene: open FightScene and re-run for the drop-in clatter.)";

            AssetDatabase.SaveAssets();
            Debug.Log($"[{Title}] Done. {directors} StageItemDirector(s) → SFX_ItemLand. Save the scene.{report}");
        }

        [MenuItem("Window/KungFu Crashout/Audio/Restore Item Audio (single Smash break, no clatter)", priority = 10079)]
        static void Restore()
        {
            var smash = AssetDatabase.LoadAssetAtPath<SfxCue>(OldBreakCue);
            if (!smash) { EditorUtility.DisplayDialog(Title, $"{OldBreakCue} not found.", "OK"); return; }
            string report = "";
            foreach (var name in ItemMaterial.Keys) report += Assign(name, smash, null, "Smash break, no clatter");
            foreach (var d in Object.FindObjectsByType<StageItemDirector>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                var so = new SerializedObject(d);
                var p = so.FindProperty("landCue");
                if (p == null) continue;
                Undo.RecordObject(d, Title);
                p.objectReferenceValue = null;
                so.ApplyModifiedProperties();
                EditorSceneManager.MarkSceneDirty(d.gameObject.scene);
            }
            AssetDatabase.SaveAssets();
            Debug.Log($"[{Title}] Restored the single Smash break on every item, clatter off. Save the scene.{report}");
        }

        /// <summary>Set the break cue (Carryable + ItemDurability) and the land cue on one item prefab.</summary>
        static string Assign(string prefabName, SfxCue breakCue, SfxCue landCue, string what)
        {
            var guid = AssetDatabase.FindAssets($"{prefabName} t:Prefab", new[] { PrefabRoot })
                .FirstOrDefault(g => System.IO.Path.GetFileNameWithoutExtension(AssetDatabase.GUIDToAssetPath(g)) == prefabName);
            if (guid == null) return $"\n  {prefabName}: prefab not found (skipped)";
            var path = AssetDatabase.GUIDToAssetPath(guid);
            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                foreach (var c in root.GetComponentsInChildren<Carryable>(true))
                {
                    var so = new SerializedObject(c);
                    so.FindProperty("breakCue").objectReferenceValue = breakCue;
                    var lp = so.FindProperty("landCue");
                    if (lp != null) lp.objectReferenceValue = landCue;
                    so.ApplyModifiedPropertiesWithoutUndo();
                }
                foreach (var d in root.GetComponentsInChildren<ItemDurability>(true))
                {
                    var so = new SerializedObject(d);
                    so.FindProperty("breakCue").objectReferenceValue = breakCue;
                    so.ApplyModifiedPropertiesWithoutUndo();
                }
                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
            return $"\n  {prefabName}: {what}";
        }

        /// <summary>Clips named prefix* in the Items folder → cue (created once with these settings; later only the clip list refreshes).</summary>
        static SfxCue MakeCue(string prefix, string cueName, float volume, int voices, int priority)
        {
            var clips = new List<AudioClip>();
            foreach (var g in AssetDatabase.FindAssets("t:AudioClip", new[] { Folder }))
            {
                var path = AssetDatabase.GUIDToAssetPath(g);
                if (!System.IO.Path.GetFileNameWithoutExtension(path).StartsWith(prefix)) continue;
                ConfigureImport(path);
                var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
                if (clip) clips.Add(clip);
            }
            if (clips.Count == 0) return null;

            string cuePath = $"{Folder}/{cueName}.asset";
            var cue = AssetDatabase.LoadAssetAtPath<SfxCue>(cuePath);
            if (!cue)
            {
                cue = ScriptableObject.CreateInstance<SfxCue>();
                cue.order = SfxCue.Order.Shuffle;
                cue.volume = volume;
                cue.volumeVariation = 0.08f;
                cue.pitch = 1f;
                cue.pitchVariation = 0.06f;
                cue.maxVoices = voices;
                cue.cooldown = 0.05f;
                cue.priority = priority; // breaks like a hit (48); the clatter below the hits and whooshes
                AssetDatabase.CreateAsset(cue, cuePath);
            }
            cue.clips = clips.OrderBy(c => c.name).ToArray();
            EditorUtility.SetDirty(cue);
            return cue;
        }

        static void ConfigureImport(string path)
        {
            var ai = AssetImporter.GetAtPath(path) as AudioImporter;
            if (!ai) return;
            ai.forceToMono = true;
            ai.loadInBackground = false;
            var s = ai.defaultSampleSettings;
            s.loadType = AudioClipLoadType.DecompressOnLoad;
            s.compressionFormat = AudioCompressionFormat.ADPCM;
            s.preloadAudioData = true;
            s.sampleRateSetting = AudioSampleRateSetting.PreserveSampleRate;
            ai.defaultSampleSettings = s;
            ai.SaveAndReimport();
        }
    }
}
#endif
