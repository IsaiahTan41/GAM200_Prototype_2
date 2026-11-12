using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using KungFuCrashout.Props;
using KungFuCrashout.UI;

namespace KungFuCrashout.EditorTools
{
    /// <summary>
    /// Adds the playtesting "Original item behaviour" toggle to the pause menu's GAME page (under the AI settings).
    /// Safe to run again: an existing toggle is replaced in place. Upgrade Fight Scene adds it too.
    /// Place in: Assets/_Project/Editor/ItemModeToggleSetup.cs
    /// </summary>
    public static class ItemModeToggleSetup
    {
        const string ToggleName = "Item Mode Toggle";

        [MenuItem("Window/KungFu Crashout/GDD Build/Add Item Mode Toggle (pause menu)", priority = 10053)]
        static void AddToOpenScene()
        {
            var options = Object.FindAnyObjectByType<OptionsMenu>(FindObjectsInactive.Include);
            if (!options)
            {
                EditorUtility.DisplayDialog("Item Mode Toggle", "No OptionsMenu in the open scene. Open FightScene first.", "OK");
                return;
            }
            var page = new SerializedObject(options).FindProperty("gamePage").objectReferenceValue as GameObject;
            if (!page)
            {
                EditorUtility.DisplayDialog("Item Mode Toggle", "The OptionsMenu has no Game Page assigned.", "OK");
                return;
            }

            var log = new List<string>();
            var aiPanel = page.transform.Find("GDD AI Settings");
            AddTo(aiPanel ? aiPanel : page.transform, log);
            EditorSceneManager.MarkSceneDirty(page.scene);
            Debug.Log("[GDD Build] " + string.Join("; ", log) + ". Save the scene (Ctrl+S).");
        }

        /// <summary>Adds (or rebuilds) the toggle under a parent on the pause menu's GAME page.</summary>
        public static void AddTo(Transform parent, List<string> log)
        {
            var old = parent.Find(ToggleName);
            if (old) Undo.DestroyObjectImmediate(old.gameObject);

            var toggle = GddUi.MakeToggle(ToggleName, parent, "Items: GDD rules", 30f);
            GddUi.Place(toggle.gameObject, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -335f), new Vector2(1000f, 70f));
            Undo.RegisterCreatedObjectUndo(toggle.gameObject, "Add Item Mode Toggle");

            var comp = toggle.gameObject.AddComponent<ItemModeToggle>();
            var so = new SerializedObject(comp);
            GddUi.Set(so, "director", Object.FindAnyObjectByType<StageItemDirector>(FindObjectsInactive.Include));
            var label = toggle.transform.Find("Label");
            GddUi.Set(so, "label", label ? label.GetComponent<TMPro.TMP_Text>() : null);
            so.ApplyModifiedPropertiesWithoutUndo();

            log.Add("Pause menu GAME page: 'Original item behaviour' playtest toggle (ticked = items live where placed)");
        }
    }
}
