#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace KungFuCrashout.EditorTools
{
    /// <summary>
    /// Window → KungFu Crashout → Animation → Set Up Monk Hit Animation (HIT 1–9, 16 fps)
    ///
    /// Replaces the Monk's single hurt drawing with the 9-drawing hit animation in
    /// Art/Characters/Animation/AnimationSprites/HIT_16fps (HIT-1 … HIT-9), through the pose-clip system:
    ///   • Imports each drawing at the hurt sprite's scale: 172 pixels per unit (the drawings are 1/2.035 the size of
    ///     Art/Characters/Monk_Sprites/Monk_Hurt.PNG, measured body-for-body; Monk_Hurt is 350 PPU) and the pivot between
    ///     the feet on the ground line, with Monk_Hurt's texture settings.
    ///   • Writes them into the hit pose clip (Animation/Poses/Pose_Monk_Hurt): 16 steps at 16 fps (1 s) with drawings
    ///     2–8 shown twice each: 1, 2, 2, 3, 3 … 8, 8, 9. Not looped: it plays once and holds drawing 9.
    ///   • FighterPoseView restarts it on every new hit, holds the last drawing while the fighter is still in hitstun /
    ///     launched (stagger), and lets it finish after a short hitstun unless the fighter acts.
    /// Both Monks (player and CPU) use the same clip. Restore Single-Drawing Monk Hurt puts the old drawing back.
    /// Place in: Assets/_Project/Editor/MonkHitAnimationSetup.cs
    /// </summary>
    public static class MonkHitAnimationSetup
    {
        const string Title = "Monk Hit Animation";
        const string FramesFolder = MonkAttackClipSetup.Folder + "/AnimationSprites/HIT_16fps";
        const string ClipPath = MonkAttackClipSetup.Folder + "/Poses/Pose_Monk_Hurt.anim";
        const string OldHurtPath = "Assets/_Project/Art/Characters/Monk_Sprites/Monk_Hurt.PNG";

        const int DrawingFps = 16;
        const int TimelineFps = 60;
        const float PixelsPerUnit = 172f;   // = Monk_Hurt (350 PPU) ÷ 2.035, matched body-for-body
        // Pivot in pixels from the drawing's top-left (292 × 354 canvas): between the feet, on the ground line.
        const float PivotX = 156f, PivotFromTop = 354f;
        /// <summary>Drawing order: drawings 2–8 doubled to reach 16 steps at 16 fps.</summary>
        static readonly int[] Sequence = { 1, 2, 2, 3, 3, 4, 4, 5, 5, 6, 6, 7, 7, 8, 8, 9 };

        [MenuItem("Window/KungFu Crashout/Animation/Set Up Monk Hit Animation (HIT 1–9, 16 fps)", priority = 10019)]
        static void SetUp()
        {
            if (EditorApplication.isPlaying) { EditorUtility.DisplayDialog(Title, "Stop Play mode first.", "OK"); return; }

            var template = AssetImporter.GetAtPath(OldHurtPath) as TextureImporter;
            var byNumber = new Dictionary<int, Sprite>();
            foreach (var path in AssetDatabase.FindAssets("t:Texture2D", new[] { FramesFolder }).Select(AssetDatabase.GUIDToAssetPath))
            {
                var name = System.IO.Path.GetFileNameWithoutExtension(path);
                if (!name.StartsWith("HIT-") || !int.TryParse(name.Substring(4), out int n)) continue;
                var sprite = Import(path, template);
                if (sprite) byNumber[n] = sprite;
            }
            var missing = Sequence.Distinct().Where(n => !byNumber.ContainsKey(n)).ToList();
            if (missing.Count > 0)
            {
                EditorUtility.DisplayDialog(Title, $"Missing HIT-{string.Join(", HIT-", missing)} in {FramesFolder}.", "OK");
                return;
            }

            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(ClipPath);
            bool created = !clip;
            if (created) clip = new AnimationClip();
            else Undo.RecordObject(clip, Title);
            clip.ClearCurves();
            clip.frameRate = TimelineFps;
            var keys = new List<ObjectReferenceKeyframe>();
            for (int i = 0; i < Sequence.Length; i++)
                keys.Add(new ObjectReferenceKeyframe { time = i / (float)DrawingFps, value = byNumber[Sequence[i]] });
            // End key: the last drawing gets its full step, then the pose holds it.
            keys.Add(new ObjectReferenceKeyframe { time = Sequence.Length / (float)DrawingFps, value = byNumber[Sequence[Sequence.Length - 1]] });
            AnimationUtility.SetObjectReferenceCurve(clip, Binding, keys.ToArray());
            var settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.loopTime = false;
            AnimationUtility.SetAnimationClipSettings(clip, settings);
            if (created) AssetDatabase.CreateAsset(clip, ClipPath);
            EditorUtility.SetDirty(clip);
            AssetDatabase.SaveAssets();

            // Hitstun → this clip, played once and held (keeps it: it has more than one drawing now).
            if (Object.FindAnyObjectByType<Fighters.FighterPoseView>(FindObjectsInactive.Include))
                PoseClipSetup.SetUp();
            else
                Debug.Log($"[{Title}] Open FightScene and run Set Up Pose Clips once if the hit pose isn't mapped yet.");

            Debug.Log($"[{Title}] Pose_Monk_Hurt: HIT 1–9 as {Sequence.Length} steps at {DrawingFps} fps (drawings 2–8 doubled, " +
                      $"{Sequence.Length / (float)DrawingFps:0.##} s), {PixelsPerUnit} PPU (= Monk_Hurt's size), pivot between the feet. " +
                      "Restarts on each hit, holds the last drawing while staggered. Restore Single-Drawing Monk Hurt undoes it.");
        }

        [MenuItem("Window/KungFu Crashout/Animation/Restore Single-Drawing Monk Hurt", priority = 10020)]
        static void Restore()
        {
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(ClipPath);
            var old = AssetDatabase.LoadAssetAtPath<Sprite>(OldHurtPath);
            if (!clip || !old) { EditorUtility.DisplayDialog(Title, "Pose_Monk_Hurt or Monk_Hurt.PNG not found.", "OK"); return; }
            Undo.RecordObject(clip, Title);
            clip.ClearCurves();
            clip.frameRate = TimelineFps;
            AnimationUtility.SetObjectReferenceCurve(clip, Binding, new[]
            {
                new ObjectReferenceKeyframe { time = 0f, value = old },
                new ObjectReferenceKeyframe { time = 1f / TimelineFps, value = old },
            });
            EditorUtility.SetDirty(clip);
            AssetDatabase.SaveAssets();
            Debug.Log($"[{Title}] Pose_Monk_Hurt is the single old hurt drawing again.");
        }

        static EditorCurveBinding Binding => EditorCurveBinding.PPtrCurve("", typeof(SpriteRenderer), "m_Sprite");

        static Sprite Import(string path, TextureImporter template)
        {
            var ti = AssetImporter.GetAtPath(path) as TextureImporter;
            if (!ti) return null;
            ti.GetSourceTextureWidthAndHeight(out int w, out int h);
            if (w <= 0 || h <= 0) { w = 292; h = 354; }
            if (w != 292 || h != 354)
                Debug.LogWarning($"[{Title}] {System.IO.Path.GetFileName(path)} is {w} × {h}, not 292 × 354: check its pivot.");

            ti.textureType = TextureImporterType.Sprite;
            ti.spriteImportMode = SpriteImportMode.Single;
            ti.spritePixelsPerUnit = PixelsPerUnit;
            ti.mipmapEnabled = false;
            ti.alphaIsTransparency = true;
            ti.wrapMode = TextureWrapMode.Clamp;
            if (template)
            {
                ti.filterMode = template.filterMode;
                ti.textureCompression = template.textureCompression;
                ti.crunchedCompression = template.crunchedCompression;
            }
            var s = new TextureImporterSettings();
            ti.ReadTextureSettings(s);
            s.spriteAlignment = (int)SpriteAlignment.Custom;
            s.spritePivot = new Vector2(PivotX / w, (h - PivotFromTop) / h);
            if (template)
            {
                var t = new TextureImporterSettings();
                template.ReadTextureSettings(t);
                s.spriteMeshType = t.spriteMeshType;
            }
            ti.SetTextureSettings(s);
            ti.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }
    }
}
#endif
