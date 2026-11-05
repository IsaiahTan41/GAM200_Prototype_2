#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace KungFuCrashout.EditorTools
{
    /// <summary>
    /// No menu of its own: called by Window → KungFu Crashout → Animation → Set Up Monk Attack (Player)
    /// Turns the two frames in Art/Characters/Animation into one attack clip, using the animation setup plan:
    ///   • Import: Sprite (Single), PPU 350 (same size as the other Monk art), pivot on the ground between the feet,
    ///     Full Rect, Bilinear, no mipmaps, max 2048.
    ///   • Clip "Monk_Attack.anim" in the same folder: 60 samples/second (1 key = 1 game frame), Loop Time off.
    ///     Timing follows Light1_Jab (startup 0.12 s, active 0.07 s, recovery 0.15 s):
    ///       frames 0–6   MONKATTACK_1 (wind-up)
    ///       frames 7–20  MONKATTACK2  (strike, held through recovery)
    ///   • Animation Events: HitboxOn @ 7, HitboxOff @ 11, CancelOpen @ 11, AttackEnd @ 20.
    ///     They're received by FighterAnimationEvents on the same GameObject as the Animator.
    /// Animates the SpriteRenderer on the object the Animator sits on (the fighter's "Visual" child).
    /// Re-running rebuilds the clip in place (same asset, so references to it keep working).
    /// Place in: Assets/_Project/Editor/MonkAttackClipSetup.cs
    /// </summary>
    public static class MonkAttackClipSetup
    {
        public const string Folder = "Assets/_Project/Art/Characters/Animation";
        public const string ClipPath = Folder + "/Monk_Attack.anim";
        const float PixelsPerUnit = 350f;
        public const float Fps = 60f;

        // Frame name → pivot measured from the art (feet midpoint, ground line). Same images as
        // Monk_TopAttack / Monk_BasicAttack, so the same pivots: the Monk stays planted between frames.
        static readonly (string name, Vector2 pivot)[] Frames =
        {
            ("MONKATTACK_1", new Vector2(0.531f, 0.062f)),
            ("MONKATTACK2",  new Vector2(0.516f, 0.061f)),
        };

        // Frame data (60 fps frames).
        public const int StrikeFrame = 7;     // first active frame: swap to the strike drawing, hitbox on
        public const int ActiveEndFrame = 11; // 4 active frames
        public const int EndFrame = 20;       // 9 recovery frames, clip length

        public static void Build()
        {
            // ── 1. Import settings ──
            var sprites = new Sprite[Frames.Length];
            for (int i = 0; i < Frames.Length; i++)
            {
                string path = FindFrame(Frames[i].name);
                var ti = path != null ? AssetImporter.GetAtPath(path) as TextureImporter : null;
                if (!ti)
                {
                    EditorUtility.DisplayDialog("Monk Attack Clip", $"Couldn't find {Frames[i].name} in {Folder}.", "OK");
                    return;
                }

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
                s.spritePivot = Frames[i].pivot;
                s.spriteMeshType = SpriteMeshType.FullRect;
                ti.SetTextureSettings(s);
                ti.SaveAndReimport();

                sprites[i] = AssetDatabase.LoadAssetAtPath<Sprite>(path);
                if (!sprites[i])
                {
                    EditorUtility.DisplayDialog("Monk Attack Clip", $"{Frames[i].name} didn't import as a sprite.", "OK");
                    return;
                }
            }

            // ── 2. Clip (rebuilt in place if it exists) ──
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(ClipPath);
            bool created = !clip;
            if (created) clip = new AnimationClip();
            else clip.ClearCurves();
            clip.frameRate = Fps;

            var binding = EditorCurveBinding.PPtrCurve("", typeof(SpriteRenderer), "m_Sprite");
            var keys = new[]
            {
                new ObjectReferenceKeyframe { time = 0f,               value = sprites[0] },
                new ObjectReferenceKeyframe { time = StrikeFrame / Fps, value = sprites[1] },
                new ObjectReferenceKeyframe { time = EndFrame / Fps,    value = sprites[1] }, // holds the strike to the end
            };
            AnimationUtility.SetObjectReferenceCurve(clip, binding, keys);

            AnimationUtility.SetAnimationEvents(clip, new[]
            {
                new AnimationEvent { time = StrikeFrame / Fps,    functionName = "HitboxOn" },
                new AnimationEvent { time = ActiveEndFrame / Fps, functionName = "HitboxOff" },
                new AnimationEvent { time = ActiveEndFrame / Fps, functionName = "CancelOpen" },
                new AnimationEvent { time = EndFrame / Fps,       functionName = "AttackEnd" },
            });

            var settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.loopTime = false; // an attack plays once
            AnimationUtility.SetAnimationClipSettings(clip, settings);

            if (created) AssetDatabase.CreateAsset(clip, ClipPath);
            else EditorUtility.SetDirty(clip);
            AssetDatabase.SaveAssets();

            Selection.activeObject = clip;
            EditorGUIUtility.PingObject(clip);
            Debug.Log($"[Animation] {(created ? "Created" : "Rebuilt")} {ClipPath}: 60 fps, {EndFrame + 1} frames ({EndFrame / Fps:0.00} s), no loop. " +
                      $"Wind-up frames 0–{StrikeFrame - 1}, strike {StrikeFrame}–{EndFrame}. Events: HitboxOn {StrikeFrame}, " +
                      $"HitboxOff + CancelOpen {ActiveEndFrame}, AttackEnd {EndFrame}. " +
                      "Before it plays in game, put FighterAnimationEvents next to the Animator (on Visual).");
        }

        static string FindFrame(string name)
        {
            if (!AssetDatabase.IsValidFolder(Folder)) return null;
            foreach (var guid in AssetDatabase.FindAssets($"{name} t:Texture2D", new[] { Folder }))
            {
                string p = AssetDatabase.GUIDToAssetPath(guid);
                if (System.IO.Path.GetFileNameWithoutExtension(p) == name) return p;
            }
            return null;
        }
    }
}
#endif
