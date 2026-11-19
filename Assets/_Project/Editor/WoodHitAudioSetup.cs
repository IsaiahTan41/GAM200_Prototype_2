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
    /// Menu: Window → KungFu Crashout → Audio → Set Up Wood Stick Hits (also sets up the Punch B layer if its clips are there)
    ///   • Imports Audio/SFX/Wood/Wooden_Stick_Hit_*.wav like the punch sounds (mono, normalised, decompress on load, ADPCM).
    ///   • Builds Audio/Cues/SFX_WoodStickHit: the clips in number order, SEQUENTIAL (each basic attack hit plays the next
    ///     one, 1 → 5 → 1…), small volume / pitch variation, same voice limits as the punches.
    ///   • Every HitFeedback in the open scene: Basic Attack Wood Cue = it, Use Wood Hit Sounds = on.
    ///   • If Audio/SFX/PunchB/Punch_B_fx_*.wav exist: SFX_PunchB_Layer (Sequential), HitFeedback → Punch Layer Cue = it,
    ///     Wood Layering = Layered Over. Wood Layering: Wood Only (original) / Layered Over / Layered After.
    ///   • Per-move layers (one cue each, same layering): heavy = Punch_A_fx_01, side / down / up special = Punch_A_fx_02 / 03 / 04,
    ///     blocked staff hits = Punch_C_fx_01 (instead of the block sound), air combo = Punch B (the basic attack's cue).
    /// HitFeedback → Use Wood Hit Sounds off = the old punch sounds. Re-run after adding / renaming clips.
    /// Place in: Assets/_Project/Editor/WoodHitAudioSetup.cs
    /// </summary>
    public static class WoodHitAudioSetup
    {
        const string Title = "Wood Stick Hits";
        const string ClipFolder = "Assets/_Project/Audio/SFX/Wood";
        const string CuePath = "Assets/_Project/Audio/Cues/SFX_WoodStickHit.asset";
        const string PunchFolder = "Assets/_Project/Audio/SFX/PunchB";
        const string PunchCuePath = "Assets/_Project/Audio/Cues/SFX_PunchB_Layer.asset";
        const string CueFolder = "Assets/_Project/Audio/Cues";

        // Punch layer per move: HitFeedback field, clip folder, clip name, cue name, cue volume (trim).
        // Side special's Punch_A_fx_02 is trimmed to 0.9: at full level it just touches 0 dBFS layered over its loudest wood pair.
        static readonly (string field, string folder, string clip, string cue, float volume)[] MoveLayers =
        {
            ("heavyLayerCue",       "Assets/_Project/Audio/SFX/PunchA", "Punch_A_fx_01", "SFX_PunchA_Heavy",       1f),
            ("sideSpecialLayerCue", "Assets/_Project/Audio/SFX/PunchA", "Punch_A_fx_02", "SFX_PunchA_SideSpecial", 0.9f),
            ("downSpecialLayerCue", "Assets/_Project/Audio/SFX/PunchA", "Punch_A_fx_03", "SFX_PunchA_DownSpecial", 1f),
            ("upSpecialLayerCue",   "Assets/_Project/Audio/SFX/PunchA", "Punch_A_fx_04", "SFX_PunchA_UpSpecial",   1f),
            ("blockLayerCue",       "Assets/_Project/Audio/SFX/PunchC", "Punch_C_fx_01", "SFX_PunchC_Block",       1f),
        };

        [MenuItem("Window/KungFu Crashout/Audio/Set Up Wood Stick Hits", priority = 10070)]
        public static void SetUp()
        {
            if (EditorApplication.isPlaying) { EditorUtility.DisplayDialog(Title, "Stop Play mode first.", "OK"); return; }

            var woodClips = ImportClips(ClipFolder, "Wooden_Stick_Hit");
            if (woodClips.Length == 0)
            {
                EditorUtility.DisplayDialog(Title, $"No Wooden_Stick_Hit_*.wav found in {ClipFolder}.", "OK");
                return;
            }
            var punchClips = ImportClips(PunchFolder, "Punch_B_fx");

            var cue = AssetDatabase.LoadAssetAtPath<SfxCue>(CuePath);
            bool created = !cue;
            if (created) cue = ScriptableObject.CreateInstance<SfxCue>();
            cue.clips = woodClips;
            cue.order = SfxCue.Order.Sequential;   // cycles 1 → 5 on every basic attack hit
            cue.volume = 1f;                      // these are ~4 dB quieter than the punches at their loudest
            cue.volumeVariation = 0.08f;
            cue.pitch = 1f;
            cue.pitchVariation = 0.04f;
            cue.maxVoices = 3;
            cue.cooldown = 0.03f;
            cue.priority = 64;
            if (created) AssetDatabase.CreateAsset(cue, CuePath);
            else EditorUtility.SetDirty(cue);

            // The Punch B layer: its own cue (cycles 1 → 5 in step with the wood), levels set on HitFeedback.
            SfxCue punchCue = null;
            if (punchClips.Length > 0)
            {
                punchCue = AssetDatabase.LoadAssetAtPath<SfxCue>(PunchCuePath);
                bool pc = !punchCue;
                if (pc) punchCue = ScriptableObject.CreateInstance<SfxCue>();
                punchCue.clips = punchClips;
                punchCue.order = SfxCue.Order.Sequential;
                punchCue.volume = 1f;
                punchCue.volumeVariation = 0.08f;
                punchCue.pitch = 1f;
                punchCue.pitchVariation = 0f;   // the layer follows the wood's pitch
                punchCue.maxVoices = 3;
                punchCue.cooldown = 0f;
                punchCue.priority = 70;         // the wood layer wins if voices run out
                if (pc) AssetDatabase.CreateAsset(punchCue, PunchCuePath);
                else EditorUtility.SetDirty(punchCue);
            }

            // One cue per move (a single clip each), same layer settings as the Punch B cue.
            var moveCues = new Dictionary<string, SfxCue>();
            foreach (var m in MoveLayers)
            {
                var found = ImportClips(m.folder, m.clip);
                if (found.Length == 0) continue;
                string path = $"{CueFolder}/{m.cue}.asset";
                var mc = AssetDatabase.LoadAssetAtPath<SfxCue>(path);
                bool mcNew = !mc;
                if (mcNew) mc = ScriptableObject.CreateInstance<SfxCue>();
                mc.clips = new[] { found[0] };
                mc.order = SfxCue.Order.Sequential;
                mc.volume = m.volume;
                mc.volumeVariation = 0.08f;
                mc.pitch = 1f;
                mc.pitchVariation = 0f;   // follows the wood's pitch
                mc.maxVoices = 3;
                mc.cooldown = 0f;
                mc.priority = 70;
                if (mcNew) AssetDatabase.CreateAsset(mc, path);
                else EditorUtility.SetDirty(mc);
                moveCues[m.field] = mc;
            }
            AssetDatabase.SaveAssets();

            int wired = 0;
            foreach (var hf in Object.FindObjectsByType<HitFeedback>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                var so = new SerializedObject(hf);
                so.FindProperty("basicAttackWoodCue").objectReferenceValue = cue;
                so.FindProperty("useWoodHitSounds").boolValue = true;
                if (punchCue)
                {
                    so.FindProperty("punchLayerCue").objectReferenceValue = punchCue;
                    so.FindProperty("woodLayering").enumValueIndex = 1; // Layered Over
                }
                foreach (var kv in moveCues) so.FindProperty(kv.Key).objectReferenceValue = kv.Value;
                so.FindProperty("airLayerCue").objectReferenceValue = null; // air combo = Punch B, cycling with the basic attack
                so.FindProperty("layerBlockedHits").boolValue = true;
                so.ApplyModifiedProperties();
                wired++;
            }
            if (wired > 0) EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());

            Debug.Log($"[Audio] {cue.name}: {string.Join(", ", cue.clips.Select(c => c.name))} (Sequential). " +
                      (punchCue ? $"{punchCue.name}: {string.Join(", ", punchCue.clips.Select(c => c.name))} (Sequential), Wood Layering = Layered Over. "
                                : $"No Punch_B_fx_*.wav in {PunchFolder}: wood only. ") +
                      (moveCues.Count > 0 ? $"Move layers: {string.Join(", ", moveCues.Select(kv => $"{kv.Key} = {kv.Value.name}"))}. " : "") +
                      (wired > 0 ? $"Wired on {wired} HitFeedback (Use Wood Hit Sounds on). Save the scene (Ctrl+S)."
                                 : "No HitFeedback in the open scene: open FightScene and run again to wire it."));
        }

        /// <summary>Import name_*.wav in folder like the punch sounds (mono, normalised, decompress on load, ADPCM), in number order.</summary>
        static AudioClip[] ImportClips(string folder, string prefix)
        {
            var clips = new List<(int n, AudioClip clip)>();
            if (!AssetDatabase.IsValidFolder(folder)) return new AudioClip[0];
            foreach (var guid in AssetDatabase.FindAssets($"{prefix} t:AudioClip", new[] { folder }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var ai = AssetImporter.GetAtPath(path) as AudioImporter;
                if (ai)
                {
                    ai.forceToMono = true;
                    var s = ai.defaultSampleSettings;
                    s.loadType = AudioClipLoadType.DecompressOnLoad; // needed to measure the onset / peak for layering
                    s.compressionFormat = AudioCompressionFormat.ADPCM;
                    s.preloadAudioData = true;
                    ai.defaultSampleSettings = s;
                    var so = new SerializedObject(ai);
                    var normalize = so.FindProperty("normalize");
                    if (normalize != null) { normalize.boolValue = true; so.ApplyModifiedPropertiesWithoutUndo(); }
                    ai.SaveAndReimport();
                }
                var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
                string digits = new string(System.IO.Path.GetFileNameWithoutExtension(path).Where(char.IsDigit).ToArray());
                if (clip) clips.Add((int.TryParse(digits, out int n) ? n : 999, clip));
            }
            clips.Sort((a, b) => a.n.CompareTo(b.n));
            return clips.Select(c => c.clip).ToArray();
        }
    }
}
#endif
