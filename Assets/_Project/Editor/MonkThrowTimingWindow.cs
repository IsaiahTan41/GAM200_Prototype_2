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
    /// Window → KungFu Crashout → Animation → Monk Throw Timing (hold frames)
    ///
    /// The Monk's throw as the artist's 6-drawing animation (Art/Characters/Animation/AnimationSprites/THROW_12-15fps,
    /// THROW__0 … 5): 0–3 wind-up (the item is lifted and drawn back), 4 the thrust (the item leaves), 5 follow-through.
    ///   • Default timing: every drawing held 4 frames (60 fps) = 15 fps, 24 frames (0.4 s); the item leaves on drawing
    ///     4's first frame (frame 16, 0.27 s). Change any hold here and Apply.
    ///   • Apply rebuilds Monk_Throw.anim in place (so every mapping to it stays) with the same events as before
    ///     (HitboxOn = the release, HitboxOff, AttackEnd), and writes the same timing into Throw_Windup (frame data and
    ///     Startup / Active / Recovery seconds). The release is what fires the throw: the item's Throw Start / Throw Idle
    ///     VFX, the throw sound and the throw pose all follow it as before.
    ///   • Scale: the same size as the old throw drawings (MONKTHROW_1…4, 350 PPU): the new drawings are 1/1.96 their
    ///     size, measured on the standing pose → 178 PPU; pivot under the body on the ground line, constant for all six.
    /// Restore Old Throw re-runs the original Set Up Monk Throw (MONKTHROW 1–4).
    /// Place in: Assets/_Project/Editor/MonkThrowTimingWindow.cs
    /// </summary>
    public class MonkThrowTimingWindow : EditorWindow
    {
        const string FramesFolder = MonkAttackClipSetup.Folder + "/AnimationSprites/THROW_12-15fps";
        const string ClipPath = MonkAttackClipSetup.Folder + "/Monk_Throw.anim";
        const string ThrowAttackPath = "Assets/_Project/Data/Attacks/Throw_Windup.asset";
        const float PixelsPerUnit = 178f;               // = MONKTHROW (350 PPU) ÷ 1.96
        const float PivotX = 250f, PivotFromTop = 361f; // 768 × 363 canvas: under the body, on the ground line
        const int Fps = 60;
        const int ReleaseDrawing = 4;                   // THROW__4: the thrust, the item leaves
        const int ActiveFrames = 2;

        static readonly string[] Labels =
        {
            "0  Pick up", "1  Lift", "2  Draw back", "3  Wind-up", "4  Thrust (release)", "5  Follow-through",
        };
        const int DefaultHold = 4; // each drawing once at 15 fps (4 frames at 60 fps)

        int[] hold = Enumerable.Repeat(DefaultHold, 6).ToArray();
        string status = "";

        [MenuItem("Window/KungFu Crashout/Animation/Monk Throw Timing (hold frames)", priority = 10022)]
        static void Open()
        {
            var w = GetWindow<MonkThrowTimingWindow>("Monk Throw Timing");
            w.minSize = new Vector2(380f, 380f);
            w.LoadCurrent();
        }

        void OnGUI()
        {
            EditorGUILayout.HelpBox("Frames at 60 fps (4 = one 15 fps step). Default: every drawing 4 frames (15 fps). " +
                                    "The item leaves on the Thrust drawing.", MessageType.None);
            EditorGUILayout.LabelField("Hold each drawing (frames)", EditorStyles.boldLabel);
            for (int i = 0; i < 6; i++) hold[i] = Mathf.Max(1, EditorGUILayout.IntField(Labels[i], hold[i]));

            int release = hold.Take(ReleaseDrawing).Sum(), total = hold.Sum();
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Result", EditorStyles.boldLabel);
            EditorGUILayout.LabelField($"Item leaves on frame {release} ({release / 60f:0.00} s)");
            EditorGUILayout.LabelField($"Throw lasts {total} frames ({total / 60f:0.00} s)");
            EditorGUILayout.LabelField("Before (MONKTHROW 1–4): item left on frame 18 (0.30 s), 33 frames");

            EditorGUILayout.Space();
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Apply", GUILayout.Height(28))) Apply();
                if (GUILayout.Button("Reset (4 frames each, 15 fps)", GUILayout.Height(28)))
                { hold = Enumerable.Repeat(DefaultHold, 6).ToArray(); Apply(); }
            }
            if (GUILayout.Button("Restore Old Throw (MONKTHROW 1–4)")) RestoreOld();
            if (!string.IsNullOrEmpty(status)) EditorGUILayout.HelpBox(status, MessageType.Info);
        }

        void LoadCurrent()
        {
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(ClipPath);
            if (!clip) return;
            var keys = AnimationUtility.GetObjectReferenceCurve(clip, Binding);
            if (keys == null || keys.Length < 7 || !(keys[0].value && keys[0].value.name.StartsWith("THROW__"))) return;
            for (int i = 0; i < 6; i++) hold[i] = Mathf.Max(1, Mathf.RoundToInt((keys[i + 1].time - keys[i].time) * Fps));
        }

        void Apply()
        {
            if (EditorApplication.isPlaying) { status = "Stop Play mode first."; return; }
            var sprites = new Sprite[6];
            for (int i = 0; i < 6; i++)
            {
                sprites[i] = Import($"{FramesFolder}/THROW__{i}.png");
                if (!sprites[i]) { status = $"THROW__{i}.png not found in {FramesFolder}."; return; }
            }

            int release = hold.Take(ReleaseDrawing).Sum();
            int activeEnd = release + ActiveFrames;
            int end = hold.Sum();

            // ── Clip (same asset: the FighterAnimator mappings and the Animator state keep working) ──
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(ClipPath);
            bool created = !clip;
            if (created) clip = new AnimationClip();
            else Undo.RecordObject(clip, "Monk Throw Timing");
            clip.ClearCurves();
            clip.frameRate = Fps;
            var keys = new List<ObjectReferenceKeyframe>();
            int t = 0;
            for (int i = 0; i < 6; i++) { keys.Add(new ObjectReferenceKeyframe { time = t / (float)Fps, value = sprites[i] }); t += hold[i]; }
            keys.Add(new ObjectReferenceKeyframe { time = end / (float)Fps, value = sprites[5] }); // hold to the end
            AnimationUtility.SetObjectReferenceCurve(clip, Binding, keys.ToArray());
            AnimationUtility.SetAnimationEvents(clip, new[]
            {
                new AnimationEvent { time = release / (float)Fps,   functionName = "HitboxOn" },  // release: the item leaves
                new AnimationEvent { time = activeEnd / (float)Fps, functionName = "HitboxOff" },
                new AnimationEvent { time = end / (float)Fps,       functionName = "AttackEnd" },
            });
            var settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.loopTime = false;
            AnimationUtility.SetAnimationClipSettings(clip, settings);
            if (created) AssetDatabase.CreateAsset(clip, ClipPath);
            EditorUtility.SetDirty(clip);

            // ── Throw timing asset: frame data + seconds, the same release ──
            var a = AssetDatabase.LoadAssetAtPath<AttackData>(ThrowAttackPath);
            if (a)
            {
                Undo.RecordObject(a, "Monk Throw Timing");
                a.startup = release / (float)Fps;
                a.active = ActiveFrames / (float)Fps;
                a.recovery = (end - activeEnd) / (float)Fps;
                a.frames = new FrameData { totalFrames = end + 1, activeStart = release, activeEnd = activeEnd, cancelFrame = -1 };
                EditorUtility.SetDirty(a);
            }
            AssetDatabase.SaveAssets();

            status = $"Throw: holds {string.Join(" / ", hold)} → item leaves on frame {release} ({release / 60f:0.00} s), " +
                     $"{end} frames ({end / 60f:0.00} s). Monk_Throw and Throw_Windup updated.";
            Debug.Log("[Monk Throw Timing] " + status);
        }

        void RestoreOld()
        {
            if (!EditorSceneManager.GetActiveScene().path.EndsWith("FightScene.unity"))
            { status = "Open FightScene first (Set Up Monk Throw works on the Player)."; return; }
            EditorApplication.ExecuteMenuItem("Window/KungFu Crashout/Animation/Set Up Monk Throw (Player)");
            var a = AssetDatabase.LoadAssetAtPath<AttackData>(ThrowAttackPath);
            if (a)
            {
                Undo.RecordObject(a, "Restore Old Throw");
                a.startup = MonkThrowAnimatorSetup.ReleaseFrame / (float)Fps;
                a.active = (MonkThrowAnimatorSetup.ActiveEndFrame - MonkThrowAnimatorSetup.ReleaseFrame) / (float)Fps;
                a.recovery = (MonkThrowAnimatorSetup.EndFrame - MonkThrowAnimatorSetup.ActiveEndFrame) / (float)Fps;
                a.frames = new FrameData
                {
                    totalFrames = MonkThrowAnimatorSetup.EndFrame + 1, activeStart = MonkThrowAnimatorSetup.ReleaseFrame,
                    activeEnd = MonkThrowAnimatorSetup.ActiveEndFrame, cancelFrame = -1,
                };
                EditorUtility.SetDirty(a);
                AssetDatabase.SaveAssets();
            }
            hold = new[] { 6, 6, 6, 6, 6, 6 };
            status = "Old throw (MONKTHROW 1–4) restored: item leaves on frame 18, 33 frames. Save the scene.";
        }

        static EditorCurveBinding Binding => EditorCurveBinding.PPtrCurve("", typeof(SpriteRenderer), "m_Sprite");

        static Sprite Import(string path)
        {
            var ti = AssetImporter.GetAtPath(path) as TextureImporter;
            if (!ti) return null;
            ti.GetSourceTextureWidthAndHeight(out int w, out int h);
            if (w <= 0 || h <= 0) { w = 768; h = 363; }
            ti.textureType = TextureImporterType.Sprite;
            ti.spriteImportMode = SpriteImportMode.Single;
            ti.spritePixelsPerUnit = PixelsPerUnit;
            ti.alphaIsTransparency = true;
            ti.mipmapEnabled = false;
            ti.maxTextureSize = 2048;
            ti.filterMode = FilterMode.Bilinear;
            ti.wrapMode = TextureWrapMode.Clamp;
            var s = new TextureImporterSettings();
            ti.ReadTextureSettings(s);
            s.spriteAlignment = (int)SpriteAlignment.Custom;
            s.spritePivot = new Vector2(PivotX / w, (h - PivotFromTop) / h);
            s.spriteMeshType = SpriteMeshType.FullRect;   // as the old throw drawings
            ti.SetTextureSettings(s);
            ti.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }
    }
}
#endif
