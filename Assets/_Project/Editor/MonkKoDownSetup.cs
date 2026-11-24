#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using KungFuCrashout.Fighters;

namespace KungFuCrashout.EditorTools
{
    /// <summary>
    /// Window → KungFu Crashout → Animation → Set Up Monk KO (hit animation → down drawing)   (open FightScene first)
    ///
    /// The new lying-down drawing (Art/Characters/Monk_Sprites/Monk_Downed_KO.png) is shown ONLY on the knockout:
    ///   • KO: the hit animation plays in full (the same drawings and timing as Pose_Monk_Hurt), then the Monk lies down on
    ///     the new drawing and stays there (pose clip Pose_Monk_Downed_KO, played once and held).
    ///   • Ordinary knockdowns (item hits, heavy stagger, guard break) no longer show a down drawing: FighterPoseView →
    ///     Downed is cleared, so they fall back to the hit animation, held on its last drawing until he gets up.
    /// Import: same as the other Monk drawings (1000 × 1000 canvas, 350 PPU, the old Monk_Downed's texture settings), pivot
    /// on the ground line under the body. Saves FightScene.
    /// Restore Old Monk Downed puts the old down drawing back on knockdowns and KO.
    /// Place in: Assets/_Project/Editor/MonkKoDownSetup.cs
    /// </summary>
    public static class MonkKoDownSetup
    {
        const string Title = "Monk KO";
        const string NewPath = "Assets/_Project/Art/Characters/Monk_Sprites/Monk_Downed_KO.png";
        const string OldPath = "Assets/_Project/Art/Characters/Monk_Sprites/Monk_Downed.PNG";
        const string HitClipPath = MonkAttackClipSetup.Folder + "/Poses/Pose_Monk_Hurt.anim";
        const string KoClipPath = MonkAttackClipSetup.Folder + "/Poses/Pose_Monk_Downed_KO.anim";
        const float Fps = 60f;

        [MenuItem("Window/KungFu Crashout/Animation/Set Up Monk KO (hit animation → down drawing)", priority = 10025)]
        static void SetUp()
        {
            if (EditorApplication.isPlaying) { EditorUtility.DisplayDialog(Title, "Stop Play mode first.", "OK"); return; }
            var down = Import();
            if (!down) { EditorUtility.DisplayDialog(Title, $"{NewPath} not found.", "OK"); return; }
            var old = AssetDatabase.LoadAssetAtPath<Sprite>(OldPath);

            // 1. KO = the new drawing; Downed cleared (knockdowns use the hit pose) — on the Monk visuals.
            int views = SetSprites(old, down, null, onlyMonk: true);
            if (views == 0) { EditorUtility.DisplayDialog(Title, "No Monk FighterPoseView in the open scene. Open FightScene first.", "OK"); return; }

            // 2. Pose clips (creates Pose_Monk_Downed_KO for the KO slot, played once and held; removes Downed).
            PoseClipSetup.SetUp();

            // 3. Turn the KO clip into: the hit animation in full, then the down drawing, held.
            var hit = AssetDatabase.LoadAssetAtPath<AnimationClip>(HitClipPath);
            var ko = AssetDatabase.LoadAssetAtPath<AnimationClip>(KoClipPath);
            if (!ko) { EditorUtility.DisplayDialog(Title, $"{KoClipPath} wasn't created.", "OK"); return; }
            var keys = new List<ObjectReferenceKeyframe>();
            float hitEnd = 0f;
            if (hit)
            {
                var hk = AnimationUtility.GetObjectReferenceCurve(hit, Binding) ?? new ObjectReferenceKeyframe[0];
                // All but the hit clip's closing hold key (its last drawing then gets its full step before the fall).
                for (int i = 0; i < hk.Length; i++)
                {
                    bool closingDuplicate = i == hk.Length - 1 && i > 0 && hk[i].value == hk[i - 1].value;
                    if (!closingDuplicate) keys.Add(hk[i]);
                }
                hitEnd = hit.length;
            }
            Undo.RecordObject(ko, Title);
            ko.ClearCurves();
            ko.frameRate = Fps;
            keys.Add(new ObjectReferenceKeyframe { time = hitEnd, value = down });
            keys.Add(new ObjectReferenceKeyframe { time = hitEnd + 1f / Fps, value = down });
            AnimationUtility.SetObjectReferenceCurve(ko, Binding, keys.ToArray());
            var settings = AnimationUtility.GetAnimationClipSettings(ko);
            settings.loopTime = false;
            AnimationUtility.SetAnimationClipSettings(ko, settings);
            EditorUtility.SetDirty(ko);
            AssetDatabase.SaveAssets();

            var scene = EditorSceneManager.GetActiveScene();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log($"[{Title}] KO: hit animation ({hitEnd:0.##} s) then Monk_Downed_KO, held. Knockdowns use the hit pose " +
                      $"(Downed cleared on {views} Monk pose views). Scene saved. Restore Old Monk Downed undoes it.");
        }

