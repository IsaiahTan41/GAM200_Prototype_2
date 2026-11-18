#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using KungFuCrashout.Fighters;

namespace KungFuCrashout.EditorTools
{
    /// <summary>
    /// Frame data (AttackData → Frame data) from the clips the fighters already play, so nothing has to be re-authored:
    ///   • HitboxOn / HitboxOff / CancelOpen / AttackEnd events → Active Start / Active End / Cancel Frame / Total Frames
    ///   • the clip's keyed Hitbox (Offset / Size) → hitbox sets, one per distinct box during the active frames
    ///   • no keyed Hitbox → one set from AttackData's Hitbox Offset / Size
    /// Menus (Window → KungFu Crashout → Animation → Frame Data):
    ///   Convert Clip Events (open scene)            — only attacks with no frame data yet (your edits are kept)
    ///   Convert Clip Events, Overwrite (open scene) — rebuilds every mapped attack's frame data from its clip
    ///   Clear Frame Data (selected attacks)         — back to clip events / seconds for the selected AttackData assets
    /// Place in: Assets/_Project/Editor/FrameDataTools.cs
    /// </summary>
    public static class FrameDataTools
    {
        const string Menu = "Window/KungFu Crashout/Animation/Frame Data/";
        const float Fps = 60f;

        [MenuItem(Menu + "Convert Clip Events (open scene)", priority = 10030)]
        static void ConvertKeep() => ConvertScene(false);

        [MenuItem(Menu + "Convert Clip Events, Overwrite (open scene)", priority = 10031)]
        static void ConvertOverwrite()
        {
            if (!EditorUtility.DisplayDialog("Frame Data", "Rebuild the frame data of EVERY attack that has a clip, from its clip? " +
                                             "Hand-edited frame data on those attacks is replaced.", "Overwrite", "Cancel")) return;
            ConvertScene(true);
        }

        [MenuItem(Menu + "Clear Frame Data (selected attacks)", priority = 10032)]
        static void ClearSelected()
        {
            int n = 0;
            foreach (var o in Selection.objects)
            {
                if (!(o is AttackData a) || a.frames == null || !a.frames.IsSet) continue;
                Undo.RecordObject(a, "Clear Frame Data");
                a.frames = new FrameData();
                EditorUtility.SetDirty(a);
                n++;
            }
            AssetDatabase.SaveAssets();
            Debug.Log($"[Frame Data] Cleared {n} attack(s): they use clip events / their seconds again.");
        }

        /// <summary>Every FighterAnimator in the open scene: convert each attack → clip mapping.</summary>
        public static int ConvertScene(bool overwrite, List<string> log = null)
        {
            int made = 0;
            var done = new HashSet<AttackData>();
            foreach (var fa in Object.FindObjectsByType<FighterAnimator>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                var so = new SerializedObject(fa);
                var list = so.FindProperty("attackClips");
                var hitbox = so.FindProperty("hitbox").objectReferenceValue as BoxCollider2D;
                var fighter = fa.GetComponentInParent<Fighter>(true);
                for (int i = 0; i < list.arraySize; i++)
                {
                    var e = list.GetArrayElementAtIndex(i);
                    var attack = e.FindPropertyRelative("attack").objectReferenceValue as AttackData;
                    var clip = e.FindPropertyRelative("clip").objectReferenceValue as AnimationClip;
                    if (!attack || !clip || done.Contains(attack)) continue;
                    done.Add(attack);
                    if (attack.frames != null && attack.frames.IsSet && !overwrite) continue;
                    if (FromClip(attack, clip, fa.transform, hitbox, fighter))
                    {
                        made++;
                        log?.Add($"{attack.name} ← {clip.name}");
                    }
                }
            }
            AssetDatabase.SaveAssets();
            Debug.Log(made > 0
                ? $"[Frame Data] Built frame data for {made} attack(s) from their clips{(log != null ? ": " + string.Join(", ", log) : "")}."
                : "[Frame Data] Nothing to convert (every mapped attack already has frame data).");
            return made;
        }

