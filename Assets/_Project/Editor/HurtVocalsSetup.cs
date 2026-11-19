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
    /// Menu: Window → KungFu Crashout → Audio → Set Up Hurt Vocals
    ///   • Imports every clip in Audio/SFX/HurtVocals (mono, normalised, decompress on load, Vorbis: kinder to voices than ADPCM).
    ///   • Builds Audio/Cues/SFX_HurtVocals: shuffled (never the same vocal twice in a row), one voice at a time, small pitch variation.
    ///   • KO cry: Audio/SFX/KOVocal → SFX_KOVocal, set as Fighter Hurt Vocals → KO Cue (plays when health is depleted).
    ///   • Adds FighterHurtVocals to every Fighter in the open scene (both Monks) with that cue. Spacing settings live on it.
    /// Place in: Assets/_Project/Editor/HurtVocalsSetup.cs
    /// </summary>
    public static class HurtVocalsSetup
    {
        const string Title = "Hurt Vocals";
        const string Folder = "Assets/_Project/Audio/SFX/HurtVocals";
        const string CuePath = "Assets/_Project/Audio/Cues/SFX_HurtVocals.asset";
        const string KoFolder = "Assets/_Project/Audio/SFX/KOVocal";
        const string KoCuePath = "Assets/_Project/Audio/Cues/SFX_KOVocal.asset";

        [MenuItem("Window/KungFu Crashout/Audio/Set Up Hurt Vocals", priority = 10074)]
        public static void SetUp()
        {
            if (EditorApplication.isPlaying) { EditorUtility.DisplayDialog(Title, "Stop Play mode first.", "OK"); return; }
            if (!AssetDatabase.IsValidFolder(Folder)) { EditorUtility.DisplayDialog(Title, $"Missing {Folder}.", "OK"); return; }

            var clips = AssetDatabase.FindAssets("t:AudioClip", new[] { Folder })
                .Select(AssetDatabase.GUIDToAssetPath).OrderBy(p => p).Select(ImportVoice).Where(c => c).ToArray();
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

            // KO cry: its own folder and cue (Audio/SFX/KOVocal), always played once when health is depleted.
            SfxCue koCue = null;
            if (AssetDatabase.IsValidFolder(KoFolder))
            {
                var koClips = AssetDatabase.FindAssets("t:AudioClip", new[] { KoFolder }).Select(AssetDatabase.GUIDToAssetPath)
                    .OrderBy(p => p).Select(ImportVoice).Where(c => c).ToArray();
                if (koClips.Length > 0)
                {
                    koCue = AssetDatabase.LoadAssetAtPath<SfxCue>(KoCuePath);
                    bool koNew = !koCue;
                    if (koNew) koCue = ScriptableObject.CreateInstance<SfxCue>();
                    koCue.clips = koClips;
                    koCue.order = SfxCue.Order.Shuffle;
                    koCue.volume = 0.95f;
                    koCue.volumeVariation = 0f;
                    koCue.pitch = 1f;
                    koCue.pitchVariation = 0.02f;
                    koCue.maxVoices = 1;
                    koCue.cooldown = 0.5f;
                    koCue.priority = 20; // the most important voice of the round
                    if (koNew) AssetDatabase.CreateAsset(koCue, KoCuePath);
                    else EditorUtility.SetDirty(koCue);
                }
            }
            AssetDatabase.SaveAssets();

            int n = 0;
            foreach (var f in Object.FindObjectsByType<Fighter>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                var hv = f.GetComponent<FighterHurtVocals>();
                if (!hv) hv = Undo.AddComponent<FighterHurtVocals>(f.gameObject);
                var so = new SerializedObject(hv);
                so.FindProperty("vocalCue").objectReferenceValue = cue;
                if (koCue) so.FindProperty("koCue").objectReferenceValue = koCue;
                so.ApplyModifiedProperties();
                n++;
            }
            if (n > 0) EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            Debug.Log($"[Audio] {cue.name}: {string.Join(", ", clips.Select(c => c.name))} (shuffled). " +
                      (koCue ? $"{koCue.name}: {string.Join(", ", koCue.clips.Select(c => c.name))} (on KO). " : $"No clip in {KoFolder}: no KO cry. ") +
                      (n > 0 ? $"Fighter Hurt Vocals on {n} fighter(s). Save the scene (Ctrl+S)." : "Open FightScene and run again."));
        }

        /// <summary>Voice import: mono, normalised, decompress on load, Vorbis (kinder to voices than ADPCM).</summary>
        static AudioClip ImportVoice(string path)
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
        }
    }
}
#endif
