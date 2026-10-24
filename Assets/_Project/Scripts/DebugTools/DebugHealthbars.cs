using UnityEngine;
using KungFuCrashout.Fighters;

namespace KungFuCrashout.DebugTools
{
    /// <summary>
    /// Temporary on-screen health bars and combo counter for testing (works on the phone).
    /// The real HUD from the mockup replaces this in M7.
    /// Place in: Assets/_Project/Scripts/DebugTools/DebugHealthBars.cs
    /// </summary>
    public class DebugHealthBars : MonoBehaviour
    {
        [SerializeField] Fighter leftFighter;
        [SerializeField] Fighter rightFighter;
        [Tooltip("How far down the screen the bars sit (0 = top edge). Moves them clear of the M0 DeviceCheck text.")]
        [SerializeField, Range(0f, 0.5f)] float topOffset = 0.22f;

        GUIStyle label, comboLabel;

        void OnGUI()
        {
            if (label == null)
            {
                label = new GUIStyle(GUI.skin.label) { fontSize = Mathf.RoundToInt(Screen.height * 0.018f) };
                label.normal.textColor = Color.white;

                comboLabel = new GUIStyle(label)
                {
                    fontSize = Mathf.RoundToInt(Screen.height * 0.03f),
                    fontStyle = FontStyle.Bold
                };
                comboLabel.normal.textColor = new Color(1f, 0.85f, 0.2f);
            }

            Rect safe = Screen.safeArea;
            float top = (Screen.height - safe.yMax) + Screen.height * topOffset;
            float margin = 30f;
            float barW = safe.width * 0.5f - margin * 1.5f;
            float barH = Screen.height * 0.022f;

            DrawFighter(leftFighter, new Rect(safe.x + margin, top, barW, barH), alignRight: false);
            DrawFighter(rightFighter, new Rect(safe.x + safe.width * 0.5f + margin * 0.5f, top, barW, barH), alignRight: true);
        }

        void DrawFighter(Fighter f, Rect bar, bool alignRight)
        {
            if (!f || !f.Health) return;

            // Background + fill (the right bar drains toward the centre, like the mockup).
            DrawRect(bar, new Color(0f, 0f, 0f, 0.6f));
            float fillW = bar.width * f.Health.Normalized;
            var fill = alignRight
                ? new Rect(bar.x, bar.y, fillW, bar.height)
                : new Rect(bar.xMax - fillW, bar.y, fillW, bar.height);
            Color colour = Color.Lerp(new Color(0.9f, 0.15f, 0.1f), new Color(0.2f, 0.85f, 0.3f), f.Health.Normalized);
            DrawRect(fill, colour);

            float line = label.fontSize * 1.4f;
            GUI.Label(new Rect(bar.x, bar.yMax + 4f, bar.width, line),
                $"{f.name}  {f.Health.Current:0}/{f.Health.Max:0}  [{f.State}]", label);

            if (f.ComboHitsTaken >= 2)
                GUI.Label(new Rect(bar.x, bar.yMax + 4f + line, bar.width, comboLabel.fontSize * 1.4f),
                    $"{f.ComboHitsTaken} HIT COMBO!", comboLabel);
        }

        static void DrawRect(Rect r, Color c)
        {
            var old = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = old;
        }
    }
}