#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using KungFuCrashout.Fighters;

namespace KungFuCrashout.EditorTools
{
    /// <summary>
    /// Window → KungFu Crashout → Animation → Monk Dash Timing (hold frames)
    ///
    /// The Monk's dash as the artist's 5-drawing animation (Art/Characters/Animation/AnimationSprites/DASH, DASH-1 … 5),
    /// timed like the artist's notes: Anticipation (1, 2) → Action (3) → Follow-through (4, 5).
    ///   • Set how many frames (60 fps) each drawing is held, then Apply: the dash pose clip (Animation/Poses/
    ///     Pose_Monk_Dash) is rebuilt and both Monks' Dash pose plays it once per dash (Set Up Pose Clips).
    ///   • The window shows how long the dash itself lasts (the fighter's archetype Dash Duration, e.g. Nam-pi 0.15 s =
    ///     9 frames): drawings that fall inside it play during the dash; the rest are the follow-through, which plays after
    ///     the dash while the fighter stands (any action cuts it). Gameplay timing never changes: this only times the art.
    ///   • Scale: the same size as the old dash drawing (Art/Characters/Monk_Sprites/Monk_Dash.PNG, 350 PPU): the new
    ///     drawings are 1/1.96 its size, measured head-for-head → 178 PPU, pivot between the feet on the ground line.
    /// Restore Old Dash Drawing puts the single old drawing back.
    /// Place in: Assets/_Project/Editor/MonkDashTimingWindow.cs
    /// </summary>
    public class MonkDashTimingWindow : EditorWindow
    {
        const string FramesFolder = MonkAttackClipSetup.Folder + "/AnimationSprites/DASH";
        const string ClipPath = MonkAttackClipSetup.Folder + "/Poses/Pose_Monk_Dash.anim";
        const string OldDashPath = "Assets/_Project/Art/Characters/Monk_Sprites/Monk_Dash.PNG";
        const float PixelsPerUnit = 178f;               // = Monk_Dash (350 PPU) ÷ 1.96
        const float PivotX = 210f, PivotFromTop = 384f; // 450 × 386 canvas: between the feet, on the ground line
        const int Fps = 60;

        static readonly string[] Labels =
        {
            "1  Anticipation (crouch)", "2  Anticipation (push off)", "3  Action (the dash)",
            "4  Follow-through (catch)", "5  Follow-through (settle)",
        };
        static readonly int[] Defaults = { 2, 2, 5, 4, 5 };

        int[] hold = (int[])Defaults.Clone();
        string status = "";

        [MenuItem("Window/KungFu Crashout/Animation/Monk Dash Timing (hold frames)", priority = 10021)]
        static void Open()
        {
            var w = GetWindow<MonkDashTimingWindow>("Monk Dash Timing");
            w.minSize = new Vector2(380f, 360f);
            w.LoadCurrent();
        }

        void OnGUI()
        {
            EditorGUILayout.HelpBox("Frames at 60 fps (6 = 0.1 s). Anticipation → Action → Follow-through, as the artist's notes. " +
                                    "Apply rebuilds the dash animation; gameplay timing is unchanged.", MessageType.None);
            EditorGUILayout.LabelField("Hold each drawing (frames)", EditorStyles.boldLabel);
            for (int i = 0; i < 5; i++) hold[i] = Mathf.Max(1, EditorGUILayout.IntField(Labels[i], hold[i]));

            int dash = DashFrames(out string from);
            int total = hold.Sum(), t = 0;
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Result", EditorStyles.boldLabel);
            EditorGUILayout.LabelField($"Dash lasts {dash} frames ({dash / 60f:0.00} s, {from})");
            for (int i = 0; i < 5; i++)
            {
                string when = t + hold[i] <= dash ? "during the dash" : t >= dash ? "after it (follow-through)" : "into the follow-through";
                EditorGUILayout.LabelField($"Drawing {i + 1}: frames {t}–{t + hold[i] - 1}", when);
                t += hold[i];
            }
            EditorGUILayout.LabelField($"Animation {total} frames ({total / 60f:0.00} s)");
            if (hold[0] + hold[1] >= dash)
                EditorGUILayout.HelpBox("The anticipation alone fills the dash: the Action drawing won't show while moving.", MessageType.Warning);

            EditorGUILayout.Space();
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Apply", GUILayout.Height(28))) Apply();
                if (GUILayout.Button("Reset To Notes Timing", GUILayout.Height(28))) { hold = (int[])Defaults.Clone(); Apply(); }
            }
            if (GUILayout.Button("Restore Old Dash Drawing")) RestoreOld();
            if (!string.IsNullOrEmpty(status)) EditorGUILayout.HelpBox(status, MessageType.Info);
        }

