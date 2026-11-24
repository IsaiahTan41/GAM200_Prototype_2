#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace KungFuCrashout.EditorTools
{
    /// <summary>
    /// Window → KungFu Crashout → Animation → Set Up Monk Jump Animation (JUMP_WAIST 00–16, 16 fps)
    ///
    /// Replaces the Monk's single jump drawing with the 17-drawing jump in
    /// Art/Characters/Animation/AnimationSprites/JUMP_Waist_16fps (JUMP_WAIST__00 … 16), through the pose-clip system:
    ///   • Imports each drawing at the old jump's scale: 178 pixels per unit (the drawings are 1/1.97 the size of
    ///     Art/Characters/Monk_Sprites/Monk_Jump.PNG, 350 PPU, measured head-for-head). The drawings keep the WAIST still,
    ///     so the pivot is placed for the waist to sit exactly where the old jump's waist sat relative to the fighter
    ///     (0.17 units back, 1.14 units up — the same spot as the air attack's waist, so jump ↔ air attack doesn't pop).
    ///   • Writes them into the jump pose clip (Animation/Poses/Pose_Monk_Jump): one drawing per 16 fps step
    ///     (17 steps, 1.06 s), played once; the last drawing holds while the fighter is still in the air.
    ///   • FighterPoseView times it from take-off, so after an air attack it carries on where the jump is.
    /// Restore Single-Drawing Monk Jump puts the old drawing back.
    /// Place in: Assets/_Project/Editor/MonkJumpAnimationSetup.cs
    /// </summary>
    public static class MonkJumpAnimationSetup
    {
        const string Title = "Monk Jump Animation";
        const string FramesFolder = MonkAttackClipSetup.Folder + "/AnimationSprites/JUMP_Waist_16fps";
        const string ClipPath = MonkAttackClipSetup.Folder + "/Poses/Pose_Monk_Jump.anim";
        const string OldJumpPath = "Assets/_Project/Art/Characters/Monk_Sprites/Monk_Jump.PNG";

        const int DrawingFps = 16;
        const int TimelineFps = 60;
        const float PixelsPerUnit = 178f;    // = Monk_Jump (350 PPU) ÷ 1.97
        // 335 × 370 canvas. Waist (sash) at ~(150, 207) px in every drawing; the old jump's waist sits 58 px back and
        // 399 px up from its pivot at 350 PPU = 29 px / 203 px at this scale → pivot at (179, 410) px from the top-left
        // (below the canvas: the feet hang under the waist in the air, the fighter's ground point is lower).
        const float PivotX = 179f, PivotFromTop = 410f;

        [MenuItem("Window/KungFu Crashout/Animation/Set Up Monk Jump Animation (JUMP_WAIST 00–16, 16 fps)", priority = 10023)]
        static void SetUp()
        {
            if (EditorApplication.isPlaying) { EditorUtility.DisplayDialog(Title, "Stop Play mode first.", "OK"); return; }

            var template = AssetImporter.GetAtPath(OldJumpPath) as TextureImporter;
            var paths = AssetDatabase.FindAssets("t:Texture2D", new[] { FramesFolder })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(p => System.IO.Path.GetFileNameWithoutExtension(p).StartsWith("JUMP_WAIST__"))
                .OrderBy(p => p, System.StringComparer.Ordinal)
                .ToList();
            if (paths.Count == 0) { EditorUtility.DisplayDialog(Title, $"No JUMP_WAIST__*.png drawings in {FramesFolder}.", "OK"); return; }
            var sprites = paths.Select(p => Import(p, template)).Where(s => s).ToList();
            if (sprites.Count == 0) return;

            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(ClipPath);
            bool created = !clip;
            if (created) clip = new AnimationClip();
            else Undo.RecordObject(clip, Title);
            clip.ClearCurves();
            clip.frameRate = TimelineFps;
            var keys = new List<ObjectReferenceKeyframe>();
            for (int i = 0; i < sprites.Count; i++)
                keys.Add(new ObjectReferenceKeyframe { time = i / (float)DrawingFps, value = sprites[i] });
            keys.Add(new ObjectReferenceKeyframe { time = sprites.Count / (float)DrawingFps, value = sprites[sprites.Count - 1] });
            AnimationUtility.SetObjectReferenceCurve(clip, Binding, keys.ToArray());
            var settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.loopTime = false;
            AnimationUtility.SetAnimationClipSettings(clip, settings);
            if (created) AssetDatabase.CreateAsset(clip, ClipPath);
            EditorUtility.SetDirty(clip);
            AssetDatabase.SaveAssets();

            // Airborne → this clip, played once and held (keeps it: it has more than one drawing now).
            if (Object.FindAnyObjectByType<Fighters.FighterPoseView>(FindObjectsInactive.Include))
                PoseClipSetup.SetUp();
            else
                Debug.Log($"[{Title}] Open FightScene and run Set Up Pose Clips once to map the jump.");

            Debug.Log($"[{Title}] Pose_Monk_Jump: {sprites.Count} drawings at {DrawingFps} fps ({sprites.Count / (float)DrawingFps:0.##} s), " +
                      $"{PixelsPerUnit} PPU, waist at the old jump's spot; holds the last drawing in the air. Save the scene.");
        }

        [MenuItem("Window/KungFu Crashout/Animation/Restore Single-Drawing Monk Jump", priority = 10024)]
        static void Restore()
        {
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(ClipPath);
            var old = AssetDatabase.LoadAssetAtPath<Sprite>(OldJumpPath);
            if (!clip || !old) { EditorUtility.DisplayDialog(Title, "Pose_Monk_Jump or Monk_Jump.PNG not found.", "OK"); return; }
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
            Debug.Log($"[{Title}] Pose_Monk_Jump is the single old jump drawing again.");
        }

        static EditorCurveBinding Binding => EditorCurveBinding.PPtrCurve("", typeof(SpriteRenderer), "m_Sprite");

        static Sprite Import(string path, TextureImporter template)
        {
            var ti = AssetImporter.GetAtPath(path) as TextureImporter;
            if (!ti) return null;
            ti.GetSourceTextureWidthAndHeight(out int w, out int h);
            if (w <= 0 || h <= 0) { w = 335; h = 370; }
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
            s.spritePivot = new Vector2(PivotX / w, (h - PivotFromTop) / h);   // y below 0: under the canvas
            s.spriteMeshType = SpriteMeshType.FullRect;
            ti.SetTextureSettings(s);
            ti.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }
    }
}
#endif
