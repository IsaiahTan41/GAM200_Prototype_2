#if UNITY_EDITOR
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using KungFuCrashout.Core;
using KungFuCrashout.Fighters;

namespace KungFuCrashout.EditorTools
{
    /// <summary>
    /// Menu: KungFu Crashout → Add Round Reset
    /// Adds a "Round Reset" object (RoundReset, watching both fighters) to the open scene, and adds the scene to
    /// File → Build Settings (first in the list) so it can be reloaded in a phone build too. Safe to run again.
    /// Save the scene afterwards.
    /// Place in: Assets/_Project/Editor/RoundResetSetup.cs
    /// </summary>
    public static class RoundResetSetup
    {
        [MenuItem("KungFu Crashout/Add Round Reset", priority = 68)]
        public static void Add()
        {
            var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            if (string.IsNullOrEmpty(scene.path))
            {
                EditorUtility.DisplayDialog("Add Round Reset", "Save the scene first.", "OK");
                return;
            }

            Undo.SetCurrentGroupName("Add Round Reset");
            int group = Undo.GetCurrentGroup();

            var reset = Object.FindFirstObjectByType<RoundReset>();
            if (!reset)
            {
                var go = new GameObject("Round Reset");
                Undo.RegisterCreatedObjectUndo(go, "Create Round Reset");
                reset = Undo.AddComponent<RoundReset>(go);
            }

            var fighters = Object.FindObjectsByType<Fighter>(FindObjectsSortMode.None);
            var so = new SerializedObject(reset);
            var list = so.FindProperty("fighters");
            list.arraySize = fighters.Length;
            for (int i = 0; i < fighters.Length; i++) list.GetArrayElementAtIndex(i).objectReferenceValue = fighters[i];
            so.ApplyModifiedProperties();

            Undo.CollapseUndoOperations(group);
            EditorSceneManager.MarkSceneDirty(scene);
            Selection.activeObject = reset.gameObject;

            // Make sure the scene is in the build (needed to reload it on the phone).
            var scenes = EditorBuildSettings.scenes.ToList();
            int existing = scenes.FindIndex(s => s.path == scene.path);
            string buildNote;
            if (existing < 0)
            {
                scenes.Insert(0, new EditorBuildSettingsScene(scene.path, true));
                EditorBuildSettings.scenes = scenes.ToArray();
                buildNote = "added to Build Settings";
            }
            else
            {
                if (!scenes[existing].enabled)
                {
                    scenes[existing].enabled = true;
                    EditorBuildSettings.scenes = scenes.ToArray();
                }
                buildNote = "already in Build Settings";
            }

            Debug.Log($"[RoundReset] Watching {fighters.Length} fighter(s): {string.Join(", ", fighters.Select(f => f.name))}. " +
                      $"Scene {buildNote}. Save the scene (Ctrl+S).");
        }
    }
}
#endif
