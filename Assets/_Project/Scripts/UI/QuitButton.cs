using UnityEngine;
using UnityEngine.UI;

namespace KungFuCrashout.UI
{
    /// <summary>
    /// Put on a UI Button (the Start screen's leave / Quit icon): tapping it closes the game with Application.Quit().
    /// In the Unity Editor it stops Play mode instead (Application.Quit does nothing there).
    /// Setup: Window → KungFu Crashout → GDD Build → Hook Up Start Screen Quit Button.
    /// Place in: Assets/_Project/Scripts/UI/QuitButton.cs
    /// </summary>
    [RequireComponent(typeof(Button))]
    public class QuitButton : MonoBehaviour
    {
        void Awake() => GetComponent<Button>().onClick.AddListener(Quit);

        public void Quit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
    }
}
