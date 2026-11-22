using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;
using KungFuCrashout.Core;
using KungFuCrashout.Fighters;

namespace KungFuCrashout.UI
{
    /// <summary>
    /// Win / lose screen from GamConcepts_UI_25 (DEFEAT) and _26 (VICTORY). Shown when the match ends:
    ///   • The player's fighter won  → VICTORY background + banner (sun).
    ///   • The player's fighter lost → DEFEAT background + banner (moon).
    ///   • Draw (no art yet)         → DEFEAT background, no banner, "DRAW" in the callout style.
    /// Behind both: the fight scene (no UI) rendered once and blurred, with the translucent background on top.
    /// Names: the winner's in yellow, the loser's in grey. Coins = rounds won. Record and streak from PlayerStats.
    /// START → start screen · CHANGE FIGHTER → roster · REMATCH → the same fight again (MatchManager).
    ///
    /// Use Concept End Screen OFF = the old "Match Over" panel instead (testing / fallback).
    /// Built by KungFu Crashout → GDD Build → Build Win-Lose Screen (Concepts 25 + 26).
    /// Place in: Assets/_Project/Scripts/UI/EndScreenConcept.cs
    /// </summary>
    public class EndScreenConcept : MonoBehaviour
    {
        [Tooltip("ON: this screen at the end of the match (the old Match Over panel is kept hidden). OFF: the old panel.")]
        [SerializeField] bool useConceptEndScreen = true;
        [SerializeField] MatchManager match;
        [Tooltip("The old Match Over panel (MatchHUD), hidden while this screen is in use.")]
        [SerializeField] GameObject legacyPanel;

        [Header("Layers")]
        [SerializeField] GameObject screen;
        [SerializeField] CanvasGroup group;
        [SerializeField] RawImage blur;
        [SerializeField] Image background;
        [SerializeField] RectTransform content;

        [Header("Art")]
        [SerializeField] Sprite victoryBackground;
        [SerializeField] Sprite defeatBackground;
        [SerializeField] Image banner;
        [SerializeField] Sprite victoryBanner;
        [SerializeField] Sprite defeatBanner;
        [Tooltip("Banner top, in concept pixels from the top of the 2424 × 1080 layout.")]
        [SerializeField] float victoryBannerTop = 0f;
        [SerializeField] float defeatBannerTop = 46f;
        [SerializeField] TMP_Text drawText;

        [Header("Fighters")]
        [SerializeField] TMP_Text playerName;
        [SerializeField] TMP_Text opponentName;
        [Tooltip("Inner coin first (next to the name), outer coin last.")]
        [SerializeField] Image[] playerCoins = new Image[2];
        [SerializeField] Image[] opponentCoins = new Image[2];
        [SerializeField] Sprite coinLit;
        [SerializeField] Sprite coinUnlit;
        [SerializeField] Color winnerColour = new Color32(0xEC, 0xD9, 0x98, 0xFF);
        [SerializeField] Color loserColour = new Color32(0xC8, 0xCA, 0xC9, 0xFF);
        [SerializeField] Color drawColour = new Color32(0xF3, 0xE7, 0xCF, 0xFF);

        [Header("Record")]
        [SerializeField] TMP_Text recordText;
        [SerializeField] TMP_Text streakText;

        [Header("Buttons")]
        [SerializeField] Button startButton;
        [SerializeField] Button changeFighterButton;
        [SerializeField] Button rematchButton;
        [SerializeField] Image rematchGlow;
        [SerializeField, Min(0f)] float glowPulseSpeed = 1.2f;
        [SerializeField, Range(0f, 1f)] float glowPulseDepth = 0.25f;

        [Header("Timing (real seconds)")]
        [Tooltip("Wait after the match ends (KO slow motion, the YOU WIN / YOU LOSE call) before the screen comes in.")]
        [SerializeField, Min(0f)] float showDelay = 1.8f;
        [SerializeField, Min(0.01f)] float fadeTime = 0.35f;
        [SerializeField, Min(0.01f)] float bannerTime = 0.45f;
        [SerializeField] float bannerDrop = 60f;

        [Header("Blur")]
        [Tooltip("Halvings of the captured fight scene (more = blurrier). 4 ≈ the concept's soft blur.")]
        [SerializeField, Range(1, 6)] int blurSteps = 4;
        [Tooltip("Shown if the fight scene can't be captured (e.g. no main camera).")]
        [SerializeField] Texture fallbackBackground;

