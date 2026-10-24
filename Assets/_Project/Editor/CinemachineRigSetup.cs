#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Unity.Cinemachine;
using KungFuCrashout.Core;
using KungFuCrashout.Feedback;
using KungFuCrashout.Fighters;

namespace KungFuCrashout.EditorTools
{
    /// <summary>
    /// Menu: KungFu Crashout → Set Up Cinemachine Camera
    ///
    /// Builds the camera hybrid in the open scene in one click (safe to run again — it reuses what exists):
    ///   Main Camera      + Cinemachine Brain (Ease In Out 0.15 s blends)
    ///                    + HitFeedback (if missing; fighters assigned, Use External Shake on)
    ///                    + CinemachineFightRig (all references wired)
    ///   CM Fight         CinemachineCamera, no position/rotation control (the rig moves it), + Impulse Listener
    ///   CM PunchIn       CinemachineCamera, disabled, higher priority, + Impulse Listener (gain 0.7)
    ///   Camera Impulses  one child per shake kind, each a Cinemachine Impulse Source with its own shape:
    ///                      Impulse - Default (Bump)    — heavy hits and anything without its own source
    ///                      Impulse - Item Hit (Recoil) — thrown item lands a clean hit
    ///                      Impulse - Parry (Rumble)
    ///                      Impulse - Downed (Bump)     — target lands knocked down
    ///                      Impulse - Wall Splat (Explosion)
    ///
    /// Everything is undoable (Ctrl+Z). Save the scene afterwards.
    /// Place in: Assets/_Project/Editor/CinemachineRigSetup.cs
    /// </summary>
    public static class CinemachineRigSetup
    {
        [MenuItem("KungFu Crashout/Set Up Cinemachine Camera", priority = 60)]
        public static void SetUp()
        {
            var mainCam = Camera.main;
            if (!mainCam) mainCam = Object.FindFirstObjectByType<Camera>();
            if (!mainCam)
            {
                EditorUtility.DisplayDialog("Set Up Cinemachine Camera", "No camera found in the open scene.", "OK");
                return;
            }

            var framing = mainCam.GetComponent<LandscapeCameraFit>();
            if (!framing)
            {
                EditorUtility.DisplayDialog("Set Up Cinemachine Camera",
                    "The Main Camera needs LandscapeCameraFit first (it decides the framing).", "OK");
                return;
            }

            Undo.SetCurrentGroupName("Set Up Cinemachine Camera");
            int undoGroup = Undo.GetCurrentGroup();

            var fighters = Object.FindObjectsByType<Fighter>(FindObjectsSortMode.None);
            float startSize = mainCam.orthographicSize > 0f ? mainCam.orthographicSize : 3f;
            Vector3 startPos = new Vector3(mainCam.transform.position.x, mainCam.transform.position.y, -10f);

            // ── Brain ──
            var brain = GetOrAdd<CinemachineBrain>(mainCam.gameObject);
            Undo.RecordObject(brain, "Brain settings");
            brain.DefaultBlend = new CinemachineBlendDefinition(CinemachineBlendDefinition.Styles.EaseInOut, 0.15f);
            brain.IgnoreTimeScale = false;

            // ── Fight camera ──
            var fight = GetOrCreateCamera("CM Fight", startPos, startSize, active: true, priority: 0, listenerGain: 1f);

            // ── Punch-in camera (kept disabled; the rig switches it on for a moment) ──
            var punch = GetOrCreateCamera("CM PunchIn", startPos, startSize * 0.7f, active: false, priority: 10, listenerGain: 0.7f);

            // ── Impulse sources ──
            var root = GetOrCreateObject("Camera Impulses", null);
            var defaultImpulse = MakeImpulse(root, "Impulse - Default", CinemachineImpulseDefinition.ImpulseShapes.Bump, 0.2f);
            var itemImpulse = MakeImpulse(root, "Impulse - Item Hit", CinemachineImpulseDefinition.ImpulseShapes.Recoil, 0.3f);
            var parryImpulse = MakeImpulse(root, "Impulse - Parry", CinemachineImpulseDefinition.ImpulseShapes.Rumble, 0.25f);
            var downedImpulse = MakeImpulse(root, "Impulse - Downed", CinemachineImpulseDefinition.ImpulseShapes.Bump, 0.25f);
            var splatImpulse = MakeImpulse(root, "Impulse - Wall Splat", CinemachineImpulseDefinition.ImpulseShapes.Explosion, 0.35f);

            // ── HitFeedback: the thing that ASKS for shakes ──
            var feedback = Object.FindFirstObjectByType<HitFeedback>();
            if (!feedback) feedback = GetOrAdd<HitFeedback>(mainCam.gameObject);
            var fb = new SerializedObject(feedback);
            SetFighters(fb.FindProperty("fighters"), fighters);
            SetBool(fb, "useExternalShake", true);
            fb.ApplyModifiedProperties();

            // ── The rig: wire everything ──
            var rig = GetOrAdd<CinemachineFightRig>(mainCam.gameObject);
            var so = new SerializedObject(rig);
            SetRef(so, "framing", framing);
            SetRef(so, "brain", brain);
            SetRef(so, "fightCamera", fight);
            SetRef(so, "hitFeedback", feedback);
            SetRef(so, "defaultImpulse", defaultImpulse);
            SetRef(so, "heavyHitImpulse", null); // uses Default
            SetRef(so, "itemHitImpulse", itemImpulse);
            SetRef(so, "parryImpulse", parryImpulse);
            SetRef(so, "downedImpulse", downedImpulse);
            SetRef(so, "wallSplatImpulse", splatImpulse);
            SetRef(so, "punchInCamera", punch);
            SetFighters(so.FindProperty("fighters"), fighters);
            so.ApplyModifiedProperties();

            Undo.CollapseUndoOperations(undoGroup);
            EditorSceneManager.MarkSceneDirty(mainCam.gameObject.scene);
            Selection.activeObject = rig;

            Debug.Log("[Cinemachine setup] Done: Brain + CinemachineFightRig + HitFeedback on " + mainCam.name +
                      ", CM Fight, CM PunchIn (disabled), Camera Impulses (5 sources). " +
                      $"Fighters found: {fighters.Length}. Save the scene (Ctrl+S).");
        }

