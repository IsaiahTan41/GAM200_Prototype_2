#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using KungFuCrashout.Fighters;

namespace KungFuCrashout.EditorTools
{
    /// <summary>
    /// Menu: Window → KungFu Crashout → Tuning → Apply Air Combo Values (tune_v3)   (and → Revert Air Combo Values)
    /// Writes the knock-up → air combo values (tracker column tune_v3) into the project, the same as editing them in the
    /// Inspector (Undo works):
    ///   Special_Uppercut   Recovery 0.40 → 0.20, Launch Direction X 0.1 → 0
    ///   Uppercut_Launch    Launch Gravity 20 → 17
    ///   Monk_AirAttack     Knockback 0.35 → 0.1
    ///   Monk_AirAttack2    Knockback 0.30 → 0.1
    ///   FightScene         both fighters' Stunned Bounce Speed 3.75 → 5.5 (Bounce on juggled hit)
    /// Saves the assets and FightScene. Values saved on the TUNING tab override these in play: press Reset All there.
    /// Place in: Assets/_Project/Editor/AirComboValuesSetup.cs
    /// </summary>
    public static class AirComboValuesSetup
    {
        const string Moves = "Assets/_Project/Data/Attacks/";
        const string FightPath = "Assets/_Project/Scenes/FightScene.unity";

        [MenuItem("Window/KungFu Crashout/Tuning/Apply Air Combo Values (tune_v3)", priority = 10310)]
        static void Apply() => Set(0.2f, 0f, 17f, 0.1f, 0.1f, 5.5f, "tune_v3 air-combo values applied");

        [MenuItem("Window/KungFu Crashout/Tuning/Revert Air Combo Values (tune_v2)", priority = 10311)]
        static void Revert() => Set(0.4f, 0.1f, 20f, 0.35f, 0.3f, 3.75f, "air-combo values reverted to tune_v2");

        static void Set(float upRecovery, float launchX, float launchGravity, float air1Kb, float air2Kb, float bounce, string what)
        {
            if (EditorApplication.isPlaying) { EditorUtility.DisplayDialog("Air Combo Values", "Stop Play mode first.", "OK"); return; }
            Undo.SetCurrentGroupName(what);
            int group = Undo.GetCurrentGroup();
            var log = new System.Text.StringBuilder();

            var up = Load<AttackData>("Special_Uppercut");
            if (up)
            {
                var so = new SerializedObject(up);
                Write(so, "recovery", upRecovery, log, up.name);
                var dir = so.FindProperty("launchDirection");
                log.Append($"{up.name}.launchDirection.x {dir.vector2Value.x:0.##} → {launchX:0.##}; ");
                dir.vector2Value = new Vector2(launchX, dir.vector2Value.y);
                so.ApplyModifiedProperties();
            }
            var launch = Load<ThrowableData>("Uppercut_Launch");
            if (launch) { var so = new SerializedObject(launch); Write(so, "launchGravity", launchGravity, log, launch.name); so.ApplyModifiedProperties(); }
            var a1 = Load<AttackData>("Monk_AirAttack");
            if (a1) { var so = new SerializedObject(a1); Write(so, "knockback", air1Kb, log, a1.name); so.ApplyModifiedProperties(); }
            var a2 = Load<AttackData>("Monk_AirAttack2");
            if (a2) { var so = new SerializedObject(a2); Write(so, "knockback", air2Kb, log, a2.name); so.ApplyModifiedProperties(); }
            AssetDatabase.SaveAssets();

            // Bounce on juggled hit lives on each fighter in FightScene (the TUNING tab sets both).
            var active = EditorSceneManager.GetActiveScene();
            bool open = active.path == FightPath;
            if (!open && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) { Undo.CollapseUndoOperations(group); return; }
            var scene = open ? active : EditorSceneManager.OpenScene(FightPath, OpenSceneMode.Additive);
            int fighters = 0;
            foreach (var root in scene.GetRootGameObjects())
                foreach (var f in root.GetComponentsInChildren<Fighter>(true))
                {
                    var so = new SerializedObject(f);
                    Write(so, "stunnedBounceSpeed", bounce, log, f.name);
                    so.ApplyModifiedProperties();
                    fighters++;
                }
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            if (!open) EditorSceneManager.CloseScene(scene, true);
            Undo.CollapseUndoOperations(group);
            Debug.Log($"[Tuning] {what} ({fighters} fighters). {log}Saved. Press Reset All on the TUNING tab so saved tab values don't override them.");
        }

        static void Write(SerializedObject so, string field, float value, System.Text.StringBuilder log, string owner)
        {
            var p = so.FindProperty(field);
            if (p == null) { log.Append($"{owner}.{field} not found; "); return; }
            log.Append($"{owner}.{field} {p.floatValue:0.##} → {value:0.##}; ");
            p.floatValue = value;
        }

        static T Load<T>(string name) where T : Object => AssetDatabase.LoadAssetAtPath<T>(Moves + name + ".asset");
    }
}
#endif
