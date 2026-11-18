using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using KungFuCrashout.UI;

namespace KungFuCrashout.EditorTools
{
    /// <summary>
    /// Adds the "Heavy attack animation" playtest toggle to the pause menu's GAME page (under the item toggles).
    /// Safe to run again: the row is replaced in place. Set Up Monk Heavy Attack runs it too.
    /// Place in: Assets/_Project/Editor/FighterSettingToggleSetup.cs
    /// </summary>
    public static class FighterSettingToggleSetup
    {
        const string HeavyToggleName = "Heavy Animation Toggle";

        [MenuItem("Window/KungFu Crashout/GDD Build/Add Heavy Animation Toggle (pause menu)", priority = 10054)]
        static void Menu()
        {
            var log = new List<string>();
            if (AddToOpenScene(log)) Debug.Log("[GDD Build] " + string.Join("; ", log) + ". Save the scene (Ctrl+S).");
        }

        /// <summary>Adds (or rebuilds) the row in the open scene's pause menu. False if there's no pause menu.</summary>
        public static bool AddToOpenScene(List<string> log)
        {
            var options = Object.FindAnyObjectByType<OptionsMenu>(FindObjectsInactive.Include);
            var page = options ? new SerializedObject(options).FindProperty("gamePage").objectReferenceValue as GameObject : null;
            if (!page)
            {
                log.Add("No pause menu (OptionsMenu → Game Page) in the open scene: heavy toggle not added");
                return false;
            }
            var aiPanel = page.transform.Find("GDD AI Settings");
            var parent = aiPanel ? aiPanel : page.transform;

            var old = parent.Find(HeavyToggleName);
            if (old) Undo.DestroyObjectImmediate(old.gameObject);

            var toggle = GddUi.MakeToggle(HeavyToggleName, parent, "Heavy attack", 28f);
            GddUi.Place(toggle.gameObject, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -475f), new Vector2(1000f, 64f));
            Undo.RegisterCreatedObjectUndo(toggle.gameObject, "Add Heavy Animation Toggle");

            var comp = toggle.gameObject.AddComponent<FighterSettingToggle>();
            var so = new SerializedObject(comp);
            so.FindProperty("setting").enumValueIndex = (int)FighterSettingToggle.Setting.HeavyUsesAnimation;
            var label = toggle.transform.Find("Label");
            GddUi.Set(so, "label", label ? label.GetComponent<TMPro.TMP_Text>() : null);
            so.ApplyModifiedPropertiesWithoutUndo();

            EditorSceneManager.MarkSceneDirty(page.scene);
            log.Add("Pause menu GAME page: 'Heavy attack' toggle (ticked = new animation, unticked = original heavy)");
            return true;
        }
    }
}