        RenderTexture blurTexture;
        bool shown;
        float shownAt;

        public bool UseConceptEndScreen { get => useConceptEndScreen; set => useConceptEndScreen = value; }

        void Awake()
        {
            if (!match) match = FindAnyObjectByType<MatchManager>();
            if (screen) screen.SetActive(false);
            if (startButton) startButton.onClick.AddListener(() => Go(() => match.QuitToStart()));
            if (changeFighterButton) changeFighterButton.onClick.AddListener(() => Go(() => match.ChangeFighter()));
            if (rematchButton) rematchButton.onClick.AddListener(() => Go(() => match.Rematch()));
        }

        void OnEnable()
        {
            if (match) match.MatchEnded += OnMatchEnded;
        }

        void OnDisable()
        {
            if (match) match.MatchEnded -= OnMatchEnded;
        }

        void OnDestroy()
        {
            if (blurTexture) { blurTexture.Release(); Destroy(blurTexture); }
        }

        void OnMatchEnded(Fighter winner)
        {
            if (!useConceptEndScreen) return;
            HideLegacy();
            StartCoroutine(ShowAfterDelay(winner));
        }

        void LateUpdate()
        {
            if (!useConceptEndScreen) return;
            // The old panel opens itself on the same event; keep it closed while this screen is in use.
            if (match && match.Phase == MatchPhase.MatchOver) HideLegacy();
            if (shown && rematchGlow)
            {
                float t = (Time.unscaledTime - shownAt) * glowPulseSpeed * Mathf.PI * 2f;
                var c = rematchGlow.color;
                c.a = 1f - glowPulseDepth * (0.5f + 0.5f * Mathf.Sin(t));
                rematchGlow.color = c;
            }
        }

        void HideLegacy()
        {
            if (legacyPanel && legacyPanel.activeSelf) legacyPanel.SetActive(false);
        }

        IEnumerator ShowAfterDelay(Fighter winner)
        {
            if (showDelay > 0f) yield return new WaitForSecondsRealtime(showDelay);
            Show(winner);
        }

        /// <summary>Fills the screen for this result and brings it in.</summary>
        public void Show(Fighter winner)
        {
            if (!screen) return;
            var player = match ? match.Player : null;
            var opponent = match ? match.Opponent : null;
            bool won = winner && winner == player;
            bool lost = winner && winner == opponent;

            CaptureBlur();

            if (background) background.sprite = won ? victoryBackground : defeatBackground;
            if (banner)
            {
                var sprite = won ? victoryBanner : lost ? defeatBanner : null;
                banner.gameObject.SetActive(sprite);
                if (sprite)
                {
                    banner.sprite = sprite;
                    var rt = banner.rectTransform;
                    rt.sizeDelta = new Vector2(sprite.rect.width, sprite.rect.height);
                    rt.anchoredPosition = new Vector2(0f, -(won ? victoryBannerTop : defeatBannerTop));
                }
            }
            if (drawText) drawText.gameObject.SetActive(!won && !lost);

            SetName(playerName, player, won ? winnerColour : lost ? loserColour : drawColour);
            SetName(opponentName, opponent, lost ? winnerColour : won ? loserColour : drawColour);
            SetCoins(playerCoins, match ? match.PlayerWins : 0);
            SetCoins(opponentCoins, match ? match.OpponentWins : 0);

            if (recordText)
                recordText.text = $"Record: {PlayerStats.Wins}W · {PlayerStats.Losses}L" + (PlayerStats.Draws > 0 ? $" · {PlayerStats.Draws}D" : "");
            if (streakText) streakText.text = $"Streak: {PlayerStats.Streak} (best {PlayerStats.BestStreak})";
            if (recordText) LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)recordText.transform.parent);

