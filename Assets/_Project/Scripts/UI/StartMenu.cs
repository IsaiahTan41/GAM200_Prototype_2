using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace KungFuCrashout.UI
{
    /// <summary>
    /// The start screen: START loads the fight, QUIT closes the app (stops Play mode in the editor).
    /// Android Back / Esc on the start screen also quits.
    /// Setup: KungFu Crashout → Set Up Start Screen + Back Button. Place in: Assets/_Project/Scripts/UI/StartMenu.cs
    /// </summary>
    public class StartMenu : MonoBehaviour
    {
        [SerializeField] Button startButton;
        [SerializeField] Button quitButton;
        [Tooltip("Scene the START button loads.")]
        [SerializeField] string fightScene = "FightScene";
        [Tooltip("Android Back button / Esc quits from the start screen.")]
        [SerializeField] bool backButtonQuits = true;

        void Awake()
        {
            Time.timeScale = 1f;
            if (startButton) startButton.onClick.AddListener(StartGame);
            if (quitButton) quitButton.onClick.AddListener(SceneLoader.Quit);
        }

        void Update()
        {
            var kb = Keyboard.current;
            if (backButtonQuits && kb != null && kb.escapeKey.wasPressedThisFrame) SceneLoader.Quit();
        }

        public void StartGame() => SceneLoader.Load(fightScene);
    }
}
