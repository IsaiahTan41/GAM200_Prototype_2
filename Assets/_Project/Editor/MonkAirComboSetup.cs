#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using KungFuCrashout.Feedback;
using KungFuCrashout.Fighters;

namespace KungFuCrashout.EditorTools
{
    /// <summary>
    /// Menu: Window → KungFu Crashout → Animation → Set Up Monk Air Combo (Player)
    /// Turns the single air attack into a 3-hit air combo string (tap in the air, tap again to chain, like the light
    /// combo). The Monk floats for the length of the string (Fighter → Float During Air Combo), then falls.
    /// No new art: the two extra hits are VARIATIONS of the five MONKAIRATTACK drawings (order and timing):
    ///   Hit 1  Monk_AirAttack   the existing overhead cut — gets a CancelOpen event @ 14 so the string can chain
    ///   Hit 2  Monk_AirAttack2  rising backhand: the same drawings played in REVERSE (5→1), quicker, hitbox higher
    ///                           frames _5@0 _4@4 _3@8 _2@11 _1@15, end 21 · HitboxOn 8, HitboxOff 14, CancelOpen 15
    ///   Hit 3  Monk_AirAttack3  finisher: the overhead cut again, slower with a held wind-up and a bigger hitbox, HEAVY
    ///                           frames _1@0 _2@10 _3@14 _4@19 _5@24, end 34 · HitboxOn 10, HitboxOff 19 (last hit: no CancelOpen)
    /// Each hit has its own AttackData (Data/Attacks/Monk_AirAttack2 / 3, created only if missing, so tuning is kept)
    /// timed so the opponent is still in hitstun when the next hit lands, with a small lunge so the string follows them.
    /// Also: Fighter → Air Combo = [1, 2, 3], FighterAnimator maps each to its clip, and if VfxSet_Monk exists, the air
    /// hits get slash effects without ground dust (VfxSet → Per Attack).
    /// Runs Set Up Monk Air Attack first if the Player has no air attack yet. Save the scene afterwards.
    /// Re-running rebuilds clips 2 and 3 (hand-keyed hitbox frames on them are reset); clip 1 only gets its CancelOpen.
    /// Place in: Assets/_Project/Editor/MonkAirComboSetup.cs
    /// </summary>
    public static class MonkAirComboSetup
    {
        const string Title = "Monk Air Combo";
        const string Clip1Path = MonkAttackClipSetup.Folder + "/Monk_AirAttack.anim";
        const string AttackFolder = "Assets/_Project/Data/Attacks";
        const string Attack1Path = AttackFolder + "/Monk_AirAttack.asset";
        const string VfxSetPath = "Assets/_Project/Data/VFX/VfxSet_Monk.asset";
        const float Fps = 60f;
        const int Hit1CancelFrame = 14;

        struct HitSpec
        {
            public string name;
            public (int drawing, int frame)[] frames; // drawing 1..5 → first frame it shows
            public int end, hitboxOn, hitboxOff, cancelOpen; // cancelOpen < 0 = none
            public Vector2 hitboxOffset, hitboxSize;          // Visual-local, facing right
            public System.Action<AttackData> tune;
        }

        static readonly HitSpec[] Hits =
        {
            new HitSpec
            {
                name = "Monk_AirAttack2",
                frames = new[] { (5, 0), (4, 4), (3, 8), (2, 11), (1, 15) },
                end = 21, hitboxOn = 8, hitboxOff = 14, cancelOpen = 15,
                hitboxOffset = new Vector2(0.5f, 0.95f), hitboxSize = new Vector2(1.3f, 1.7f),
                tune = a =>
                {
                    a.damage = 5f; a.weight = HitWeight.Light;
                    a.knockback = 0.3f; a.hitstun = 0.38f; a.blockstun = 0.22f; a.chipDamage = 0.1f;
                    a.lunge = 0.2f;
                },
            },
            new HitSpec
            {
                name = "Monk_AirAttack3",
                frames = new[] { (1, 0), (2, 10), (3, 14), (4, 19), (5, 24) },
                end = 34, hitboxOn = 10, hitboxOff = 19, cancelOpen = -1,
                hitboxOffset = new Vector2(0.5f, 0.45f), hitboxSize = new Vector2(1.6f, 2.2f),
                tune = a =>
                {
                    a.damage = 8f; a.weight = HitWeight.Heavy;
                    a.knockback = 0.7f; a.hitstun = 0.5f; a.blockstun = 0.3f; a.chipDamage = 0.15f;
                    a.lunge = 0.25f;
                },
            },
        };

