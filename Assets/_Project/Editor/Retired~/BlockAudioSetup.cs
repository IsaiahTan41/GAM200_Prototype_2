#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using KungFuCrashout.Fighters;
using KungFuCrashout.Sound;

namespace KungFuCrashout.EditorTools
{
    /// <summary>
    /// Menu: KungFu Crashout → Set Up Defence Audio (Block, Catch, Guard Break)   (same pipeline as Set Up Punch Audio)
    ///   Also: "GuardBreak_*" clips → cue SFX_GuardBreak (volume 0.6 — the stinger is ~10 dB hotter than the other SFX —
    ///   one voice, 0.5 s cooldown, priority 16 = the most important sound), assigned to FighterDefenseSfx → Stagger Cue:
    ///   played on a guard break and on a clean thrown-item hit that staggers the fighter.
    ///
    /// 1. Finds every AudioClip named "Block_*" under Assets/_Project/Audio and sets mobile import settings: Force To Mono,
    ///    Decompress On Load, ADPCM, Preload, sample rate 48 kHz (the source files are 96 kHz; halving it saves memory with
    ///    no audible difference on a phone speaker). The files were already boosted to a -0.5 dBFS peak.
    /// 2. Creates / updates the cue SFX_Block next to the clips: volume 1.0 (the loudest cue), pitch 1 ± 5%,
    ///    3 voices, priority 32 (as important as the heaviest hit, so a block is never cut off).
    ///    Re-running only refreshes the clip list; your tuning is kept.
    /// 3. Makes sure the scene has an AudioManager, and adds FighterDefenseSfx to every Fighter with SFX_Block as both
    ///    its Block Cue (manual block, auto-block, blocked items) and Catch Cue (catching a thrown item).
    /// Undoable for the scene part. Save the scene afterwards.
    /// Place in: Assets/_Project/Editor/BlockAudioSetup.cs
    /// </summary>
    public static class BlockAudioSetup
    {
        const string AudioRoot = "Assets/_Project/Audio";
        const string ClipPrefix = "Block_";
        const string CueName = "SFX_Block";
        const string StaggerPrefix = "GuardBreak_";
        const string StaggerCueName = "SFX_GuardBreak";

        [MenuItem("KungFu Crashout/Set Up Defence Audio (Block, Catch, Guard Break)", priority = 63)]
        public static void SetUp()
        {
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
                EditorUtility.DisplayDialog("Set Up Block Audio", $"No clips named {ClipPrefix}* found under {AudioRoot}.", "OK");
                return;
            }
            var all = clips.OrderBy(c => c.name).ToArray();

            // ── Cue ──
            SfxCue cue = null;
            foreach (var guid in AssetDatabase.FindAssets($"{CueName} t:SfxCue"))
            {
                var found = AssetDatabase.LoadAssetAtPath<SfxCue>(AssetDatabase.GUIDToAssetPath(guid));
                if (found && found.name == CueName) { cue = found; break; }
            }
            if (!cue)
            {
                cue = ScriptableObject.CreateInstance<SfxCue>();
                cue.order = SfxCue.Order.Shuffle;
                cue.volume = 1f;            // the loudest cue (punches 0.9–1.0, items 0.8–0.9, movement 0.65–0.7)
                cue.volumeVariation = 0.05f;
                cue.pitch = 1f;
                cue.pitchVariation = 0.05f;
                cue.maxVoices = 3;          // blocked combo strings overlap
                cue.cooldown = 0.04f;
                cue.priority = 32;          // same as the heaviest hit
                cue.clips = all;
                AssetDatabase.CreateAsset(cue, $"{folder}/{CueName}.asset");
            }
            else
            {
                cue.clips = all;
                EditorUtility.SetDirty(cue);
            }
            // ── Guard break / stagger stinger ──
            var stingers = new List<AudioClip>();
            foreach (var guid in AssetDatabase.FindAssets("t:AudioClip", new[] { AudioRoot }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (path.Contains("__MACOSX") || !System.IO.Path.GetFileNameWithoutExtension(path).StartsWith(StaggerPrefix)) continue;
                ConfigureImport(path);
                var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
                if (clip) stingers.Add(clip);
            }
            SfxCue staggerCue = null;
            if (stingers.Count > 0)
            {
                foreach (var guid in AssetDatabase.FindAssets($"{StaggerCueName} t:SfxCue"))
                {
                    var found = AssetDatabase.LoadAssetAtPath<SfxCue>(AssetDatabase.GUIDToAssetPath(guid));
                    if (found && found.name == StaggerCueName) { staggerCue = found; break; }
                }
                if (!staggerCue)
                {
                    staggerCue = ScriptableObject.CreateInstance<SfxCue>();
                    staggerCue.order = SfxCue.Order.Shuffle;
                    staggerCue.volume = 0.6f;       // the stinger is much hotter than the other clips
                    staggerCue.volumeVariation = 0f;
                    staggerCue.pitch = 1f;
                    staggerCue.pitchVariation = 0.03f;
                    staggerCue.maxVoices = 1;       // one stinger at a time (it's ~5 s long)
                    staggerCue.cooldown = 0.5f;     // a guard break + item hit in the same moment plays once
                    staggerCue.priority = 16;       // the most important sound in a fight
                    string f = System.IO.Path.GetDirectoryName(AssetDatabase.GetAssetPath(stingers[0])).Replace('\\', '/');
                    AssetDatabase.CreateAsset(staggerCue, $"{f}/{StaggerCueName}.asset");
                }
                staggerCue.clips = stingers.OrderBy(c => c.name).ToArray();
                EditorUtility.SetDirty(staggerCue);
            }
            AssetDatabase.SaveAssets();

            // ── Scene ──
            Undo.SetCurrentGroupName("Set Up Block Audio");
            int group = Undo.GetCurrentGroup();

            var manager = Object.FindFirstObjectByType<AudioManager>();
            if (!manager)
            {
                var go = new GameObject("Audio");
                Undo.RegisterCreatedObjectUndo(go, "Create Audio");
                manager = Undo.AddComponent<AudioManager>(go);
            }

            string report = "";
            foreach (var fighter in Object.FindObjectsByType<Fighter>(FindObjectsSortMode.None))
            {
                var sfx = fighter.GetComponent<FighterDefenseSfx>();
                if (!sfx) sfx = Undo.AddComponent<FighterDefenseSfx>(fighter.gameObject);
                var so = new SerializedObject(sfx);
                so.FindProperty("blockCue").objectReferenceValue = cue;
                so.FindProperty("catchCue").objectReferenceValue = cue;
                if (staggerCue) so.FindProperty("staggerCue").objectReferenceValue = staggerCue;
                so.ApplyModifiedProperties();
                report += $"\n  {fighter.name}: block + catch sounds" + (staggerCue ? " + guard-break stinger" : "");
            }

            Undo.CollapseUndoOperations(group);
            EditorSceneManager.MarkSceneDirty(manager.gameObject.scene);
            Selection.activeObject = cue;

            Debug.Log($"[Block audio] {all.Length} clips set to mono / ADPCM / 48 kHz ({string.Join(", ", all.Select(c => c.name))}) " +
                      $"→ {AssetDatabase.GetAssetPath(cue)}{report}\n  Save the scene (Ctrl+S).");
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
            s.sampleRateSetting = AudioSampleRateSetting.OverrideSampleRate;
            s.sampleRateOverride = 48000;
            ai.defaultSampleSettings = s;
            ai.SaveAndReimport();
        }
    }
}
#endif
