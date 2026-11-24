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
    /// Menu: Window → KungFu Crashout → Animation → Set Up Pose Clips (all sprites)
    /// Puts every drawing a fighter shows outside its attack clips through the new animation system:
    ///   • For each sprite assigned on a FighterPoseView (Idle, Block, Dash, Hitstun, Airborne, Downed, Throw, the special
    ///     poses…) it builds a pose clip "Pose_&lt;sprite name&gt;" in Animation/Poses (one drawing, keyed at frame 0).
    ///   • Adds a state per clip to the fighter's Animator Controller (no transitions: the frame counter picks the frame).
    ///   • Maps each pose to its clip on the FighterAnimator (Pose Clips) and turns Use Pose Clips on.
    /// The sprites keep their own import settings (PPU, pivot at the feet), so the scale on screen doesn't change.
    /// Re-run after swapping a drawing on FighterPoseView. A pose clip you have edited by hand (more than one drawing) is
    /// kept, not rebuilt: so an animated idle / block can replace the single drawing and this menu leaves it alone.
    /// Place in: Assets/_Project/Editor/PoseClipSetup.cs
    /// </summary>
    public static class PoseClipSetup
    {
        const string Title = "Pose Clips";
        const string Folder = MonkAttackClipSetup.Folder + "/Poses";
        const float Fps = 60f;

        // Played once and held (a reaction), the rest loop while the pose lasts.
        static readonly HashSet<PoseSlot> HoldLast = new HashSet<PoseSlot>
        {
            PoseSlot.Hitstun, PoseSlot.Downed, PoseSlot.KO, PoseSlot.Throw, PoseSlot.WindUp, PoseSlot.Attack,
            PoseSlot.HeavyAttack, PoseSlot.Recovery, PoseSlot.SideSpecial, PoseSlot.UpSpecial, PoseSlot.DownSpecial,
            PoseSlot.Dash, // the dash animation plays once per dash (restarted on each dash, follow-through after it)
        };

        [MenuItem("Window/KungFu Crashout/Animation/Set Up Pose Clips (all sprites)", priority = 10016)]
        public static void SetUp()
        {
            if (EditorApplication.isPlaying) { EditorUtility.DisplayDialog(Title, "Stop Play mode first.", "OK"); return; }

            Undo.SetCurrentGroupName("Set Up Pose Clips");
            int group = Undo.GetCurrentGroup();
            AnimatorSetupUtil.EnsureFolder(Folder);
            var log = new List<string>();
            var built = new Dictionary<Sprite, AnimationClip>();
            int fighters = 0;

            foreach (var view in Object.FindObjectsByType<FighterPoseView>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                var fa = view.GetComponent<FighterAnimator>();
                var animator = fa ? fa.GetComponent<Animator>() : null;
                var controller = animator ? animator.runtimeAnimatorController as AnimatorController : null;
                if (!controller) { log.Add($"{view.transform.root.name}: no FighterAnimator / controller, skipped"); continue; }

                var so = new SerializedObject(fa);
                var list = so.FindProperty("poseClips");
                int n = 0, row = 0;
                foreach (PoseSlot slot in System.Enum.GetValues(typeof(PoseSlot)))
                {
                    if (slot == PoseSlot.None) continue;
                    var sprite = view.SpriteFor(slot);
                    if (!sprite) { Remove(list, slot); continue; }

                    if (!built.TryGetValue(sprite, out var clip))
                    {
                        clip = BuildClip(sprite);
                        built[sprite] = clip;
                    }
                    EnsurePoseState(controller, clip, new Vector3(650f, -300f + 60f * row++, 0f));
                    Map(list, slot, clip, !HoldLast.Contains(slot));
                    n++;
                }
                so.FindProperty("usePoseClips").boolValue = true;
                so.ApplyModifiedProperties();
                EditorUtility.SetDirty(controller);
                fighters++;
                log.Add($"{view.transform.root.name}: {n} poses");
            }

            if (fighters == 0)
            {
                EditorUtility.DisplayDialog(Title, "No fighter with FighterPoseView + FighterAnimator in the open scene. Open FightScene first " +
                                                   "(run Set Up Monk Basic Attack first if the fighters have no FighterAnimator yet).", "OK");
                return;
            }

            AssetDatabase.SaveAssets();
            Undo.CollapseUndoOperations(group);
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            Debug.Log($"[Animation] Pose clips: {string.Join("; ", log)}. {built.Count} clips in {Folder}. " +
                      "FighterAnimator → Use Pose Clips is on (off = the old sprite swap). Save the scene (Ctrl+S).");
        }

        /// <summary>One-drawing clip for this sprite (kept as-is if it has been edited into a multi-drawing clip).</summary>
        static AnimationClip BuildClip(Sprite sprite)
        {
            string path = $"{Folder}/Pose_{sprite.name}.anim";
            var binding = EditorCurveBinding.PPtrCurve("", typeof(SpriteRenderer), "m_Sprite");
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            if (clip)
            {
                var existing = AnimationUtility.GetObjectReferenceCurve(clip, binding);
                var distinct = new HashSet<Object>();
                if (existing != null) foreach (var k in existing) distinct.Add(k.value);
                if (distinct.Count > 1) return clip; // hand-made animation: keep it
                clip.ClearCurves();
            }
            bool created = !clip;
            if (created) clip = new AnimationClip();
            clip.frameRate = Fps;
            AnimationUtility.SetObjectReferenceCurve(clip, binding, new[]
            {
                new ObjectReferenceKeyframe { time = 0f, value = sprite },
                new ObjectReferenceKeyframe { time = 1f / Fps, value = sprite }, // one frame long
            });
            var settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.loopTime = false; // the frame counter loops it, not the Animator
            AnimationUtility.SetAnimationClipSettings(clip, settings);
            if (created) AssetDatabase.CreateAsset(clip, path);
            else EditorUtility.SetDirty(clip);
            return clip;
        }

        /// <summary>A state named after the clip, with no transitions (FighterAnimator plays it at the frame it wants).</summary>
        static void EnsurePoseState(AnimatorController controller, AnimationClip clip, Vector3 position)
        {
            var sm = controller.layers[0].stateMachine;
            AnimatorState state = null;
            foreach (var child in sm.states) if (child.state && child.state.name == clip.name) state = child.state;
            if (!state) state = sm.AddState(clip.name, position);
            state.motion = clip;
            state.writeDefaultValues = false;
            foreach (var t in state.transitions) state.RemoveTransition(t);
        }

        static void Map(SerializedProperty list, PoseSlot slot, AnimationClip clip, bool loop)
        {
            SerializedProperty entry = null;
            for (int i = 0; i < list.arraySize; i++)
            {
                var e = list.GetArrayElementAtIndex(i);
                if (e.FindPropertyRelative("pose").enumValueIndex == (int)slot) { entry = e; break; }
            }
            if (entry == null)
            {
                list.InsertArrayElementAtIndex(list.arraySize);
                entry = list.GetArrayElementAtIndex(list.arraySize - 1);
                entry.FindPropertyRelative("pose").enumValueIndex = (int)slot;
            }
            entry.FindPropertyRelative("clip").objectReferenceValue = clip;
            entry.FindPropertyRelative("loop").boolValue = loop;
        }

        static void Remove(SerializedProperty list, PoseSlot slot)
        {
            for (int i = list.arraySize - 1; i >= 0; i--)
                if (list.GetArrayElementAtIndex(i).FindPropertyRelative("pose").enumValueIndex == (int)slot)
                    list.DeleteArrayElementAtIndex(i);
        }
    }
}
#endif
