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
    /// Menu: KungFu Crashout → Set Up Movement Audio   (the dash / jump version of Set Up Punch Audio)
    ///
    /// 1. Finds every AudioClip named "Dash_*" under Assets/_Project/Audio (the cloth-swish movement sounds) and sets the
    ///    same mobile import settings as the punches: Force To Mono, Decompress On Load, ADPCM, Preload.
    /// 2. Creates / updates two Sfx Cues next to the clips:
    ///      SFX_Dash — all swishes, normal pitch
    ///      SFX_Jump — the same swishes, pitched up a little so a jump reads differently from a dash
    ///    Low priority (128), so a movement sound never steals a voice from a hit or an item whoosh.
    ///    Re-running only refreshes the clip lists; your tuning (volume, pitch, limits) is kept.
    /// 3. Makes sure the scene has an AudioManager, and adds FighterMovementSfx to every Fighter with the two cues
    ///    assigned (its Land Cue is left as it is).
    /// Undoable for the scene part. Save the scene afterwards.
    /// Place in: Assets/_Project/Editor/MovementAudioSetup.cs
    /// </summary>
    public static class MovementAudioSetup
    {
        const string AudioRoot = "Assets/_Project/Audio";
        const string ClipPrefix = "Dash_";

        [MenuItem("KungFu Crashout/Set Up Movement Audio", priority = 64)]
        public static void SetUp()
        {
            // ── Clips ──
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
                EditorUtility.DisplayDialog("Set Up Movement Audio", $"No clips named {ClipPrefix}* found under {AudioRoot}.", "OK");
                return;
            }
            var all = clips.OrderBy(c => c.name).ToArray();

            // ── Cues ──
            var dash = MakeCue("SFX_Dash", folder, all, volume: 0.7f, pitch: 1f, pitchVar: 0.08f);
            var jump = MakeCue("SFX_Jump", folder, all, volume: 0.65f, pitch: 1.1f, pitchVar: 0.06f);
            AssetDatabase.SaveAssets();

            // ── Scene ──
            Undo.SetCurrentGroupName("Set Up Movement Audio");
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
                var sfx = fighter.GetComponent<FighterMovementSfx>();
                if (!sfx) sfx = Undo.AddComponent<FighterMovementSfx>(fighter.gameObject);
                var so = new SerializedObject(sfx);
                so.FindProperty("dashCue").objectReferenceValue = dash;
                so.FindProperty("jumpCue").objectReferenceValue = jump;
                so.ApplyModifiedProperties();
                report += $"\n  {fighter.name}: dash + jump sounds";
            }

            Undo.CollapseUndoOperations(group);
            EditorSceneManager.MarkSceneDirty(manager.gameObject.scene);
            Selection.activeObject = dash;

            Debug.Log($"[Movement audio] {all.Length} clips set to mono / ADPCM / decompress on load " +
                      $"({string.Join(", ", all.Select(c => c.name))}).\n  Cues: {AssetDatabase.GetAssetPath(dash)}, {AssetDatabase.GetAssetPath(jump)}" +
                      $"{report}\n  AudioManager on '{manager.name}'. Save the scene (Ctrl+S).");
        }

        static void ConfigureImport(string path)
        {
            var ai = AssetImporter.GetAtPath(path) as AudioImporter;
            if (!ai) return;

            ai.forceToMono = true;          // 2D one-screen game: stereo only doubles memory
            ai.loadInBackground = false;

            var s = ai.defaultSampleSettings;
            s.loadType = AudioClipLoadType.DecompressOnLoad;   // short and frequent: decode once, play instantly
            s.compressionFormat = AudioCompressionFormat.ADPCM;
            s.preloadAudioData = true;
            s.sampleRateSetting = AudioSampleRateSetting.PreserveSampleRate;
            ai.defaultSampleSettings = s;

            ai.SaveAndReimport();
        }

        /// <summary>Reuse a cue with this name (unless it's the item whoosh by mistake), else create it in folder.</summary>
        static SfxCue MakeCue(string name, string folder, AudioClip[] clips, float volume, float pitch, float pitchVar)
        {
            SfxCue cue = null;
            foreach (var guid in AssetDatabase.FindAssets($"{name} t:SfxCue"))
            {
                var found = AssetDatabase.LoadAssetAtPath<SfxCue>(AssetDatabase.GUIDToAssetPath(guid));
                if (found && found.name == name) { cue = found; break; }
            }

            if (!cue)
            {
                cue = ScriptableObject.CreateInstance<SfxCue>();
                cue.order = SfxCue.Order.Shuffle;
                cue.volume = volume;
                cue.volumeVariation = 0.1f;
                cue.pitch = pitch;
                cue.pitchVariation = pitchVar;
                cue.maxVoices = 2;     // both fighters can move at once
                cue.cooldown = 0.05f;  // ignores double triggers
                cue.priority = 128;    // least important: never cuts off a hit or an item whoosh
                cue.clips = clips;
                AssetDatabase.CreateAsset(cue, $"{folder}/{name}.asset");
            }
            else
            {
                cue.clips = clips; // keep the user's tuning; refresh only the variations
                EditorUtility.SetDirty(cue);
            }
            return cue;
        }
    }
}
#endif
