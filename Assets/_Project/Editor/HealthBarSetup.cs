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
    /// Menu: KungFu Crashout → Hook Up Health Bars
    /// Adds FighterHealthBar to every Slider in the open scene whose name contains "Health" and assigns its
    /// fighter: names containing "Enemy" → the "Opponent" fighter, the others → the "Player" fighter.
    /// Safe to run again; undoable (Ctrl+Z). Save the scene afterwards.
    /// Place in: Assets/_Project/Editor/HealthBarSetup.cs
    /// </summary>
    public static class HealthBarSetup
    {
        [MenuItem("KungFu Crashout/Hook Up Health Bars", priority = 61)]
        public static void HookUp()
        {
            Fighter player = null, opponent = null;
            foreach (var f in Object.FindObjectsByType<Fighter>(FindObjectsSortMode.None))
            {
                string n = f.name.ToLowerInvariant();
                if (n.Contains("opponent") || n.Contains("enemy")) opponent = f;
                else if (n.Contains("player")) player = f;
            }

            Undo.SetCurrentGroupName("Hook Up Health Bars");
            int group = Undo.GetCurrentGroup();
            int count = 0;
            string report = "";

            foreach (var slider in Object.FindObjectsByType<Slider>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (!slider.name.ToLowerInvariant().Contains("health")) continue;

                bool enemy = slider.name.ToLowerInvariant().Contains("enemy");
                var target = enemy ? opponent : player;

                var bar = slider.GetComponent<FighterHealthBar>();
                if (!bar) bar = Undo.AddComponent<FighterHealthBar>(slider.gameObject);

                var so = new SerializedObject(bar);
                so.FindProperty("fighter").objectReferenceValue = target;
                if (slider.fillRect)
                    so.FindProperty("fill").objectReferenceValue = slider.fillRect.GetComponent<Image>();
                so.ApplyModifiedProperties();

                Undo.RecordObject(slider, "Slider settings");
                slider.interactable = false;
                slider.transition = Selectable.Transition.None;

                count++;
                report += $"\n  {slider.name} → {(target ? target.name : "NO FIGHTER FOUND")}";
                EditorSceneManager.MarkSceneDirty(slider.gameObject.scene);
            }

            Undo.CollapseUndoOperations(group);

            if (count == 0)
                EditorUtility.DisplayDialog("Hook Up Health Bars", "No Slider with \"Health\" in its name was found in the open scene.", "OK");
            else
                Debug.Log($"[Health bars] Hooked up {count} slider(s):{report}\nSave the scene (Ctrl+S).");
        }
    }
}
#endif
