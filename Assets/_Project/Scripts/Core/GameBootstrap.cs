using UnityEngine;

namespace KungFuCrashout.Core
{
    /// <summary>
    /// Runs automatically before the first scene loads. No GameObject needed.
    ///   • 60 FPS (mobile defaults to 30)
    ///   • screen never sleeps mid-fight
    ///   • LANDSCAPE ONLY: follows the phone between landscape-left and landscape-right,
    ///     never rotates to portrait
    /// Place this file in: Assets/_Project/Scripts/Core/GameBootstrap.cs
    /// </summary>
    public static class GameBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Init()
        {
            // Mobile defaults to 30 FPS. A fighting game needs 60 for responsive input.
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = 60;

            // Don't let the screen dim mid-fight.
            Screen.sleepTimeout = SleepTimeout.NeverSleep;

            // Landscape only, either way round. Matches Player Settings (ProjectSetup)
            // in case device settings disagree.
            Screen.autorotateToPortrait = false;
            Screen.autorotateToPortraitUpsideDown = false;
            Screen.autorotateToLandscapeLeft = true;
            Screen.autorotateToLandscapeRight = true;
            Screen.orientation = ScreenOrientation.AutoRotation;
        }
    }
}