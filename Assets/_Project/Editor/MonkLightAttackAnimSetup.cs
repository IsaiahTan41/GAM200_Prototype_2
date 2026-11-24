#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using KungFuCrashout.Feedback;
using KungFuCrashout.Fighters;

namespace KungFuCrashout.EditorTools
{
    /// <summary>
    /// Window → KungFu Crashout → Animation → Set Up Monk Light Hits (ATTACK 1–10) + MONKBASIC Heavy
    ///
    /// LIGHT HITS (all three: Monk_BasicAttack for hits 1–2, Monk_BasicAttack_Finisher for hit 3) play the 10-drawing
    /// ATTACK animation (Art/Characters/Animation/AnimationSprites/ATTACK_10Frames), timed like the basic attack it replaces:
    ///   1 ready @ the old drawing-1 frame · 2 drawn back · 3 leap · 4 slam ON THE SAME FRAME as before (the hit) ·
    ///   5–10 follow-through and back to stance, spread over the rest of the move (default 0 / 3 / 6 / 10, then
    ///   13 15 16 18 19 21, ends 22). Only the drawings change: the hitbox, hurtboxes, travel, cancel window and events
    ///   (frame data) stay exactly as they are. No VFX on them: the slam's smear is painted into the drawings.
    ///   Import: PPU 350 (the drawings are the same pixel size as MONKBASIC), one pivot for all ten — between the feet on
    ///   the ground line (975, 1174 of the 1700 × 1200 canvas), since the drawings are registered on one canvas.
    /// HEAVY: the MONKBASIC 1–4 system is repurposed for it (Set Up Monk Heavy Attack: drawing 2, staff drawn back, held
    ///   frames 4–19, leap 20, slam 24, 48 frames) and keeps the swing VFX laid over the painted swing. The Nam-pi archetype
    ///   (both Monks in a match) gets Monk_HeavyAttack as its heavy, so matches use it (it was Heavy_PalmStrike, no animation).
    ///   The Monk Attack Timing window edits this heavy.
    /// Restore MONKBASIC Light Hits: the light hits play MONKBASIC 1–4 again (same timing), light hits get the swing VFX
    ///   again, the heavy goes back to its VFX Set slot and Nam-pi's heavy to Heavy_PalmStrike. Save the scene afterwards.
    /// Place in: Assets/_Project/Editor/MonkLightAttackAnimSetup.cs
    /// </summary>
    public static class MonkLightAttackAnimSetup
    {
        const string Title = "Monk Light Hits (ATTACK 1–10)";
        const string FramesFolder = MonkAttackClipSetup.Folder + "/AnimationSprites/ATTACK_10Frames";
        const string BasicClip = MonkAttackClipSetup.Folder + "/Monk_BasicAttack.anim";
        const string FinisherClip = MonkAttackClipSetup.Folder + "/Monk_BasicAttack_Finisher.anim";
        const string NamPi = "Assets/_Project/Data/GDD/Archetype_1_NamPi.asset";
        const string MonkHeavy = "Assets/_Project/Data/Attacks/Monk_HeavyAttack.asset";
        const string PalmStrike = "Assets/_Project/Data/Attacks/Heavy_PalmStrike.asset";
        const string SwingCue = "Assets/_Project/Data/VFX/Animated/VFX_Anim_Monk_BasicSwing.asset";
        static readonly string[] Sets = { "Assets/_Project/Data/VFX/VfxSet_Monk.asset", "Assets/_Project/Data/VFX/VfxSet_Monk_Animated.asset" };
        static readonly string[] Lights =
        {
            "Assets/_Project/Data/Attacks/Monk_LightAttack_1.asset",
            "Assets/_Project/Data/Attacks/Monk_LightAttack2.asset",
            "Assets/_Project/Data/Attacks/Monk_LightFinisher_3.asset",
        };

        const float Fps = 60f;
        const float PixelsPerUnit = 350f;
        const float CanvasW = 1700f, CanvasH = 1200f;
        const float PivotX = 975f, GroundY = 1174f; // pixels from the top-left
        const int Drawings = 10;

