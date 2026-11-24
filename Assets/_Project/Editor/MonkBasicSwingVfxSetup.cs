using UnityEditor;
using UnityEngine;
using KungFuCrashout.Feedback;
using KungFuCrashout.Fighters;

namespace KungFuCrashout.EditorTools
{
    /// <summary>
    /// Monk basic attack swing VFX, laid exactly over the white swing painted in MONKBASIC_4.
    /// Apply: the three Monk light attacks (Monk_LightAttack_1, Monk_LightAttack2, Monk_LightFinisher_3) get a Per Attack
    /// entry in VfxSet_Monk and VfxSet_Monk_Animated → strike = VFX_Anim_Monk_BasicSwing (Anchor To Art), no dust, no light.
    /// Restore: removes those entries, so the light combo goes back to its old strike + dust slots.
    /// Place in: Assets/_Project/Editor/MonkBasicSwingVfxSetup.cs
    /// </summary>
    public static class MonkBasicSwingVfxSetup
    {
        const string Cue = "Assets/_Project/Data/VFX/Animated/VFX_Anim_Monk_BasicSwing.asset";
        static readonly string[] Sets = { "Assets/_Project/Data/VFX/VfxSet_Monk.asset", "Assets/_Project/Data/VFX/VfxSet_Monk_Animated.asset" };
        static readonly string[] Attacks =
        {
            "Assets/_Project/Data/Attacks/Monk_LightAttack_1.asset",
            "Assets/_Project/Data/Attacks/Monk_LightAttack2.asset",
            "Assets/_Project/Data/Attacks/Monk_LightFinisher_3.asset",
        };

        [MenuItem("Window/KungFu Crashout/VFX/Monk Basic Swing VFX → Apply (overlay painted swing)", priority = 10500)]
        static void Apply() => Run(true);

        [MenuItem("Window/KungFu Crashout/VFX/Monk Basic Swing VFX → Restore Previous", priority = 10501)]
        static void Restore() => Run(false);

        static void Run(bool apply)
        {
            var cue = AssetDatabase.LoadAssetAtPath<VfxCue>(Cue);
            if (!cue) { Debug.LogError("[Swing VFX] Missing " + Cue); return; }
            foreach (var path in Sets)
            {
                var set = AssetDatabase.LoadAssetAtPath<VfxSet>(path);
                if (!set) continue;
                Undo.RecordObject(set, "Monk swing VFX");
                set.perAttack.RemoveAll(o => o.vfx.strike == cue);
                if (apply)
                    for (int i = Attacks.Length - 1; i >= 0; i--)
                    {
                        var atk = AssetDatabase.LoadAssetAtPath<AttackData>(Attacks[i]);
                        if (!atk) continue;
                        set.perAttack.RemoveAll(o => o.attack == atk);
                        set.perAttack.Insert(0, new VfxSet.AttackOverride { attack = atk, vfx = new VfxSet.AttackVfx { strike = cue, dust = null, scale = 1f } });
                    }
                EditorUtility.SetDirty(set);
            }
            AssetDatabase.SaveAssets();
            Debug.Log(apply ? "[Swing VFX] Monk light attacks now play VFX_Anim_Monk_BasicSwing over the painted swing (no dust, no light)."
                            : "[Swing VFX] Restored: Monk light attacks use the VFX Set's light combo strike + dust again.");
        }
    }
}
