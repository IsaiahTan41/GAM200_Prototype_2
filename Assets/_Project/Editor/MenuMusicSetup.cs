#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using KungFuCrashout.Sound;

namespace KungFuCrashout.EditorTools
{
    /// <summary>
    /// Menu: Window → KungFu Crashout → Audio → Set Up Roster + Level Select Music
    /// Same method as the other background music:
    ///   • Audio/BGM/Iris_Boss_BGM_Loop_FadeOut-3.25sec imported like the other BGM: stereo, Streaming, Vorbis quality 0.6,
    ///     no preload, load in background.
    ///   • A "Music" object with a MusicPlayer (Track = it, Play On Start, Persist Across Scenes, volume 0.6, fade-in 1.5 s,
    ///     crossfade 1 s) in the Roster and LevelSelect scenes. The MusicPlayer loops its track; coming from the Start screen
    ///     it crossfades in, and Roster → LevelSelect keeps it playing without a restart (same track). Both scenes are saved.
    /// Place in: Assets/_Project/Editor/MenuMusicSetup.cs
    /// </summary>
    public static class MenuMusicSetup
    {
        const string Title = "Roster + Level Select Music";
        const string TrackPath = "Assets/_Project/Audio/BGM/Iris_Boss_BGM_Loop_FadeOut-3.25sec.wav";
        static readonly string[] Scenes =
        {
            "Assets/_Project/Scenes/Roster.unity",
            "Assets/_Project/Scenes/LevelSelect.unity",
        };

        [MenuItem("Window/KungFu Crashout/Audio/Set Up Roster + Level Select Music", priority = 10075)]
        public static void SetUp()
        {
            if (EditorApplication.isPlaying) { EditorUtility.DisplayDialog(Title, "Stop Play mode first.", "OK"); return; }
            var clip = ImportBgm(TrackPath);
            if (!clip) { EditorUtility.DisplayDialog(Title, $"Missing {TrackPath}.", "OK"); return; }
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            var active = SceneManager.GetActiveScene();
            foreach (var path in Scenes)
            {
                var scene = SceneManager.GetSceneByPath(path);
                bool opened = false;
                if (!scene.isLoaded) { scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive); opened = true; }

                MusicPlayer player = null;
                foreach (var root in scene.GetRootGameObjects())
                {
                    player = root.GetComponentInChildren<MusicPlayer>(true);
                    if (player) break;
                }
                if (!player)
                {
                    var go = new GameObject("Music");
                    SceneManager.MoveGameObjectToScene(go, scene);
                    player = go.AddComponent<MusicPlayer>();
                }
                var so = new SerializedObject(player);
                so.FindProperty("track").objectReferenceValue = clip;
                so.FindProperty("playOnStart").boolValue = true;
                so.FindProperty("persistAcrossScenes").boolValue = true;
                so.FindProperty("volume").floatValue = 0.6f;
                so.FindProperty("fadeInTime").floatValue = 1.5f;
                so.FindProperty("crossfadeTime").floatValue = 1f;
                so.ApplyModifiedPropertiesWithoutUndo();

                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
                if (opened && scene != active) EditorSceneManager.CloseScene(scene, true);
            }
            if (active.IsValid()) SceneManager.SetActiveScene(active);
            Debug.Log($"[Audio] {clip.name} ({clip.length:0.##} s, loops) is the music of the Roster and LevelSelect (scenes saved).");
        }

        static AudioClip ImportBgm(string path)
        {
            var ai = AssetImporter.GetAtPath(path) as AudioImporter;
            if (!ai) return null;
            ai.forceToMono = false;
            ai.loadInBackground = true;
            var s = ai.defaultSampleSettings;
            s.loadType = AudioClipLoadType.Streaming;
            s.compressionFormat = AudioCompressionFormat.Vorbis;
            s.quality = 0.6f;
            s.sampleRateSetting = AudioSampleRateSetting.PreserveSampleRate;
            s.preloadAudioData = false;
            ai.defaultSampleSettings = s;
            ai.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<AudioClip>(path);
        }
    }
}
#endif
