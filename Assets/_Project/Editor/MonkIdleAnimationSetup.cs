#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace KungFuCrashout.EditorTools
{
    /// <summary>
    /// Window → KungFu Crashout → Animation → Set Up Monk Idle Animation (24 frames, 12 fps)
    ///
    /// Turns the Monk's idle into the 24-drawing animation in Art/Characters/Animation/AnimationSprites/Idle
    /// (IDLE_00, IDLE_02 … IDLE_46), through the pose-clip system (FighterAnimator → Use Pose Clips):
    ///   • Imports each drawing at the idle's scale: 339 pixels per unit and the pivot between the feet on the ground line,
    ///     the same as Art/Characters/Monk_Idle.PNG (the drawings overlay the old idle body-for-body), with the old idle's
    ///     texture settings.
    ///   • Writes them into the idle's pose clip (Animation/Poses/Pose_Monk_Idle) on the 60 fps timeline, one drawing every
    ///     5 frames = 12 fps, 2 s per loop. The pose clips loop the idle, so it cycles while the Monk stands.
    ///   • Then runs Set Up Pose Clips so both Monks (player and CPU) map their idle to it. That menu keeps a clip with
    ///     more than one drawing, so re-running it later won't undo the animation.
    /// IDLE_48 (the same drawing as IDLE_00, the loop's return) is not copied in, so the loop doesn't show it twice.
    ///
    /// Window → KungFu Crashout → Animation → Restore Single-Drawing Monk Idle puts the one old drawing back.
    /// Place in: Assets/_Project/Editor/MonkIdleAnimationSetup.cs
    /// </summary>
    public static class MonkIdleAnimationSetup
    {
        const string Title = "Monk Idle Animation";
        const string FramesFolder = MonkAttackClipSetup.Folder + "/AnimationSprites/Idle";
        const string ClipPath = MonkAttackClipSetup.Folder + "/Poses/Pose_Monk_Idle.anim";
        const string OldIdlePath = "Assets/_Project/Art/Characters/Monk_Idle.PNG";

        const int DrawingFps = 12;      // the art's frame rate
        const int TimelineFps = 60;     // pose clips run on a 60 fps frame counter
        const float PixelsPerUnit = 339f;   // = the old idle drawing
        // Pivot in pixels from the drawing's top-left (685 × 735 canvas): centre between the feet, on the ground line.
        const float PivotX = 379f, PivotFromTop = 734f;

        [MenuItem("Window/KungFu Crashout/Animation/Set Up Monk Idle Animation (24 frames, 12 fps)", priority = 10017)]
        static void SetUp()
        {
            if (EditorApplication.isPlaying) { EditorUtility.DisplayDialog(Title, "Stop Play mode first.", "OK"); return; }

            var paths = AssetDatabase.FindAssets("t:Texture2D", new[] { FramesFolder })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(p => System.IO.Path.GetFileNameWithoutExtension(p).StartsWith("IDLE_"))
                .OrderBy(p => p, System.StringComparer.Ordinal)
                .ToList();
            if (paths.Count == 0) { EditorUtility.DisplayDialog(Title, $"No IDLE_*.png drawings in {FramesFolder}.", "OK"); return; }

            var template = AssetImporter.GetAtPath(OldIdlePath) as TextureImporter;
            var sprites = new List<Sprite>();
            foreach (var path in paths)
            {
                var sprite = Import(path, template);
                if (sprite) sprites.Add(sprite);
            }
            if (sprites.Count == 0) return;

            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(ClipPath);
            bool created = !clip;
            if (created) clip = new AnimationClip();
            else Undo.RecordObject(clip, Title);
            clip.ClearCurves();
            clip.frameRate = TimelineFps;
            int hold = TimelineFps / DrawingFps; // 5 timeline frames per drawing
            var keys = new List<ObjectReferenceKeyframe>();
            for (int i = 0; i < sprites.Count; i++)
                keys.Add(new ObjectReferenceKeyframe { time = i * hold / (float)TimelineFps, value = sprites[i] });
            // End key so the last drawing gets its full 5 frames before the loop wraps.
            keys.Add(new ObjectReferenceKeyframe { time = sprites.Count * hold / (float)TimelineFps, value = sprites[sprites.Count - 1] });
            AnimationUtility.SetObjectReferenceCurve(clip, Binding, keys.ToArray());
            var settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.loopTime = false; // the pose frame counter loops it
            AnimationUtility.SetAnimationClipSettings(clip, settings);
            if (created) AssetDatabase.CreateAsset(clip, ClipPath);
            EditorUtility.SetDirty(clip);
            AssetDatabase.SaveAssets();

            // Map both Monks' idle to the clip (keeps it: it has more than one drawing now).
            if (Object.FindAnyObjectByType<Fighters.FighterPoseView>(FindObjectsInactive.Include))
                PoseClipSetup.SetUp();
            else
                Debug.Log($"[{Title}] Open FightScene and run Set Up Pose Clips once if the idle isn't mapped yet.");

            Debug.Log($"[{Title}] Pose_Monk_Idle: {sprites.Count} drawings at {DrawingFps} fps ({sprites.Count * hold / (float)TimelineFps:0.##} s loop), " +
                      $"{PixelsPerUnit} PPU, pivot between the feet. Restore Single-Drawing Monk Idle puts the old drawing back.");
        }

        [MenuItem("Window/KungFu Crashout/Animation/Restore Single-Drawing Monk Idle", priority = 10018)]
        static void Restore()
        {
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(ClipPath);
            var old = AssetDatabase.LoadAssetAtPath<Sprite>(OldIdlePath);
            if (!clip || !old) { EditorUtility.DisplayDialog(Title, "Pose_Monk_Idle or Monk_Idle.PNG not found.", "OK"); return; }
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
            Debug.Log($"[{Title}] Pose_Monk_Idle is the single old idle drawing again.");
        }

        static EditorCurveBinding Binding => EditorCurveBinding.PPtrCurve("", typeof(SpriteRenderer), "m_Sprite");

        static Sprite Import(string path, TextureImporter template)
        {
            var ti = AssetImporter.GetAtPath(path) as TextureImporter;
            if (!ti) return null;
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            ti.GetSourceTextureWidthAndHeight(out int w, out int h);
            if (w <= 0 || h <= 0) { w = tex ? tex.width : 685; h = tex ? tex.height : 735; }
            if (w != 685 || h != 735)
                Debug.LogWarning($"[{Title}] {System.IO.Path.GetFileName(path)} is {w} × {h}, not 685 × 735: check its pivot.");

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
                ti.maxTextureSize = template.maxTextureSize;
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
