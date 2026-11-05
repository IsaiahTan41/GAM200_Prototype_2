#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using KungFuCrashout.Fighters;
using KungFuCrashout.UI;

namespace KungFuCrashout.EditorTools
{
    /// <summary>
    /// Menu: KungFu Crashout → Add Stamina Bars
    /// For every Fighter with a Stamina component, adds a small "[Fighter] Stamina Bar" Slider (no handle, not
    /// interactive) to the HUD canvas, with FighterStaminaBar so it follows just beneath that fighter.
    /// Existing bars are reused (re-run safely). Undoable. Save the scene afterwards.
    /// Place in: Assets/_Project/Editor/StaminaBarSetup.cs
    /// </summary>
    public static class StaminaBarSetup
    {
        static readonly Vector2 BarSize = new Vector2(150f, 16f); // reference units (canvas scales with screen)

        [MenuItem("KungFu Crashout/Add Stamina Bars", priority = 62)]
        public static void Add()
        {
            Canvas canvas = null;
            foreach (var c in Object.FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (c.isRootCanvas && c.renderMode == RenderMode.ScreenSpaceOverlay) { canvas = c; break; }
            if (!canvas)
            {
                EditorUtility.DisplayDialog("Add Stamina Bars",
                    "No Screen Space Overlay canvas found. Run KungFu Crashout → Fix Health Bar Layout first (it sets up the HUD canvas).", "OK");
                return;
            }

            Undo.SetCurrentGroupName("Add Stamina Bars");
            int group = Undo.GetCurrentGroup();
            string report = "";

            foreach (var fighter in Object.FindObjectsByType<Fighter>(FindObjectsSortMode.None))
            {
                if (!fighter.GetComponent<Stamina>()) { report += $"\n  {fighter.name}: skipped (no Stamina component)"; continue; }

                string barName = fighter.name + " Stamina Bar";
                var existing = canvas.transform.Find(barName);
                GameObject go = existing ? existing.gameObject : CreateBar(barName, canvas.transform);

                var bar = go.GetComponent<FighterStaminaBar>();
                if (!bar) bar = Undo.AddComponent<FighterStaminaBar>(go);
                var so = new SerializedObject(bar);
                so.FindProperty("fighter").objectReferenceValue = fighter;
                so.ApplyModifiedProperties();
                report += $"\n  {fighter.name}: {barName}";
            }

            Undo.CollapseUndoOperations(group);
            EditorSceneManager.MarkSceneDirty(canvas.gameObject.scene);
            Debug.Log($"[Stamina bars] Added / updated:{report}\nThey follow beneath each fighter in Play mode. Save the scene (Ctrl+S).");
        }

        static GameObject CreateBar(string barName, Transform parent)
        {
            var go = DefaultControls.CreateSlider(new DefaultControls.Resources());
            go.name = barName;
            Undo.RegisterCreatedObjectUndo(go, "Create " + barName);
            go.transform.SetParent(parent, false);

            var slider = go.GetComponent<Slider>();
            // A display, not a control: remove the handle.
            if (slider.handleRect) Object.DestroyImmediate(slider.handleRect.parent.gameObject);
            slider.handleRect = null;
            slider.interactable = false;
            slider.transition = Selectable.Transition.None;
            slider.value = 1f;

            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = BarSize;

            // Fill area to the full bar (the default leaves room for the handle).
            var fillArea = (RectTransform)slider.fillRect.parent;
            fillArea.offsetMin = Vector2.zero;
            fillArea.offsetMax = Vector2.zero;
            slider.fillRect.offsetMin = Vector2.zero;
            slider.fillRect.offsetMax = Vector2.zero;

            var bg = go.transform.Find("Background")?.GetComponent<Image>();
            if (bg) bg.color = new Color(0f, 0f, 0f, 0.55f);
            var fill = slider.fillRect.GetComponent<Image>();
            if (fill) fill.color = new Color(0.3f, 0.85f, 1f);

            return go;
        }
    }
}
#endif
