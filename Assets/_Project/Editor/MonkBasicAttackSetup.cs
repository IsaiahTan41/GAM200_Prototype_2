#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using KungFuCrashout.Fighters;

namespace KungFuCrashout.EditorTools
{
    /// <summary>
    /// Menu: Window → KungFu Crashout → Animation → Set Up Monk Basic Attack (MONKBASIC 1–4)
    /// The new basic attack (the light combo's hits) from the four drawings MONKBASIC_1…4 in AnimationSprites, in order:
    ///   1 ready stance · 2 staff drawn back · 3 leaping spin · 4 overhead slam into the ground (the strike)
    /// It replaces Monk_Attack for every attack that played it (both fighters), and gives those attacks FRAME DATA:
    ///   frames (60 fps)  0 drawing 1 · 3 drawing 2 · 6 drawing 3 · 10 drawing 4 (held to 22)
    ///   active 10–13 (hitbox sets: 10 the falling arc in front, 12 the staff along the ground) · cancel 14 · 23 frames
    ///   travel: the forward step drawn into the art (each drawing's pivot sits at its own feet, the fighter steps instead)
    ///   hurtbox sets: one body box per drawing (lower while crouched, raised in the leap)
    /// Import: Sprite (Single), PPU 350 (the same scale as the old basic attack: head and beads measure the same), Full Rect,
    /// Bilinear, no mipmaps, max 2048, pivot at each drawing's feet. The clip also keeps events + keyed Hitbox, so turning
    /// Fighter → Use Frame Data off still plays it the old way. Re-running rebuilds the clip and the frame data.
    /// Also converts the other mapped clips (air combo, throw) to frame data if they have none, and turns on Use Frame Data.
    ///
    /// Menu: Window → KungFu Crashout → Animation → Set Up Monk Heavy Attack (MONKBASIC, held leap)
    /// The heavy attack from the same four drawings, with drawing 3 (the leap, staff raised) HELD for Heavy Hold Frames
    /// before the slam: frames 0 / 4 / 8 (held to 23) / 24 slam, active 24–29, 48 frames, no cancel. Writes the heavy's
    /// frame data (its seconds stay, so the original heavy still works), maps it on both fighters, turns on Fighter →
    /// Heavy Uses Animation and adds the pause-menu "Heavy attack" toggle (ticked = this, unticked = the original heavy).
    /// Place in: Assets/_Project/Editor/MonkBasicAttackSetup.cs
    /// </summary>
    public static class MonkBasicAttackSetup
    {
        const string Title = "Monk Basic Attack";
        const string SpriteFolder = MonkAttackClipSetup.Folder + "/AnimationSprites";
        const string OldClipName = "Monk_Attack";
        const float PixelsPerUnit = 350f;
        const float Fps = 60f;
        const float CanvasW = 1500f, CanvasH = 1000f;
        const float GroundY = 975f; // feet line in the 1500 x 1000 drawings (pixels from the top)

        // Drawing file and the x (pixels) of its feet centre (the pivot).
        static readonly (string file, float feetX)[] Drawings =
        {
            ("MONKBASIC_1", 669f),
            ("MONKBASIC_2", 713f),
            ("MONKBASIC_3", 745f),  // airborne: between the take-off and the landing
            ("MONKBASIC_4", 778f),
        };

        // Boxes in pixels of each drawing (x0, y0, x1, y1), converted to Visual-local units at that drawing's pivot.
        static readonly Vector4 ArcPx = new Vector4(900, 300, 1450, 975);     // drawing 4: the arc coming down in front
        static readonly Vector4 GroundPx = new Vector4(800, 820, 1450, 975);  // drawing 4: the staff along the ground
        static readonly Vector4[] BodyPx =
        {
            new Vector4(500, 362, 880, 979),   // drawing 1 standing
            new Vector4(520, 545, 960, 970),   // drawing 2 crouched
            new Vector4(560, 220, 880, 955),   // drawing 3 leaping
            new Vector4(640, 540, 1040, 971),  // drawing 4 slam
        };

        /// <summary>One move built from the four drawings: when each drawing appears and the frame data around it.</summary>
        internal struct Spec
        {
            public string clipPath;
            public int[] drawingFrames;   // first frame of drawings 1–4
            public int activeStart, activeEnd, cancelFrame, lastFrame;
            public int groundBoxFrame;    // when the hitbox changes from the arc to the staff on the ground
        }

        /// <summary>Basic attack (light combo): quick, chains from frame 14.</summary>
        internal static readonly Spec Basic = new Spec
        {
            clipPath = MonkAttackClipSetup.Folder + "/Monk_BasicAttack.anim",
            drawingFrames = new[] { 0, 3, 6, 10 },
            activeStart = 10, activeEnd = 14, cancelFrame = 14, lastFrame = 22, groundBoxFrame = 12,
        };

