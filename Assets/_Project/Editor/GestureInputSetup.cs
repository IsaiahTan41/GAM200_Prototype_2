#if UNITY_EDITOR
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using KungFuCrashout.Controls;
using KungFuCrashout.Fighters;

namespace KungFuCrashout.EditorTools
{
    /// <summary>
    /// Window → KungFu Crashout → Controls:
    ///   • Use Gesture Arbiter Input — adds GestureArbiterInput next to the Player's touch handler (same Fighter) and turns
    ///     the old TapAnywhereInputHandler / TouchInputHandler off (kept, so you can switch back). The old handler also
    ///     gets the quick fixes (Tap During Motion off, Swipe While Holding Special off, Two Finger Window 0.16).
    ///   • Use Old Tap Anywhere Input — the reverse, for A/B testing.
    ///   • Replay Touch Log… — runs a touch log recorded on the phone (Record Touches) through the gesture engine with the
    ///     scene's settings, and lists what it would have sent to the fighter, with times. Also saves the list next to the log.
    /// Place in: Assets/_Project/Editor/GestureInputSetup.cs
    /// </summary>
    public static class GestureInputSetup
    {
        [MenuItem("Window/KungFu Crashout/Controls/Use Gesture Arbiter Input", priority = 10001)]
        public static void UseArbiter()
        {
            var old = Object.FindFirstObjectByType<TapAnywhereInputHandler>(FindObjectsInactive.Include);
            var arbiter = Object.FindFirstObjectByType<GestureArbiterInput>(FindObjectsInactive.Include);
            if (!old && !arbiter)
            {
                EditorUtility.DisplayDialog("Gesture Input", "No TapAnywhereInputHandler in the open scene (open FightScene).", "OK");
                return;
            }

            Undo.SetCurrentGroupName("Use Gesture Arbiter Input");
            int group = Undo.GetCurrentGroup();

            if (!arbiter)
            {
                arbiter = Undo.AddComponent<GestureArbiterInput>(old.gameObject);
                var from = new SerializedObject(old).FindProperty("fighter").objectReferenceValue;
                var so = new SerializedObject(arbiter);
                so.FindProperty("fighter").objectReferenceValue = from;
                so.ApplyModifiedProperties();
            }
            Undo.RecordObject(arbiter, "Enable");
            arbiter.enabled = true;

            foreach (var t in Object.FindObjectsByType<TapAnywhereInputHandler>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                // The assessment's quick fixes, so the old handler is better too if you switch back to compare.
                var so = new SerializedObject(t);
                so.FindProperty("tapDuringMotion").boolValue = false;
                so.FindProperty("swipeWhileHoldingSpecial").boolValue = false;
                so.FindProperty("twoFingerWindow").floatValue = 0.16f;
                so.ApplyModifiedProperties();
                Undo.RecordObject(t, "Disable");
                t.enabled = false;
            }
            foreach (var t in Object.FindObjectsByType<TouchInputHandler>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            { Undo.RecordObject(t, "Disable"); t.enabled = false; }

            Undo.CollapseUndoOperations(group);
            EditorSceneManager.MarkSceneDirty(arbiter.gameObject.scene);
            Selection.activeObject = arbiter.gameObject;
            Debug.Log($"[Gestures] '{arbiter.gameObject.name}' now uses GestureArbiterInput (old handlers kept, switched off). " +
                      "Save the scene (Ctrl+S), then rebuild for Android.");
        }

        [MenuItem("Window/KungFu Crashout/Controls/Use Old Tap Anywhere Input", priority = 10002)]
        public static void UseOld()
        {
            var old = Object.FindFirstObjectByType<TapAnywhereInputHandler>(FindObjectsInactive.Include);
            if (!old) { EditorUtility.DisplayDialog("Gesture Input", "No TapAnywhereInputHandler in the open scene.", "OK"); return; }
            Undo.SetCurrentGroupName("Use Old Tap Anywhere Input");
            int group = Undo.GetCurrentGroup();
            Undo.RecordObject(old, "Enable");
            old.enabled = true;
            foreach (var a in Object.FindObjectsByType<GestureArbiterInput>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            { Undo.RecordObject(a, "Disable"); a.enabled = false; }
            Undo.CollapseUndoOperations(group);
            EditorSceneManager.MarkSceneDirty(old.gameObject.scene);
            Debug.Log("[Gestures] Back on the old TapAnywhereInputHandler. Save the scene (Ctrl+S).");
        }

        // ───────────── Replay ─────────────

        /// <summary>Stands in for the fighter: writes down every command with the time it was sent.</summary>
        class RecordingCommands : IFighterCommands
        {
            public double now, t0;
            public readonly List<string> lines = new List<string>();
            public readonly Dictionary<string, int> counts = new Dictionary<string, int>();

            void Add(string what)
            {
                lines.Add($"{now - t0,8:0.000} s  {what}");
                string key = what.Split('(')[0];
                counts[key] = counts.TryGetValue(key, out int n) ? n + 1 : 1;
            }

            public void Dash(int direction) => Add($"Dash({(direction > 0 ? "right" : "left")})");
            public void JumpUp() => Add("JumpUp");
            public void JumpToward(Vector2 direction) => Add($"JumpToward({(direction.x > 0 ? "right" : "left")})");
            public void Attack() => Add("Attack");
            public void HeavyAttack() => Add("HeavyAttack");
            public void BlockStart() => Add("BlockStart");
            public void BlockEnd() => Add("BlockEnd");
            public bool Special(SpecialMove move, int direction) { Add($"Special({move})"); return true; }
        }

        [MenuItem("Window/KungFu Crashout/Controls/Replay Touch Log…", priority = 10003)]
        public static void Replay()
        {
            string path = EditorUtility.OpenFilePanel("Replay touch log", Application.persistentDataPath, "csv");
            if (string.IsNullOrEmpty(path)) return;

            var scene = Object.FindFirstObjectByType<GestureArbiterInput>(FindObjectsInactive.Include);
            var settings = scene ? JsonUtility.FromJson<GestureSettings>(JsonUtility.ToJson(scene.Settings)) : new GestureSettings();

            var rec = new RecordingCommands();
            var gestures = new List<string>();
            var engine = new GestureEngine(settings, rec);
            engine.report = g => gestures.Add($"{rec.now - rec.t0,8:0.000} s  · {g}");

            var inv = CultureInfo.InvariantCulture;
            var frame = new List<RawTouch>();
            bool started = false;
            int frames = 0;

            void Flush()
            {
                if (!started) return;
                engine.Process(frame, rec.now);
                frame.Clear();
                frames++;
            }

            foreach (var raw in File.ReadLines(path))
            {
                string lineText = raw.Trim();
                if (lineText.Length == 0) continue;
                if (lineText.StartsWith("#"))
                {
                    foreach (var part in lineText.Split(','))
                    {
                        var kv = part.Split('=');
                        if (kv.Length != 2) continue;
                        if (kv[0].Trim() == "dpi") engine.dpi = float.Parse(kv[1], inv);
                        if (kv[0].Trim() == "height") engine.screenHeight = float.Parse(kv[1], inv);
                    }
                    continue;
                }
                var c = lineText.Split(',');
                if (c[0] == "F")
                {
                    Flush();
                    rec.now = double.Parse(c[1], inv);
                    if (!started) { rec.t0 = rec.now; started = true; }
                }
                else if (c[0] == "T" && c.Length >= 8)
                {
                    frame.Add(new RawTouch
                    {
                        id = int.Parse(c[1], inv),
                        phase = (RawPhase)int.Parse(c[2], inv),
                        position = new Vector2(float.Parse(c[3], inv), float.Parse(c[4], inv)),
                        startPosition = new Vector2(float.Parse(c[5], inv), float.Parse(c[6], inv)),
                        startTime = double.Parse(c[7], inv),
                    });
                }
            }
            Flush();

            var sb = new StringBuilder();
            sb.AppendLine($"[Gestures] Replayed {Path.GetFileName(path)}: {frames} frames, dpi {engine.dpi}, settings from " +
                          (scene ? $"'{scene.name}'" : "defaults") + ".");
            sb.Append("  Commands:");
            foreach (var kv in rec.counts) sb.Append($"  {kv.Key} ×{kv.Value}");
            sb.AppendLine();
            sb.AppendLine("  Timeline (commands, and · the arbiter's reading):");
            var merged = new List<string>(rec.lines);
            merged.AddRange(gestures);
            merged.Sort(System.StringComparer.Ordinal);
            foreach (var l in merged) sb.AppendLine("  " + l);

            string outPath = Path.ChangeExtension(path, null) + "_replay.txt";
            File.WriteAllText(outPath, sb.ToString());
            Debug.Log(sb.ToString() + $"\n  Saved to {outPath}");
        }
    }
}
#endif
