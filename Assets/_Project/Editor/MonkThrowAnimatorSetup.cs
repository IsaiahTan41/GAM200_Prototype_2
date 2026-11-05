#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using KungFuCrashout.Fighters;
using KungFuCrashout.Props;

namespace KungFuCrashout.EditorTools
{
    /// <summary>
    /// Menu: Window → KungFu Crashout → Animation → Set Up Monk Throw (Player)
    /// Turns MONKTHROW_1…4 into one throw clip that plays in place of the throw pose, with a real wind-up:
    /// the item only leaves on the last drawing.
    ///  1. Import (same as the other Monk art): Sprite (Single), PPU 350, pivot on the ground between the feet,
    ///     Full Rect, Bilinear, no mipmaps, max 2048.
    ///  2. Clip Monk_Throw.anim (next to Monk_Attack.anim): 60 samples/second, Loop Time off.
    ///       frames 0–5   MONKTHROW_1  scoop the item
    ///       frames 6–11  MONKTHROW_2  lift
    ///       frames 12–17 MONKTHROW_3  raise (wind-up)
    ///       frames 18–32 MONKTHROW_4  release, held through recovery
    ///     Events: HitboxOn @ 18 (= release: the item leaves here), HitboxOff @ 20, AttackEnd @ 32.
    ///  3. Data/Attacks/Throw_Windup.asset (created once, keeps your tuning): the same timing for fighters without the
    ///     clip (Startup 0.30 s = until release, Active 0.03 s, Recovery 0.20 s). No hitbox: the item does the hitting.
    ///  4. Player: "Monk_Throw" state on the Player's controller, FighterAnimator maps Throw_Windup → Monk_Throw,
    ///     ItemHolder → Throw Wind-up on with Throw Attack = Throw_Windup. The Monk_Attack setup is kept.
    /// Undoable for the scene part. Save the scene afterwards. Re-running rebuilds the clip in place.
    /// Place in: Assets/_Project/Editor/MonkThrowAnimatorSetup.cs
    /// </summary>
    public static class MonkThrowAnimatorSetup
    {
        const string Title = "Monk Throw";
        const string ClipPath = MonkAttackClipSetup.Folder + "/Monk_Throw.anim";
        const string AttackFolder = "Assets/_Project/Data/Attacks";
        const string ThrowAttackPath = AttackFolder + "/Throw_Windup.asset";
        const float PixelsPerUnit = 350f;
        const float Fps = 60f;

        // Drawing → (first frame it shows, pivot measured from the art: feet midpoint, ground line).
        static readonly (string name, int frame, Vector2 pivot)[] Frames =
        {
            ("MONKTHROW_1", 0,  new Vector2(0.235f, 0.071f)),
            ("MONKTHROW_2", 6,  new Vector2(0.235f, 0.071f)),
            ("MONKTHROW_3", 12, new Vector2(0.235f, 0.071f)), // same drawing as Monk_Idle
            ("MONKTHROW_4", 18, new Vector2(0.238f, 0.072f)), // same drawing as Monk_Throw
        };

        public const int ReleaseFrame = 18;   // last drawing appears: the item leaves
        public const int ActiveEndFrame = 20;
        public const int EndFrame = 32;       // clip length (0.53 s)

        [MenuItem("Window/KungFu Crashout/Animation/Set Up Monk Throw (Player)", priority = 10011)]
        public static void SetUp()
        {
            if (EditorApplication.isPlaying) { EditorUtility.DisplayDialog(Title, "Stop Play mode first.", "OK"); return; }

            var visual = AnimatorSetupUtil.FindPlayerVisual(Title, out var player);
            if (!visual) return;
            var holder = player.GetComponent<ItemHolder>();
            if (!holder)
            {
                EditorUtility.DisplayDialog(Title, $"'{player.name}' has no ItemHolder (it's what throws items).", "OK");
                return;
            }

            // ── 1–2. Import + clip ──
            var clip = BuildClip();
            if (!clip) return;

            // ── 3. Throw timing asset ──
            var throwAttack = GetOrCreateThrowAttack();

            // ── 4. Scene ──
            Undo.SetCurrentGroupName("Set Up Monk Throw");
            int group = Undo.GetCurrentGroup();

            var animator = AnimatorSetupUtil.EnsureAnimator(visual, out var controller);
            AnimatorSetupUtil.EnsureClipState(controller, clip, new Vector3(300f, 240f, 0f));
            var fa = AnimatorSetupUtil.EnsureFighterAnimator(visual, animator);
            AnimatorSetupUtil.MapAttack(fa, throwAttack, clip);

            var fso = new SerializedObject(player);
            fso.FindProperty("fighterAnimator").objectReferenceValue = fa;
            fso.ApplyModifiedProperties();

            var hso = new SerializedObject(holder);
            hso.FindProperty("throwWindup").boolValue = true;
            hso.FindProperty("throwAttack").objectReferenceValue = throwAttack;
            hso.ApplyModifiedProperties();
            AssetDatabase.SaveAssets();

            Undo.CollapseUndoOperations(group);
            EditorSceneManager.MarkSceneDirty(player.gameObject.scene);
            Selection.activeObject = visual.gameObject;

            Debug.Log($"[Animation] Player throws with {clip.name} (controller: {controller.name}): wind-up frames 0–{ReleaseFrame - 1}, " +
                      $"item leaves on frame {ReleaseFrame} ({ReleaseFrame / Fps:0.00} s), clip ends on {EndFrame}. " +
                      $"ItemHolder → Throw Wind-up on, Throw Attack = {throwAttack.name}. Save the scene (Ctrl+S).");
        }