        /// <summary>The dash length in frames: Nam-pi's archetype Dash Duration (what the Roster loads), else the scene fighter's.</summary>
        static int DashFrames(out string from)
        {
            var arch = AssetDatabase.LoadAssetAtPath<ScriptableObject>("Assets/_Project/Data/GDD/Archetype_1_NamPi.asset");
            if (arch)
            {
                var p = new SerializedObject(arch).FindProperty("dashDuration");
                if (p != null) { from = "Nam-pi archetype"; return Mathf.Max(1, Mathf.RoundToInt(p.floatValue * Fps)); }
            }
            var f = Object.FindObjectsByType<Fighter>(FindObjectsInactive.Include, FindObjectsSortMode.None).FirstOrDefault();
            if (f) { from = f.name; return Mathf.Max(1, Mathf.RoundToInt(new SerializedObject(f).FindProperty("dashDuration").floatValue * Fps)); }
            from = "default"; return 9;
        }

        /// <summary>Read the holds back from the clip (one key per drawing change).</summary>
        void LoadCurrent()
        {
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(ClipPath);
            if (!clip) return;
            var keys = AnimationUtility.GetObjectReferenceCurve(clip, Binding);
            if (keys == null || keys.Length < 6) return;
            for (int i = 0; i < 5; i++) hold[i] = Mathf.Max(1, Mathf.RoundToInt((keys[i + 1].time - keys[i].time) * Fps));
        }

        void Apply()
        {
            if (EditorApplication.isPlaying) { status = "Stop Play mode first."; return; }
            var template = AssetImporter.GetAtPath(OldDashPath) as TextureImporter;
            var sprites = new Sprite[5];
            for (int i = 0; i < 5; i++)
            {
                sprites[i] = Import($"{FramesFolder}/DASH-{i + 1}.png", template);
                if (!sprites[i]) { status = $"DASH-{i + 1}.png not found in {FramesFolder}."; return; }
            }

            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(ClipPath);
            bool created = !clip;
            if (created) clip = new AnimationClip();
            else Undo.RecordObject(clip, "Monk Dash Timing");
            clip.ClearCurves();
            clip.frameRate = Fps;
            var keys = new List<ObjectReferenceKeyframe>();
            int t = 0;
            for (int i = 0; i < 5; i++) { keys.Add(new ObjectReferenceKeyframe { time = t / (float)Fps, value = sprites[i] }); t += hold[i]; }
            keys.Add(new ObjectReferenceKeyframe { time = t / (float)Fps, value = sprites[4] }); // last drawing gets its full hold
            AnimationUtility.SetObjectReferenceCurve(clip, Binding, keys.ToArray());
            var settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.loopTime = false;
            AnimationUtility.SetAnimationClipSettings(clip, settings);
            if (created) AssetDatabase.CreateAsset(clip, ClipPath);
            EditorUtility.SetDirty(clip);
            AssetDatabase.SaveAssets();

            if (Object.FindAnyObjectByType<FighterPoseView>(FindObjectsInactive.Include)) PoseClipSetup.SetUp();
            status = $"Dash animation: {string.Join(" / ", hold)} frames ({t} frames, {t / 60f:0.00} s). " +
                     (Object.FindAnyObjectByType<FighterPoseView>(FindObjectsInactive.Include)
                         ? "Mapped on both Monks — save the scene." : "Open FightScene and Apply again to map it.");
            Debug.Log("[Monk Dash Timing] " + status);
        }

        void RestoreOld()
        {
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(ClipPath);
            var old = AssetDatabase.LoadAssetAtPath<Sprite>(OldDashPath);
            if (!clip || !old) { status = "Pose_Monk_Dash or Monk_Dash.PNG not found."; return; }
            Undo.RecordObject(clip, "Restore Old Dash");
            clip.ClearCurves();
            clip.frameRate = Fps;
            AnimationUtility.SetObjectReferenceCurve(clip, Binding, new[]
            {
                new ObjectReferenceKeyframe { time = 0f, value = old },
                new ObjectReferenceKeyframe { time = 1f / Fps, value = old },
            });
            EditorUtility.SetDirty(clip);
            AssetDatabase.SaveAssets();
            status = "Pose_Monk_Dash is the single old dash drawing again.";
        }

        static EditorCurveBinding Binding => EditorCurveBinding.PPtrCurve("", typeof(SpriteRenderer), "m_Sprite");

        static Sprite Import(string path, TextureImporter template)
        {
            var ti = AssetImporter.GetAtPath(path) as TextureImporter;
            if (!ti) return null;
            ti.GetSourceTextureWidthAndHeight(out int w, out int h);
            if (w <= 0 || h <= 0) { w = 450; h = 386; }
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
