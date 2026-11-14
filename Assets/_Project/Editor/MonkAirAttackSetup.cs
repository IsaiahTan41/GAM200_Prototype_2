#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using KungFuCrashout.Fighters;

namespace KungFuCrashout.EditorTools
{
    /// <summary>
    /// Menu: Window → KungFu Crashout → Animation → Set Up Monk Air Attack (Player)
    /// Gives the Player an air attack (a tap during a jump): an overhead staff swing that cuts down in front and
    /// carries on under the body, with a swoosh crescent that thins out behind it.
    /// The five drawings are made from Monk_Jump (same Monk, same lines and colours): the staff and the gripping hand
    /// swing about the grip and the figure tilts with the cut, so the clip takes over from the jump pose without a pop.
    ///  1. Import MONKAIRATTACK_1…5 (same as the other Monk art): Sprite (Single), PPU 350, Full Rect, Bilinear,
    ///     no mipmaps, max 2048. Pivot = the same body point as Monk_Jump's pivot.
    ///  2. Clip Monk_AirAttack.anim (next to Monk_Attack.anim): 60 samples/second, Loop Time off.
    ///       frames 0–5   MONKAIRATTACK_1  wind up (staff high behind)
    ///       frames 6–9   MONKAIRATTACK_2  the cut, full crescent (hitbox live 6–11)
    ///       frames 10–13 MONKAIRATTACK_3  carries on under the body, crescent thinning
    ///       frames 14–17 MONKAIRATTACK_4  follow-through, last wisps
    ///       frames 18–23 MONKAIRATTACK_5  settling back toward the jump pose
    ///     Events: HitboxOn @ 6, HitboxOff @ 12, CancelOpen @ 14 (the air combo's next hit), AttackEnd @ 23.
    ///     Set Up Monk Air Combo adds hits 2 and 3 (MonkAirComboSetup).
    ///     Hitbox (Visual/Hitbox Offset + Size) keyed over the front / lower-front the cut passes through; edit it in
    ///     the Animation window.
    ///  3. Data/Attacks/Monk_AirAttack.asset (used as it is if it exists, so your tuning is kept; created if missing):
    ///     the same timing for when the clip isn't used (Startup 0.10 s, Active 0.10 s, Recovery 0.18 s), light
    ///     weight, no lunge (the jump carries it).
    ///  4. Player: "Monk_AirAttack" state on the Player's controller, FighterAnimator maps Monk_AirAttack → the clip,
    ///     Fighter → Attacks → Air Attack = Monk_AirAttack. The Monk_Attack and Monk_Throw setups are kept.
    /// Needs the Fighter.cs that has the Air Attack field. Undoable for the scene part. Save the scene afterwards.
    /// Re-running rebuilds the clip in place (hand-keyed hitbox frames are reset).
    /// Place in: Assets/_Project/Editor/MonkAirAttackSetup.cs
    /// </summary>
    public static class MonkAirAttackSetup
    {
        const string Title = "Monk Air Attack";
        const string ClipPath = MonkAttackClipSetup.Folder + "/Monk_AirAttack.anim";
        const string AttackFolder = "Assets/_Project/Data/Attacks";
        const string AttackPath = AttackFolder + "/Monk_AirAttack.asset";
        const float PixelsPerUnit = 350f;
        const float Fps = 60f;

        // All five drawings share one 999 x 1026 canvas, so one pivot: the point Monk_Jump's pivot sits on.
        static readonly Vector2 Pivot = new Vector2(0.6096f, 0.0526f);

        // Drawing → first frame it shows.
        static readonly (string name, int frame)[] Frames =
        {
            ("MONKAIRATTACK_1", 0),
            ("MONKAIRATTACK_2", 6),
            ("MONKAIRATTACK_3", 10),
            ("MONKAIRATTACK_4", 14),
            ("MONKAIRATTACK_5", 18),
        };

        public const int StrikeFrame = 6;     // the cut drawing appears, hitbox on
        public const int ActiveEndFrame = 12; // 6 active frames
        public const int EndFrame = 23;       // clip length (0.40 s): fits inside one jump (about 0.7 s of air time)

        // Hitbox in the Visual's local space (= sprite units from the pivot, drawn facing right): the front and
        // lower-front the staff cuts through (tip at about x 0.86, y 0.75 on the cut drawing, then down past the feet).
        static readonly Vector2 HitboxOffset = new Vector2(0.45f, 0.55f);
        static readonly Vector2 HitboxSize = new Vector2(1.3f, 1.9f);

