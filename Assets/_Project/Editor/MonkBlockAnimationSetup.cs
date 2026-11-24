#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace KungFuCrashout.EditorTools
{
    /// <summary>
    /// Window → KungFu Crashout → Animation → Set Up Monk Block Animation (BLOCK 00–11, 12 fps)
    ///
    /// Replaces the Monk's single block drawing with the 12-drawing block animation in
    /// Art/Characters/Animation/AnimationSprites/BLOCK_12fps (BLOCK__00 … BLOCK__11), through the pose-clip system:
    ///   • Imports each drawing at the old block sprite's scale: 175 pixels per unit (the drawings are exactly half the size
    ///     of Art/Characters/Monk_Sprites/Monk_Block.PNG, matched outline-for-outline; Monk_Block is 350 PPU), pivot on the
    ///     same spot as Monk_Block's (between the feet, on the ground line), with Monk_Block's texture settings.
    ///   • Writes them into the block pose clip (Animation/Poses/Pose_Monk_Block): 12 drawings at 12 fps (1 s), not looped.
    ///   • FighterPoseView restarts it on every new block; it holds the last drawing for as long as the block is held.
    /// Both Monks (player and CPU) use the same clip. Restore Single-Drawing Monk Block puts the old drawing back.
    /// Place in: Assets/_Project/Editor/MonkBlockAnimationSetup.cs
    /// </summary>
    public static class MonkBlockAnimationSetup
    {
        const string Title = "Monk Block Animation";
        const string FramesFolder = MonkAttackClipSetup.Folder + "/AnimationSprites/BLOCK_12fps";
        const string ClipPath = MonkAttackClipSetup.Folder + "/Poses/Pose_Monk_Block.anim";
        const string OldBlockPath = "Assets/_Project/Art/Characters/Monk_Sprites/Monk_Block.PNG";

        const int DrawingFps = 12;
        const int TimelineFps = 60;
        const float PixelsPerUnit = 175f;   // = Monk_Block (350 PPU) × 0.5, matched outline-for-outline
        const int CanvasW = 340, CanvasH = 437;
        // Pivot in pixels from the drawing's top-left: Monk_Block's pivot (0.528, 0.079 of 1000 × 1000) mapped in.
        const float PivotX = 184f, PivotFromTop = 436.5f;
        const int Drawings = 12; // BLOCK__00 … BLOCK__11

        [MenuItem("Window/KungFu Crashout/Animation/Set Up Monk Block Animation (BLOCK 00–11, 12 fps)", priority = 10023)]
        static void SetUp()
        {
            if (EditorApplication.isPlaying) { EditorUtility.DisplayDialog(Title, "Stop Play mode first.", "OK"); return; }

            var template = AssetImporter.GetAtPath(OldBlockPath) as TextureImporter;
            var byNumber = new Dictionary<int, Sprite>();
            foreach (var path in AssetDatabase.FindAssets("t:Texture2D", new[] { FramesFolder }).Select(AssetDatabase.GUIDToAssetPath))
            {
                var name = System.IO.Path.GetFileNameWithoutExtension(path);
                if (!name.StartsWith("BLOCK__") || !int.TryParse(name.Substring(7), out int n)) continue;
                var sprite = Import(path, template);
                if (sprite) byNumber[n] = sprite;
            }
            var missing = Enumerable.Range(0, Drawings).Where(n => !byNumber.ContainsKey(n)).ToList();
            if (missing.Count > 0)
            {
                EditorUtility.DisplayDialog(Title, $"Missing BLOCK__{string.Join(", BLOCK__", missing.Select(m => m.ToString("00")))} in {FramesFolder}.", "OK");
                return;
            }

            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(ClipPath);
            bool created = !clip;
            if (created) clip = new AnimationClip();
            else Undo.RecordObject(clip, Title);
            clip.ClearCurves();
            clip.frameRate = TimelineFps;
            var keys = new List<ObjectReferenceKeyframe>();
            for (int i = 0; i < Drawings; i++)
                keys.Add(new ObjectReferenceKeyframe { time = i / (float)DrawingFps, value = byNumber[i] });
            // End key: the last drawing gets its full step, then the pose holds it.
            keys.Add(new ObjectReferenceKeyframe { time = Drawings / (float)DrawingFps, value = byNumber[Drawings - 1] });
            AnimationUtility.SetObjectReferenceCurve(clip, Binding, keys.ToArray());
            var settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.loopTime = false;
            AnimationUtility.SetAnimationClipSettings(clip, settings);
            if (created) AssetDatabase.CreateAsset(clip, ClipPath);
            EditorUtility.SetDirty(clip);
            AssetDatabase.SaveAssets();

            // Block → this clip, played once and held (kept: it has more than one drawing now).
            if (Object.FindAnyObjectByType<Fighters.FighterPoseView>(FindObjectsInactive.Include))
                PoseClipSetup.SetUp();
            else
                Debug.Log($"[{Title}] Open FightScene and run Set Up Pose Clips once so the block pose plays once and holds.");

            Debug.Log($"[{Title}] Pose_Monk_Block: BLOCK 00–11 at {DrawingFps} fps ({Drawings / (float)DrawingFps:0.##} s), " +
                      $"{PixelsPerUnit} PPU (= Monk_Block's size), pivot on Monk_Block's. Restarts on each block, holds the last " +
                      "drawing while the block is held. Save the scene. Restore Single-Drawing Monk Block undoes it.");
        }

        [MenuItem("Window/KungFu Crashout/Animation/Restore Single-Drawing Monk Block", priority = 10024)]
        static void Restore()
        {
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(ClipPath);
            var old = AssetDatabase.LoadAssetAtPath<Sprite>(OldBlockPath);
            if (!clip || !old) { EditorUtility.DisplayDialog(Title, "Pose_Monk_Block or Monk_Block.PNG not found.", "OK"); return; }
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
            Debug.Log($"[{Title}] Pose_Monk_Block is the single old block drawing again.");
        }

        static EditorCurveBinding Binding => EditorCurveBinding.PPtrCurve("", typeof(SpriteRenderer), "m_Sprite");

        static Sprite Import(string path, TextureImporter template)
        {
            var ti = AssetImporter.GetAtPath(path) as TextureImporter;
            if (!ti) return null;
            ti.GetSourceTextureWidthAndHeight(out int w, out int h);
            if (w <= 0 || h <= 0) { w = CanvasW; h = CanvasH; }
            if (w != CanvasW || h != CanvasH)
                Debug.LogWarning($"[{Title}] {System.IO.Path.GetFileName(path)} is {w} × {h}, not {CanvasW} × {CanvasH}: check its pivot.");

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