        [MenuItem("Window/KungFu Crashout/Animation/Set Up Monk Light Hits (ATTACK 1–10) + MONKBASIC Heavy", priority = 10013)]
        static void SetUp()
        {
            if (EditorApplication.isPlaying) { EditorUtility.DisplayDialog(Title, "Stop Play mode first.", "OK"); return; }
            if (!Object.FindAnyObjectByType<Fighter>(FindObjectsInactive.Include))
            {
                EditorUtility.DisplayDialog(Title, "Open FightScene first (the heavy is mapped on its fighters).", "OK");
                return;
            }

            var sprites = ImportFrames();
            if (sprites == null) return;

            var log = new List<string>();
            foreach (var path in new[] { BasicClip, FinisherClip })
            {
                var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
                if (!clip) { log.Add($"{path}: not found, skipped"); continue; }
                var (t, last) = ReadTiming(clip);
                var frames = LightFrames(t, last);
                Undo.RecordObject(clip, Title);
                var keys = new List<ObjectReferenceKeyframe>();
                for (int i = 0; i < Drawings; i++) keys.Add(Key(frames[i], sprites[i]));
                keys.Add(Key(last, sprites[Drawings - 1]));
                AnimationUtility.SetObjectReferenceCurve(clip, Binding, keys.ToArray());
                EditorUtility.SetDirty(clip);
                log.Add($"{clip.name}: ATTACK 1–10 on frames {string.Join(" ", frames)} (ends {last}), frame data unchanged");
            }

            // The heavy: MONKBASIC 1–4 with the drawn-back hold (maps it, writes its frame data, Heavy Uses Animation on).
            MonkBasicAttackSetup.SetUpHeavy();
            SetArchetypeHeavy(MonkHeavy, log);
            MonkBasicSwingVfxSetup.Apply(); // heavy: swing overlay · light hits: no VFX

            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            Debug.Log($"[{Title}] Done. Save the scene.\n  " + string.Join("\n  ", log));
        }

        [MenuItem("Window/KungFu Crashout/Animation/Restore MONKBASIC Light Hits (and Nam-pi Palm Strike heavy)", priority = 10014)]
        static void Restore()
        {
            if (EditorApplication.isPlaying) { EditorUtility.DisplayDialog(Title, "Stop Play mode first.", "OK"); return; }
            var mb = MonkBasicAttackSetup.ImportDrawings();
            if (mb == null) return;
            var log = new List<string>();
            foreach (var path in new[] { BasicClip, FinisherClip })
            {
                var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
                if (!clip) continue;
                var (t, last) = ReadTiming(clip);
                Undo.RecordObject(clip, Title);
                var keys = new List<ObjectReferenceKeyframe>();
                for (int i = 0; i < 4; i++) keys.Add(Key(t[i], mb[i]));
                keys.Add(Key(last, mb[3]));
                AnimationUtility.SetObjectReferenceCurve(clip, Binding, keys.ToArray());
                EditorUtility.SetDirty(clip);
                log.Add($"{clip.name}: MONKBASIC 1–4 on frames {string.Join(" ", t)} (ends {last})");
            }
            SetArchetypeHeavy(PalmStrike, log);

            // VFX as before: light hits get the swing overlay, the heavy its VFX Set slot.
            var cue = AssetDatabase.LoadAssetAtPath<VfxCue>(SwingCue);
            var heavy = AssetDatabase.LoadAssetAtPath<AttackData>(MonkHeavy);
            var lights = System.Array.ConvertAll(Lights, p => AssetDatabase.LoadAssetAtPath<AttackData>(p));
            foreach (var sp in Sets)
            {
                var set = AssetDatabase.LoadAssetAtPath<VfxSet>(sp);
                if (!set) continue;
                Undo.RecordObject(set, Title);
                set.perAttack.RemoveAll(o => o.attack == heavy || System.Array.IndexOf(lights, o.attack) >= 0);
                for (int i = lights.Length - 1; i >= 0; i--)
                    if (lights[i]) set.perAttack.Insert(0, new VfxSet.AttackOverride
                        { attack = lights[i], vfx = new VfxSet.AttackVfx { strike = cue, dust = null, scale = 1f } });
                EditorUtility.SetDirty(set);
            }
            AssetDatabase.SaveAssets();
            Debug.Log($"[{Title}] Restored the MONKBASIC light hits (with the swing VFX) and Nam-pi's Palm Strike heavy. " +
                      "The heavy clip keeps its drawn-back hold (Monk Attack Timing window → Heavy to change it). Save the scene.\n  " +
                      string.Join("\n  ", log));
        }

        /// <summary>True when the light-hit clip shows the ATTACK drawings (used by the Monk Attack Timing window).</summary>
        public static bool LightHitsUseNewAnimation()
        {
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(BasicClip);
            if (!clip) return false;
            var keys = AnimationUtility.GetObjectReferenceCurve(clip, Binding);
            return keys != null && keys.Length > 0 && keys[0].value && keys[0].value.name.StartsWith("ATTACK-");
        }

        // ───────────── Timing ─────────────

