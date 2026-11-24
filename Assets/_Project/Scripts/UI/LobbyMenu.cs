using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using KungFuCrashout.Core;

namespace KungFuCrashout.UI
{
    /// <summary>
    /// Online 1v1 lobby (GamConcepts_UI_17 + Online 1v1 UI Notes.txt). Layout only for now: no networking.
    ///   • Shown exactly as concept 17 (P1 ready, P2 waiting, room code copied, Join available with its glow).
    ///   • READY: the button switches to its selected art and "Waiting for opponent" (Aladin 50, wave), as concept 18,
    ///     for Waiting Time, then the Roster loads as usual.
    ///   • Copy / Join are live buttons (press tint) with nothing hooked up yet. The Join glow pulses gently.
    ///   • Back lantern / Android Back → Start screen.
    /// Built by Window → KungFu Crashout → GDD Build → Build Online Lobby (Concept 17).
    /// Place in: Assets/_Project/Scripts/UI/LobbyMenu.cs
    /// </summary>
    public class LobbyMenu : MonoBehaviour
    {
        [Header("Ready")]
        [SerializeField] Button readyButton;
        [SerializeField] Sprite readySelected;
        [SerializeField] TMP_Text readyText;
        [SerializeField] TMP_Text waitingText;
        [Tooltip("How long \"Waiting for opponent\" shows before the Roster loads.")]
        [SerializeField, Min(0f)] float waitingTime = 1.6f;
        [SerializeField] string nextScene = "Roster";

        [Header("Join glow")]
        [SerializeField] Image joinGlow;
        [SerializeField] bool pulseGlow = true;
        [SerializeField, Range(0f, 1f)] float pulseDepth = 0.25f;
        [SerializeField, Min(0.1f)] float pulseSpeed = 1.2f;

        [Header("Back")]
        [SerializeField] Button backButton;
        [SerializeField] string backScene = "StartScreen";

        float loadAt = -1f;
        bool committed;

        void Awake()
        {
            Time.timeScale = 1f;
            if (readyButton) readyButton.onClick.AddListener(Ready);
            if (backButton) backButton.onClick.AddListener(Back);
            if (waitingText) waitingText.gameObject.SetActive(false);
        }

        void Update()
        {
            var kb = Keyboard.current;
            if (!committed && kb != null)
            {
                if (kb.escapeKey.wasPressedThisFrame) Back();
                if (kb.enterKey.wasPressedThisFrame) Ready();
            }
            if (joinGlow && pulseGlow)
            {
                var c = joinGlow.color;
                c.a = 1f - pulseDepth * (0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * pulseSpeed * Mathf.PI * 2f));
                joinGlow.color = c;
            }
            if (loadAt > 0f && Time.unscaledTime >= loadAt) { loadAt = -1f; SceneLoader.Load(nextScene); }
        }

        void Ready()
        {
            if (committed) return;
            committed = true;
            KungFuCrashout.Sound.UiTapSound.PlayBeforeDelayedLoad(); // on the press; the delayed load won't play it again
            if (readyButton)
            {
                if (readySelected && readyButton.targetGraphic is Image img) img.sprite = readySelected;
                readyButton.interactable = false;
            }
            if (readyText) readyText.gameObject.SetActive(false);
            if (waitingText) waitingText.gameObject.SetActive(true);
            if (backButton) backButton.interactable = false;
            loadAt = Time.unscaledTime + waitingTime;
            if (waitingTime <= 0f) { loadAt = -1f; SceneLoader.Load(nextScene); }
        }

        void Back()
        {
            if (committed) return;
            SceneLoader.Load(backScene);
        }
    }
}
