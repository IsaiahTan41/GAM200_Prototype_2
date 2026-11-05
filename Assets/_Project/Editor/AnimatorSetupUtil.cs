#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using KungFuCrashout.Fighters;

namespace KungFuCrashout.EditorTools
{
    /// <summary>
    /// Shared steps for the "Window → KungFu Crashout → Animation" setup menus, so each menu ADDS its clip to the Player's
    /// controller instead of replacing what another menu set up.
    ///   • Controller: whatever the Visual's Animator already uses; else AnimationControllers/Player.controller (created if missing).
    ///   • States: an empty "Idle" (default) + one state per clip (named after the clip) that returns to Idle when it ends.
    ///   • FighterAnimator mappings are replaced per attack, never cleared wholesale.
    /// Place in: Assets/_Project/Editor/AnimatorSetupUtil.cs
    /// </summary>
    public static class AnimatorSetupUtil
    {
        public const string ControllerFolder = "Assets/_Project/Art/Characters/Animation/AnimationControllers";
        public const string ControllerPath = ControllerFolder + "/Player.controller";
        public const string IdleState = "Idle";
        public const string HitboxName = "Hitbox";

        public static Fighter FindPlayer()
        {
            var all = Object.FindObjectsByType<Fighter>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (var f in all) if (f.CompareTag("Player")) return f;
            foreach (var f in all) if (f.name.ToLowerInvariant().Contains("player")) return f;
            return null;
        }

        /// <summary>The Player's "Visual" child with a SpriteRenderer, or null (with a dialog).</summary>
        public static Transform FindPlayerVisual(string title, out Fighter player)
        {
            player = FindPlayer();
            var visual = player ? player.transform.Find("Visual") : null;
            if (!visual || !visual.GetComponent<SpriteRenderer>())
            {
                EditorUtility.DisplayDialog(title, "Open FightScene: need the Player fighter with a \"Visual\" child that has a SpriteRenderer.", "OK");
                return null;
            }
            return visual;
        }

        /// <summary>Animator on the Visual (added if missing) with a controller that has the Idle state.</summary>
        public static Animator EnsureAnimator(Transform visual, out AnimatorController controller)
        {
            var animator = visual.GetComponent<Animator>();
            if (!animator) animator = Undo.AddComponent<Animator>(visual.gameObject);

            controller = animator.runtimeAnimatorController as AnimatorController;
            if (!controller) controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            if (!controller)
            {
                EnsureFolder(ControllerFolder);
                controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
            }

            Undo.RecordObject(animator, "Animator");
            animator.runtimeAnimatorController = controller;
            animator.applyRootMotion = false;                          // Fighter moves the character
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;  // events still fire off screen
            animator.updateMode = AnimatorUpdateMode.Normal;           // freezes with the hitstop

            EnsureIdle(controller);
            return animator;
        }

        static void EnsureIdle(AnimatorController controller)
        {
            var sm = controller.layers[0].stateMachine;
            var idle = FindState(sm, IdleState);
            if (!idle) idle = sm.AddState(IdleState, new Vector3(300f, 0f, 0f));
            idle.motion = null;
            idle.writeDefaultValues = false; // empty: the sprite poses draw the fighter
            sm.defaultState = idle;
            EditorUtility.SetDirty(controller);
        }

        /// <summary>A state named after the clip (added or updated) that plays it once and returns to Idle.</summary>
        public static void EnsureClipState(AnimatorController controller, AnimationClip clip, Vector3 position)
        {
            var sm = controller.layers[0].stateMachine;
            var idle = FindState(sm, IdleState);
            var state = FindState(sm, clip.name);
            if (!state) state = sm.AddState(clip.name, position);
            state.motion = clip;
            state.writeDefaultValues = false;

            foreach (var old in state.transitions) state.RemoveTransition(old);
            var back = state.AddTransition(idle);
            back.hasExitTime = true;
            back.exitTime = 1f;
            back.hasFixedDuration = true;
            back.duration = 0f; // sprites don't blend
            EditorUtility.SetDirty(controller);
        }

        static AnimatorState FindState(AnimatorStateMachine sm, string name)
        {
            foreach (var child in sm.states)
                if (child.state && child.state.name == name) return child.state;
            return null;
        }

        /// <summary>FighterAnimationEvents + FighterAnimator on the Visual, wired to the Animator and the Hitbox.</summary>
        public static FighterAnimator EnsureFighterAnimator(Transform visual, Animator animator)
        {
            if (!visual.GetComponent<FighterAnimationEvents>()) Undo.AddComponent<FighterAnimationEvents>(visual.gameObject);

            var fa = visual.GetComponent<FighterAnimator>();
            if (!fa) fa = Undo.AddComponent<FighterAnimator>(visual.gameObject);
            var so = new SerializedObject(fa);
            so.FindProperty("animator").objectReferenceValue = animator;
            so.FindProperty("hitbox").objectReferenceValue = EnsureHitbox(visual);
            so.FindProperty("idleState").stringValue = IdleState;
            so.ApplyModifiedProperties();
            return fa;
        }

        /// <summary>The disabled "Hitbox" child (a shape only: the clips key its Offset / Size).</summary>
        public static BoxCollider2D EnsureHitbox(Transform visual)
        {
            var t = visual.Find(HitboxName);
            if (!t)
            {
                var go = new GameObject(HitboxName);
                Undo.RegisterCreatedObjectUndo(go, "Hitbox");
                t = go.transform;
                t.SetParent(visual, false);
            }
            var hb = t.GetComponent<BoxCollider2D>();
            if (!hb) hb = Undo.AddComponent<BoxCollider2D>(t.gameObject);
            Undo.RecordObject(hb, "Hitbox");
            hb.isTrigger = true;
            hb.enabled = false;
            return hb;
        }

        /// <summary>Make this attack play this clip (replaces an existing entry for the attack, keeps the others).</summary>
        public static void MapAttack(FighterAnimator fa, AttackData attack, AnimationClip clip)
        {
            var so = new SerializedObject(fa);
            var list = so.FindProperty("attackClips");
            SerializedProperty entry = null;
            for (int i = 0; i < list.arraySize; i++)
            {
                var e = list.GetArrayElementAtIndex(i);
                if (e.FindPropertyRelative("attack").objectReferenceValue == attack) { entry = e; break; }
            }
            if (entry == null)
            {
                list.InsertArrayElementAtIndex(list.arraySize);
                entry = list.GetArrayElementAtIndex(list.arraySize - 1);
                entry.FindPropertyRelative("attack").objectReferenceValue = attack;
            }
            entry.FindPropertyRelative("clip").objectReferenceValue = clip;
            so.ApplyModifiedProperties();
        }

        public static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = System.IO.Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, System.IO.Path.GetFileName(path));
        }
    }
}
#endif
