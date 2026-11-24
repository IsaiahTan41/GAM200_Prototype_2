#if UNITY_EDITOR
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using KungFuCrashout.Core;
using KungFuCrashout.Sound;

namespace KungFuCrashout.EditorTools
{
    /// <summary>
    /// Menu: Window → KungFu Crashout → Audio → Set Up Match KO Announcer   (open FightScene first)
    ///   • Imports every clip in Audio/SFX/Announcer (VO_KO_01 … 04): stereo kept, levels as mastered by the sound team
    ///     (no normalising), Vorbis quality 0.7, compressed in memory (4 s takes), preloaded so the first one has no delay.
    ///   • Builds Audio/Cues/SFX_MatchKO: RANDOM take each match (never the same one twice in a row), no pitch / volume
    ///     variation (it's a voice-over), one voice, top priority (never cut by hits or whooshes).
    ///   • MatchManager → Match Ko Cue = it. It plays only on the knockout that ends the match, as the KO slow motion
    ///     starts, so the 4 s take runs through the slow motion and ends as the win / lose screen appears.
    /// Re-run after adding / renaming takes. Place in: Assets/_Project/Editor/MatchKoSetup.cs
    /// </summary>
    public static class MatchKoSetup
    {
        const string Title = "Match KO Announcer";
        const string Folder = "Assets/_Project/Audio/SFX/Announcer";
        const string CuePath = "Assets/_Project/Audio/Cues/SFX_MatchKO.asset";

        [MenuItem("Window/KungFu Crashout/Audio/Set Up Match KO Announcer", priority = 10076)]
        public static void SetUp()
        {
            if (EditorApplication.isPlaying) { EditorUtility.DisplayDialog(Title, "Stop Play mode first.", "OK"); return; }
            if (!AssetDatabase.IsValidFolder(Folder)) { EditorUtility.DisplayDialog(Title, $"Missing {Folder}.", "OK"); return; }

            var clips = ImportVoices("VO_KO");
            if (clips.Length == 0) { EditorUtility.DisplayDialog(Title, $"No VO_KO clips in {Folder}.", "OK"); return; }

            var cue = VoiceCue(CuePath, clips);
            cue.order = SfxCue.Order.Random;
            cue.avoidRepeatingLast = 1;    // random take, never the same one twice in a row
            EditorUtility.SetDirty(cue);
            AssetDatabase.SaveAssets();

            int n = 0;
            foreach (var mm in Object.FindObjectsByType<MatchManager>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                var so = new SerializedObject(mm);
                so.FindProperty("matchKoCue").objectReferenceValue = cue;
                so.ApplyModifiedProperties();
                n++;
            }
            if (n > 0) EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            Debug.Log($"[Audio] {cue.name}: {string.Join(", ", clips.Select(c => c.name))} (random). " +
                      (n > 0 ? $"MatchManager → Match Ko Cue set. Save the scene (Ctrl+S)." : "Open FightScene and run again to assign it."));
        }

        /// <summary>
        /// Menu: Window → KungFu Crashout → Audio → Set Up Win / Lose Announcer   (open FightScene first)
        /// VO_Wins_Player1 → SFX_Wins_Player1 on the VICTORY screen (the player won); VO_Wins_Player2 → SFX_Wins_Player2 on
        /// the DEFEAT screen (the player lost). Played by the end screen (End Screen → Victory / Defeat Voice) as it appears.
        /// Same import as the KO takes. A draw plays neither.
        /// </summary>
        [MenuItem("Window/KungFu Crashout/Audio/Set Up Win - Lose Announcer", priority = 10077)]
        public static void SetUpWins()
        {
            const string title = "Win / Lose Announcer";
            if (EditorApplication.isPlaying) { EditorUtility.DisplayDialog(title, "Stop Play mode first.", "OK"); return; }
            var p1 = ImportVoices("VO_Wins_Player1");
            var p2 = ImportVoices("VO_Wins_Player2");
            if (p1.Length == 0 && p2.Length == 0) { EditorUtility.DisplayDialog(title, $"No VO_Wins_Player1 / 2 clips in {Folder}.", "OK"); return; }
            var win = p1.Length > 0 ? VoiceCue("Assets/_Project/Audio/Cues/SFX_Wins_Player1.asset", p1) : null;
            var lose = p2.Length > 0 ? VoiceCue("Assets/_Project/Audio/Cues/SFX_Wins_Player2.asset", p2) : null;
            AssetDatabase.SaveAssets();

            int n = 0;
            foreach (var es in Object.FindObjectsByType<KungFuCrashout.UI.EndScreenConcept>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                var so = new SerializedObject(es);
                if (win) so.FindProperty("victoryVoice").objectReferenceValue = win;
                if (lose) so.FindProperty("defeatVoice").objectReferenceValue = lose;
                so.ApplyModifiedProperties();
                n++;
            }
            if (n > 0) EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            Debug.Log($"[Audio] Victory screen: {(win ? win.name : "none")}, defeat screen: {(lose ? lose.name : "none")}. " +
                      (n > 0 ? "End Screen voices set. Save the scene (Ctrl+S)." : "No End Screen in the open scene: open FightScene and run again."));
        }

