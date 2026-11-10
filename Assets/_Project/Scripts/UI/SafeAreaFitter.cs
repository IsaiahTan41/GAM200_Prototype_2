using UnityEngine;

namespace KungFuCrashout.UI
{
    /// <summary>
    /// Keeps a UI panel inside the phone's safe area (clear of punch-hole cameras, notches and the
    /// gesture bar). Put it on a full-screen RectTransform directly under the Canvas, then parent
    /// the HUD to that panel. Re-fits when the screen size, orientation or safe area changes.
    /// In the editor (not playing) it stays full screen, so layouts are authored on the whole 1920 × 1080 canvas and no
    /// odd editor-window size gets saved into the scene. A safe area that doesn't fit the screen is ignored.
    /// (KungFu Crashout → Fix Health Bar Layout sets this up.)
    /// Place in: Assets/_Project/Scripts/UI/SafeAreaFitter.cs
    /// </summary>
    [ExecuteAlways]
    [RequireComponent(typeof(RectTransform))]
    public class SafeAreaFitter : MonoBehaviour
    {
        RectTransform rt;
        Rect lastSafeArea;
        Vector2Int lastScreen;

        void OnEnable()
        {
            rt = GetComponent<RectTransform>();
            Fit();
        }

        void Update()
        {
            if (Screen.safeArea != lastSafeArea || lastScreen.x != Screen.width || lastScreen.y != Screen.height)
                Fit();
        }

        void Fit()
        {
            if (!rt || Screen.width <= 0 || Screen.height <= 0) return;

            Rect safe = Screen.safeArea;
            lastSafeArea = safe;
            lastScreen = new Vector2Int(Screen.width, Screen.height);

            var min = Vector2.zero;
            var max = Vector2.one;
            bool fits = safe.width > 0f && safe.height > 0f && safe.xMin >= 0f && safe.yMin >= 0f &&
                        safe.xMax <= Screen.width + 1f && safe.yMax <= Screen.height + 1f;
            if (Application.isPlaying && fits)
            {
                min = new Vector2(safe.xMin / Screen.width, safe.yMin / Screen.height);
                max = new Vector2(safe.xMax / Screen.width, safe.yMax / Screen.height);
            }
            rt.anchorMin = min;
            rt.anchorMax = max;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }
    }
}
