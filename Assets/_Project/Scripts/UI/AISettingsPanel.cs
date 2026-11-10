using TMPro;
using UnityEngine;
using UnityEngine.UI;
using KungFuCrashout.AI;
using KungFuCrashout.Core;
using KungFuCrashout.DebugTools;

namespace KungFuCrashout.UI
{
    /// <summary>
    /// The pause menu's GAME page in the GDD build (replaces Prototype 2's State Machine / Practice Dummy toggle):
    ///   • Opponent: BEHAVIOUR TREE AI (on) or PRACTICE DUMMY (off → the TrainingDummy, standing still, for drills)
    ///   • Difficulty: Easy / Normal / Hard (saved; the AI switches tier straight away)
    ///   • Show AI thinking: the AI's current behaviour-tree branch over its head (testing / marking)
    ///   • Resume (primary) and Quit to Start
    /// Setup: KungFu Crashout → GDD Build → Upgrade Fight Scene builds it into the Options popup.
    /// Place in: Assets/_Project/Scripts/UI/AISettingsPanel.cs
    /// </summary>
    public class AISettingsPanel : MonoBehaviour
    {
        [SerializeField] BehaviourTreeAI brain;
        [SerializeField] TrainingDummy dummy;
        [SerializeField] Toggle aiToggle;
        [SerializeField] TMP_Text aiLabel;
        [Tooltip("Easy, Normal, Hard.")]
        [SerializeField] Button[] difficultyButtons = new Button[3];
        [SerializeField] Toggle thinkingToggle;
        [SerializeField] Button resumeButton;
        [SerializeField] Button quitButton;
        [SerializeField] OptionsMenu menu;
        [SerializeField] string startScene = "StartScreen";
        [SerializeField] Color selected = new Color(1f, 0.78f, 0.2f);
        [SerializeField] Color normal = new Color(0.25f, 0.25f, 0.3f);

        const string KeyAiOn = "kfc.gdd.aiOn";
        const string KeyThinking = "kfc.gdd.aiThinking";

        void Awake()
        {
            if (!menu) menu = GetComponentInParent<OptionsMenu>(true);
            if (aiToggle) aiToggle.onValueChanged.AddListener(SetAi);
            if (thinkingToggle) thinkingToggle.onValueChanged.AddListener(SetThinking);
            for (int i = 0; i < difficultyButtons.Length; i++)
            {
                var d = (Difficulty)i;
                if (difficultyButtons[i]) difficultyButtons[i].onClick.AddListener(() => SetDifficulty(d));
            }
            if (resumeButton) resumeButton.onClick.AddListener(() => { if (menu) menu.Close(); });
            if (quitButton) quitButton.onClick.AddListener(() => SceneLoader.Load(startScene));
        }

        void Start()
        {
            bool aiOn = PlayerPrefs.GetInt(KeyAiOn, 1) == 1;
            bool thinking = PlayerPrefs.GetInt(KeyThinking, 0) == 1;
            if (aiToggle) aiToggle.SetIsOnWithoutNotify(aiOn);
            if (thinkingToggle) thinkingToggle.SetIsOnWithoutNotify(thinking);
            SetAi(aiOn);
            SetThinking(thinking);
            RefreshDifficulty();
        }

        void SetAi(bool on)
        {
            if (brain) brain.enabled = on;
            if (dummy)
            {
                dummy.Mode = TrainingDummy.BehaviourMode.Dummy;
                dummy.enabled = !on;
            }
            if (aiLabel)
                aiLabel.text = on ? $"Opponent: BEHAVIOUR TREE AI  ({GameSettings.Difficulty})"
                                  : "Opponent: PRACTICE DUMMY  (Training Dummy settings)";
            PlayerPrefs.SetInt(KeyAiOn, on ? 1 : 0);
        }

        void SetThinking(bool on)
        {
            if (brain) brain.ShowDebugLabel = on;
            PlayerPrefs.SetInt(KeyThinking, on ? 1 : 0);
        }

        void SetDifficulty(Difficulty d)
        {
            GameSettings.Difficulty = d;
            if (brain) brain.RefreshTier();
            RefreshDifficulty();
            if (aiToggle) SetAi(aiToggle.isOn);
        }

        void RefreshDifficulty()
        {
            int current = (int)GameSettings.Difficulty;
            for (int i = 0; i < difficultyButtons.Length; i++)
            {
                var b = difficultyButtons[i];
                if (!b) continue;
                if (b.targetGraphic) b.targetGraphic.color = i == current ? selected : normal;
                var label = b.GetComponentInChildren<TMP_Text>();
                if (label) label.color = i == current ? Color.black : Color.white; // contrast on gold
            }
        }
    }
}
