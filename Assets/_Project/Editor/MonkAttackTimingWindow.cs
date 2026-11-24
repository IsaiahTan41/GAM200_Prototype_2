#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using KungFuCrashout.Fighters;

namespace KungFuCrashout.EditorTools
{
    /// <summary>
    /// Window → KungFu Crashout → Animation → Monk Attack Timing (hold frames)
    /// Edit how long each of the four MONKBASIC drawings is held, in frames (60 fps), then Apply: the clip and the
    /// attack's frame data are rebuilt together, so the art, hitbox, hurtboxes and cancel window stay in step.
    ///   1 Ready · 2 Drawn back (startup) · 3 Held up (leap, staff raised) · 4 Slam: Active frames, then Recovery hold
    /// Targets:
    ///   • All light hits — the one clip every light combo hit plays (Monk_BasicAttack): changes all three hits.
    ///   • Finisher only — gives the last light hit its own clip (Monk_BasicAttack_Finisher), so only it gets the longer
    ///     hold; hits 1 and 2 stay quick.
    ///   • Heavy — the held-leap heavy (Monk_HeavyAttack).
    /// A longer startup means the next combo hit lands later: the window shows whether each chain still lands inside the
    /// previous hit's hitstun, and can raise that hitstun on Apply so the combo keeps working.
    /// Open FightScene first. Undo works; Reset To Original puts the shipped timing back.
    /// Place in: Assets/_Project/Editor/MonkAttackTimingWindow.cs
    /// </summary>
    public class MonkAttackTimingWindow : EditorWindow
    {
        enum Target { AllLightHits, FinisherOnly, Heavy }

        const string FinisherClip = MonkAttackClipSetup.Folder + "/Monk_BasicAttack_Finisher.anim";

        Target target = Target.FinisherOnly;
        int ready = 3, drawnBack = 3, heldUp = 4, active = 4, recovery = 9;
        bool keepCombo = true;
        string status = "";

        [MenuItem("Window/KungFu Crashout/Animation/Monk Attack Timing (hold frames)", priority = 10016)]
        static void Open()
        {
            var w = GetWindow<MonkAttackTimingWindow>("Monk Attack Timing");
            w.minSize = new Vector2(380f, 420f);
            w.LoadCurrent();
        }

        void OnGUI()
        {
            EditorGUILayout.HelpBox("Frames are at 60 fps (6 frames = 0.1 s). Hold a drawing longer to make the move more " +
                                    "readable; Apply rebuilds the clip and the frame data together.", MessageType.None);

            EditorGUI.BeginChangeCheck();
            target = (Target)EditorGUILayout.EnumPopup(new GUIContent("Edit", "All light hits share one clip; Finisher only " +
                "gives the last light hit its own; Heavy = the held-leap heavy."), target);
            if (EditorGUI.EndChangeCheck()) LoadCurrent();

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Hold each drawing (frames)", EditorStyles.boldLabel);
            ready = Mathf.Max(1, EditorGUILayout.IntField(new GUIContent("1  Ready stance"), ready));
            drawnBack = Mathf.Max(1, EditorGUILayout.IntField(new GUIContent("2  Drawn back (startup)"), drawnBack));
            heldUp = Mathf.Max(1, EditorGUILayout.IntField(new GUIContent("3  Held up (leap)", "The pose before the slam."), heldUp));
            active = Mathf.Max(1, EditorGUILayout.IntField(new GUIContent("4  Slam: active frames", "Hitbox live."), active));
            recovery = Mathf.Max(1, EditorGUILayout.IntField(new GUIContent("4  Slam: recovery hold", "After the hitbox ends."), recovery));

            var spec = BuildSpec();
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Result", EditorStyles.boldLabel);
            EditorGUILayout.LabelField($"Hit lands on frame {spec.activeStart}  ({spec.activeStart / 60f:0.00} s)");
            EditorGUILayout.LabelField($"Active {spec.activeStart}–{spec.activeEnd - 1}, " +
                                       (spec.cancelFrame >= 0 ? $"cancel from {spec.cancelFrame}, " : "no cancel, ") +
                                       $"{spec.lastFrame + 1} frames ({(spec.lastFrame + 1) / 60f:0.00} s)");

            if (target != Target.Heavy)
            {
                EditorGUILayout.Space();
                EditorGUILayout.LabelField("Combo check", EditorStyles.boldLabel);
                foreach (var (label, gap, stun, _) in ComboChecks(spec))
                {
                    bool ok = gap < stun;
                    EditorGUILayout.LabelField($"{label}: next hit {gap} f after, hitstun {stun:0.#} f",
                        ok ? "lands" : "DROPS", ok ? EditorStyles.label : EditorStyles.boldLabel);
                }
                keepCombo = EditorGUILayout.ToggleLeft(new GUIContent("On Apply, raise hitstun where needed so the combo still lands",
                    "Sets the earlier hit's hitstun to the gap + 3 frames (only if it's shorter)."), keepCombo);
            }

            EditorGUILayout.Space();
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Apply", GUILayout.Height(28))) Apply();
                if (GUILayout.Button("Reset To Original", GUILayout.Height(28))) { ResetDefaults(); Apply(); }
            }
            if (GUILayout.Button("Show the clip in the Animation window")) ShowClip();
            if (!string.IsNullOrEmpty(status)) EditorGUILayout.HelpBox(status, MessageType.Info);
        }

