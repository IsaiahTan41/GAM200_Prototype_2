using System.Collections;
using UnityEngine;

namespace KungFuCrashout.UI
{
    /// <summary>
    /// Created by the pause menu when the fight loads (it's hidden, so the TUNING page itself can't run yet).
    /// On the first launch of a newly installed build it forgets values saved with the older build (Clear On New Build).
    /// Applies the saved tuning straight away, again one frame later (after every fighter has set up its move set),
    /// then every half second for values marked Sweep (items spawned later get the tuned throw speed).
    /// Place in: Assets/_Project/Scripts/UI/TuningRuntime.cs
    /// </summary>
    public class TuningRuntime : MonoBehaviour
    {
        static TuningRuntime instance;
        const string BuildKey = "kfc.tuning.meta.buildId";

        /// <summary>True when this launch is the first of a newly installed build and old tab values were cleared.</summary>
        public static bool ClearedForNewBuild { get; private set; }

        public static void Ensure(bool clearOnNewBuild = true)
        {
            if (instance) return;
            if (clearOnNewBuild) ClearIfNewBuild();
            instance = new GameObject("Tuning Runtime").AddComponent<TuningRuntime>();
            TuningRegistry.OnSceneLoaded();
        }

        /// <summary>
        /// A new build carries the team's latest balance in its own data, so values a tester saved on the phone with an
        /// older build are forgotten on the first launch of the new one. Not in the Editor (no build id there).
        /// </summary>
        static void ClearIfNewBuild()
        {
            if (Application.isEditor) return;
            string id = string.IsNullOrEmpty(Application.buildGUID) ? Application.version : Application.buildGUID;
            if (PlayerPrefs.GetString(BuildKey, "") == id) return;
            ClearedForNewBuild = TuningRegistry.AnySaved();
            TuningRegistry.ClearSaved();
            PlayerPrefs.SetString(BuildKey, id);
            PlayerPrefs.Save();
            if (ClearedForNewBuild) Debug.Log("[Tuning] New build installed: saved tuning values cleared.");
        }

        IEnumerator Start()
        {
            yield return null;
            TuningRegistry.ApplySaved();
            var wait = new WaitForSecondsRealtime(0.5f);
            while (true)
            {
                yield return wait;
                TuningRegistry.Sweep();
            }
        }

        void OnDestroy()
        {
            if (instance == this) instance = null;
        }
    }
}
