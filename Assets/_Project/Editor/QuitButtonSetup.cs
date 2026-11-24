#if UNITY_EDITOR
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using KungFuCrashout.UI;

namespace KungFuCrashout.EditorTools
{
    /// <summary>
    /// Menu: Window → KungFu Crashout → GDD Build → Hook Up Start Screen Quit Button
    /// Adds QuitButton to the Start screen's "Quit" icon (Start Screen (Concept 29)) and saves StartScreen.
    /// Undo: remove the Quit Button component from that icon.
    /// Place in: Assets/_Project/Editor/QuitButtonSetup.cs
    /// </summary>
    public static class QuitButtonSetup
    {
        const string StartPath = "Assets/_Project/Scenes/StartScreen.unity";

        [MenuItem("Window/KungFu Crashout/GDD Build/Hook Up Start Screen Quit Button", priority = 10068)]
        public static void HookUp()
        {
            const string title = "Quit Button";
            if (EditorApplication.isPlaying) { EditorUtility.DisplayDialog(title, "Stop Play mode first.", "OK"); return; }
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            var active = EditorSceneManager.GetActiveScene();
            bool opened = active.path != StartPath;
            var scene = opened ? EditorSceneManager.OpenScene(StartPath, OpenSceneMode.Additive) : active;

            Button quit = null;
            foreach (var r in scene.GetRootGameObjects())
            {
                var screen = r.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "Start Screen (Concept 29)");
                if (screen) quit = screen.GetComponentsInChildren<Button>(true).FirstOrDefault(b => b.name == "Quit");
                if (quit) break;
            }
            if (quit)
            {
                if (!quit.GetComponent<QuitButton>()) quit.gameObject.AddComponent<QuitButton>();
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
                Debug.Log("[Start Screen] Quit icon → Application.Quit (stops Play mode in the editor). StartScreen saved.");
            }
            else Debug.LogWarning("[Start Screen] No \"Quit\" button under Start Screen (Concept 29) — run Build Start Screen first.");
            if (opened) EditorSceneManager.CloseScene(scene, true);
        }
    }
}
#endif