        /// <summary>Frame data for one attack from its clip's events and keyed Hitbox. False if the clip has no usable events.</summary>
        public static bool FromClip(AttackData attack, AnimationClip clip, Transform visual, BoxCollider2D hitbox, Fighter fighter)
        {
            int on = -1, off = -1, cancel = -1, end = -1;
            foreach (var ev in AnimationUtility.GetAnimationEvents(clip))
            {
                int f = Mathf.RoundToInt(ev.time * Fps);
                switch (ev.functionName)
                {
                    case "HitboxOn": on = f; break;
                    case "HitboxOff": off = f; break;
                    case "CancelOpen": cancel = f; break;
                    case "AttackEnd": end = f; break;
                }
            }
            if (end < 0) end = Mathf.Max(1, Mathf.RoundToInt(clip.length * Fps));
            if (on < 0) { Debug.LogWarning($"[Frame Data] {clip.name} has no HitboxOn event: {attack.name} skipped."); return false; }
            if (off < 0) off = on + Mathf.Max(1, Mathf.RoundToInt(attack.active * Fps));

            var fd = new FrameData
            {
                totalFrames = end + 1,
                activeStart = on,
                activeEnd = Mathf.Max(on, off),
                cancelFrame = cancel,
            };

            // Hitbox sets from the keyed Offset / Size, one per distinct box while active.
            var times = new SortedSet<int> { on };
            var curves = new AnimationCurve[4];
            string[] props = { "m_Offset.x", "m_Offset.y", "m_Size.x", "m_Size.y" };
            bool keyed = false;
            for (int i = 0; i < 4; i++)
            {
                var b = EditorCurveBinding.FloatCurve(AnimatorSetupUtil.HitboxName, typeof(BoxCollider2D), props[i]);
                curves[i] = AnimationUtility.GetEditorCurve(clip, b);
                if (curves[i] == null) continue;
                keyed = true;
                foreach (var k in curves[i].keys)
                {
                    int f = Mathf.RoundToInt(k.time * Fps);
                    if (f > on && f < fd.activeEnd) times.Add(f);
                }
            }

            Vector2 lastOff = new Vector2(float.NaN, float.NaN), lastSize = lastOff;
            if (keyed)
            {
                foreach (int f in times)
                {
                    float t = f / Fps + 0.0001f;
                    var off2 = new Vector2(Eval(curves[0], t, hitbox ? hitbox.offset.x : 0f), Eval(curves[1], t, hitbox ? hitbox.offset.y : 0f));
                    var size2 = new Vector2(Eval(curves[2], t, hitbox ? hitbox.size.x : 1f), Eval(curves[3], t, hitbox ? hitbox.size.y : 1f));
                    // Hitbox child space → Visual space (normally the same: the child sits at the Visual's origin).
                    if (hitbox && visual && hitbox.transform != visual)
                    {
                        off2 = visual.InverseTransformPoint(hitbox.transform.TransformPoint(off2));
                        Vector3 hs = hitbox.transform.lossyScale, vs = visual.lossyScale;
                        size2 = new Vector2(size2.x * Mathf.Abs(hs.x / vs.x), size2.y * Mathf.Abs(hs.y / vs.y));
                    }
                    if (off2 == lastOff && size2 == lastSize) continue;
                    lastOff = off2; lastSize = size2;
                    fd.hitboxSets.Add(new BoxSet { frame = f, boxes = new List<FrameBox> { new FrameBox { offset = off2, size = size2 } } });
                }
            }
            else if (visual && fighter)
            {
                // AttackData's box (fighter space, facing right) → Visual space.
                float facing = Mathf.Sign(fighter.transform.lossyScale.x);
                Vector3 world = fighter.transform.position + new Vector3(attack.hitboxOffset.x * facing, attack.hitboxOffset.y, 0f);
                Vector2 local = visual.InverseTransformPoint(world);
                Vector3 vs = visual.lossyScale;
                fd.hitboxSets.Add(new BoxSet
                {
                    frame = on,
                    boxes = new List<FrameBox> { new FrameBox { offset = local, size = new Vector2(attack.hitboxSize.x / Mathf.Abs(vs.x), attack.hitboxSize.y / Mathf.Abs(vs.y)) } },
                });
            }

            Undo.RecordObject(attack, "Frame Data From Clip");
            attack.frames = fd;
            EditorUtility.SetDirty(attack);
            return true;
        }

        static float Eval(AnimationCurve c, float t, float fallback) => c != null ? c.Evaluate(t) : fallback;

        /// <summary>Turn on Use Frame Data on every Fighter in the open scene.</summary>
        public static void EnableOnFighters()
        {
            foreach (var f in Object.FindObjectsByType<Fighter>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                var so = new SerializedObject(f);
                var p = so.FindProperty("useFrameData");
                if (p == null || p.boolValue) continue;
                p.boolValue = true;
                so.ApplyModifiedProperties();
            }
        }
    }
}
#endif
