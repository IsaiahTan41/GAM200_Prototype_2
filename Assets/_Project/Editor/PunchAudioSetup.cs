#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using KungFuCrashout.Feedback;
using KungFuCrashout.Fighters;
using KungFuCrashout.Sound;

namespace KungFuCrashout.EditorTools
{
    /// <summary>
    /// Menu: KungFu Crashout → Set Up Punch Audio
    ///
    /// 1. Offers to delete "__MACOSX" folders under Assets/_Project/Audio (Mac zip leftovers whose ._*.wav files
    ///    aren't audio and cause import errors).
    /// 2. Finds every AudioClip named "Punch_*" under Assets/_Project/Audio (kicks are ignored) and sets mobile import
    ///    settings: Force To Mono, Decompress On Load, ADPCM, Preload (short, frequent, must play instantly).
    /// 3. Creates/updates two Sfx Cues in Assets/_Project/Audio/Cues:
    ///      SFX_PunchLight — the short clips (≤ 0.5 s): jabs and combo hits
    ///      SFX_PunchHeavy — the longer, ringing clips: heavy hits, finishers, item impacts, wall splats
    ///    Re-running only refreshes the clip lists; your tuning (volume, pitch, limits) is kept.
    /// 4. Adds an "Audio" object with AudioManager to the open scene, and assigns the cues to HitFeedback
    ///    (adds HitFeedback to the Main Camera if the scene has none).
    /// Undoable for the scene part. Save the scene afterwards.
    /// Place in: Assets/_Project/Editor/PunchAudioSetup.cs
    /// </summary>
    public static class PunchAudioSetup
    {
        const string AudioRoot = "Assets/_Project/Audio";
        const string CueFolder = AudioRoot + "/Cues";
        const float HeavyIfLongerThan = 0.5f; // seconds

