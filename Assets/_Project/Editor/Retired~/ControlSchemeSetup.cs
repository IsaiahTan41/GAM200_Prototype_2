#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using KungFuCrashout.Controls;
using KungFuCrashout.DebugTools;

namespace KungFuCrashout.EditorTools
{
    /// <summary>
    /// Menu: KungFu Crashout → Set Up Control Schemes
    ///
    /// Makes sure only ONE touch scheme reads the screen at a time. With both TouchInputHandler (split zones) and
    /// TapAnywhereInputHandler on, every touch was read twice: e.g. a two-finger block's right-hand finger also became a
    /// split-zones hold → heavy attack, which cancels the block.
    ///   • Adds (or updates) a ControlSchemeSwitcher on the object that holds the handlers and assigns both
    ///   • Sets the scheme to Tap Anywhere (the one with the two-finger block) and leaves only that handler enabled
    ///   • Hooks the Defense Debug HUD to the switcher, so its block hint matches the active scheme
    /// Switch schemes later with the switcher's Scheme dropdown (live in Play mode) or Tab in the editor.
    /// Undoable. Save the scene afterwards.
    /// Place in: Assets/_Project/Editor/ControlSchemeSetup.cs
    /// </summary>
    public static class ControlSchemeSetup
    {
        [MenuItem("KungFu Crashout/Set Up Control Schemes", priority = 66)]
        public static void SetUp()
        {
            var tap = Object.FindFirstObjectByType<TapAnywhereInputHandler>(FindObjectsInactive.Include);
            var split = Object.FindFirstObjectByType<TouchInputHandler>(FindObjectsInactive.Include);
            if (!tap && !split)
            {
                EditorUtility.DisplayDialog("Set Up Control Schemes", "No TouchInputHandler or TapAnywhereInputHandler in the open scene.", "OK");
                return;
            }

            Undo.SetCurrentGroupName("Set Up Control Schemes");
            int group = Undo.GetCurrentGroup();

            var host = (tap ? tap : (Component)split).gameObject;
            var switcher = Object.FindFirstObjectByType<ControlSchemeSwitcher>(FindObjectsInactive.Include);
            if (!switcher) switcher = Undo.AddComponent<ControlSchemeSwitcher>(host);

            var so = new SerializedObject(switcher);
            so.FindProperty("splitZones").objectReferenceValue = split;
            so.FindProperty("tapAnywhere").objectReferenceValue = tap;
            so.FindProperty("scheme").enumValueIndex = tap
                ? (int)ControlSchemeSwitcher.Scheme.TapAnywhere
                : (int)ControlSchemeSwitcher.Scheme.SplitZones;
            so.ApplyModifiedProperties();

            // Match the enabled flags to the chosen scheme in the saved scene too (the switcher also does it on Awake).
            if (tap) { Undo.RecordObject(tap, "Enable Tap Anywhere"); tap.enabled = true; }
            if (split && tap) { Undo.RecordObject(split, "Disable Split Zones"); split.enabled = false; }

            var hud = Object.FindFirstObjectByType<DefenseDebugHUD>(FindObjectsInactive.Include);
            if (hud)
            {
                var hso = new SerializedObject(hud);
                var sw = hso.FindProperty("schemeSwitcher");
                var ta = hso.FindProperty("tapAnywhere");
                if (sw != null) sw.objectReferenceValue = switcher;
                if (ta != null) ta.objectReferenceValue = tap;
                hso.ApplyModifiedProperties();
            }

            Undo.CollapseUndoOperations(group);
            EditorSceneManager.MarkSceneDirty(host.scene);
            Selection.activeObject = host;

            Debug.Log($"[Controls] ControlSchemeSwitcher on '{host.name}', scheme {(tap ? "Tap Anywhere" : "Split Zones")}; " +
                      $"only that handler is enabled{(hud ? ", Defense Debug HUD hooked up" : "")}. Save the scene (Ctrl+S).");
        }
    }
}
#endif
