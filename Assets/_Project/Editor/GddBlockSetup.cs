#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using KungFuCrashout.Controls;
using KungFuCrashout.Props;

namespace KungFuCrashout.EditorTools
{
    /// <summary>
    /// Menu: Window → KungFu Crashout → Controls → Use GDD Block (Two Fingers)
    /// Sets the touch block back to the GDD's design: press TWO fingers on the screen together and keep holding = block;
    /// landing them together just before a hit = parry / item catch. A quick double tap with one finger is then just
    /// two attacks (the combo), not a block.
    ///   • Every TapAnywhereInputHandler: Block Input = Two Finger Hold.
    ///   • ItemTapHandler: if its pick-up was a two-finger swipe down, it's set to Tap, so pressing two fingers to block
    ///     can't be read as a pick-up.
    /// Undoable. Save the scene afterwards.
    /// Place in: Assets/_Project/Editor/GddBlockSetup.cs
    /// </summary>
    public static class GddBlockSetup
    {
        [MenuItem("Window/KungFu Crashout/Controls/Use GDD Block (Two Fingers)", priority = 10000)]
        public static void Apply()
        {
            var handlers = Object.FindObjectsByType<TapAnywhereInputHandler>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            if (handlers.Length == 0)
            {
                EditorUtility.DisplayDialog("GDD Block", "No TapAnywhereInputHandler in the open scene (open FightScene).", "OK");
                return;
            }

            Undo.SetCurrentGroupName("Use GDD Block");
            int group = Undo.GetCurrentGroup();
            string report = "";

            foreach (var h in handlers)
            {
                var so = new SerializedObject(h);
                var input = so.FindProperty("blockInput");
                string before = input.enumDisplayNames[input.enumValueIndex];
                input.enumValueIndex = (int)TapAnywhereInputHandler.BlockInput.TwoFingerHold;
                so.ApplyModifiedProperties();
                float window = so.FindProperty("twoFingerWindow").floatValue;
                report += $"\n  {h.name}: Block Input {before} → Two Finger Hold (fingers must land within {window:0.00} s)";
            }

            foreach (var t in Object.FindObjectsByType<ItemTapHandler>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                var so = new SerializedObject(t);
                var gesture = so.FindProperty("pickUpGesture");
                if (gesture != null && gesture.enumValueIndex != (int)ItemTapHandler.PickUpGesture.Tap)
                {
                    gesture.enumValueIndex = (int)ItemTapHandler.PickUpGesture.Tap;
                    so.ApplyModifiedProperties();
                    report += $"\n  {t.name}: Pick Up Gesture → Tap (two-finger swipe pick-up would clash with the block)";
                }
            }

            Undo.CollapseUndoOperations(group);
            EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
            Debug.Log("[Controls] GDD block: two fingers together + hold = block, timed = parry / catch. One-finger double tap = attacks." +
                      report + "\n  Save the scene (Ctrl+S), then rebuild for Android.");
        }
    }
}
#endif