        [MenuItem("Window/KungFu Crashout/Animation/Restore Old Monk Downed", priority = 10026)]
        static void Restore()
        {
            var old = AssetDatabase.LoadAssetAtPath<Sprite>(OldPath);
            var down = AssetDatabase.LoadAssetAtPath<Sprite>(NewPath);
            if (!old) { EditorUtility.DisplayDialog(Title, $"{OldPath} not found.", "OK"); return; }
            int views = 0;
            foreach (var v in Object.FindObjectsByType<FighterPoseView>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                var so = new SerializedObject(v);
                if (so.FindProperty("ko").objectReferenceValue != down) continue;
                Undo.RecordObject(v, Title);
                so.FindProperty("downed").objectReferenceValue = old;
                so.FindProperty("ko").objectReferenceValue = null;
                so.ApplyModifiedProperties();
                views++;
            }
            PoseClipSetup.SetUp();
            var scene = EditorSceneManager.GetActiveScene();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log($"[{Title}] Old Monk_Downed back on knockdowns and KO ({views} pose views). Scene saved.");
        }

        /// <summary>KO = newKo, Downed = newDowned on the pose views that show the Monk (their Downed was the old drawing).</summary>
        static int SetSprites(Sprite oldDowned, Sprite newKo, Sprite newDowned, bool onlyMonk)
        {
            int n = 0;
            foreach (var v in Object.FindObjectsByType<FighterPoseView>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                var so = new SerializedObject(v);
                var d = so.FindProperty("downed");
                var k = so.FindProperty("ko");
                bool monk = d.objectReferenceValue == oldDowned || k.objectReferenceValue == newKo;
                if (onlyMonk && !monk) continue;
                Undo.RecordObject(v, Title);
                k.objectReferenceValue = newKo;
                d.objectReferenceValue = newDowned;
                so.ApplyModifiedProperties();
                n++;
            }
            return n;
        }

        static EditorCurveBinding Binding => EditorCurveBinding.PPtrCurve("", typeof(SpriteRenderer), "m_Sprite");

        static Sprite Import()
        {
            var ti = AssetImporter.GetAtPath(NewPath) as TextureImporter;
            if (!ti) return null;
            var template = AssetImporter.GetAtPath(OldPath) as TextureImporter;
            ti.textureType = TextureImporterType.Sprite;
            ti.spriteImportMode = SpriteImportMode.Single;
            ti.spritePixelsPerUnit = 350f;
            ti.mipmapEnabled = false;
            ti.alphaIsTransparency = true;
            ti.wrapMode = TextureWrapMode.Clamp;
            if (template)
            {
                ti.filterMode = template.filterMode;
                ti.textureCompression = template.textureCompression;
                ti.crunchedCompression = template.crunchedCompression;
                ti.maxTextureSize = template.maxTextureSize;
            }
            var s = new TextureImporterSettings();
            ti.ReadTextureSettings(s);
            s.spriteAlignment = (int)SpriteAlignment.Custom;
            s.spritePivot = new Vector2(0.51f, 0.068f);   // under the body, on the ground line (y 932 of 1000)
            if (template)
            {
                var t = new TextureImporterSettings();
                template.ReadTextureSettings(t);
                s.spriteMeshType = t.spriteMeshType;
            }
            ti.SetTextureSettings(s);
            ti.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Sprite>(NewPath);
        }
    }
}
#endif
