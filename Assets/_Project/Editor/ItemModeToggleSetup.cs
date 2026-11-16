using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using KungFuCrashout.Props;
using KungFuCrashout.UI;

namespace KungFuCrashout.EditorTools
{
    /// <summary>
    /// Adds the playtesting switches to the pause menu's GAME page (under the AI settings):
    ///   Item Mode Toggle      — "Original item behaviour" (items live where placed) vs the GDD sleeping-item rules
    ///   Shockwave Mode Toggle — "Original shockwaves" (nearest item, one roll) vs Radius (every item in reach rolls)
    /// Safe to run again: existing rows are replaced in place. Upgrade Fight Scene adds them too.
    /// Place in: Assets/_Project/Editor/ItemModeToggleSetup.cs
    /// </summary>
    public static class ItemModeToggleSetup
    {
        const string ItemToggleName = "Item Mode Toggle";
        const string ShockwaveToggleName = "Shockwave Mode Toggle";

        [MenuItem("Window/KungFu Crashout/GDD Build/Add Item Mode Toggles (pause menu)", priority = 10053)]
        static void AddToOpenScene()
        {
            var options = Object.FindAnyObjectByType<OptionsMenu>(FindObjectsInactive.Include);
            if (!options)
            {
                EditorUtility.DisplayDialog("Item Mode Toggles", "No OptionsMenu in the open scene. Open FightScene first.", "OK");
                return;
            }
            var page = new SerializedObject(options).FindProperty("gamePage").objectReferenceValue as GameObject;
            if (!page)
            {
                EditorUtility.DisplayDialog("Item Mode Toggles", "The OptionsMenu has no Game Page assigned.", "OK");
                return;
            }

            var log = new List<string>();
            var aiPanel = page.transform.Find("GDD AI Settings");
            AddTo(aiPanel ? aiPanel : page.transform, log);
            EditorSceneManager.MarkSceneDirty(page.scene);
            Debug.Log("[GDD Build] " + string.Join("; ", log) + ". Save the scene (Ctrl+S).");
        }

        /// <summary>Adds (or rebuilds) both rows under a parent on the pause menu's GAME page.</summary>
        public static void AddTo(Transform parent, List<string> log)
        {
            AddRow(parent, ItemToggleName, ItemModeToggle.Setting.OriginalItemBehaviour, -335f);
            AddRow(parent, ShockwaveToggleName, ItemModeToggle.Setting.OriginalShockwaves, -405f);
            log.Add("Pause menu GAME page: 'Original item behaviour' and 'Original shockwaves' playtest toggles");
        }

        static void AddRow(Transform parent, string objectName, ItemModeToggle.Setting setting, float y)
        {
            var old = parent.Find(objectName);
            if (old) Undo.DestroyObjectImmediate(old.gameObject);

            var toggle = GddUi.MakeToggle(objectName, parent, objectName, 28f);
            GddUi.Place(toggle.gameObject, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, y), new Vector2(1000f, 64f));
            Undo.RegisterCreatedObjectUndo(toggle.gameObject, "Add " + objectName);

            var comp = toggle.gameObject.AddComponent<ItemModeToggle>();
            var so = new SerializedObject(comp);
            so.FindProperty("setting").enumValueIndex = (int)setting;
            GddUi.Set(so, "director", Object.FindAnyObjectByType<StageItemDirector>(FindObjectsInactive.Include));
            var label = toggle.transform.Find("Label");
            GddUi.Set(so, "label", label ? label.GetComponent<TMPro.TMP_Text>() : null);
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