        static AnimationClip BuildClip()
        {
            var sprites = new Sprite[Frames.Length];
            for (int i = 0; i < Frames.Length; i++)
            {
                string path = FindFrame(Frames[i].name);
                var ti = path != null ? AssetImporter.GetAtPath(path) as TextureImporter : null;
                if (!ti)
                {
                    EditorUtility.DisplayDialog(Title, $"Couldn't find {Frames[i].name} under {MonkAttackClipSetup.Folder}.", "OK");
                    return null;
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
                    EditorUtility.DisplayDialog(Title, $"{Frames[i].name} didn't import as a sprite.", "OK");
                    return null;
                }
            }

            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(ClipPath);
            bool created = !clip;
            if (created) clip = new AnimationClip();
            else clip.ClearCurves();
            clip.frameRate = Fps;

            var keys = new ObjectReferenceKeyframe[Frames.Length + 1];
            for (int i = 0; i < Frames.Length; i++)
                keys[i] = new ObjectReferenceKeyframe { time = Frames[i].frame / Fps, value = sprites[i] };
            keys[Frames.Length] = new ObjectReferenceKeyframe { time = EndFrame / Fps, value = sprites[Frames.Length - 1] }; // hold to the end
            AnimationUtility.SetObjectReferenceCurve(clip, EditorCurveBinding.PPtrCurve("", typeof(SpriteRenderer), "m_Sprite"), keys);

            AnimationUtility.SetAnimationEvents(clip, new[]
            {
                new AnimationEvent { time = ReleaseFrame / Fps,   functionName = "HitboxOn" },  // release: the item leaves
                new AnimationEvent { time = ActiveEndFrame / Fps, functionName = "HitboxOff" },
                new AnimationEvent { time = EndFrame / Fps,       functionName = "AttackEnd" },
            });

            var settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.loopTime = false;
            AnimationUtility.SetAnimationClipSettings(clip, settings);

            if (created) AssetDatabase.CreateAsset(clip, ClipPath);
            else EditorUtility.SetDirty(clip);
            AssetDatabase.SaveAssets();
            return clip;
        }

        static AttackData GetOrCreateThrowAttack()
        {
            var existing = AssetDatabase.LoadAssetAtPath<AttackData>(ThrowAttackPath);
            if (existing) return existing; // keep your tuning

            AnimatorSetupUtil.EnsureFolder(AttackFolder);
            var a = ScriptableObject.CreateInstance<AttackData>();
            a.damage = 0f;
            a.weight = HitWeight.Light;
            a.startup = ReleaseFrame / Fps;                        // until the item leaves
            a.active = (ActiveEndFrame - ReleaseFrame) / Fps;
            a.recovery = (EndFrame - ActiveEndFrame) / Fps;
            a.hitboxOffset = Vector2.zero;
            a.hitboxSize = Vector2.zero;                          // no hitbox: the item does the hitting
            a.knockback = 0f;
            a.hitstun = 0f;
            a.lunge = 0f;
            a.activeTravel = 0f;
            AssetDatabase.CreateAsset(a, ThrowAttackPath);
            return a;
        }

        static string FindFrame(string name)
        {
            foreach (var guid in AssetDatabase.FindAssets($"{name} t:Texture2D", new[] { MonkAttackClipSetup.Folder }))
            {
                string p = AssetDatabase.GUIDToAssetPath(guid);
                if (System.IO.Path.GetFileNameWithoutExtension(p) == name) return p;
            }
            return null;
        }
    }
}
#endif
