#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace KungFuCrashout.EditorTools
{
    /// <summary>
    /// Landscape only, from the very first frame (startup logos included).
    /// Player Settings → Resolution and Presentation:
    ///   • Default Orientation = Auto Rotation, with only Landscape Left / Landscape Right allowed (portrait and
    ///     upside-down off). On Android that becomes screenOrientation "sensorLandscape" in the manifest, so the OS opens
    ///     the app in landscape and it flips between the two landscape sides, never portrait.
    /// Applied automatically when the editor loads (only if something differs) and again before every build, so it
    /// can't drift back. GameBootstrap does the same at runtime as a second safety net.
    /// Turn off: Window → KungFu Crashout → Build → Enforce Landscape Only (untick).
    /// Place in: Assets/_Project/Editor/LandscapeOnlySettings.cs
    /// </summary>
    [InitializeOnLoad]
    public class LandscapeOnlySettings : IPreprocessBuildWithReport
    {
        const string Pref = "KFC_EnforceLandscapeOnly";
        const string MenuPath = "Window/KungFu Crashout/Build/Enforce Landscape Only";

        static bool Enabled
        {
            get => EditorPrefs.GetBool(Pref, true);
            set => EditorPrefs.SetBool(Pref, value);
        }

        static LandscapeOnlySettings() => EditorApplication.delayCall += () => Apply(false);

        public int callbackOrder => 0;
        public void OnPreprocessBuild(BuildReport report) => Apply(true);

        [MenuItem(MenuPath, priority = 10200)]
        static void Toggle()
        {
            Enabled = !Enabled;
            Apply(true);
        }

        [MenuItem(MenuPath, true)]
        static bool ToggleValidate()
        {
            Menu.SetChecked(MenuPath, Enabled);
            return true;
        }

        static void Apply(bool log)
        {
            if (!Enabled) return;
            bool changed = PlayerSettings.defaultInterfaceOrientation != UIOrientation.AutoRotation
                           || PlayerSettings.allowedAutorotateToPortrait
                           || PlayerSettings.allowedAutorotateToPortraitUpsideDown
                           || !PlayerSettings.allowedAutorotateToLandscapeLeft
                           || !PlayerSettings.allowedAutorotateToLandscapeRight;
            if (!changed) { if (log) Debug.Log("[Build] Landscape only: already set."); return; }
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.AutoRotation;
            PlayerSettings.allowedAutorotateToPortrait = false;
            PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;
            PlayerSettings.allowedAutorotateToLandscapeLeft = true;
            PlayerSettings.allowedAutorotateToLandscapeRight = true;
            AssetDatabase.SaveAssets();
            Debug.Log("[Build] Landscape only: Default Orientation = Auto Rotation (Landscape Left / Right only, no portrait). " +
                      "The app now opens in landscape, startup logos included.");
        }
    }
}
#endif