            SetButtons(true);
            screen.SetActive(true);
            shown = true;
            shownAt = Time.unscaledTime;
            StopCoroutine(nameof(Intro));
            StartCoroutine(nameof(Intro));
        }

        static void SetName(TMP_Text text, Fighter f, Color colour)
        {
            if (!text) return;
            text.text = f ? f.DisplayName : "";
            text.color = colour;
        }

        void SetCoins(Image[] coins, int won)
        {
            for (int i = 0; i < coins.Length; i++)
                if (coins[i]) coins[i].sprite = i < won ? coinLit : coinUnlit;
        }

        void SetButtons(bool on)
        {
            if (startButton) startButton.interactable = on;
            if (changeFighterButton) changeFighterButton.interactable = on;
            if (rematchButton) rematchButton.interactable = on;
        }

        void Go(System.Action action)
        {
            if (!match) return;
            SetButtons(false); // one tap only
            action();
        }

        IEnumerator Intro()
        {
            var bannerRt = banner ? banner.rectTransform : null;
            Vector2 bannerEnd = bannerRt ? bannerRt.anchoredPosition : Vector2.zero;
            float t = 0f;
            float total = Mathf.Max(fadeTime, bannerTime);
            while (t < total)
            {
                t += Time.unscaledDeltaTime;
                float f = Mathf.Clamp01(t / fadeTime);
                if (group) group.alpha = f;
                if (bannerRt)
                {
                    float b = 1f - Mathf.Pow(1f - Mathf.Clamp01(t / bannerTime), 3f); // ease out
                    bannerRt.anchoredPosition = bannerEnd + new Vector2(0f, bannerDrop * (1f - b));
                }
                yield return null;
            }
            if (group) group.alpha = 1f;
            if (bannerRt) bannerRt.anchoredPosition = bannerEnd;
        }

        // ───────────── Blurred fight scene ─────────────

        /// <summary>Renders the main camera once (the canvas is an overlay, so no UI) and blurs it by halving.</summary>
        void CaptureBlur()
        {
            if (!blur) return;
            var cam = Camera.main;
            Texture result = null;
            if (cam)
            {
                int w = Mathf.Max(64, Screen.width / 2), h = Mathf.Max(64, Screen.height / 2);
                var full = RenderTexture.GetTemporary(w, h, 24, RenderTextureFormat.Default);
                var temps = new List<RenderTexture> { full };
                try
                {
                    var request = new RenderPipeline.StandardRequest();
                    if (RenderPipeline.SupportsRenderRequest(cam, request))
                    {
                        request.destination = full;
                        RenderPipeline.SubmitRenderRequest(cam, request);
                    }
                    else
                    {
                        var previous = cam.targetTexture;
                        cam.targetTexture = full;
                        cam.Render();
                        cam.targetTexture = previous;
                    }

                    RenderTexture src = full;
                    for (int i = 0; i < blurSteps; i++)
                    {
                        w = Mathf.Max(8, w / 2);
                        h = Mathf.Max(8, h / 2);
                        var down = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.Default);
                        down.filterMode = FilterMode.Bilinear;
                        Graphics.Blit(src, down);
                        temps.Add(down);
                        src = down;
                    }
                    for (int i = 0; i < 2; i++) // back up a little: smoother than stretching the smallest one
                    {
                        var up = RenderTexture.GetTemporary(src.width * 2, src.height * 2, 0, RenderTextureFormat.Default);
                        up.filterMode = FilterMode.Bilinear;
                        Graphics.Blit(src, up);
                        temps.Add(up);
                        src = up;
                    }

                    if (!blurTexture || blurTexture.width != src.width || blurTexture.height != src.height)
                    {
                        if (blurTexture) { blurTexture.Release(); Destroy(blurTexture); }
                        blurTexture = new RenderTexture(src.width, src.height, 0, RenderTextureFormat.Default) { name = "End Screen Blur", filterMode = FilterMode.Bilinear };
                    }
                    Graphics.Blit(src, blurTexture);
                    result = blurTexture;
                }
                catch (System.Exception ex)
                {
                    Debug.LogWarning("[End Screen] Couldn't capture the fight scene: " + ex.Message);
                }
                finally
                {
                    foreach (var rt in temps) RenderTexture.ReleaseTemporary(rt);
                }
            }
            blur.texture = result ? result : fallbackBackground;
            blur.color = blur.texture ? Color.white : Color.black;
        }

#if UNITY_EDITOR
        [ContextMenu("Preview Victory (Play mode)")]
        void PreviewVictory() { if (Application.isPlaying && match) Show(match.Player); }
        [ContextMenu("Preview Defeat (Play mode)")]
        void PreviewDefeat() { if (Application.isPlaying && match) Show(match.Opponent); }
        [ContextMenu("Preview Draw (Play mode)")]
        void PreviewDraw() { if (Application.isPlaying) Show(null); }
        [ContextMenu("Hide (Play mode)")]
        void PreviewHide() { if (screen) screen.SetActive(false); shown = false; }
#endif
    }
}