        /// <summary>The frames drawings 1–4 start on (MONKBASIC_n or ATTACK-n) and the clip's last frame.</summary>
        static (int[] t, int last) ReadTiming(AnimationClip clip)
        {
            int[] t = { 0, 3, 6, 10 };
            int last = 22;
            var keys = AnimationUtility.GetObjectReferenceCurve(clip, Binding);
            if (keys == null || keys.Length == 0) return (t, last);
            var seen = new bool[4];
            foreach (var k in keys)
            {
                int f = Mathf.RoundToInt(k.time * Fps);
                string n = k.value ? k.value.name : "";
                int d = n.StartsWith("MONKBASIC_") ? Parse(n.Substring(10)) : n.StartsWith("ATTACK-") ? Parse(n.Substring(7)) : -1;
                if (d >= 1 && d <= 4 && !seen[d - 1]) { t[d - 1] = f; seen[d - 1] = true; }
            }
            last = Mathf.RoundToInt(keys[keys.Length - 1].time * Fps);
            return (t, last);
        }

        static int Parse(string s) => int.TryParse(s, out int v) ? v : -1;

        /// <summary>Drawings 1–4 on the old frames (4 = the hit), 5–10 spread from 3 frames after the hit to the end.</summary>
        static int[] LightFrames(int[] t, int last)
        {
            var f = new int[Drawings];
            for (int i = 0; i < 4; i++) f[i] = t[i];
            int from = t[3] + 3, to = Mathf.Max(from + 5, last - 1);
            for (int i = 4; i < Drawings; i++)
                f[i] = Mathf.Max(f[i - 1] + 1, Mathf.RoundToInt(Mathf.Lerp(from, to, (i - 4) / 5f)));
            return f;
        }

        static ObjectReferenceKeyframe Key(int frame, Sprite s) => new ObjectReferenceKeyframe { time = frame / Fps, value = s };
        static EditorCurveBinding Binding => EditorCurveBinding.PPtrCurve("", typeof(SpriteRenderer), "m_Sprite");

        // ───────────── Archetype ─────────────

        static void SetArchetypeHeavy(string attackPath, List<string> log)
        {
            var arch = AssetDatabase.LoadAssetAtPath<FighterArchetype>(NamPi);
            var atk = AssetDatabase.LoadAssetAtPath<AttackData>(attackPath);
            if (!arch || !atk) { log.Add($"Nam-pi archetype or {attackPath} not found: heavy not changed"); return; }
            var so = new SerializedObject(arch);
            var p = so.FindProperty("heavyAttack");
            if (p == null) { log.Add("Nam-pi archetype has no heavyAttack field"); return; }
            Undo.RecordObject(arch, Title);
            p.objectReferenceValue = atk;
            so.ApplyModifiedProperties();
            EditorUtility.SetDirty(arch);
            log.Add($"Nam-pi archetype heavy → {atk.name}");
        }

        // ───────────── Import ─────────────

        static Sprite[] ImportFrames()
        {
            var sprites = new Sprite[Drawings];
            for (int i = 0; i < Drawings; i++)
            {
                string name = $"ATTACK-{i + 1}";
                string path = AssetDatabase.FindAssets($"{name} t:Texture2D", new[] { FramesFolder })
                    .Select(AssetDatabase.GUIDToAssetPath)
                    .FirstOrDefault(p => System.IO.Path.GetFileNameWithoutExtension(p) == name);
                var ti = path != null ? AssetImporter.GetAtPath(path) as TextureImporter : null;
                if (!ti) { EditorUtility.DisplayDialog(Title, $"Couldn't find {name}.png in {FramesFolder}.", "OK"); return null; }
                ti.textureType = TextureImporterType.Sprite;
                ti.spriteImportMode = SpriteImportMode.Single;
                ti.spritePixelsPerUnit = PixelsPerUnit;
                ti.alphaIsTransparency = true;
                ti.mipmapEnabled = false;
                ti.maxTextureSize = 2048;
                ti.filterMode = FilterMode.Bilinear;
                var s = new TextureImporterSettings();
                ti.ReadTextureSettings(s);
                s.spriteAlignment = (int)SpriteAlignment.Custom;
                s.spritePivot = new Vector2(PivotX / CanvasW, 1f - GroundY / CanvasH);
                s.spriteMeshType = SpriteMeshType.FullRect;
                ti.SetTextureSettings(s);
                ti.SaveAndReimport();
                sprites[i] = AssetDatabase.LoadAssetAtPath<Sprite>(path);
                if (!sprites[i]) { EditorUtility.DisplayDialog(Title, $"{name} didn't import as a sprite.", "OK"); return null; }
            }
            return sprites;
        }
    }
}
#endif