        // ───────────── Helpers ─────────────

        static T GetOrAdd<T>(GameObject go) where T : Component
        {
            var c = go.GetComponent<T>();
            return c ? c : Undo.AddComponent<T>(go);
        }

        static GameObject GetOrCreateObject(string name, Transform parent)
        {
            var existing = parent ? parent.Find(name) : null;
            GameObject go = existing ? existing.gameObject : (parent ? null : GameObject.Find(name));
            if (go) return go;

            go = new GameObject(name);
            Undo.RegisterCreatedObjectUndo(go, "Create " + name);
            if (parent) go.transform.SetParent(parent, false);
            return go;
        }

        /// <summary>A CinemachineCamera with no position/rotation control (the rig moves it) and an Impulse Listener.</summary>
        static CinemachineCamera GetOrCreateCamera(string name, Vector3 pos, float orthoSize, bool active, int priority, float listenerGain)
        {
            var go = FindIncludingInactive(name);
            if (!go)
            {
                go = new GameObject(name);
                Undo.RegisterCreatedObjectUndo(go, "Create " + name);
                go.transform.position = pos;
            }

            var cam = GetOrAdd<CinemachineCamera>(go);
            Undo.RecordObject(cam, "Camera settings");
            var lens = cam.Lens;
            lens.OrthographicSize = orthoSize;
            cam.Lens = lens;
            cam.Priority = priority;

            var listener = GetOrAdd<CinemachineImpulseListener>(go);
            Undo.RecordObject(listener, "Listener settings");
            listener.ApplyAfter = CinemachineCore.Stage.Noise;
            listener.ChannelMask = 1;          // hears channel 1, where all our impulse sources broadcast
            listener.Gain = listenerGain;
            listener.Use2DDistance = true;
            listener.UseCameraSpace = true;
            listener.ReactionSettings = new CinemachineImpulseListener.ImpulseReaction
            {
                AmplitudeGain = 1f,
                FrequencyGain = 1f,
                Duration = 1f
            };

            Undo.RecordObject(go, "Active");
            go.SetActive(active);
            return cam;
        }

        static CinemachineImpulseSource MakeImpulse(GameObject root, string name,
                                                    CinemachineImpulseDefinition.ImpulseShapes shape, float duration)
        {
            var go = GetOrCreateObject(name, root.transform);
            var src = GetOrAdd<CinemachineImpulseSource>(go);
            Undo.RecordObject(src, "Impulse settings");
            src.ImpulseDefinition = new CinemachineImpulseDefinition
            {
                ImpulseChannel = 1,
                ImpulseShape = shape,
                CustomImpulseShape = new AnimationCurve(),
                ImpulseDuration = duration,
                ImpulseType = CinemachineImpulseDefinition.ImpulseTypes.Uniform, // felt fully everywhere: right for one screen
                DissipationDistance = 100f,
                DissipationRate = 0.25f,
                PropagationSpeed = 343f
            };
            src.DefaultVelocity = Vector3.down; // unused: the rig always passes direction × strength
            src.ImpulseDefinition.OnValidate();
            return src;
        }

        static GameObject FindIncludingInactive(string name)
        {
            foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (t.name == name && !t.parent) return t.gameObject;
            return null;
        }

        static void SetRef(SerializedObject so, string field, Object value)
        {
            var p = so.FindProperty(field);
            if (p != null) p.objectReferenceValue = value;
            else Debug.LogWarning($"[Cinemachine setup] Field '{field}' not found on {so.targetObject.GetType().Name}");
        }

        static void SetBool(SerializedObject so, string field, bool value)
        {
            var p = so.FindProperty(field);
            if (p != null) p.boolValue = value;
        }

        static void SetFighters(SerializedProperty list, Fighter[] fighters)
        {
            if (list == null) return;
            list.arraySize = fighters.Length;
            for (int i = 0; i < fighters.Length; i++)
                list.GetArrayElementAtIndex(i).objectReferenceValue = fighters[i];
        }
    }
}
#endif
