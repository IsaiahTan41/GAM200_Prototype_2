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
                            fv.PlayPreview(values[j]);
                    EditorGUILayout.EndHorizontal();
                }
            }
            if (Application.isPlaying)
                EditorGUILayout.HelpBox("Changes made now are lost when you stop. ⋮ → Copy Component, stop, then ⋮ → Paste Component Values.",
                                        MessageType.Warning);
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
