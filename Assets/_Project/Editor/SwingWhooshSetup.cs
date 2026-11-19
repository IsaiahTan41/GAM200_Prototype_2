#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using KungFuCrashout.Feedback;
using KungFuCrashout.Sound;

namespace KungFuCrashout.EditorTools
{
    /// <summary>
    /// Menu: Window → KungFu Crashout → Audio → Set Up Swing Whooshes
    ///   • Imports every .wav in Audio/SFX/Swings: mono, normalised, resampled to 48 kHz (the sources are 96 kHz),
    ///     decompress on load (needed to find each whoosh's peak), ADPCM.
    ///   • Builds Audio/Cues/SFX_SwingWhoosh: all of them, SEQUENTIAL (every swing plays the next one, then round again),
    ///     in this order: Wind Cool S01–S03, Airy Short S00–S01, Airy Windy Slow S00–S01. Volume 0.5 so the swing sits
    ///     under the impacts.
    ///   • Every HitFeedback in the open scene: Swing Whoosh Cue = it, Use Swing Whooshes on, Align Whoosh To Strike on.
    /// Every Monk swing (basic, heavy, air, specials) plays one, hit or miss; item throws keep their own whoosh.
    /// Place in: Assets/_Project/Editor/SwingWhooshSetup.cs
    /// </summary>
    public static class SwingWhooshSetup
    {
        const string Title = "Swing Whooshes";
        const string Folder = "Assets/_Project/Audio/SFX/Swings";
        const string CuePath = "Assets/_Project/Audio/Cues/SFX_SwingWhoosh.asset";
        static readonly string[] Order =
        {
            "Wind_Cool_Short_01_SND55199_S01", "Wind_Cool_Short_01_SND55199_S02", "Wind_Cool_Short_01_SND55199_S03",
            "Airy_Short_SND3623_S00", "Airy_Short_SND3623_S01",
            "Airy_Windy_Slow_Variations_SND121744_S00", "Airy_Windy_Slow_Variations_SND121744_S01",
        };

        [MenuItem("Window/KungFu Crashout/Audio/Set Up Swing Whooshes", priority = 10071)]
        public static void SetUp()
        {
            if (EditorApplication.isPlaying) { EditorUtility.DisplayDialog(Title, "Stop Play mode first.", "OK"); return; }
            if (!AssetDatabase.IsValidFolder(Folder)) { EditorUtility.DisplayDialog(Title, $"Missing {Folder}.", "OK"); return; }

            var clips = new List<AudioClip>();
            foreach (var guid in AssetDatabase.FindAssets("t:AudioClip", new[] { Folder }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var ai = AssetImporter.GetAtPath(path) as AudioImporter;
                if (ai)
                {
                    ai.forceToMono = true;
                    var s = ai.defaultSampleSettings;
                    s.loadType = AudioClipLoadType.DecompressOnLoad;
                    s.compressionFormat = AudioCompressionFormat.ADPCM;
                    s.sampleRateSetting = AudioSampleRateSetting.OverrideSampleRate;
                    s.sampleRateOverride = 48000;
                    s.preloadAudioData = true;
                    ai.defaultSampleSettings = s;
                    var so = new SerializedObject(ai);
                    var normalize = so.FindProperty("normalize");
                    if (normalize != null) { normalize.boolValue = true; so.ApplyModifiedPropertiesWithoutUndo(); }
                    ai.SaveAndReimport();
                }
                var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
                if (clip) clips.Add(clip);
            }
            if (clips.Count == 0) { EditorUtility.DisplayDialog(Title, $"No audio clips in {Folder}.", "OK"); return; }

            int Rank(AudioClip c)
            {
                for (int i = 0; i < Order.Length; i++) if (c.name.EndsWith(Order[i])) return i;
                return Order.Length;
            }
            clips = clips.OrderBy(Rank).ThenBy(c => c.name).ToList();

            var cue = AssetDatabase.LoadAssetAtPath<SfxCue>(CuePath);
            bool created = !cue;
            if (created) cue = ScriptableObject.CreateInstance<SfxCue>();
            cue.clips = clips.ToArray();
            cue.order = SfxCue.Order.Sequential;
            cue.volume = 0.5f;
            cue.volumeVariation = 0.1f;
            cue.pitch = 1f;
            cue.pitchVariation = 0.05f;
            cue.maxVoices = 3;
            cue.cooldown = 0.03f;
            cue.priority = 96; // under the impacts when voices run out
            if (created) AssetDatabase.CreateAsset(cue, CuePath);
            else EditorUtility.SetDirty(cue);
            AssetDatabase.SaveAssets();

            int wired = 0;
            foreach (var hf in Object.FindObjectsByType<HitFeedback>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                var so = new SerializedObject(hf);
                so.FindProperty("swingWhooshCue").objectReferenceValue = cue;
                so.FindProperty("useSwingWhooshes").boolValue = true;
                so.FindProperty("alignWhooshToStrike").boolValue = true;
                so.ApplyModifiedProperties();
                wired++;
            }
            if (wired > 0) EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            Debug.Log($"[Audio] {cue.name}: {string.Join(", ", cue.clips.Select(c => c.name))} (Sequential, volume {cue.volume}). " +
                      (wired > 0 ? $"Wired on {wired} HitFeedback. Save the scene (Ctrl+S)." : "Open FightScene and run again to wire it."));
        }
    }
}
#endif