        // ───────────── Spec ─────────────

        MonkBasicAttackSetup.Spec BuildSpec()
        {
            int a = ready, b = ready + drawnBack, c = ready + drawnBack + heldUp;
            bool heavy = target == Target.Heavy;
            return new MonkBasicAttackSetup.Spec
            {
                clipPath = target == Target.Heavy ? MonkBasicAttackSetup.Heavy.clipPath
                         : target == Target.FinisherOnly ? FinisherClip : MonkBasicAttackSetup.Basic.clipPath,
                drawingFrames = new[] { 0, a, b, c },
                activeStart = c,
                activeEnd = c + active,
                cancelFrame = heavy ? -1 : c + active,
                lastFrame = c + active + recovery - 1,
                groundBoxFrame = c + Mathf.Min(active - 1, heavy ? 3 : 2),
            };
        }

        void ResetDefaults()
        {
            var s = target == Target.Heavy ? MonkBasicAttackSetup.Heavy : MonkBasicAttackSetup.Basic;
            FromSpec(s.drawingFrames, s.activeStart, s.activeEnd, s.lastFrame);
        }

        void FromSpec(int[] drawings, int aStart, int aEnd, int last)
        {
            ready = Mathf.Max(1, drawings[1] - drawings[0]);
            drawnBack = Mathf.Max(1, drawings[2] - drawings[1]);
            heldUp = Mathf.Max(1, drawings[3] - drawings[2]);
            active = Mathf.Max(1, aEnd - aStart);
            recovery = Mathf.Max(1, last + 1 - aEnd);
        }

        /// <summary>Read the timing the chosen move has now (its hurtbox sets start on each drawing).</summary>
        void LoadCurrent()
        {
            status = "";
            var a = TargetAttacks().FirstOrDefault();
            var fd = a ? a.frames : null;
            if (fd != null && fd.IsSet && fd.hurtboxSets != null && fd.hurtboxSets.Count >= 4)
                FromSpec(fd.hurtboxSets.Take(4).Select(s => s.frame).ToArray(), fd.activeStart, fd.activeEnd, fd.totalFrames - 1);
            else ResetDefaults();
            Repaint();
        }

        // ───────────── Which attacks ─────────────

        static Fighter PlayerFighter()
        {
            var all = Object.FindObjectsByType<Fighter>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            return all.FirstOrDefault(f => f.name == "Player") ?? all.FirstOrDefault();
        }

        static AttackData[] LightCombo(Fighter f)
        {
            if (!f) return new AttackData[0];
            var p = new SerializedObject(f).FindProperty("lightCombo");
            var list = new List<AttackData>();
            for (int i = 0; i < p.arraySize; i++)
                if (p.GetArrayElementAtIndex(i).objectReferenceValue is AttackData a) list.Add(a);
            return list.ToArray();
        }

