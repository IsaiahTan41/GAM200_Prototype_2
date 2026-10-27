using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using KungFuCrashout.Fighters;

namespace KungFuCrashout.Core
{
    /// <summary>
    /// Prototype round loop: when ANY watched fighter's health reaches 0, wait Reset Delay (real seconds, so the KO
    /// slow-motion / hit freeze plays out), then RELOAD the whole scene: both fighters back at full health and their
    /// start positions, items, timers and AI all fresh. The background music keeps playing (MusicPlayer persists).
    ///
    /// Leave Fighters empty to watch every Fighter in the scene. Turn off TrainingDummy → Practice Mode on the fighters,
    /// or it will revive them before the reset.
    /// Remove (or turn off) once MatchManager runs real rounds.
    /// Setup: KungFu Crashout → Add Round Reset. Place in: Assets/_Project/Scripts/Core/RoundReset.cs
    /// </summary>
    public class RoundReset : MonoBehaviour
    {
        [Tooltip("Fighters whose KO resets the scene. Empty = every Fighter in the scene.")]
        [SerializeField] Fighter[] fighters = new Fighter[0];
        [Tooltip("Seconds (real time) between the KO and the reload, so the KO can be seen.")]
        [SerializeField, Min(0f)] float resetDelay = 2f;
        [Tooltip("Turn the automatic reset on/off (also while playing).")]
        [SerializeField] bool resetOnKO = true;

        bool resetting;

        void Start()
        {
            if (fighters == null || fighters.Length == 0)
                fighters = FindObjectsByType<Fighter>(FindObjectsSortMode.None);

            foreach (var f in fighters)
                if (f && f.Health) f.Health.Died += OnDied;
        }

        void OnDestroy()
        {
            foreach (var f in fighters)
                if (f && f.Health) f.Health.Died -= OnDied;
        }

        void OnDied()
        {
            if (!resetOnKO || resetting) return;
            resetting = true;
            StartCoroutine(ResetAfterDelay());
        }

        IEnumerator ResetAfterDelay()
        {
            Debug.Log($"[RoundReset] KO! Reloading the scene in {resetDelay:0.#} s");
            if (resetDelay > 0f) yield return new WaitForSecondsRealtime(resetDelay);
            ReloadScene();
        }

        /// <summary>Reload the current scene now (also usable from a UI button).</summary>
        public void ReloadScene()
        {
            Time.timeScale = 1f; // never carry a hit freeze / slow motion into the new round
            var scene = SceneManager.GetActiveScene();

            if (scene.buildIndex >= 0)
            {
                SceneManager.LoadScene(scene.buildIndex);
                return;
            }
#if UNITY_EDITOR
            // Not in Build Settings yet: the editor can still reload it by path.
            UnityEditor.SceneManagement.EditorSceneManager.LoadSceneInPlayMode(
                scene.path, new LoadSceneParameters(LoadSceneMode.Single));
#else
            Debug.LogError("[RoundReset] This scene isn't in File → Build Settings, so it can't be reloaded. Add it there.");
#endif
        }
    }
}
