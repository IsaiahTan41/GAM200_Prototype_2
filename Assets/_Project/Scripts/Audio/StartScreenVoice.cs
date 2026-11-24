using System.Collections;
using UnityEngine;

namespace KungFuCrashout.Sound
{
    /// <summary>
    /// Start screen: the announcer's "Press Start" as the screen first appears, right after the startup logos (Unity's
    /// splash screen). Waits until the splash has finished (SplashScreen.isFinished), so it never plays under the logos,
    /// then Delay After Logos, then plays Voice through AudioManager (follows the SFX volume setting).
    /// Every time the Start screen loads by default; Only On App Launch = just the first time after the app starts.
    /// Setup: Window → KungFu Crashout → Audio → Set Up Start Screen Voice. Place in: Assets/_Project/Scripts/Audio/StartScreenVoice.cs
    /// </summary>
    public class StartScreenVoice : MonoBehaviour
    {
        [Tooltip("The voice line (SFX_PressStart).")]
        [SerializeField] SfxCue voice;
        [Tooltip("Real seconds after the logos finish (or the scene appears) before the voice plays.")]
        [SerializeField, Min(0f)] float delayAfterLogos = 0.25f;
        [Tooltip("Only the first time the Start screen opens after the app launches. Off = every time it loads " +
                 "(e.g. coming back from a match too).")]
        [SerializeField] bool onlyOnAppLaunch = false;

        static bool playedThisLaunch;

        IEnumerator Start()
        {
            if (!voice || (onlyOnAppLaunch && playedThisLaunch)) yield break;
            while (!UnityEngine.Rendering.SplashScreen.isFinished) yield return null; // after the startup logos
            if (delayAfterLogos > 0f) yield return new WaitForSecondsRealtime(delayAfterLogos);
            playedThisLaunch = true;
            AudioManager.Play(voice);
        }
    }
}
