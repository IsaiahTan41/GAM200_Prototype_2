#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

namespace KungFuCrashout.EditorTools
{
    /// <summary>
    /// One-click project setup for KungFu Crashout (M0).
    /// Adds a "KungFu Crashout" menu to the Unity menu bar.
    /// Place this file in: Assets/_Project/Editor/ProjectSetup.cs
    /// </summary>
    public static class ProjectSetup
    {
        const string Company  = "Je-gnomes";
        const string Product  = "KungFu Crashout";
        const string BundleId = "com.jegnomes.kungfucrashout";

        // Parents must come before children.
        static readonly string[] Folders =
        {
            "Assets/_Project",
            "Assets/_Project/Scenes",
            "Assets/_Project/Scripts",
            "Assets/_Project/Scripts/Core",
            "Assets/_Project/Scripts/Fighters",
            "Assets/_Project/Scripts/Input",
            "Assets/_Project/Scripts/AI",
            "Assets/_Project/Scripts/Environment",
            "Assets/_Project/Scripts/UI",
            "Assets/_Project/Scripts/Feedback",
            "Assets/_Project/Scripts/DebugTools",
            "Assets/_Project/Data",
            "Assets/_Project/Prefabs",
            "Assets/_Project/Art",
            "Assets/_Project/Audio",
            "Assets/_Project/Editor",
        };

        [MenuItem("KungFu Crashout/Run Full Setup", priority = 0)]
        public static void RunAll()
        {
            CreateFolders();
            ApplyPlayerSettings();
            SwitchToAndroid();
            Debug.Log("[ProjectSetup] Full setup complete.");
        }

        [MenuItem("KungFu Crashout/1. Create Folders", priority = 20)]
        public static void CreateFolders()
        {
            foreach (var path in Folders)
            {
                if (AssetDatabase.IsValidFolder(path)) continue;
                int slash = path.LastIndexOf('/');
                AssetDatabase.CreateFolder(path.Substring(0, slash), path.Substring(slash + 1));
            }
            AssetDatabase.Refresh();
            Debug.Log("[ProjectSetup] Folders created.");
        }

        [MenuItem("KungFu Crashout/2. Apply Android Landscape Settings", priority = 21)]
        public static void ApplyPlayerSettings()
        {
            var android = NamedBuildTarget.Android;

            // Identity
            PlayerSettings.companyName = Company;
            PlayerSettings.productName = Product;
            PlayerSettings.SetApplicationIdentifier(android, BundleId);

            // Landscape only: auto-rotate between the two landscape directions, never portrait
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.AutoRotation;
            PlayerSettings.allowedAutorotateToPortrait = false;
            PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;
            PlayerSettings.allowedAutorotateToLandscapeLeft = true;
            PlayerSettings.allowedAutorotateToLandscapeRight = true;

            // 64-bit Android (required by modern devices / Google Play)
            PlayerSettings.SetScriptingBackend(android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.Android.targetSdkVersion = AndroidSdkVersions.AndroidApiLevelAuto;

            // Development builds let you see logs and use the Profiler on device
            EditorUserBuildSettings.development = true;

            AssetDatabase.SaveAssets();
            Debug.Log("[ProjectSetup] Player Settings applied (Landscape, IL2CPP, ARM64).");
        }

        [MenuItem("KungFu Crashout/3. Switch Platform to Android", priority = 22)]
        public static void SwitchToAndroid()
        {
            if (EditorUserBuildSettings.activeBuildTarget == BuildTarget.Android)
            {
                Debug.Log("[ProjectSetup] Already on Android.");
                return;
            }
            EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android);
            Debug.Log("[ProjectSetup] Switched platform to Android.");
        }
    }
}
#endif