        /// <summary>How long the heavy holds drawing 3 (the leap, staff raised) before the slam, in frames (60 fps).</summary>
        public const int HeavyHoldFrames = 16;

        /// <summary>Heavy attack: the same drawings, drawing 3 HELD before the slam; no cancel; 48 frames like the old heavy.</summary>
        internal static readonly Spec Heavy = new Spec
        {
            clipPath = MonkAttackClipSetup.Folder + "/Monk_HeavyAttack.anim",
            drawingFrames = new[] { 0, 4, 8, 8 + HeavyHoldFrames },
            activeStart = 8 + HeavyHoldFrames, activeEnd = 8 + HeavyHoldFrames + 6, cancelFrame = -1, lastFrame = 47,
            groundBoxFrame = 8 + HeavyHoldFrames + 3,
        };

        public const int ActiveStart = 10, ActiveEnd = 14, CancelFrame = 14, LastFrame = 22; // the basic attack (kept for reference)

        [MenuItem("Window/KungFu Crashout/Animation/Set Up Monk Basic Attack (MONKBASIC 1–4)", priority = 10014)]
        public static void SetUp()
        {
            if (EditorApplication.isPlaying) { EditorUtility.DisplayDialog(Title, "Stop Play mode first.", "OK"); return; }

            var sprites = ImportDrawings();
            if (sprites == null) return;
            var clip = BuildClip(Basic, sprites);
            var frames = BuildFrameData(Basic);

            Undo.SetCurrentGroupName("Set Up Monk Basic Attack");
            int group = Undo.GetCurrentGroup();
            var log = new List<string>();
            var replaced = new HashSet<AttackData>();

            foreach (var fa in Object.FindObjectsByType<FighterAnimator>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                var animator = fa.GetComponent<Animator>();
                var controller = animator ? animator.runtimeAnimatorController as AnimatorController : null;
                if (!controller) continue;
                AnimatorSetupUtil.EnsureClipState(controller, clip, new Vector3(300f, 300f, 0f));

                var so = new SerializedObject(fa);
                var list = so.FindProperty("attackClips");
                int n = 0;
                for (int i = 0; i < list.arraySize; i++)
                {
                    var e = list.GetArrayElementAtIndex(i);
                    var c = e.FindPropertyRelative("clip").objectReferenceValue as AnimationClip;
                    if (!c || (c.name != OldClipName && c != clip)) continue;
                    e.FindPropertyRelative("clip").objectReferenceValue = clip;
                    var attack = e.FindPropertyRelative("attack").objectReferenceValue as AttackData;
                    if (attack) replaced.Add(attack);
                    n++;
                }
                so.ApplyModifiedProperties();
                if (n > 0) log.Add($"{fa.transform.root.name}/{fa.name}: {n} attack(s) now play {clip.name}");
            }

            if (replaced.Count == 0)
            {
                EditorUtility.DisplayDialog(Title, $"No fighter in the open scene plays {OldClipName}. Open FightScene first " +
                                                   "(the basic attack replaces Monk_Attack wherever it is used).", "OK");
                return;
            }

            foreach (var a in replaced)
            {
                Undo.RecordObject(a, "Monk Basic Attack frame data");
                a.frames = Clone(frames);
                a.startup = ActiveStart / Fps;                 // the same timing for Use Frame Data off
                a.active = (ActiveEnd - ActiveStart) / Fps;
                a.recovery = (LastFrame + 1 - ActiveEnd) / Fps;
                EditorUtility.SetDirty(a);
            }

            var converted = new List<string>();
            FrameDataTools.ConvertScene(false, converted);
            FrameDataTools.EnableOnFighters();
            AssetDatabase.SaveAssets();
            Undo.CollapseUndoOperations(group);
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());

            Debug.Log($"[Animation] Basic attack = {clip.name} (MONKBASIC 1–4): {string.Join("; ", log)}. Frame data on " +
                      $"{string.Join(", ", new List<AttackData>(replaced).ConvertAll(a => a.name))}: {LastFrame + 1} frames, " +
                      $"active {ActiveStart}–{ActiveEnd - 1}, cancel {CancelFrame}. " +
                      (converted.Count > 0 ? $"Also converted: {string.Join(", ", converted)}. " : "") +
                      "Use Frame Data is on. Save the scene (Ctrl+S).");
        }

