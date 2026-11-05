#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using KungFuCrashout.Fighters;

namespace KungFuCrashout.EditorTools
{
    /// <summary>
    /// Menu: Window → KungFu Crashout → Animation → Set Up Monk Attack (Player)
    /// Puts the Monk_Attack clip on the Player in place of the single BasicAttack sprite, with frame-driven hitboxes:
    ///  1. (Re)builds Monk_Attack.anim (MonkAttackClipSetup.Build) and keys its Hitbox (child BoxCollider2D Offset / Size)
    ///     from the jab's hitbox, so the box can then be edited per frame in the Animation window.
    ///  2. Adds a "Monk_Attack" state to the Player's controller (the Visual's Animator's, else
    ///     AnimationControllers/Player.controller), returning to the empty "Idle" state when the clip ends.
    ///  3. Player → Visual: Animator, FighterAnimationEvents, FighterAnimator (all three light-combo attacks → Monk_Attack),
    ///     and a disabled "Hitbox" child. Other clips already set up (e.g. Monk_Throw) are kept.
    /// Heavy and specials keep their sprite poses. Undoable for the scene part; save the scene afterwards.
    /// Re-run after changing the clip menu's frame numbers (it rebuilds the clip, which clears hand-keyed hitbox frames).
    /// Place in: Assets/_Project/Editor/MonkAttackAnimatorSetup.cs
    /// </summary>
    public static class MonkAttackAnimatorSetup
    {
        const string Title = "Monk Attack";

        [MenuItem("Window/KungFu Crashout/Animation/Set Up Monk Attack (Player)", priority = 10010)]
        public static void SetUp()
        {
            if (EditorApplication.isPlaying) { EditorUtility.DisplayDialog(Title, "Stop Play mode first.", "OK"); return; }

            var visual = AnimatorSetupUtil.FindPlayerVisual(Title, out var player);
            if (!visual) return;

            var fso = new SerializedObject(player);
            var combo = fso.FindProperty("lightCombo");
            if (combo == null || combo.arraySize == 0 || !(combo.GetArrayElementAtIndex(0).objectReferenceValue is AttackData jab))
            {
                EditorUtility.DisplayDialog(Title, "The Player's Light Combo is empty.", "OK");
                return;
            }

            // ── 1. Clip ──
            MonkAttackClipSetup.Build();
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(MonkAttackClipSetup.ClipPath);
            if (!clip) return; // Build already explained why

            Undo.SetCurrentGroupName("Set Up Monk Attack");
            int group = Undo.GetCurrentGroup();

            // ── Hitbox: the jab's box (fighter space, facing right) in the Visual's local space ──
            var hb = AnimatorSetupUtil.EnsureHitbox(visual);
            var hbT = hb.transform;
            Undo.RecordObject(hbT, "Hitbox");
            hbT.localPosition = Vector3.zero;
            hbT.localRotation = Quaternion.identity;
            hbT.localScale = Vector3.one;

            float facing = Mathf.Sign(player.transform.localScale.x);
            Vector3 worldCentre = player.transform.position + new Vector3(jab.hitboxOffset.x * facing, jab.hitboxOffset.y, 0f);
            Vector2 localOffset = visual.InverseTransformPoint(worldCentre);
            Vector3 ls = visual.lossyScale;
            Vector2 localSize = new Vector2(jab.hitboxSize.x / Mathf.Max(0.0001f, Mathf.Abs(ls.x)),
                                            jab.hitboxSize.y / Mathf.Max(0.0001f, Mathf.Abs(ls.y)));
            hb.offset = localOffset;
            hb.size = localSize;

            // Key it in the clip (same box on every key: edit per frame in the Animation window).
            float[] keyTimes =
            {
                0f,
                MonkAttackClipSetup.StrikeFrame / MonkAttackClipSetup.Fps,
                MonkAttackClipSetup.ActiveEndFrame / MonkAttackClipSetup.Fps,
                MonkAttackClipSetup.EndFrame / MonkAttackClipSetup.Fps,
            };
            KeyConstant(clip, "m_Offset.x", localOffset.x, keyTimes);
            KeyConstant(clip, "m_Offset.y", localOffset.y, keyTimes);
            KeyConstant(clip, "m_Size.x", localSize.x, keyTimes);
            KeyConstant(clip, "m_Size.y", localSize.y, keyTimes);
            EditorUtility.SetDirty(clip);

            // ── 2–3. Controller state + components ──
            var animator = AnimatorSetupUtil.EnsureAnimator(visual, out var controller);
            AnimatorSetupUtil.EnsureClipState(controller, clip, new Vector3(300f, 120f, 0f));
            var fa = AnimatorSetupUtil.EnsureFighterAnimator(visual, animator);

            string mapped = "";
            for (int i = 0; i < combo.arraySize; i++)
            {
                var attack = combo.GetArrayElementAtIndex(i).objectReferenceValue as AttackData;
                if (!attack) continue;
                AnimatorSetupUtil.MapAttack(fa, attack, clip);
                mapped += (mapped.Length > 0 ? ", " : "") + attack.name;
            }

            fso.FindProperty("fighterAnimator").objectReferenceValue = fa;
            fso.FindProperty("useAnimationTiming").boolValue = true;
            fso.ApplyModifiedProperties();
            AssetDatabase.SaveAssets();

            Undo.CollapseUndoOperations(group);
            EditorSceneManager.MarkSceneDirty(player.gameObject.scene);
            Selection.activeObject = visual.gameObject;

            Debug.Log($"[Animation] Player plays {clip.name} for: {mapped} (controller: {controller.name}). Phases follow the clip's events " +
                      $"(hitbox live frames {MonkAttackClipSetup.StrikeFrame}–{MonkAttackClipSetup.ActiveEndFrame}), " +
                      $"hitbox = Visual/{AnimatorSetupUtil.HitboxName} keyed in the clip (starts as {jab.name}'s box). " +
                      "Save the scene (Ctrl+S).");
        }

        /// <summary>Key one BoxCollider2D property of the Hitbox child to a constant value at the given times.</summary>
        static void KeyConstant(AnimationClip clip, string property, float value, float[] times)
        {
            var curve = new AnimationCurve();
            foreach (float t in times) curve.AddKey(new Keyframe(t, value, 0f, 0f));
            var binding = EditorCurveBinding.FloatCurve(AnimatorSetupUtil.HitboxName, typeof(BoxCollider2D), property);
            AnimationUtility.SetEditorCurve(clip, binding, curve);
        }
    }
}
#endif