        /// <summary>
        /// Menu: Window → KungFu Crashout → Audio → Set Up Round Announcer   (open FightScene first)
        /// VO_Round1* / VO_Round2* / VO_Round3* → SFX_Round1 / 2 / 3 → MatchManager → Round Voices. Each plays with the
        /// ROUND callout at the start of its round; rounds after 3 (draws) reuse Round 3. Same import as the KO takes.
        /// </summary>
        [MenuItem("Window/KungFu Crashout/Audio/Set Up Round Announcer", priority = 10078)]
        public static void SetUpRounds()
        {
            const string title = "Round Announcer";
            if (EditorApplication.isPlaying) { EditorUtility.DisplayDialog(title, "Stop Play mode first.", "OK"); return; }
            var cues = new SfxCue[3];
            for (int r = 1; r <= 3; r++)
            {
                var clips = ImportVoices($"VO_Round{r}");
                if (clips.Length > 0) cues[r - 1] = VoiceCue($"Assets/_Project/Audio/Cues/SFX_Round{r}.asset", clips);
            }
            if (cues.All(c => !c)) { EditorUtility.DisplayDialog(title, $"No VO_Round1 / 2 / 3 clips in {Folder}.", "OK"); return; }
            AssetDatabase.SaveAssets();

            int n = 0;
            foreach (var mm in Object.FindObjectsByType<MatchManager>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                var so = new SerializedObject(mm);
                var arr = so.FindProperty("roundVoices");
                arr.arraySize = 3;
                for (int i = 0; i < 3; i++) arr.GetArrayElementAtIndex(i).objectReferenceValue = cues[i];
                so.ApplyModifiedProperties();
                n++;
            }
            if (n > 0) EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            Debug.Log($"[Audio] Round voices: {string.Join(", ", cues.Select((c, i) => $"Round {i + 1} = {(c ? c.clips[0].name : "none")}"))}; " +
                      "rounds after 3 reuse Round 3. " +
                      (n > 0 ? "MatchManager → Round Voices set. Save the scene (Ctrl+S)." : "Open FightScene and run again to assign them."));
        }

        /// <summary>Announcer clips in the folder whose name starts with Prefix: stereo, levels as mastered, Vorbis 0.7,
        /// compressed in memory, preloaded.</summary>
        static AudioClip[] ImportVoices(string prefix)
        {
            return AssetDatabase.FindAssets("t:AudioClip", new[] { Folder })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(p => System.IO.Path.GetFileNameWithoutExtension(p).StartsWith(prefix))
                .OrderBy(p => p).Select(path =>
                {
                    var ai = AssetImporter.GetAtPath(path) as AudioImporter;
                    if (ai)
                    {
                        ai.forceToMono = false;
                        var s = ai.defaultSampleSettings;
                        s.loadType = AudioClipLoadType.CompressedInMemory;
                        s.compressionFormat = AudioCompressionFormat.Vorbis;
                        s.quality = 0.7f;
                        s.preloadAudioData = true;
                        ai.defaultSampleSettings = s;
                        ai.SaveAndReimport();
                    }
                    return AssetDatabase.LoadAssetAtPath<AudioClip>(path);
                }).Where(c => c).ToArray();
        }

        /// <summary>An announcer cue: no pitch / volume variation, one voice, top priority (never cut by other sounds).</summary>
        static SfxCue VoiceCue(string path, AudioClip[] clips)
        {
            var cue = AssetDatabase.LoadAssetAtPath<SfxCue>(path);
            bool created = !cue;
            if (created) cue = ScriptableObject.CreateInstance<SfxCue>();
            cue.clips = clips;
            cue.order = SfxCue.Order.Shuffle;
            cue.volume = 1f;
            cue.volumeVariation = 0f;
            cue.pitch = 1f;
            cue.pitchVariation = 0f;
            cue.maxVoices = 1;
            cue.cooldown = 1f;
            cue.priority = 0;
            if (created) AssetDatabase.CreateAsset(cue, path);
            else EditorUtility.SetDirty(cue);
            return cue;
        }
    }
}
#endif
