#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using KungFuCrashout.Sound;

namespace KungFuCrashout.EditorTools
{
    /// <summary>
    /// Menu: KungFu Crashout → Set Up Background Music
    ///
    /// 1. Finds the track whose name contains "Chinese-Epic-Action" under Assets/_Project/Audio/BGM and sets
    ///    music import settings: Streaming (not held in memory), Vorbis 60%, stereo, load in background.
    /// 2. Creates a root "Music" object with MusicPlayer (track assigned, play on start, persistent) in the open
    ///    scene, or updates the existing one.
    /// Undoable for the scene part. Save the scene afterwards.
    /// Place in: Assets/_Project/Editor/MusicSetup.cs
    /// </summary>
    public static class MusicSetup
    {
        const string BgmFolder = "Assets/_Project/Audio/BGM";
        const string TrackNameContains = "Chinese-Epic-Action";

        [MenuItem("KungFu Crashout/Set Up Background Music", priority = 64)]
        public static void SetUp()
        {
            AudioClip clip = null;
            string clipPath = null;
            foreach (var guid in AssetDatabase.FindAssets("t:AudioClip", new[] { BgmFolder }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!System.IO.Path.GetFileNameWithoutExtension(path).Contains(TrackNameContains)) continue;
                clipPath = path;
                break;
            }

            if (clipPath == null)
            {
                EditorUtility.DisplayDialog("Set Up Background Music",
                    $"No audio file with \"{TrackNameContains}\" in its name was found in {BgmFolder}.", "OK");
                return;
            }

            // ── Import: stream it, compressed ──
            var ai = AssetImporter.GetAtPath(clipPath) as AudioImporter;
            if (ai)
            {
                ai.forceToMono = false;      // keep the stereo width for music
                ai.loadInBackground = true;
                var s = ai.defaultSampleSettings;
                s.loadType = AudioClipLoadType.Streaming;          // ~9 MB WAV: read from disk as it plays
                s.compressionFormat = AudioCompressionFormat.Vorbis;
                s.quality = 0.6f;                                  // ~60%: good for music on phone speakers
                s.preloadAudioData = false;
                s.sampleRateSetting = AudioSampleRateSetting.PreserveSampleRate;
                ai.defaultSampleSettings = s;
                ai.SaveAndReimport();
            }
            clip = AssetDatabase.LoadAssetAtPath<AudioClip>(clipPath);

            // ── Scene: persistent Music object ──
            Undo.SetCurrentGroupName("Set Up Background Music");
            int group = Undo.GetCurrentGroup();

            var player = Object.FindFirstObjectByType<MusicPlayer>();
            if (!player)
            {
                var go = new GameObject("Music");
                Undo.RegisterCreatedObjectUndo(go, "Create Music");
                player = Undo.AddComponent<MusicPlayer>(go);
            }

            var so = new SerializedObject(player);
            so.FindProperty("track").objectReferenceValue = clip;
            so.FindProperty("playOnStart").boolValue = true;
            so.FindProperty("persistAcrossScenes").boolValue = true;
            so.ApplyModifiedProperties();

            Undo.CollapseUndoOperations(group);
            EditorSceneManager.MarkSceneDirty(player.gameObject.scene);
            Selection.activeObject = player.gameObject;

            Debug.Log($"[Music] '{clip.name}' set to Streaming / Vorbis 60%, assigned to MusicPlayer on '{player.name}' " +
                      "(plays on start, loops, persists across scenes). Save the scene (Ctrl+S).");
        }
    }
}
#endif
