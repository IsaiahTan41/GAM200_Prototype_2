#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using KungFuCrashout.UI;

namespace KungFuCrashout.EditorTools
{
    /// <summary>
    /// Menu: KungFu Crashout → Fix Health Bar Layout
    ///
    /// Makes the health bars keep the same size and place on every phone and tablet:
    ///   • Canvas: Screen Space Overlay + Canvas Scaler "Scale With Screen Size", reference 1920×1080, match HEIGHT
    ///     → 1 UI unit is the same fraction of screen height everywhere; only the usable width changes.
    ///   • A full-screen "SafeArea" panel (SafeAreaFitter) under the Canvas, so nothing sits under a notch/punch-hole.
    ///   • Every Slider with "Health" in its name moves under SafeArea and is laid out as a top bar:
    ///       Player bar  → left  BarWidthFraction of the screen, Enemy bar ("Enemy" in the name) → right,
    ///       fixed height and margins (reference units), so they never overlap and always hug their corner.
    ///     Enemy bar fills right-to-left (drains toward the centre, like the player's), the classic fighting-game look.
    /// Run again after changing the numbers below. Undoable. Save the scene afterwards.
    /// Place in: Assets/_Project/Editor/HudLayoutSetup.cs
    /// </summary>
    public static class HudLayoutSetup
    {
        static readonly Vector2 ReferenceResolution = new Vector2(1920f, 1080f);
        const float MatchHeight = 1f;          // landscape phones vary in width, not height
        const float BarWidthFraction = 0.42f;  // each bar's share of the (safe) screen width
        const float BarHeight = 96f;           // reference units (the visible bar is the middle half: ~48)
        const float SideMargin = 48f;          // from the screen edge
        const float TopMargin = 28f;           // from the top of the safe area

        [MenuItem("KungFu Crashout/Fix Health Bar Layout", priority = 65)]
        public static void Fix()
        {
            Slider sample = null;
            foreach (var s in Object.FindObjectsByType<Slider>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (s.name.ToLowerInvariant().Contains("health")) { sample = s; break; }
            if (!sample)
            {
                EditorUtility.DisplayDialog("Fix Health Bar Layout", "No Slider with \"Health\" in its name found in the open scene.", "OK");
                return;
            }

            var canvas = sample.GetComponentInParent<Canvas>().rootCanvas;

            Undo.SetCurrentGroupName("Fix Health Bar Layout");
            int group = Undo.GetCurrentGroup();

            // ── Canvas + scaler ──
            Undo.RecordObject(canvas, "Canvas");
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            var scaler = canvas.GetComponent<CanvasScaler>();
            if (!scaler) scaler = Undo.AddComponent<CanvasScaler>(canvas.gameObject);
            Undo.RecordObject(scaler, "Canvas Scaler");
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = ReferenceResolution;
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = MatchHeight;

            // ── Safe area panel ──
            var safeT = canvas.transform.Find("SafeArea") as RectTransform;
            if (!safeT)
            {
                var go = new GameObject("SafeArea", typeof(RectTransform));
                Undo.RegisterCreatedObjectUndo(go, "Create SafeArea");
                safeT = (RectTransform)go.transform;
                safeT.SetParent(canvas.transform, false);
                safeT.SetAsFirstSibling();
            }
            Undo.RecordObject(safeT, "SafeArea rect");
            safeT.anchorMin = Vector2.zero;
            safeT.anchorMax = Vector2.one;
            safeT.offsetMin = Vector2.zero;
            safeT.offsetMax = Vector2.zero;
            safeT.localScale = Vector3.one;
            if (!safeT.GetComponent<SafeAreaFitter>()) Undo.AddComponent<SafeAreaFitter>(safeT.gameObject);

            // ── Bars ──
            string report = "";
            foreach (var slider in Object.FindObjectsByType<Slider>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (!slider.name.ToLowerInvariant().Contains("health")) continue;
                bool enemy = slider.name.ToLowerInvariant().Contains("enemy");

                if (slider.transform.parent != safeT)
                    Undo.SetTransformParent(slider.transform, safeT, "Move bar under SafeArea");

                var rt = (RectTransform)slider.transform;
                Undo.RecordObject(rt, "Bar layout");
                rt.localScale = Vector3.one;
                rt.localRotation = Quaternion.identity;
                rt.pivot = new Vector2(enemy ? 1f : 0f, 1f);

                if (!enemy)
                {
                    rt.anchorMin = new Vector2(0f, 1f);
                    rt.anchorMax = new Vector2(BarWidthFraction, 1f);
                    rt.offsetMin = new Vector2(SideMargin, -TopMargin - BarHeight);
                    rt.offsetMax = new Vector2(0f, -TopMargin);
                }
                else
                {
                    rt.anchorMin = new Vector2(1f - BarWidthFraction, 1f);
                    rt.anchorMax = new Vector2(1f, 1f);
                    rt.offsetMin = new Vector2(0f, -TopMargin - BarHeight);
                    rt.offsetMax = new Vector2(-SideMargin, -TopMargin);
                }

                Undo.RecordObject(slider, "Bar direction");
                slider.direction = enemy ? Slider.Direction.RightToLeft : Slider.Direction.LeftToRight;

                report += $"\n  {slider.name}: {(enemy ? "right" : "left")} {BarWidthFraction:P0} of width, {BarHeight} high";
            }

            Undo.CollapseUndoOperations(group);
            EditorSceneManager.MarkSceneDirty(canvas.gameObject.scene);
            Selection.activeObject = safeT.gameObject;
            Debug.Log($"[HUD] Canvas → Overlay, Scale With Screen Size {ReferenceResolution.x}×{ReferenceResolution.y} (match height); " +
                      $"bars under SafeArea:{report}\nSave the scene (Ctrl+S). Check a few shapes in the Game view's aspect dropdown or the Device Simulator.");
        }
    }
}
#endif
