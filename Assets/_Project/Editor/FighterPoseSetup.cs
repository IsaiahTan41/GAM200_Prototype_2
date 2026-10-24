#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using KungFuCrashout.Fighters;

namespace KungFuCrashout.EditorTools
{
    /// <summary>
    /// Menu: KungFu Crashout → Set Up Fighter Poses
    ///
    /// 1. Sets the import settings of the pose drawings in Assets/_Project/Art/Characters/
    ///    (Sprite, Single, 1000 pixels per unit, pivot at the feet, transparent, no mipmaps, max 2048).
    /// 2. On every Fighter in the open scene: creates a child "Visual" (Sprite Renderer in front of the
    ///    Fighter's box, its pivot on the box's bottom edge = the feet) with FighterPoseView, and assigns
    ///    Idle = Stance, Attack / Heavy Attack = Punch, Throw = Throw. The coloured box is hidden but kept
    ///    as the hurtbox.
    /// Safe to run again; undoable. Save the scene afterwards.
    /// New pose drawings: drop them in the same folder named Fighter_<Pose>.png, add them below, run again —
    /// or just drag them into the FighterPoseView slots.
    /// Place in: Assets/_Project/Editor/FighterPoseSetup.cs
    /// </summary>
    public static class FighterPoseSetup
    {
        const string Folder = "Assets/_Project/Art/Characters";
        const float PixelsPerUnit = 1000f; // standing stance ≈ 2.1 units tall, matching the 2-unit hurtbox

        // File → horizontal pivot (0 = left edge, 1 = right edge), measured between the feet. Vertical pivot = bottom.
        static readonly (string file, float pivotX)[] Poses =
        {
            ("Fighter_Stance.png", 0.49f),
            ("Fighter_Punch.png", 0.556f),
            ("Fighter_Throw.png", 0.498f),
        };

        [MenuItem("KungFu Crashout/Set Up Fighter Poses", priority = 62)]
        public static void SetUp()
        {
            // ── 1. Import settings ──
            foreach (var (file, pivotX) in Poses)
            {
                string path = $"{Folder}/{file}";
                var ti = AssetImporter.GetAtPath(path) as TextureImporter;
                if (!ti) { Debug.LogWarning($"[Poses] Missing {path}"); continue; }

                ti.textureType = TextureImporterType.Sprite;
                ti.spriteImportMode = SpriteImportMode.Single;
                ti.spritePixelsPerUnit = PixelsPerUnit;
                ti.alphaIsTransparency = true;
                ti.mipmapEnabled = false;
                ti.maxTextureSize = 2048;
                ti.filterMode = FilterMode.Bilinear;

                var s = new TextureImporterSettings();
                ti.ReadTextureSettings(s);
                s.spriteAlignment = (int)SpriteAlignment.Custom;
                s.spritePivot = new Vector2(pivotX, 0f);
                s.spriteMeshType = SpriteMeshType.FullRect;
                ti.SetTextureSettings(s);
                ti.SaveAndReimport();
            }

            var stance = AssetDatabase.LoadAssetAtPath<Sprite>($"{Folder}/Fighter_Stance.png");
            var punch = AssetDatabase.LoadAssetAtPath<Sprite>($"{Folder}/Fighter_Punch.png");
            var throwPose = AssetDatabase.LoadAssetAtPath<Sprite>($"{Folder}/Fighter_Throw.png");
            if (!stance)
            {
                EditorUtility.DisplayDialog("Set Up Fighter Poses", $"Couldn't load {Folder}/Fighter_Stance.png.", "OK");
                return;
            }

            // ── 2. Visual child on each fighter ──
            Undo.SetCurrentGroupName("Set Up Fighter Poses");
            int group = Undo.GetCurrentGroup();
            string report = "";

            foreach (var fighter in Object.FindObjectsByType<Fighter>(FindObjectsSortMode.None))
            {
                var root = fighter.transform;
                var box = fighter.GetComponent<SpriteRenderer>();

                var visualT = root.Find("Visual");
                GameObject visual;
                if (visualT) visual = visualT.gameObject;
                else
                {
                    visual = new GameObject("Visual");
                    Undo.RegisterCreatedObjectUndo(visual, "Create Visual");
                    visual.transform.SetParent(root, false);
                }

                // Feet = bottom edge of the Fighter's box.
                float feetLocalY = -1f;
                if (box)
                {
                    float scaleY = Mathf.Approximately(root.lossyScale.y, 0f) ? 1f : root.lossyScale.y;
                    feetLocalY = (box.bounds.min.y - root.position.y) / scaleY;
                }
                Undo.RecordObject(visual.transform, "Place Visual");
                visual.transform.localPosition = new Vector3(0f, feetLocalY, 0f);
                visual.transform.localRotation = Quaternion.identity;
                visual.transform.localScale = Vector3.one;

                var sr = visual.GetComponent<SpriteRenderer>();
                if (!sr) sr = Undo.AddComponent<SpriteRenderer>(visual);
                Undo.RecordObject(sr, "Visual renderer");
                sr.sprite = stance;
                sr.flipX = true; // drawings face left
                if (box)
                {
                    sr.sortingLayerID = box.sortingLayerID;
                    sr.sortingOrder = box.sortingOrder + 1;
                }

                var view = visual.GetComponent<FighterPoseView>();
                if (!view) view = Undo.AddComponent<FighterPoseView>(visual);
                var so = new SerializedObject(view);
                so.FindProperty("fighter").objectReferenceValue = fighter;
                so.FindProperty("idle").objectReferenceValue = stance;
                so.FindProperty("attack").objectReferenceValue = punch;
                so.FindProperty("heavyAttack").objectReferenceValue = punch;
                so.FindProperty("throwPose").objectReferenceValue = throwPose;
                so.FindProperty("artFacesLeft").boolValue = true;
                so.ApplyModifiedProperties();

                EditorSceneManager.MarkSceneDirty(fighter.gameObject.scene);
                report += $"\n  {fighter.name}: Visual at feet (y {feetLocalY:0.##})";
            }

            Undo.CollapseUndoOperations(group);
            Debug.Log($"[Poses] Imported {Poses.Length} drawings ({PixelsPerUnit} px/unit, pivot at feet) and set up:{report}\n" +
                      "Save the scene (Ctrl+S). Tune each pose in FighterPoseView; set Debug Box Alpha ~0.25 to see the hurtbox.");
        }
    }
}
#endif
