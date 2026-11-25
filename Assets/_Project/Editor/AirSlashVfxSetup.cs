#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using KungFuCrashout.Feedback;
using KungFuCrashout.Fighters;

namespace KungFuCrashout.EditorTools
{
    /// <summary>
    /// Temporary air-attack slash VFX (2026-11-25): each Monk air hit's slash is turned so its INNER curve faces the Monk
    /// and sits just in front of his body (Anchor To Art), and the air hitboxes are raised to match.
    ///   Monk_AirAttack  → VFX_Strike1_Air  (rot 179.7, art pos 0.82, 1.47)        hitbox y +0.30
    ///   Monk_AirAttack2 → VFX_Strike2_Air  (rot −38.1, art pos 0.46, 1.43)        hitbox y +0.25
    ///   Monk_AirAttack3 → VFX_Strike2_Air3 (rot −38.1, art pos 0.57, 1.48, ×1.3)  hitbox y +0.30
    /// Hitbox = frame data box, AttackData Hitbox Offset (Use Frame Data off) and the clip's keyed Hitbox, all together.
    /// Restore: the old slashes (Strike1 / Strike2 / Strike2 ×1.3 at the hitbox) and the old hitbox heights.
    /// Place in: Assets/_Project/Editor/AirSlashVfxSetup.cs
    /// </summary>
    public static class AirSlashVfxSetup
    {
        const string Root = "Assets/_Project/";
        static readonly string[] Sets = { Root + "Data/VFX/VfxSet_Monk.asset", Root + "Data/VFX/VfxSet_Monk_Animated.asset" };

        struct Hit { public string attack, clip, newCue, oldCue; public float oldScale, frameY, dataY, dy; }
        static readonly Hit[] Hits =
        {
            new Hit { attack = "Monk_AirAttack",  clip = "Monk_AirAttack",  newCue = "VFX_Strike1_Air",  oldCue = "VFX_Strike1", oldScale = 1f,   frameY = 0.55f, dataY = -0.45f, dy = 0.30f },
            new Hit { attack = "Monk_AirAttack2", clip = "Monk_AirAttack2", newCue = "VFX_Strike2_Air",  oldCue = "VFX_Strike2", oldScale = 1f,   frameY = 0.95f, dataY = -0.05f, dy = 0.25f },
            new Hit { attack = "Monk_AirAttack3", clip = "Monk_AirAttack3", newCue = "VFX_Strike2_Air3", oldCue = "VFX_Strike2", oldScale = 1.3f, frameY = 0.45f, dataY = -0.55f, dy = 0.30f },
        };

        [MenuItem("Window/KungFu Crashout/VFX/Air Slash VFX → Apply (curve in front, hitboxes raised)", priority = 10510)]
        static void Apply() => Run(true);

        [MenuItem("Window/KungFu Crashout/VFX/Air Slash VFX → Restore Previous", priority = 10511)]
        static void Restore() => Run(false);

        static void Run(bool apply)
        {
            foreach (var h in Hits)
            {
                var attack = AssetDatabase.LoadAssetAtPath<AttackData>(Root + "Data/Attacks/" + h.attack + ".asset");
                var cue = AssetDatabase.LoadAssetAtPath<VfxCue>(Root + "Data/VFX/" + (apply ? h.newCue : h.oldCue) + ".asset");
                if (!attack || !cue) { Debug.LogError($"[Air Slash] Missing {h.attack} or its cue."); continue; }
                float y = h.frameY + (apply ? h.dy : 0f);

                // VFX
                foreach (var sp in Sets)
                {
                    var set = AssetDatabase.LoadAssetAtPath<VfxSet>(sp);
                    if (!set) continue;
                    Undo.RecordObject(set, "Air slash VFX");
                    set.perAttack.RemoveAll(o => o.attack == attack);
                    set.perAttack.Add(new VfxSet.AttackOverride
                        { attack = attack, vfx = new VfxSet.AttackVfx { strike = cue, dust = null, scale = apply ? 1f : h.oldScale } });
                    EditorUtility.SetDirty(set);
                }

                // Hitbox: frame data, seconds-mode offset, clip keys
                Undo.RecordObject(attack, "Air hitbox");
                if (attack.frames != null)
                    foreach (var bs in attack.frames.hitboxSets)
                        for (int i = 0; i < bs.boxes.Count; i++) { var b = bs.boxes[i]; b.offset.y = y; bs.boxes[i] = b; }
                attack.hitboxOffset = new Vector2(attack.hitboxOffset.x, h.dataY + (apply ? h.dy : 0f));
                EditorUtility.SetDirty(attack);

                var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(MonkAttackClipSetup.Folder + "/" + h.clip + ".anim");
                if (clip)
                {
                    Undo.RecordObject(clip, "Air hitbox");
                    foreach (var b in AnimationUtility.GetCurveBindings(clip))
                    {
                        if (b.type != typeof(BoxCollider2D) || b.propertyName != "m_Offset.y") continue;
                        var c = AnimationUtility.GetEditorCurve(clip, b);
                        var keys = c.keys;
                        for (int k = 0; k < keys.Length; k++) keys[k].value = y;
                        c.keys = keys;
                        AnimationUtility.SetEditorCurve(clip, b, c);
                    }
                    EditorUtility.SetDirty(clip);
                }
            }
            AssetDatabase.SaveAssets();
            Debug.Log(apply ? "[Air Slash] Air hits: slash curve in front of the Monk, hitboxes raised (+0.30 / +0.25 / +0.30)."
                            : "[Air Slash] Restored the old air slashes and hitbox heights.");
        }
    }
}
#endif
