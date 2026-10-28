using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using KungFuCrashout.Controls;

namespace KungFuCrashout.UI
{
    /// <summary>
    /// Put on a UI Button: clicking it loads Scene Name (e.g. the fight's Back button → "StartScreen").
    ///   • Time is set back to normal first, so a hit freeze / paused options menu never carries into the next scene.
    ///   • Claim Touches: a finger that starts on the button is ignored by the fight controls (TouchClaims), so tapping it
    ///     in the middle of a fight doesn't also punch.
    /// The scene must be in File → Build Profiles / Build Settings for phone builds (the editor can load it anyway).
    /// Setup: KungFu Crashout → Set Up Start Screen + Back Button. Place in: Assets/_Project/Scripts/UI/SceneButton.cs
    /// </summary>
    [RequireComponent(typeof(Button))]
    [DefaultExecutionOrder(-45)] // before the fight input handlers read this frame's touches
    public class SceneButton : MonoBehaviour
    {
        [Tooltip("Scene to load when clicked (its name, as in Build Settings).")]
        [SerializeField] string sceneName = "StartScreen";
        [Tooltip("Fingers that start on this button aren't read as fight input.")]
        [SerializeField] bool claimTouches = true;

        Button button;
        RectTransform rect;
        readonly List<int> claimed = new List<int>();

        public string SceneName { get => sceneName; set => sceneName = value; }

        void Awake()
        {
            button = GetComponent<Button>();
            rect = (RectTransform)transform;
            button.onClick.AddListener(Load);
        }

        void OnDisable()
        {
            foreach (var id in claimed) TouchClaims.Release(id);
            claimed.Clear();
        }

        void Update()
        {
            if (!claimTouches) return;
            var ts = Touchscreen.current;
            if (ts == null) return;
            foreach (var t in ts.touches)
            {
                int id = t.touchId.ReadValue();
                if (t.press.wasPressedThisFrame && RectTransformUtility.RectangleContainsScreenPoint(rect, t.position.ReadValue(), null))
                {
                    TouchClaims.Claim(id);
                    if (!claimed.Contains(id)) claimed.Add(id);
                }
                else if (t.press.wasReleasedThisFrame && claimed.Remove(id))
                {
                    TouchClaims.Release(id);
                }
            }
        }

        public void Load() => SceneLoader.Load(sceneName);
    }

    /// <summary>Loads scenes by name with the clean-ups every scene change needs.</summary>
    public static class SceneLoader
    {
        public static void Load(string sceneName)
        {
            if (string.IsNullOrEmpty(sceneName)) return;
            Time.timeScale = 1f;
            TouchClaims.Clear();

            if (Application.CanStreamedLevelBeLoaded(sceneName))
            {
                SceneManager.LoadScene(sceneName);
                return;
            }
#if UNITY_EDITOR
            // Not in Build Settings yet: the editor can still load it by path.
            foreach (var guid in UnityEditor.AssetDatabase.FindAssets($"{sceneName} t:Scene"))
            {
                string path = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
                if (System.IO.Path.GetFileNameWithoutExtension(path) != sceneName) continue;
                Debug.LogWarning($"[SceneLoader] '{sceneName}' isn't in Build Settings — loading it by path (editor only). Add it for phone builds.");
                UnityEditor.SceneManagement.EditorSceneManager.LoadSceneInPlayMode(path, new LoadSceneParameters(LoadSceneMode.Single));
                return;
            }
#endif
            Debug.LogError($"[SceneLoader] Scene '{sceneName}' not found in Build Settings.");
        }

        public static void Quit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
    }
}
