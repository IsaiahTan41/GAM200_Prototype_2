using UnityEditor;
using UnityEngine;
using KungFuCrashout.Feedback;
using KungFuCrashout.Fighters;

namespace KungFuCrashout.EditorTools
{
    /// <summary>
    /// Monk swing VFX, laid exactly over the white swing painted in MONKBASIC_4 (VFX_Anim_Monk_BasicSwing, Anchor To Art).
    /// The MONKBASIC drawings are the HEAVY now (the light hits play ATTACK 1–10, which paints its own smear), so:
    /// Apply: Monk_HeavyAttack → the swing VFX (no dust, no light); the three Monk light hits (Monk_LightAttack_1,
    ///   Monk_LightAttack2, Monk_LightFinisher_3) → No Effects. Per Attack entries in VfxSet_Monk and VfxSet_Monk_Animated.
    /// Restore: removes those four entries, so every one of them goes back to its VFX Set slot (light combo / heavy:
    ///   the old strike + dust).
    /// Place in: Assets/_Project/Editor/MonkBasicSwingVfxSetup.cs
    /// </summary>
    public static class MonkBasicSwingVfxSetup
    {
        const string Cue = "Assets/_Project/Data/VFX/Animated/VFX_Anim_Monk_BasicSwing.asset";
        static readonly string[] Sets = { "Assets/_Project/Data/VFX/VfxSet_Monk.asset", "Assets/_Project/Data/VFX/VfxSet_Monk_Animated.asset" };
        const string Heavy = "Assets/_Project/Data/Attacks/Monk_HeavyAttack.asset";
        static readonly string[] Lights =
        {
            "Assets/_Project/Data/Attacks/Monk_LightAttack_1.asset",
            "Assets/_Project/Data/Attacks/Monk_LightAttack2.asset",
            "Assets/_Project/Data/Attacks/Monk_LightFinisher_3.asset",
        };

        [MenuItem("Window/KungFu Crashout/VFX/Monk Swing VFX → Apply (heavy: swing overlay · light hits: none)", priority = 10500)]
        public static void Apply() => Run(true);

        [MenuItem("Window/KungFu Crashout/VFX/Monk Swing VFX → Restore Set Defaults", priority = 10501)]
        public static void Restore() => Run(false);

        static void Run(bool apply)
        {
            var cue = AssetDatabase.LoadAssetAtPath<VfxCue>(Cue);
            var heavy = AssetDatabase.LoadAssetAtPath<AttackData>(Heavy);
            if (!cue || !heavy) { Debug.LogError("[Swing VFX] Missing " + (!cue ? Cue : Heavy)); return; }
            var lights = System.Array.ConvertAll(Lights, p => AssetDatabase.LoadAssetAtPath<AttackData>(p));

            foreach (var path in Sets)
            {
                var set = AssetDatabase.LoadAssetAtPath<VfxSet>(path);
                if (!set) continue;
                Undo.RecordObject(set, "Monk swing VFX");
                set.perAttack.RemoveAll(o => o.attack == heavy || System.Array.IndexOf(lights, o.attack) >= 0 || o.vfx.strike == cue);
                if (apply)
                {
                    set.perAttack.Insert(0, new VfxSet.AttackOverride
                        { attack = heavy, vfx = new VfxSet.AttackVfx { strike = cue, dust = null, scale = 1f } });
                    foreach (var a in lights)
                        if (a) set.perAttack.Insert(0, new VfxSet.AttackOverride { attack = a, noEffects = true });
                }
                EditorUtility.SetDirty(set);
            }
            AssetDatabase.SaveAssets();
            Debug.Log(apply ? "[Swing VFX] Heavy → VFX_Anim_Monk_BasicSwing over the painted MONKBASIC swing; light hits → no VFX (ATTACK 1–10 paints its own)."
                            : "[Swing VFX] Restored: the Monk heavy and light hits use their VFX Set slots (strike + dust) again.");
        }
    }
}