        [MenuItem("Window/KungFu Crashout/Animation/Set Up Monk Heavy Attack (MONKBASIC, held leap)", priority = 10015)]
        public static void SetUpHeavy()
        {
            if (EditorApplication.isPlaying) { EditorUtility.DisplayDialog(Title, "Stop Play mode first.", "OK"); return; }

            var sprites = ImportDrawings();
            if (sprites == null) return;
            var clip = BuildClip(Heavy, sprites);
            var frames = BuildFrameData(Heavy);

            Undo.SetCurrentGroupName("Set Up Monk Heavy Attack");
            int group = Undo.GetCurrentGroup();
            var log = new List<string>();
            var heavies = new HashSet<AttackData>();

            foreach (var fighter in Object.FindObjectsByType<Fighter>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                var fso = new SerializedObject(fighter);
                var heavy = fso.FindProperty("heavyAttack").objectReferenceValue as AttackData;
                if (!heavy) continue;
                var fa = fso.FindProperty("fighterAnimator").objectReferenceValue as FighterAnimator;
                if (!fa) fa = fighter.GetComponentInChildren<FighterAnimator>(false);
                var animator = fa ? fa.GetComponent<Animator>() : null;
                var controller = animator ? animator.runtimeAnimatorController as AnimatorController : null;
                if (!controller) { log.Add($"{fighter.name}: no FighterAnimator / controller, skipped"); continue; }

                AnimatorSetupUtil.EnsureClipState(controller, clip, new Vector3(300f, 240f, 0f));
                AnimatorSetupUtil.MapAttack(fa, heavy, clip);
                fso.FindProperty("heavyUsesAnimation").boolValue = true;
                fso.ApplyModifiedProperties();
                heavies.Add(heavy);
                log.Add($"{fighter.name}: {heavy.name} plays {clip.name}");
            }

            if (heavies.Count == 0)
            {
                EditorUtility.DisplayDialog(Title, "No fighter with a Heavy Attack and a FighterAnimator in the open scene. Open FightScene first.", "OK");
                return;
            }

            // Frame data only: the heavy's Startup / Active / Recovery seconds stay as they were (the original heavy).
            foreach (var a in heavies)
            {
                Undo.RecordObject(a, "Monk Heavy Attack frame data");
                a.frames = Clone(frames);
                EditorUtility.SetDirty(a);
            }

            FighterSettingToggleSetup.AddToOpenScene(log);
            AssetDatabase.SaveAssets();
            Undo.CollapseUndoOperations(group);
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());