        [MenuItem("Window/KungFu Crashout/Animation/Set Up Monk Air Attack (Player)", priority = 10012)]
        public static void SetUp()
        {
            if (EditorApplication.isPlaying) { EditorUtility.DisplayDialog(Title, "Stop Play mode first.", "OK"); return; }

            var visual = AnimatorSetupUtil.FindPlayerVisual(Title, out var player);
            if (!visual) return;

            var fso = new SerializedObject(player);
            var airProp = fso.FindProperty("airAttack");
            if (airProp == null)
            {
                EditorUtility.DisplayDialog(Title, "This Fighter.cs has no Air Attack field. Update Scripts/Fighters/Fighter.cs first.", "OK");
                return;
            }

            // ── 1–2. Import + clip ──
            var clip = BuildClip();
            if (!clip) return;

            Undo.SetCurrentGroupName("Set Up Monk Air Attack");
            int group = Undo.GetCurrentGroup();

            // ── Hitbox child (shared with the other clips; each clip keys its own Offset / Size) ──
            var hb = AnimatorSetupUtil.EnsureHitbox(visual);
            var hbT = hb.transform;
            Undo.RecordObject(hbT, "Hitbox");
            hbT.localPosition = Vector3.zero;
            hbT.localRotation = Quaternion.identity;
            hbT.localScale = Vector3.one;

            float[] keyTimes = { 0f, StrikeFrame / Fps, ActiveEndFrame / Fps, EndFrame / Fps };
            KeyConstant(clip, "m_Offset.x", HitboxOffset.x, keyTimes);
            KeyConstant(clip, "m_Offset.y", HitboxOffset.y, keyTimes);
            KeyConstant(clip, "m_Size.x", HitboxSize.x, keyTimes);
            KeyConstant(clip, "m_Size.y", HitboxSize.y, keyTimes);
            EditorUtility.SetDirty(clip);

            // ── 3. Attack asset (its box = the keyed box, in fighter space, for when the clip isn't driving) ──
            var attack = GetOrCreateAttack(player, visual);

            // ── 4. Controller state + components ──
            var animator = AnimatorSetupUtil.EnsureAnimator(visual, out var controller);
            AnimatorSetupUtil.EnsureClipState(controller, clip, new Vector3(300f, 360f, 0f));
            var fa = AnimatorSetupUtil.EnsureFighterAnimator(visual, animator);
            AnimatorSetupUtil.MapAttack(fa, attack, clip);

            airProp.objectReferenceValue = attack;
            fso.FindProperty("fighterAnimator").objectReferenceValue = fa;
            fso.FindProperty("useAnimationTiming").boolValue = true;
            fso.ApplyModifiedProperties();
            AssetDatabase.SaveAssets();

            Undo.CollapseUndoOperations(group);
            EditorSceneManager.MarkSceneDirty(player.gameObject.scene);
            Selection.activeObject = visual.gameObject;

            Debug.Log($"[Animation] Player's air attack = {attack.name}, playing {clip.name} (controller: {controller.name}): " +
                      $"wind up frames 0–{StrikeFrame - 1}, hitbox live {StrikeFrame}–{ActiveEndFrame - 1}, ends on {EndFrame} " +
                      $"({EndFrame / Fps:0.00} s). Tap during a jump to use it. Save the scene (Ctrl+S).");
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
                s.spritePivot = Pivot;
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
                new AnimationEvent { time = StrikeFrame / Fps,    functionName = "HitboxOn" },
                new AnimationEvent { time = ActiveEndFrame / Fps, functionName = "HitboxOff" },
                new AnimationEvent { time = 14 / Fps,             functionName = "CancelOpen" }, // the air combo chains from here
                new AnimationEvent { time = EndFrame / Fps,       functionName = "AttackEnd" },
            });

            var settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.loopTime = false; // an attack plays once
            AnimationUtility.SetAnimationClipSettings(clip, settings);

            if (created) AssetDatabase.CreateAsset(clip, ClipPath);
            else EditorUtility.SetDirty(clip);
            AssetDatabase.SaveAssets();
            return clip;
        }

        static AttackData GetOrCreateAttack(Fighter player, Transform visual)
        {
            var existing = AssetDatabase.LoadAssetAtPath<AttackData>(AttackPath);
            if (existing) return existing; // keep your tuning

            // The keyed box (Visual space) expressed the AttackData way: relative to the fighter, facing right.
            float facing = Mathf.Sign(player.transform.localScale.x);
            Vector2 world = visual.TransformPoint(HitboxOffset);
            Vector2 fromFighter = world - (Vector2)player.transform.position;
            Vector3 ls = visual.lossyScale;

            AnimatorSetupUtil.EnsureFolder(AttackFolder);
            var a = ScriptableObject.CreateInstance<AttackData>();
            a.damage = 5f;                                        // = the second light hit (only counts on a Downed target)
            a.weight = HitWeight.Light;
            a.startup = StrikeFrame / Fps;
            a.active = (ActiveEndFrame - StrikeFrame) / Fps;
            a.recovery = (EndFrame - ActiveEndFrame) / Fps;
            a.hitboxOffset = new Vector2(fromFighter.x * facing, fromFighter.y);
            a.hitboxSize = new Vector2(HitboxSize.x * Mathf.Abs(ls.x), HitboxSize.y * Mathf.Abs(ls.y));
            a.knockback = 0.35f;
            a.hitstun = 0.36f;                                    // long enough to land and carry on with the light combo
            a.chipDamage = 0.1f;
            a.blockstun = 0.22f;
            a.blockPushback = 0.25f;
            a.blockStaminaCost = -1f;                             // automatic, like the light attacks
            a.lunge = 0f;                                         // the jump's own drift carries it
            a.activeTravel = 0f;
            AssetDatabase.CreateAsset(a, AttackPath);
            return a;
        }

        /// <summary>Key one BoxCollider2D property of the Hitbox child to a constant value at the given times.</summary>
        static void KeyConstant(AnimationClip clip, string property, float value, float[] times)
        {
            var curve = new AnimationCurve();
            foreach (float t in times) curve.AddKey(new Keyframe(t, value, 0f, 0f));
            var binding = EditorCurveBinding.FloatCurve(AnimatorSetupUtil.HitboxName, typeof(BoxCollider2D), property);
            AnimationUtility.SetEditorCurve(clip, binding, curve);
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
