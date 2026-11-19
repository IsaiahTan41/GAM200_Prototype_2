#if UNITY_EDITOR
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using KungFuCrashout.Fighters;
using KungFuCrashout.Sound;

namespace KungFuCrashout.EditorTools
{
    /// <summary>
    /// Menu: Window → KungFu Crashout → Audio → Set Up Hit Vocals
    ///   • Imports every clip in Audio/SFX/HitVocals (mono, normalised, decompress on load, Vorbis: kinder to voices than ADPCM).
    ///   • Builds Audio/Cues/SFX_HitVocals: shuffled (never the same vocal twice in a row), one voice at a time, small pitch variation.
    ///   • Adds FighterHitVocals to every Fighter in the open scene (both Monks) with that cue. Spacing settings live on it.
    /// Place in: Assets/_Project/Editor/HitVocalsSetup.cs
    /// </summary>
    public static class HitVocalsSetup
    {
        const string Title = "Hit Vocals";
        const string Folder = "Assets/_Project/Audio/SFX/HitVocals";
        const string CuePath = "Assets/_Project/Audio/Cues/SFX_HitVocals.asset";

        [MenuItem("Window/KungFu Crashout/Audio/Set Up Hit Vocals", priority = 10073)]
        public static void SetUp()
        {
            if (EditorApplication.isPlaying) { EditorUtility.DisplayDialog(Title, "Stop Play mode first.", "OK"); return; }
            if (!AssetDatabase.IsValidFolder(Folder)) { EditorUtility.DisplayDialog(Title, $"Missing {Folder}.", "OK"); return; }

            var clips = AssetDatabase.FindAssets("t:AudioClip", new[] { Folder })
                .Select(AssetDatabase.GUIDToAssetPath).OrderBy(p => p).Select(path =>
                {
                    var ai = AssetImporter.GetAtPath(path) as AudioImporter;
                    if (ai)
                    {
                        ai.forceToMono = true;
                        var s = ai.defaultSampleSettings;
                        s.loadType = AudioClipLoadType.DecompressOnLoad;
                        s.compressionFormat = AudioCompressionFormat.Vorbis;
                        s.quality = 0.7f;
                        s.preloadAudioData = true;
                        ai.defaultSampleSettings = s;
                        var so = new SerializedObject(ai);
                        var normalize = so.FindProperty("normalize");
                        if (normalize != null) { normalize.boolValue = true; so.ApplyModifiedPropertiesWithoutUndo(); }
                        ai.SaveAndReimport();
                    }
                    return AssetDatabase.LoadAssetAtPath<AudioClip>(path);
                }).Where(c => c).ToArray();
            if (clips.Length == 0) { EditorUtility.DisplayDialog(Title, $"No audio clips in {Folder}.", "OK"); return; }

            var cue = AssetDatabase.LoadAssetAtPath<SfxCue>(CuePath);
            bool created = !cue;
            if (created) cue = ScriptableObject.CreateInstance<SfxCue>();
            cue.clips = clips;
            cue.order = SfxCue.Order.Shuffle;
            cue.volume = 0.85f;
            cue.volumeVariation = 0.06f;
            cue.pitch = 1f;
            cue.pitchVariation = 0.03f;   // voices: only a touch, or they sound chipmunked
            cue.maxVoices = 1;
            cue.cooldown = 0.5f;
            cue.priority = 50;
            if (created) AssetDatabase.CreateAsset(cue, CuePath);
            else EditorUtility.SetDirty(cue);
            AssetDatabase.SaveAssets();

            int n = 0;
            foreach (var f in Object.FindObjectsByType<Fighter>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                var hv = f.GetComponent<FighterHitVocals>();
                if (!hv) hv = Undo.AddComponent<FighterHitVocals>(f.gameObject);
                var so = new SerializedObject(hv);
                so.FindProperty("vocalCue").objectReferenceValue = cue;
                so.ApplyModifiedProperties();
                n++;
            }
            if (n > 0) EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            Debug.Log($"[Audio] {cue.name}: {string.Join(", ", clips.Select(c => c.name))} (shuffled). " +
                      (n > 0 ? $"Fighter Hit Vocals on {n} fighter(s). Save the scene (Ctrl+S)." : "Open FightScene and run again."));
        }
    }
}
#endif