            Debug.Log($"[Animation] Heavy = {clip.name} (MONKBASIC 1–4, drawing 3 held {HeavyHoldFrames} frames): " +
                      $"{string.Join("; ", log)}. Slam on frame {Heavy.activeStart} (active {Heavy.activeStart}–{Heavy.activeEnd - 1}), " +
                      $"{Heavy.lastFrame + 1} frames, no cancel. Fighter → Heavy Uses Animation (or the pause menu) switches back " +
                      "to the original heavy. Save the scene (Ctrl+S).");
        }

        // ───────────── Import ─────────────

        internal static Sprite[] ImportDrawings()
        {
            var sprites = new Sprite[Drawings.Length];
            for (int i = 0; i < Drawings.Length; i++)
            {
                string path = Find(Drawings[i].file);
                var ti = path != null ? AssetImporter.GetAtPath(path) as TextureImporter : null;
                if (!ti)
                {
                    EditorUtility.DisplayDialog(Title, $"Couldn't find {Drawings[i].file}.png in {SpriteFolder}.", "OK");
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
                s.spritePivot = new Vector2(Drawings[i].feetX / CanvasW, 1f - GroundY / CanvasH);
                s.spriteMeshType = SpriteMeshType.FullRect;
                ti.SetTextureSettings(s);
                ti.SaveAndReimport();
                sprites[i] = AssetDatabase.LoadAssetAtPath<Sprite>(path);
                if (!sprites[i]) { EditorUtility.DisplayDialog(Title, $"{Drawings[i].file} didn't import as a sprite.", "OK"); return null; }
            }
            return sprites;
        }

        static string Find(string name)
        {
            foreach (var guid in AssetDatabase.FindAssets($"{name} t:Texture2D", new[] { SpriteFolder }))
            {
                string p = AssetDatabase.GUIDToAssetPath(guid);
                if (System.IO.Path.GetFileNameWithoutExtension(p) == name) return p;
            }
            return null;
        }

        // ───────────── Clip (sprites + the old-style events and keyed hitbox, for Use Frame Data off) ─────────────

        internal static AnimationClip BuildClip(Spec spec, Sprite[] sprites)
        {
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(spec.clipPath);
            bool created = !clip;
            if (created) clip = new AnimationClip();
            else clip.ClearCurves();
            clip.frameRate = Fps;

            var keys = new ObjectReferenceKeyframe[Drawings.Length + 1];
            for (int i = 0; i < Drawings.Length; i++)
                keys[i] = new ObjectReferenceKeyframe { time = spec.drawingFrames[i] / Fps, value = sprites[i] };
            keys[Drawings.Length] = new ObjectReferenceKeyframe { time = spec.lastFrame / Fps, value = sprites[Drawings.Length - 1] };
            AnimationUtility.SetObjectReferenceCurve(clip, EditorCurveBinding.PPtrCurve("", typeof(SpriteRenderer), "m_Sprite"), keys);

            // Keyed Hitbox (stepped), the same boxes as the frame data.
            var boxes = new List<(int frame, FrameBox box)>
            {
                (spec.activeStart, ToBox(ArcPx, 3)),
                (spec.groundBoxFrame, ToBox(GroundPx, 3)),
            };
            string[] props = { "m_Offset.x", "m_Offset.y", "m_Size.x", "m_Size.y" };
            for (int p = 0; p < 4; p++)
            {
                var curve = new AnimationCurve();
                float Val(FrameBox b) => p == 0 ? b.offset.x : p == 1 ? b.offset.y : p == 2 ? b.size.x : b.size.y;
                curve.AddKey(new Keyframe(0f, Val(boxes[0].box), float.PositiveInfinity, float.PositiveInfinity));
                foreach (var (frame, box) in boxes)
                    curve.AddKey(new Keyframe(frame / Fps, Val(box), float.PositiveInfinity, float.PositiveInfinity));
                curve.AddKey(new Keyframe(spec.lastFrame / Fps, Val(boxes[boxes.Count - 1].box), float.PositiveInfinity, float.PositiveInfinity));
                AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve(AnimatorSetupUtil.HitboxName, typeof(BoxCollider2D), props[p]), curve);
            }

            var events = new List<AnimationEvent>
            {
                new AnimationEvent { time = spec.activeStart / Fps, functionName = "HitboxOn" },
                new AnimationEvent { time = spec.activeEnd / Fps,   functionName = "HitboxOff" },
            };
            if (spec.cancelFrame >= 0) events.Add(new AnimationEvent { time = (spec.cancelFrame + 0.5f) / Fps, functionName = "CancelOpen" });
            events.Add(new AnimationEvent { time = (spec.lastFrame - 0.5f) / Fps, functionName = "AttackEnd" });
            AnimationUtility.SetAnimationEvents(clip, events.ToArray());

            var settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.loopTime = false;
            AnimationUtility.SetAnimationClipSettings(clip, settings);

            if (created) AssetDatabase.CreateAsset(clip, spec.clipPath);
            else EditorUtility.SetDirty(clip);
            AssetDatabase.SaveAssets();
            return clip;
        }

        // ───────────── Frame data ─────────────

        internal static FrameData BuildFrameData(Spec spec)
        {
            var fd = new FrameData
            {
                totalFrames = spec.lastFrame + 1,
                activeStart = spec.activeStart,
                activeEnd = spec.activeEnd,
                cancelFrame = spec.cancelFrame,
            };
            fd.hitboxSets.Add(new BoxSet { frame = spec.activeStart, boxes = new List<FrameBox> { ToBox(ArcPx, 3) } });
            fd.hitboxSets.Add(new BoxSet { frame = spec.groundBoxFrame, boxes = new List<FrameBox> { ToBox(GroundPx, 3) } });
            for (int i = 0; i < Drawings.Length; i++)
                fd.hurtboxSets.Add(new BoxSet { frame = spec.drawingFrames[i], boxes = new List<FrameBox> { ToBox(BodyPx[i], i) } });
            // The step drawn into the art: the fighter moves by the change in feet position at each drawing.
            for (int i = 1; i < Drawings.Length; i++)
                fd.travel.Add(new TravelKey { frame = spec.drawingFrames[i], distance = (Drawings[i].feetX - Drawings[i - 1].feetX) / PixelsPerUnit });
            return fd;
        }

        /// <summary>Pixel box (x0, y0, x1, y1; y down) on a drawing → Visual-local box at that drawing's pivot.</summary>
        static FrameBox ToBox(Vector4 px, int drawing)
        {
            float pivotX = Drawings[drawing].feetX;
            Vector2 centre = new Vector2(((px.x + px.z) * 0.5f - pivotX) / PixelsPerUnit, (GroundY - (px.y + px.w) * 0.5f) / PixelsPerUnit);
            Vector2 size = new Vector2((px.z - px.x) / PixelsPerUnit, (px.w - px.y) / PixelsPerUnit);
            return new FrameBox { offset = centre, size = size };
        }

        internal static FrameData Clone(FrameData src) => JsonUtility.FromJson<FrameData>(JsonUtility.ToJson(src));
    }
}
#endif
