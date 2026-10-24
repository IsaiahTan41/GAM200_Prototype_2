using UnityEngine;
using UnityEngine.InputSystem;

namespace KungFuCrashout.DebugTools
{
    /// <summary>
    /// M0 sanity check: shows resolution, safe area, FPS and live touch count on screen.
    /// Attach to any GameObject in the scene. Remove once M1 is working.
    /// Place this file in: Assets/_Project/Scripts/DebugTools/DeviceCheck.cs
    /// </summary>
    public class DeviceCheck : MonoBehaviour
    {
        float fps;
        GUIStyle style;

        void Update()
        {
            float current = 1f / Mathf.Max(Time.unscaledDeltaTime, 0.0001f);
            fps = Mathf.Lerp(fps, current, 0.1f);
        }

        void OnGUI()
        {
            if (style == null)
            {
                style = new GUIStyle(GUI.skin.label)
                {
                    fontSize = Mathf.RoundToInt(Screen.height * 0.022f)
                };
                style.normal.textColor = Color.white;
            }

            var touchscreen = Touchscreen.current;
            int activeTouches = 0;
            if (touchscreen != null)
            {
                foreach (var touch in touchscreen.touches)
                    if (touch.press.isPressed) activeTouches++;
            }

            Rect safe = Screen.safeArea;
            string text =
                "KungFu Crashout - M0 check\n" +
                $"Resolution: {Screen.width} x {Screen.height}\n" +
                $"Safe area: {safe.width:0} x {safe.height:0}\n" +
                $"Orientation: {Screen.orientation}\n" +
                $"FPS: {fps:0}\n" +
                $"Touchscreen found: {(touchscreen != null ? "yes" : "no")}\n" +
                $"Active touches: {activeTouches}";

            // GUI coordinates are top-down; safeArea is bottom-up.
            float top = Screen.height - safe.yMax;
            GUI.Label(new Rect(safe.x + 30, top + 30, safe.width - 60, safe.height - 60), text, style);
        }
    }
}