using UnityEditor;
using UnityEngine;
using KungFuCrashout.Feedback;

namespace KungFuCrashout.EditorTools
{
    /// <summary>
    /// FighterVfx Inspector + Scene view:
    ///   • Scene view: four coloured dots (Feet, Strike, Hit, Body). Drag them to move where those effects appear;
    ///     the Positioning offsets update (X is stored "forward", so it mirrors when the fighter turns round).
    ///   • Inspector (Play mode): preview buttons that fire each effect on the spot.
    /// Play mode edits are lost when you stop: ⋮ → Copy Component, stop, ⋮ → Paste Component Values.
    /// Place in: Assets/_Project/Editor/FighterVfxEditor.cs
    /// </summary>
    [CustomEditor(typeof(FighterVfx))]
    public class FighterVfxEditor : Editor
    {
        // Repeat preview: replays the last previewed effect so its cue can be tuned live (Play mode).
        static bool repeat;
        static float repeatEvery = 1.2f;
        static FighterVfx.Preview last;
        static bool hasLast;
        static FighterVfx repeatTarget;
        static double nextAt;
        Editor cueEditor;

        void OnEnable() => EditorApplication.update += Tick;
        void OnDisable()
        {
            EditorApplication.update -= Tick;
            if (cueEditor) DestroyImmediate(cueEditor);
        }

        static void Tick()
        {
            if (!repeat || !hasLast || !Application.isPlaying || !repeatTarget) return;
            if (EditorApplication.timeSinceStartup < nextAt) return;
            nextAt = EditorApplication.timeSinceStartup + repeatEvery;
            repeatTarget.PlayPreview(last);
        }
        static readonly (FighterVfx.Anchor anchor, Color colour, string label)[] Anchors =
        {
            (FighterVfx.Anchor.Feet,   new Color(0.85f, 0.75f, 0.55f), "Feet (dust, lotus, dash, jump, land)"),
            (FighterVfx.Anchor.Strike, new Color(1f, 0.85f, 0.3f),     "Strike (slash)"),
            (FighterVfx.Anchor.Hit,    new Color(1f, 0.4f, 0.3f),      "Hit (burst, parry spark)"),
            (FighterVfx.Anchor.Body,   new Color(0.6f, 0.8f, 1f),      "Body (KO)"),
        };

        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            var fv = (FighterVfx)target;

            EditorGUILayout.Space();
            EditorGUILayout.HelpBox(
                "Move effects: select this fighter and drag the coloured dots in the Scene view (or type the offsets above).\n" +
                "Fine-tune one effect for every character: its VFX Cue → Placement → Offset.\n" +
                "Hit's dot is shown next to the strike; in a fight it sits between the two fighters.",
                MessageType.Info);

            EditorGUILayout.LabelField("Preview (Play mode)", EditorStyles.boldLabel);
            using (new EditorGUI.DisabledScope(!Application.isPlaying))
            {
                var values = (FighterVfx.Preview[])System.Enum.GetValues(typeof(FighterVfx.Preview));
                for (int i = 0; i < values.Length; i += 4)
                {
                    EditorGUILayout.BeginHorizontal();
                    for (int j = i; j < Mathf.Min(i + 4, values.Length); j++)
                        if (GUILayout.Button(ObjectNames.NicifyVariableName(values[j].ToString())))
                        {
                            fv.PlayPreview(values[j]);
                            last = values[j]; hasLast = true; repeatTarget = fv;
                            nextAt = EditorApplication.timeSinceStartup + repeatEvery;
                        }
                    EditorGUILayout.EndHorizontal();
                }
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                repeat = EditorGUILayout.ToggleLeft("Repeat the last preview every", repeat, GUILayout.Width(190));
                repeatEvery = Mathf.Max(0.3f, EditorGUILayout.FloatField(repeatEvery, GUILayout.Width(40)));
                GUILayout.Label("s");
            }

            // The previewed effect's own settings: Offset / Size / Start Frame edits show on the next repeat,
            // and are kept after Play mode (they live in the cue asset).
            if (hasLast && repeatTarget == fv)
            {
                var cue = fv.PreviewCue(last);
                EditorGUILayout.Space();
                EditorGUILayout.LabelField($"Effect: {ObjectNames.NicifyVariableName(last.ToString())}", EditorStyles.boldLabel);
                if (!cue) EditorGUILayout.HelpBox("No cue in the set in use for this effect.", MessageType.None);
                else
                {
                    EditorGUILayout.ObjectField("Cue (asset)", cue, typeof(VfxCue), false);
                    if (!cueEditor || cueEditor.target != cue)
                    {
                        if (cueEditor) DestroyImmediate(cueEditor);
                        cueEditor = CreateEditor(cue);
                    }
                    EditorGUILayout.HelpBox("Placement → Offset moves it (X = forward), Size → Scale resizes it, Animation → Start Delay " +
                                            "Frames lines it up with the character. Saved in the cue: kept after Play mode.", MessageType.Info);
                    using (new EditorGUI.IndentLevelScope()) cueEditor.OnInspectorGUI();
                }
            }

            if (Application.isPlaying)
                EditorGUILayout.HelpBox("FighterVfx changes made now are lost when you stop (cue edits are kept). " +
                                        "⋮ → Copy Component, stop, then ⋮ → Paste Component Values.", MessageType.Warning);
        }

        void OnSceneGUI()
        {
            var fv = (FighterVfx)target;
            if (!fv.ShowAnchors) return;

            foreach (var (anchor, colour, label) in Anchors)
            {
                Vector3 pos = fv.AnchorPosition(anchor);
                float size = HandleUtility.GetHandleSize(pos) * 0.08f;

                Handles.color = colour;
                EditorGUI.BeginChangeCheck();
                Vector3 moved = Handles.FreeMoveHandle(pos, size, Vector3.zero, Handles.DotHandleCap);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(fv, "Move VFX anchor");
                    moved.z = pos.z;
                    fv.SetAnchorPosition(anchor, moved);
                    PrefabUtility.RecordPrefabInstancePropertyModifications(fv);
                    EditorUtility.SetDirty(fv);
                }
                Handles.DrawWireDisc(pos, Vector3.forward, size * 2.2f);
                Handles.Label(pos + Vector3.right * size * 3f, label, EditorStyles.miniBoldLabel);
            }
        }
    }
}