        [MenuItem("Window/KungFu Crashout/Animation/Set Up Monk Air Combo (Player)", priority = 10013)]
        public static void SetUp()
        {
            if (EditorApplication.isPlaying) { EditorUtility.DisplayDialog(Title, "Stop Play mode first.", "OK"); return; }

            var visual = AnimatorSetupUtil.FindPlayerVisual(Title, out var player);
            if (!visual) return;

            var fso = new SerializedObject(player);
            var comboProp = fso.FindProperty("airCombo");
            if (comboProp == null)
            {
                EditorUtility.DisplayDialog(Title, "This Fighter.cs has no Air Combo field. Update Scripts/Fighters/Fighter.cs first.", "OK");
                return;
            }

            // Hit 1 comes from the air attack setup.
            var clip1 = AssetDatabase.LoadAssetAtPath<AnimationClip>(Clip1Path);
            var attack1 = AssetDatabase.LoadAssetAtPath<AttackData>(Attack1Path);
            if (!clip1 || !attack1)
            {
                MonkAirAttackSetup.SetUp();
                clip1 = AssetDatabase.LoadAssetAtPath<AnimationClip>(Clip1Path);
                attack1 = AssetDatabase.LoadAssetAtPath<AttackData>(Attack1Path);
                if (!clip1 || !attack1) return; // that setup already said why
                fso.Update();
            }
            AddCancelOpen(clip1, Hit1CancelFrame);

            var drawings = LoadDrawings();
            if (drawings == null) return;

            Undo.SetCurrentGroupName("Set Up Monk Air Combo");
            int group = Undo.GetCurrentGroup();

            var animator = AnimatorSetupUtil.EnsureAnimator(visual, out var controller);
            var fa = AnimatorSetupUtil.EnsureFighterAnimator(visual, animator);
            AnimatorSetupUtil.MapAttack(fa, attack1, clip1);

            var attacks = new List<AttackData> { attack1 };
            for (int i = 0; i < Hits.Length; i++)
            {
                var spec = Hits[i];
                var clip = BuildClip(spec, drawings);
                var attack = GetOrCreateAttack(spec, attack1, player, visual);
                AnimatorSetupUtil.EnsureClipState(controller, clip, new Vector3(300f, 420f + 60f * i, 0f));
                AnimatorSetupUtil.MapAttack(fa, attack, clip);
                attacks.Add(attack);
            }

            comboProp.arraySize = attacks.Count;
            for (int i = 0; i < attacks.Count; i++) comboProp.GetArrayElementAtIndex(i).objectReferenceValue = attacks[i];
            fso.FindProperty("airAttack").objectReferenceValue = attack1;
            fso.FindProperty("floatDuringAirCombo").boolValue = true;
            fso.FindProperty("fighterAnimator").objectReferenceValue = fa;
            fso.FindProperty("useAnimationTiming").boolValue = true;
            fso.ApplyModifiedProperties();

            string vfx = AddVfxOverrides(attacks);
            AssetDatabase.SaveAssets();

            Undo.CollapseUndoOperations(group);
            EditorSceneManager.MarkSceneDirty(player.gameObject.scene);
            Selection.activeObject = player.gameObject;

            Debug.Log($"[Animation] Player's air combo = {string.Join(" → ", attacks.ConvertAll(a => a.name))} " +
                      $"(clip 1 CancelOpen @ {Hit1CancelFrame}; 2 = reversed rising backhand; 3 = slow heavy finisher). " +
                      $"Float During Air Combo on. {vfx}Tap in the air, tap again to chain. Save the scene (Ctrl+S).");
        }

        // ───────────── Clips ─────────────

        static Sprite[] LoadDrawings()
        {
            var sprites = new Sprite[5];
            for (int i = 0; i < 5; i++)
            {
                string name = $"MONKAIRATTACK_{i + 1}";
                string path = null;
                foreach (var guid in AssetDatabase.FindAssets($"{name} t:Texture2D", new[] { MonkAttackClipSetup.Folder }))
                {
                    string p = AssetDatabase.GUIDToAssetPath(guid);
                    if (System.IO.Path.GetFileNameWithoutExtension(p) == name) { path = p; break; }
                }
                sprites[i] = path != null ? AssetDatabase.LoadAssetAtPath<Sprite>(path) : null;
                if (!sprites[i])
                {
                    EditorUtility.DisplayDialog(Title, $"{name} isn't imported as a sprite. Run Set Up Monk Air Attack (Player) first.", "OK");
                    return null;
                }
            }
            return sprites;
        }