        List<AttackData> TargetAttacks()
        {
            var player = PlayerFighter();
            var lights = LightCombo(player);
            switch (target)
            {
                case Target.FinisherOnly: return lights.Length > 0 ? new List<AttackData> { lights[lights.Length - 1] } : new List<AttackData>();
                case Target.Heavy:
                    var h = player ? new SerializedObject(player).FindProperty("heavyAttack").objectReferenceValue as AttackData : null;
                    return h ? new List<AttackData> { h } : new List<AttackData>();
                default:
                    // Every attack that plays the shared basic clip, on any fighter (and the player's lights).
                    var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(MonkBasicAttackSetup.Basic.clipPath);
                    var set = new List<AttackData>();
                    foreach (var fa in Object.FindObjectsByType<FighterAnimator>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                    {
                        var arr = new SerializedObject(fa).FindProperty("attackClips");
                        for (int i = 0; i < arr.arraySize; i++)
                        {
                            var e = arr.GetArrayElementAtIndex(i);
                            if (e.FindPropertyRelative("clip").objectReferenceValue == clip &&
                                e.FindPropertyRelative("attack").objectReferenceValue is AttackData a && !set.Contains(a)) set.Add(a);
                        }
                    }
                    return set;
            }
        }

        /// <summary>For each chain into a changed hit: frames from the earlier hit landing to the next one landing, and the earlier hit's hitstun.</summary>
        IEnumerable<(string label, int gap, float stun, AttackData prev)> ComboChecks(MonkBasicAttackSetup.Spec spec)
        {
            var lights = LightCombo(PlayerFighter());
            for (int i = 0; i + 1 < lights.Length; i++)
            {
                var prev = lights[i];
                var next = lights[i + 1];
                bool nextChanged = target == Target.AllLightHits || i + 1 == lights.Length - 1;
                if (!nextChanged) continue;
                var pf = prev.frames;
                bool prevChanged = target == Target.AllLightHits;
                int prevActive = prevChanged ? spec.activeStart : (pf != null && pf.IsSet ? pf.activeStart : Mathf.RoundToInt(prev.startup * 60f));
                int prevCancel = prevChanged ? spec.cancelFrame : (pf != null && pf.IsSet && pf.cancelFrame >= 0 ? pf.cancelFrame : prevActive + 4);
                int gap = (prevCancel - prevActive) + spec.activeStart;
                yield return ($"Hit {i + 1} → {i + 2}", gap, prev.hitstun * 60f, prev);
            }
        }

        // ───────────── Apply ─────────────

        void Apply()
        {
            if (EditorApplication.isPlaying) { status = "Stop Play mode first."; return; }
            var attacks = TargetAttacks();
            if (attacks.Count == 0) { status = "Open FightScene first (no Monk attacks found for this choice)."; return; }
            var sprites = MonkBasicAttackSetup.ImportDrawings();
            if (sprites == null) { status = "MONKBASIC drawings not found."; return; }

            var spec = BuildSpec();
            Undo.SetCurrentGroupName("Monk Attack Timing");
            int group = Undo.GetCurrentGroup();
            var clip = MonkBasicAttackSetup.BuildClip(spec, sprites);
            var fd = MonkBasicAttackSetup.BuildFrameData(spec);
            var log = new List<string>();

            if (keepCombo && target != Target.Heavy)
            {
                foreach (var (label, gap, stun, prev) in ComboChecks(spec).ToList())
                {
                    float need = (gap + 3) / 60f;
                    if (prev.hitstun < need)
                    {
                        Undo.RecordObject(prev, "Monk Attack Timing hitstun");
                        log.Add($"{prev.name} hitstun {prev.hitstun:0.00} → {need:0.00} s");
                        prev.hitstun = need;
                        EditorUtility.SetDirty(prev);
                    }
                }
            }

            foreach (var a in attacks)
            {
                Undo.RecordObject(a, "Monk Attack Timing");
                a.frames = MonkBasicAttackSetup.Clone(fd);
                if (target != Target.Heavy)
                {
                    a.startup = spec.activeStart / 60f;      // same timing when Use Frame Data is off
                    a.active = (spec.activeEnd - spec.activeStart) / 60f;
                    a.recovery = (spec.lastFrame + 1 - spec.activeEnd) / 60f;
                }
                EditorUtility.SetDirty(a);
            }

            // Finisher only: map it to its own clip on every fighter that uses it.
            if (target == Target.FinisherOnly)
            {
                foreach (var fa in Object.FindObjectsByType<FighterAnimator>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                {
                    var arr = new SerializedObject(fa).FindProperty("attackClips");
                    bool uses = false;
                    for (int i = 0; i < arr.arraySize; i++)
                        if (attacks.Contains(arr.GetArrayElementAtIndex(i).FindPropertyRelative("attack").objectReferenceValue as AttackData)) uses = true;
                    var animator = fa.GetComponent<Animator>();
                    var controller = animator ? animator.runtimeAnimatorController as AnimatorController : null;
                    if (!uses || !controller) continue;
                    AnimatorSetupUtil.EnsureClipState(controller, clip, new Vector3(300f, 360f, 0f));
                    foreach (var a in attacks) AnimatorSetupUtil.MapAttack(fa, a, clip);
                    log.Add($"{fa.transform.root.name}: {string.Join(", ", attacks.Select(a => a.name))} → {clip.name}");
                }
            }

            AssetDatabase.SaveAssets();
            Undo.CollapseUndoOperations(group);
            var scene = EditorSceneManager.GetActiveScene();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            status = $"Applied to {string.Join(", ", attacks.Select(a => a.name))}: drawings at {string.Join(" / ", spec.drawingFrames)}, " +
                     $"hit on frame {spec.activeStart}, {spec.lastFrame + 1} frames." + (log.Count > 0 ? " " + string.Join("; ", log) + "." : "") +
                     " Scene saved.";
            Debug.Log("[Monk Attack Timing] " + status);
        }

        void ShowClip()
        {
            var path = BuildSpec().clipPath;
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            if (!clip) { status = "Apply first (this clip doesn't exist yet)."; return; }
            var fighter = PlayerFighter();
            var fa = fighter ? fighter.GetComponentInChildren<FighterAnimator>(true) : null;
            Selection.activeObject = fa ? fa.gameObject : (Object)clip;
            EditorApplication.ExecuteMenuItem("Window/Animation/Animation");
            status = $"Animation window: pick {clip.name} in its clip list and scrub to preview the holds.";
        }
    }
}
#endif
