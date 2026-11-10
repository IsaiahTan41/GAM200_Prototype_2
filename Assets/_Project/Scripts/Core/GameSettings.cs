using UnityEngine;
using UnityEngine.Audio;

namespace KungFuCrashout.Core
{
    public enum Difficulty { Easy, Normal, Hard }

    /// <summary>
    /// Player settings that survive closing the app: AI difficulty, music and SFX volume.
    ///
    /// Replaces the settings half of the Udemy sample's SaveScript (static fields that reset on
    /// every launch) and the storage side of OptionsScript. Values are saved to PlayerPrefs as
    /// soon as they change, because Android can kill the app at any time in the background.
    /// Volumes are 0–1 here and converted to decibels for the mixer, so sliders feel even.
    ///
    /// No GameObject needed; it's a static class.
    /// Place in: Assets/_Project/Scripts/Core/GameSettings.cs
    /// </summary>
    public static class GameSettings
    {
        // Exposed AudioMixer parameter names (same names the Udemy mixers used).
        public const string MusicParam = "MusicLevel";
        public const string SfxParam = "SFXLevel";

        const string DifficultyKey = "kfc.difficulty";
        const string MusicKey = "kfc.musicVolume";
        const string SfxKey = "kfc.sfxVolume";

        public static Difficulty Difficulty
        {
            get => (Difficulty)PlayerPrefs.GetInt(DifficultyKey, (int)Difficulty.Normal);
            set { PlayerPrefs.SetInt(DifficultyKey, (int)value); PlayerPrefs.Save(); }
        }

        /// <summary>0 = silent, 1 = full.</summary>
        public static float MusicVolume
        {
            get => PlayerPrefs.GetFloat(MusicKey, 0.8f);
            set { PlayerPrefs.SetFloat(MusicKey, Mathf.Clamp01(value)); PlayerPrefs.Save(); }
        }

        /// <summary>0 = silent, 1 = full.</summary>
        public static float SfxVolume
        {
            get => PlayerPrefs.GetFloat(SfxKey, 1f);
            set { PlayerPrefs.SetFloat(SfxKey, Mathf.Clamp01(value)); PlayerPrefs.Save(); }
        }

        /// <summary>Push the saved volumes into the mixer. Call on scene start and after a slider moves.</summary>
        public static void ApplyVolumes(AudioMixer mixer)
        {
            if (!mixer) return;
            mixer.SetFloat(MusicParam, ToDecibels(MusicVolume));
            mixer.SetFloat(SfxParam, ToDecibels(SfxVolume));
        }

        /// <summary>0–1 slider value → mixer decibels (-80 dB is silent).</summary>
        public static float ToDecibels(float volume) =>
            volume <= 0.0001f ? -80f : Mathf.Log10(volume) * 20f;
    }
}