        [MenuItem("KungFu Crashout/Set Up Punch Audio", priority = 63)]
        public static void SetUp()
        {
            RemoveMacJunk();

            // ── Find the punch clips ──
            var punches = new List<AudioClip>();
            foreach (var guid in AssetDatabase.FindAssets("t:AudioClip", new[] { AudioRoot }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                string file = System.IO.Path.GetFileNameWithoutExtension(path);
                if (path.Contains("__MACOSX") || !file.StartsWith("Punch_")) continue;

                ConfigureImport(path);
                var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
                if (clip) punches.Add(clip);
            }

            if (punches.Count == 0)
            {
                EditorUtility.DisplayDialog("Set Up Punch Audio", $"No clips named Punch_* found under {AudioRoot}.", "OK");
                return;
            }

            punches = punches.OrderBy(c => c.name).ToList();
            var light = punches.Where(c => c.length <= HeavyIfLongerThan).ToArray();
            var heavy = punches.Where(c => c.length > HeavyIfLongerThan).ToArray();
            if (light.Length == 0) light = punches.ToArray(); // all one length: use every clip for both
            if (heavy.Length == 0) heavy = punches.ToArray();

            // ── Cues ──
            if (!AssetDatabase.IsValidFolder(CueFolder)) AssetDatabase.CreateFolder(AudioRoot, "Cues");
            var lightCue = MakeCue("SFX_PunchLight", light, volume: 0.9f, pitch: 1f, pitchVar: 0.06f, maxVoices: 3, priority: 64);
            var heavyCue = MakeCue("SFX_PunchHeavy", heavy, volume: 1f, pitch: 0.95f, pitchVar: 0.04f, maxVoices: 2, priority: 32);
            AssetDatabase.SaveAssets();

            // ── Scene: AudioManager + HitFeedback ──
            Undo.SetCurrentGroupName("Set Up Punch Audio");
            int group = Undo.GetCurrentGroup();

            var manager = Object.FindFirstObjectByType<AudioManager>();
            if (!manager)
            {
                var go = new GameObject("Audio");
                Undo.RegisterCreatedObjectUndo(go, "Create Audio");
                manager = Undo.AddComponent<AudioManager>(go);
            }

            var feedback = Object.FindFirstObjectByType<HitFeedback>();
            if (!feedback)
            {
                var cam = Camera.main;
                if (cam)
                {
                    feedback = Undo.AddComponent<HitFeedback>(cam.gameObject);
                    var fighters = Object.FindObjectsByType<Fighter>(FindObjectsSortMode.None);
                    var fso = new SerializedObject(feedback);
                    var list = fso.FindProperty("fighters");
                    list.arraySize = fighters.Length;
                    for (int i = 0; i < fighters.Length; i++) list.GetArrayElementAtIndex(i).objectReferenceValue = fighters[i];
                    fso.ApplyModifiedProperties();
                }
            }

            if (feedback)
            {
                var so = new SerializedObject(feedback);
                so.FindProperty("lightImpactCue").objectReferenceValue = lightCue;
                so.FindProperty("heavyImpactCue").objectReferenceValue = heavyCue;
                so.ApplyModifiedProperties();
            }

            Undo.CollapseUndoOperations(group);
            EditorSceneManager.MarkSceneDirty(manager.gameObject.scene);
            Selection.activeObject = lightCue;

            Debug.Log($"[Punch audio] {punches.Count} punch clips set to mono / ADPCM / decompress on load.\n" +
                      $"  SFX_PunchLight: {string.Join(", ", light.Select(c => c.name))}\n" +
                      $"  SFX_PunchHeavy: {string.Join(", ", heavy.Select(c => c.name))}\n" +
                      $"  AudioManager on '{manager.name}'. HitFeedback: {(feedback ? feedback.name : "NOT FOUND")}. Save the scene (Ctrl+S).");
        }

        static void ConfigureImport(string path)
        {
            var ai = AssetImporter.GetAtPath(path) as AudioImporter;
            if (!ai) return;

            ai.forceToMono = true;          // one-screen 2D game: stereo doubles memory for nothing
            ai.loadInBackground = false;

            var s = ai.defaultSampleSettings;
            s.loadType = AudioClipLoadType.DecompressOnLoad;  // short and frequent: decode once, play instantly
            s.compressionFormat = AudioCompressionFormat.ADPCM; // cheap to decode, keeps the punch transient crisp
            s.preloadAudioData = true;
            s.sampleRateSetting = AudioSampleRateSetting.PreserveSampleRate;
            ai.defaultSampleSettings = s;

            ai.SaveAndReimport();
        }

        static SfxCue MakeCue(string name, AudioClip[] clips, float volume, float pitch, float pitchVar, int maxVoices, int priority)
        {
            string path = $"{CueFolder}/{name}.asset";
            var cue = AssetDatabase.LoadAssetAtPath<SfxCue>(path);
            if (!cue)
            {
                cue = ScriptableObject.CreateInstance<SfxCue>();
                cue.order = SfxCue.Order.Shuffle;
                cue.volume = volume;
                cue.volumeVariation = 0.1f;
                cue.pitch = pitch;
                cue.pitchVariation = pitchVar;
                cue.maxVoices = maxVoices;
                cue.cooldown = 0.03f;
                cue.priority = priority;
                cue.clips = clips;
                AssetDatabase.CreateAsset(cue, path);
            }
            else
            {
                cue.clips = clips; // keep the user's tuning; refresh only the variations
                EditorUtility.SetDirty(cue);
            }
            return cue;
        }

        static void RemoveMacJunk()
        {
            var junk = AssetDatabase.FindAssets("__MACOSX", new[] { AudioRoot })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(p => AssetDatabase.IsValidFolder(p) && p.EndsWith("__MACOSX"))
                .Distinct().ToList();
            if (junk.Count == 0) return;

            bool delete = EditorUtility.DisplayDialog("Set Up Punch Audio",
                "Found Mac zip leftovers (not real audio, they cause import errors):\n\n" + string.Join("\n", junk) +
                "\n\nDelete them?", "Delete", "Keep");
            if (!delete) return;
            foreach (var p in junk) AssetDatabase.DeleteAsset(p);
        }
    }
}
#endif
