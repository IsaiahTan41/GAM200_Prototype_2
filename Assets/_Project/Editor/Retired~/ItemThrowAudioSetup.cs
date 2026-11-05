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
    /// Menu: KungFu Crashout → Set Up Item Audio (Throw + Break)
    ///   Also sets up the BREAK sound: "Smash_*" clips → cue SFX_ItemBreak (priority 48, like a hit), assigned to
    ///   Carryable → Break Cue. Played only when an item breaks (blocked without a catch, or its last throw ends),
    ///   never on pick-up or catch.
    ///
    /// The "Whoosh_*" clips are the sound of a thrown item flying through the air. This menu:
    /// 1. Moves Audio/SFX/Movement → Audio/SFX/Items (inside Unity, so every .meta / reference survives).
    /// 2. Sets the punch-style mobile import settings on every Whoosh_* clip (mono, ADPCM, decompress on load, preload).
    /// 3. Makes the cue SFX_ItemThrow: renames the old SFX_Dash (keeping its asset) or creates it, and tunes it for throws
    ///    (volume 0.8, pitch 1 ± 8%, 3 voices for several crates in the air, priority 96 = below the impact hits).
    ///    Deletes the old SFX_Jump cue. Re-running only refreshes the clip list; your later tuning is kept.
    /// 4. Clears SFX_Dash / SFX_Jump from FighterMovementSfx on the fighters, and removes that component when it has no
    ///    cues left (it was added for these clips by mistake).
    /// 5. Assigns SFX_ItemThrow to Carryable → Throw Cue on the CrateItem prefab (and any non-prefab Carryable in the scene).
    /// Undoable for the scene part. Save the scene afterwards.
    /// Place in: Assets/_Project/Editor/ItemThrowAudioSetup.cs
    /// </summary>
    public static class ItemThrowAudioSetup
    {
        const string AudioRoot = "Assets/_Project/Audio";
        const string OldFolder = AudioRoot + "/SFX/Movement";
        const string NewFolder = AudioRoot + "/SFX/Items";
        const string ClipPrefix = "Whoosh_";
        const string CueName = "SFX_ItemThrow";
        const string BreakPrefix = "Smash_";
        const string BreakCueName = "SFX_ItemBreak";
        const string PrefabFolder = "Assets/_Project/Prefabs";

        [MenuItem("KungFu Crashout/Set Up Item Audio (Throw + Break)", priority = 63)]
        public static void SetUp()
        {
            // ── 1. Folder: Movement → Items ──
            // Only when Movement still holds the whooshes and nothing else (it now holds the real dash / jump sounds).
            if (AssetDatabase.IsValidFolder(OldFolder) && !AssetDatabase.IsValidFolder(NewFolder) && FolderIsOnlyWhooshes(OldFolder))
            {
                string err = AssetDatabase.MoveAsset(OldFolder, NewFolder);
                if (!string.IsNullOrEmpty(err)) Debug.LogWarning($"[Item throw audio] Couldn't move {OldFolder}: {err}");
            }

            // ── 2. Clips ──
            var clips = new List<AudioClip>();
            string folder = null;
            foreach (var guid in AssetDatabase.FindAssets("t:AudioClip", new[] { AudioRoot }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (path.Contains("__MACOSX") || !System.IO.Path.GetFileNameWithoutExtension(path).StartsWith(ClipPrefix)) continue;
                ConfigureImport(path);
                var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
                if (!clip) continue;
                clips.Add(clip);
                folder ??= System.IO.Path.GetDirectoryName(path).Replace('\\', '/');
            }
            if (clips.Count == 0)
            {
                EditorUtility.DisplayDialog("Set Up Item Throw Audio", $"No clips named {ClipPrefix}* found under {AudioRoot}.", "OK");
                return;
            }
            var all = clips.OrderBy(c => c.name).ToArray();

            // ── 3. Cue ──
            // The old dash / jump cues only count as "the mistake" if they actually hold the whoosh clips;
            // the real movement cues (Dash_* clips, Set Up Movement Audio) are left alone.
            var oldDash = FindCue("SFX_Dash");
            var oldJump = FindCue("SFX_Jump");
            if (!IsWhooshCue(oldDash)) oldDash = null;
            if (!IsWhooshCue(oldJump)) oldJump = null;
            var cue = FindCue(CueName);
            bool fresh = false;

            if (!cue && oldDash)
            {
                AssetDatabase.RenameAsset(AssetDatabase.GetAssetPath(oldDash), CueName);
                cue = oldDash;
                oldDash = null;
                fresh = true; // was tuned for dashes: retune for throws
            }
            if (!cue)
            {
                cue = ScriptableObject.CreateInstance<SfxCue>();
                AssetDatabase.CreateAsset(cue, $"{folder}/{CueName}.asset");
                fresh = true;
            }
            if (fresh)
            {
                cue.order = SfxCue.Order.Shuffle;
                cue.volume = 0.8f;
                cue.volumeVariation = 0.1f;
                cue.pitch = 1f;
                cue.pitchVariation = 0.08f;
                cue.maxVoices = 3;      // several crates can be in the air at once
                cue.cooldown = 0.05f;
                cue.priority = 96;      // above whooshes / footsteps, below impact hits (32–64)
            }
            cue.clips = all;
            EditorUtility.SetDirty(cue);

            // ── 4. Unhook the fighters (scene) before deleting the jump cue ──
            Undo.SetCurrentGroupName("Set Up Item Throw Audio");
            int group = Undo.GetCurrentGroup();

            string report = "";
            foreach (var sfx in Object.FindObjectsByType<FighterMovementSfx>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                var so = new SerializedObject(sfx);
                bool any = false;
                foreach (var field in new[] { "dashCue", "jumpCue", "landCue" })
                {
                    var p = so.FindProperty(field);
                    if (p == null) continue;
                    var c = p.objectReferenceValue as SfxCue;
                    // The whoosh cues (whatever their name now) come off the fighters.
                    if (c && (c == cue || c == oldJump || c == oldDash)) p.objectReferenceValue = null;
                    if (p.objectReferenceValue) any = true;
                }
                so.ApplyModifiedProperties();

                if (!any)
                {
                    report += $"\n  {sfx.name}: removed FighterMovementSfx (no movement sounds left)";
                    Undo.DestroyObjectImmediate(sfx);
                }
                else report += $"\n  {sfx.name}: whooshes cleared from FighterMovementSfx (other cues kept)";
            }

            if (oldJump) AssetDatabase.DeleteAsset(AssetDatabase.GetAssetPath(oldJump));
            if (oldDash) AssetDatabase.DeleteAsset(AssetDatabase.GetAssetPath(oldDash));

            // ── 4b. Break sound: Smash_* clips → SFX_ItemBreak (only played when an item breaks) ──
            var smashes = new List<AudioClip>();
            foreach (var guid in AssetDatabase.FindAssets("t:AudioClip", new[] { AudioRoot }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (path.Contains("__MACOSX") || !System.IO.Path.GetFileNameWithoutExtension(path).StartsWith(BreakPrefix)) continue;
                ConfigureImport(path);
                var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
                if (clip) smashes.Add(clip);
            }
            SfxCue breakCue = null;
            if (smashes.Count > 0)
            {
                breakCue = FindCue(BreakCueName);
                if (!breakCue)
                {
                    breakCue = ScriptableObject.CreateInstance<SfxCue>();
                    breakCue.order = SfxCue.Order.Shuffle;
                    breakCue.volume = 0.9f;
                    breakCue.volumeVariation = 0.08f;
                    breakCue.pitch = 1f;
                    breakCue.pitchVariation = 0.06f;
                    breakCue.maxVoices = 2;
                    breakCue.cooldown = 0.05f;
                    breakCue.priority = 48; // as important as a hit: a smash must always be heard
                    string smashFolder = System.IO.Path.GetDirectoryName(AssetDatabase.GetAssetPath(smashes[0])).Replace('\\', '/');
                    AssetDatabase.CreateAsset(breakCue, $"{smashFolder}/{BreakCueName}.asset");
                }
                breakCue.clips = smashes.OrderBy(c => c.name).ToArray();
                EditorUtility.SetDirty(breakCue);
            }

            // ── 5. Assign to the items ──
            int prefabs = 0;
            foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { PrefabFolder }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var item = AssetDatabase.LoadAssetAtPath<GameObject>(path)?.GetComponent<Carryable>();
                if (!item) continue;
                var so = new SerializedObject(item);
                so.FindProperty("throwCue").objectReferenceValue = cue;
                if (breakCue) so.FindProperty("breakCue").objectReferenceValue = breakCue;
                so.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(item);
                prefabs++;
                report += $"\n  Prefab {System.IO.Path.GetFileNameWithoutExtension(path)}: Throw Cue = {CueName}" +
                          (breakCue ? $", Break Cue = {BreakCueName}" : "");
            }

            // Scene items that aren't prefab instances (prefab instances get it from the prefab).
            foreach (var item in Object.FindObjectsByType<Carryable>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (PrefabUtility.IsPartOfPrefabInstance(item)) continue;
                var so = new SerializedObject(item);
                var p = so.FindProperty("throwCue");
                var b = so.FindProperty("breakCue");
                bool changed = false;
                if (!p.objectReferenceValue) { p.objectReferenceValue = cue; changed = true; }
                if (breakCue && b != null && !b.objectReferenceValue) { b.objectReferenceValue = breakCue; changed = true; }
                if (!changed) continue;
                so.ApplyModifiedProperties();
                report += $"\n  Scene item {item.name}: throw / break cues set";
            }

            AssetDatabase.SaveAssets();
            Undo.CollapseUndoOperations(group);
            EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
            Selection.activeObject = cue;

            Debug.Log($"[Item audio] {all.Length} whoosh clips → {AssetDatabase.GetAssetPath(cue)} (played when an item is thrown)" +
                      (breakCue ? $"; {breakCue.clips.Length} smash clips → {AssetDatabase.GetAssetPath(breakCue)} (played when an item breaks)."
                                : $"; no {BreakPrefix}* clips found, so no break sound.") +
                      $"{report}" +
                      (prefabs == 0 ? $"\n  No Carryable prefab found in {PrefabFolder}: set Throw Cue on your items by hand." : "") +
                      "\n  Save the scene (Ctrl+S).");
        }

        static bool IsWhooshCue(SfxCue c) =>
            c && c.clips != null && c.clips.Length > 0 && c.clips.All(clip => clip && clip.name.StartsWith(ClipPrefix));

        static bool FolderIsOnlyWhooshes(string folder)
        {
            bool any = false;
            foreach (var guid in AssetDatabase.FindAssets("t:AudioClip", new[] { folder }))
            {
                string n = System.IO.Path.GetFileNameWithoutExtension(AssetDatabase.GUIDToAssetPath(guid));
                if (!n.StartsWith(ClipPrefix)) return false;
                any = true;
            }
            return any;
        }

        static SfxCue FindCue(string name)
        {
            foreach (var guid in AssetDatabase.FindAssets($"{name} t:SfxCue"))
            {
                var c = AssetDatabase.LoadAssetAtPath<SfxCue>(AssetDatabase.GUIDToAssetPath(guid));
                if (c && c.name == name) return c;
            }
            return null;
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
