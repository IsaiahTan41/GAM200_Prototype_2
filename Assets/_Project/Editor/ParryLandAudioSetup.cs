#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using KungFuCrashout.Feedback;
using KungFuCrashout.Sound;

namespace KungFuCrashout.EditorTools
{
    /// <summary>
    /// Menu: Window → KungFu Crashout → Audio → Set Up Parry and Landing Sounds
    ///   • Imports (mono, normalised, 48 kHz, decompress on load, ADPCM):
    ///       Audio/SFX/Parry/Fight_Impact_Light_Block_Hand_Block_Parry_Deflect_SND117399_S00
    ///       Audio/SFX/Movement/Footsteps_Human_Dirty_Ground_Jump_Land_SND62080_S10
    ///   • Cues in Audio/Cues: SFX_Parry (the fight impact), SFX_Land (footsteps landing, 0.7 volume),
    ///     SFX_Knockdown (the same landing, full volume, pitched down 10 % so it lands heavier).
    ///   • HitFeedback (both fighters): Parry Cue = SFX_Parry, Downed Cue = SFX_Knockdown.
    ///     FighterMovementSfx on each fighter: Land Cue = SFX_Land.
    /// Place in: Assets/_Project/Editor/ParryLandAudioSetup.cs
    /// </summary>
    public static class ParryLandAudioSetup
    {
        const string Title = "Parry and Landing Sounds";
        const string CueFolder = "Assets/_Project/Audio/Cues";
        const string ParryPath = "Assets/_Project/Audio/SFX/Parry/Fight_Impact_Light_Block_Hand_Block_Parry_Deflect_SND117399_S00.wav";
        const string LandPath = "Assets/_Project/Audio/SFX/Movement/Footsteps_Human_Dirty_Ground_Jump_Land_SND62080_S10.wav";

        [MenuItem("Window/KungFu Crashout/Audio/Set Up Parry and Landing Sounds", priority = 10072)]
        public static void SetUp()
        {
            if (EditorApplication.isPlaying) { EditorUtility.DisplayDialog(Title, "Stop Play mode first.", "OK"); return; }
            var parryClip = Import(ParryPath);
            var landClip = Import(LandPath);
            if (!parryClip || !landClip)
            {
                EditorUtility.DisplayDialog(Title, $"Missing {(parryClip ? LandPath : ParryPath)}.", "OK");
                return;
            }

            var parry = Cue("SFX_Parry", parryClip, 0.9f, 1f, 0.04f, 40);
            var land = Cue("SFX_Land", landClip, 0.7f, 1f, 0.06f, 110);
            var knockdown = Cue("SFX_Knockdown", landClip, 1f, 0.9f, 0.04f, 60);
            AssetDatabase.SaveAssets();

            var log = new List<string>();
            foreach (var hf in Object.FindObjectsByType<HitFeedback>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                var so = new SerializedObject(hf);
                so.FindProperty("parryCue").objectReferenceValue = parry;
                so.FindProperty("downedCue").objectReferenceValue = knockdown;
                so.ApplyModifiedProperties();
                log.Add($"{hf.name}: parry + knockdown");
            }
            foreach (var mv in Object.FindObjectsByType<FighterMovementSfx>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                var so = new SerializedObject(mv);
                so.FindProperty("landCue").objectReferenceValue = land;
                so.ApplyModifiedProperties();
                log.Add($"{mv.name}: landing");
            }
            if (log.Count > 0) EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            Debug.Log($"[Audio] {parry.name}, {land.name}, {knockdown.name}. " +
                      (log.Count > 0 ? string.Join("; ", log) + ". Save the scene (Ctrl+S)." : "Open FightScene and run again to wire them."));
        }

        static AudioClip Import(string path)
        {
            var ai = AssetImporter.GetAtPath(path) as AudioImporter;
            if (!ai) return null;
            ai.forceToMono = true;
            var s = ai.defaultSampleSettings;
            s.loadType = AudioClipLoadType.DecompressOnLoad; // needed to find the parry's first contact
            s.compressionFormat = AudioCompressionFormat.ADPCM;
            s.sampleRateSetting = AudioSampleRateSetting.OverrideSampleRate;
            s.sampleRateOverride = 48000;
            s.preloadAudioData = true;
            ai.defaultSampleSettings = s;
            var so = new SerializedObject(ai);
            var normalize = so.FindProperty("normalize");
            if (normalize != null) { normalize.boolValue = true; so.ApplyModifiedPropertiesWithoutUndo(); }
            ai.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<AudioClip>(path);
        }

        static SfxCue Cue(string name, AudioClip clip, float volume, float pitch, float pitchVariation, int priority)
        {
            string path = $"{CueFolder}/{name}.asset";
            var cue = AssetDatabase.LoadAssetAtPath<SfxCue>(path);
            bool created = !cue;
            if (created) cue = ScriptableObject.CreateInstance<SfxCue>();
            cue.clips = new[] { clip };
            cue.order = SfxCue.Order.Sequential;
            cue.volume = volume;
            cue.volumeVariation = 0.08f;
            cue.pitch = pitch;
            cue.pitchVariation = pitchVariation;
            cue.maxVoices = 2;
            cue.cooldown = 0.05f;
            cue.priority = priority;
            if (created) AssetDatabase.CreateAsset(cue, path);
            else EditorUtility.SetDirty(cue);
            return cue;
        }
    }
}
#endif