        static AnimationClip BuildClip(HitSpec spec, Sprite[] drawings)
        {
            string path = $"{MonkAttackClipSetup.Folder}/{spec.name}.anim";
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            bool created = !clip;
            if (created) clip = new AnimationClip();
            else clip.ClearCurves();
            clip.frameRate = Fps;

            var keys = new ObjectReferenceKeyframe[spec.frames.Length + 1];
            for (int i = 0; i < spec.frames.Length; i++)
                keys[i] = new ObjectReferenceKeyframe { time = spec.frames[i].frame / Fps, value = drawings[spec.frames[i].drawing - 1] };
            keys[spec.frames.Length] = new ObjectReferenceKeyframe
            {
                time = spec.end / Fps, value = drawings[spec.frames[spec.frames.Length - 1].drawing - 1], // hold to the end
            };
            AnimationUtility.SetObjectReferenceCurve(clip, EditorCurveBinding.PPtrCurve("", typeof(SpriteRenderer), "m_Sprite"), keys);

            float[] keyTimes = { 0f, spec.hitboxOn / Fps, spec.hitboxOff / Fps, spec.end / Fps };
            KeyConstant(clip, "m_Offset.x", spec.hitboxOffset.x, keyTimes);
            KeyConstant(clip, "m_Offset.y", spec.hitboxOffset.y, keyTimes);
            KeyConstant(clip, "m_Size.x", spec.hitboxSize.x, keyTimes);
            KeyConstant(clip, "m_Size.y", spec.hitboxSize.y, keyTimes);

            var events = new List<AnimationEvent>
            {
                new AnimationEvent { time = spec.hitboxOn / Fps,  functionName = "HitboxOn" },
                new AnimationEvent { time = spec.hitboxOff / Fps, functionName = "HitboxOff" },
            };
            if (spec.cancelOpen >= 0) events.Add(new AnimationEvent { time = spec.cancelOpen / Fps, functionName = "CancelOpen" });
            events.Add(new AnimationEvent { time = spec.end / Fps, functionName = "AttackEnd" });
            AnimationUtility.SetAnimationEvents(clip, events.ToArray());

            var settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.loopTime = false;
            AnimationUtility.SetAnimationClipSettings(clip, settings);

            if (created) AssetDatabase.CreateAsset(clip, path);
            else EditorUtility.SetDirty(clip);
            return clip;
        }

        /// <summary>Adds a CancelOpen event to a clip (only if it has none), keeping its curves and other events.</summary>
        static void AddCancelOpen(AnimationClip clip, int frame)
        {
            var events = new List<AnimationEvent>(AnimationUtility.GetAnimationEvents(clip));
            if (events.Exists(e => e.functionName == "CancelOpen")) return;
            events.Add(new AnimationEvent { time = frame / Fps, functionName = "CancelOpen" });
            events.Sort((a, b) => a.time.CompareTo(b.time));
            AnimationUtility.SetAnimationEvents(clip, events.ToArray());
            EditorUtility.SetDirty(clip);
        }

        static void KeyConstant(AnimationClip clip, string property, float value, float[] times)
        {
            var curve = new AnimationCurve();
            foreach (float t in times) curve.AddKey(new Keyframe(t, value, 0f, 0f));
            var binding = EditorCurveBinding.FloatCurve(AnimatorSetupUtil.HitboxName, typeof(BoxCollider2D), property);
            AnimationUtility.SetEditorCurve(clip, binding, curve);
        }

        // ───────────── Attack data ─────────────

        static AttackData GetOrCreateAttack(HitSpec spec, AttackData template, Fighter player, Transform visual)
        {
            string path = $"{AttackFolder}/{spec.name}.asset";
            var existing = AssetDatabase.LoadAssetAtPath<AttackData>(path);
            if (existing) return existing; // keep your tuning

            var a = Object.Instantiate(template); // block / stamina / launch settings as hit 1
            a.startup = spec.hitboxOn / Fps;
            a.active = (spec.hitboxOff - spec.hitboxOn) / Fps;
            a.recovery = (spec.end - spec.hitboxOff) / Fps;

            // The keyed box (Visual space) the AttackData way: relative to the fighter, facing right.
            float facing = Mathf.Sign(player.transform.localScale.x);
            Vector2 world = visual.TransformPoint(spec.hitboxOffset);
            Vector2 fromFighter = world - (Vector2)player.transform.position;
            Vector3 ls = visual.lossyScale;
            a.hitboxOffset = new Vector2(fromFighter.x * facing, fromFighter.y);
            a.hitboxSize = new Vector2(spec.hitboxSize.x * Mathf.Abs(ls.x), spec.hitboxSize.y * Mathf.Abs(ls.y));
            a.activeTravel = 0f;
            spec.tune(a);

            AnimatorSetupUtil.EnsureFolder(AttackFolder);
            AssetDatabase.CreateAsset(a, path);
            return a;
        }

        // ───────────── VFX ─────────────

        /// <summary>Air hits: slash only (no ground dust under a floating fighter). Adds entries that aren't there yet.</summary>
        static string AddVfxOverrides(List<AttackData> attacks)
        {
            var set = AssetDatabase.LoadAssetAtPath<VfxSet>(VfxSetPath);
            if (!set) return "";
            VfxCue s1 = set.lightCombo != null && set.lightCombo.Length > 0 ? set.lightCombo[0].strike : null;
            VfxCue s2 = set.lightCombo != null && set.lightCombo.Length > 1 ? set.lightCombo[1].strike : s1;
            var strikes = new[] { (s1, 1f), (s2, 1f), (s2, 1.3f) };

            int added = 0;
            for (int i = 0; i < attacks.Count && i < strikes.Length; i++)
            {
                if (set.perAttack.Exists(o => o.attack == attacks[i])) continue;
                set.perAttack.Add(new VfxSet.AttackOverride
                {
                    attack = attacks[i],
                    vfx = new VfxSet.AttackVfx { strike = strikes[i].Item1, dust = null, scale = strikes[i].Item2 },
                });
                added++;
            }
            if (added > 0) EditorUtility.SetDirty(set);
            return added > 0 ? $"VfxSet_Monk: {added} air hit slash(es) added. " : "";
        }
    }
}
#endif
