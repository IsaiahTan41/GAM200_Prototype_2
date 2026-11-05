#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using KungFuCrashout.Sound;

namespace KungFuCrashout.EditorTools
{
    /// <summary>
    /// Menu: KungFu Crashout → Set Up Start Screen Music
    /// 1. Finds the BGM track whose name contains "10Suns" (or "WalkAbout") in Assets/_Project/Audio/BGM and gives it the
    ///    same music import settings as the fight track: Streaming, Vorbis 60%, stereo, load in background.
    /// 2. Opens StartScreen.unity, adds (or updates) a root "Music" object with MusicPlayer (that track, play on start,
    ///    persistent), saves it, and returns you to the scene you had open.
    /// How the two tracks work together: MusicPlayer persists between scenes; when a scene with a different track loads,
    /// it crossfades to it (Crossfade Time). Start screen → START = crossfade to the fight music; Back = crossfade home.
    /// Place in: Assets/_Project/Editor/StartScreenMusicSetup.cs
    /// </summary>
    public static class StartScreenMusicSetup
    {
        const string BgmFolder = "Assets/_Project/Audio/BGM";
        const string StartPath = "Assets/_Project/Scenes/StartScreen.unity";

        [MenuItem("KungFu Crashout/Set Up Start Screen Music", priority = 71)]
        public static void SetUp()
        {
            if (EditorApplication.isPlaying) { EditorUtility.DisplayDialog("Start Screen Music", "Stop Play mode first.", "OK"); return; }

            string clipPath = null;
            foreach (var guid in AssetDatabase.FindAssets("t:AudioClip", new[] { BgmFolder }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                string n = System.IO.Path.GetFileNameWithoutExtension(path).ToLowerInvariant();
                if (n.Contains("10suns") || n.Contains("walkabout")) { clipPath = path; break; }
            }
            if (clipPath == null)
            {
                EditorUtility.DisplayDialog("Start Screen Music", $"No track with \"10Suns\" or \"WalkAbout\" in its name in {BgmFolder}.", "OK");
                return;
            }
            if (!System.IO.File.Exists(StartPath))
            {
                EditorUtility.DisplayDialog("Start Screen Music", $"{StartPath} not found.", "OK");
                return;
            }

            // ── Import: stream it, compressed (a ~10 MB WAV) ──
            var ai = AssetImporter.GetAtPath(clipPath) as AudioImporter;
            if (ai)
            {
                ai.forceToMono = false;
                ai.loadInBackground = true;
                var s = ai.defaultSampleSettings;
                s.loadType = AudioClipLoadType.Streaming;
                s.compressionFormat = AudioCompressionFormat.Vorbis;
                s.quality = 0.6f;
                s.preloadAudioData = false;
                s.sampleRateSetting = AudioSampleRateSetting.PreserveSampleRate;
                ai.defaultSampleSettings = s;
                ai.SaveAndReimport();
            }
            var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(clipPath);

            // ── Scene ──
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            string returnTo = UnityEngine.SceneManagement.SceneManager.GetActiveScene().path;

            var scene = EditorSceneManager.OpenScene(StartPath, OpenSceneMode.Single);
            var player = Object.FindFirstObjectByType<MusicPlayer>();
            if (!player)
            {
                var go = new GameObject("Music");
                player = go.AddComponent<MusicPlayer>();
            }
            var so = new SerializedObject(player);
            so.FindProperty("track").objectReferenceValue = clip;
            so.FindProperty("playOnStart").boolValue = true;
            so.FindProperty("persistAcrossScenes").boolValue = true;
            so.ApplyModifiedPropertiesWithoutUndo();

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);

            if (!string.IsNullOrEmpty(returnTo) && returnTo != StartPath) EditorSceneManager.OpenScene(returnTo, OpenSceneMode.Single);

            Debug.Log($"[Music] StartScreen plays '{clip.name}' (Streaming / Vorbis 60%). The fight keeps its own track; " +
                      "MusicPlayer crossfades between them when you press START / Back. StartScreen saved.");
        }
    }
}
#endif
