using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using KungFuCrashout.Fighters;
using KungFuCrashout.Props;
using KungFuCrashout.UI;

namespace KungFuCrashout.EditorTools
{
    /// <summary>
    /// Window → KungFu Crashout → Tuning → Import Tuning JSON…
    ///
    /// Reads a tuning snapshot exported from the TUNING tab (a build on the phone, or Play mode) and writes its values
    /// into the project, so the next build plays with them:
    ///   1. Source: Open File… (any .json) or Paste from Clipboard (code fences / extra text around the JSON are ignored).
    ///   2. Check: not a tuning file → stop. Unknown id, not in this scene, not a number → skipped and listed.
    ///      Outside the value's range → clamped, with a note. Different data asset or player than the export → not
    ///      ticked (tick it to apply anyway). Missing values are left as they are.
    ///   3. Preview: section · value · project now · file · result. Untick rows to leave them out.
    ///   4. Apply: one Undo step (Ctrl+Z reverts all of it). Scene values go into the open FightScene, move / launch /
    ///      item hit values into their data assets, item throw speed / holding into the scene items AND the item
    ///      prefabs. Before writing it saves the project's current values to Docs/Tuning/backup_before_*.json
    ///      (importing that file puts everything back). Optionally forgets this computer's saved tab values.
    ///   Save the scene and project afterwards (Ctrl+S).
    ///
    /// Window → KungFu Crashout → Tuning → Export Project Values (baseline) writes the project's own values as a
    /// snapshot into Docs/Tuning (e.g. tune_v1). Importing it straight back reports 0 changes.
    ///
    /// Place in: Assets/_Project/Editor/TuningImportWindow.cs
    /// </summary>
    public class TuningImportWindow : EditorWindow
    {
        const string Menu = "Window/KungFu Crashout/Tuning/";
        const string ItemPrefabFolder = "Assets/_Project/Prefabs";
        const double SameTolerance = 0.00005;

        enum Status { Change, Clamped, Same, AssetDiffers, NotInScene, UnknownId, Invalid }

        class Row
        {
            public TuningEntry entry;
            public TuneDef def;
            public float now = float.NaN;
            public float target = float.NaN;
            public Status status;
            public string note = "";
            public bool include;
            public bool Applicable => status == Status.Change || status == Status.Clamped || status == Status.AssetDiffers;
        }

        TuningSnapshot snapshot;
        string sourceLabel;
        string sourceText;
        string error;
        readonly List<string> warnings = new List<string>();
        readonly List<Row> rows = new List<Row>();
        Vector2 scroll;
        bool changesOnly = true;
        bool[] sectionOn;
        bool writeBackup = true;
        bool clearSavedTabValues = true;

        static string DocsTuningFolder => Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Docs", "Tuning");

        // ───────────── Menus ─────────────

        [MenuItem(Menu + "Import Tuning JSON…", priority = 10057)]
        static void Open()
        {
            var w = GetWindow<TuningImportWindow>("Tuning Import");
            w.minSize = new Vector2(860f, 420f);
            w.Show();
        }

        [MenuItem(Menu + "Export Project Values (baseline)…", priority = 10058)]
        static void ExportProjectValues()
        {
            if (Application.isPlaying) { EditorUtility.DisplayDialog("Export Project Values", "Exit Play mode first (Play mode values may be tuned).", "OK"); return; }
            if (!SceneReady(out string why)) { EditorUtility.DisplayDialog("Export Project Values", why, "OK"); return; }
            Directory.CreateDirectory(DocsTuningFolder);
            string path = EditorUtility.SaveFilePanel("Export project tuning values", DocsTuningFolder,
                $"tune_v1_{DateTime.Now:yyyyMMdd-HHmmss}.json", "json");
            if (string.IsNullOrEmpty(path)) return;
            string name = Path.GetFileNameWithoutExtension(path);
            int cut = name.LastIndexOf('_');
            if (cut > 0 && name.Length - cut == 16) name = name.Substring(0, cut); // drop the _yyyyMMdd-HHmmss stamp
            var snap = TuningExport.Snapshot(name, "Project values (exported in the Editor)");
            File.WriteAllText(path, TuningExport.ToJson(snap), new System.Text.UTF8Encoding(false));
            Debug.Log($"[Tuning] Project values ({snap.valueCount}) exported → {path}");
            EditorUtility.RevealInFinder(path);
        }

        // ───────────── Loading ─────────────

        void OnEnable()
        {
            if (sectionOn == null || sectionOn.Length != TuningRegistry.Sections.Length)
            {
                sectionOn = new bool[TuningRegistry.Sections.Length];
                for (int i = 0; i < sectionOn.Length; i++) sectionOn[i] = true;
            }
            if (!string.IsNullOrEmpty(sourceText)) Load(sourceText, sourceLabel); // survive script reloads
        }

        void OpenFile()
        {
            string start = Directory.Exists(TuningExport.ExportFolder) ? TuningExport.ExportFolder : DocsTuningFolder;
            string path = EditorUtility.OpenFilePanel("Import tuning JSON", start, "json");
            if (string.IsNullOrEmpty(path)) return;
            try { Load(File.ReadAllText(path), Path.GetFileName(path)); }
            catch (Exception ex) { Fail("Couldn't read the file: " + ex.Message); }
        }

        void PasteClipboard()
        {
            string text = EditorGUIUtility.systemCopyBuffer;
            if (string.IsNullOrWhiteSpace(text)) { Fail("The clipboard is empty."); return; }
            Load(text, "clipboard");
        }

        void Fail(string message)
        {
            error = message;
            snapshot = null;
            rows.Clear();
            warnings.Clear();
            Repaint();
        }

        void Load(string text, string label)
        {
            sourceText = text;
            sourceLabel = label;
            error = null;
            snapshot = null;
            rows.Clear();
            warnings.Clear();

            // Pasted from a chat app: keep only the outermost { … } (drops ```json fences and any message text).
            int start = text.IndexOf('{'), end = text.LastIndexOf('}');
            if (start < 0 || end <= start) { Fail("No JSON object found (expected { … })."); return; }
            string json = text.Substring(start, end - start + 1);

            TuningSnapshot snap;
            try { snap = JsonUtility.FromJson<TuningSnapshot>(json); }
            catch (Exception ex) { Fail("Not valid JSON: " + ex.Message); return; }

            if (snap == null || snap.format != TuningExport.Format)
            {
                Fail($"Not a KungFu Crashout tuning file (format is \"{snap?.format}\", expected \"{TuningExport.Format}\").");
                return;
            }
            if (snap.values == null || snap.values.Count == 0) { Fail("The file has no values."); return; }
            if (snap.schema > TuningExport.Schema)
                warnings.Add($"The file uses a newer format (schema {snap.schema}; this project reads {TuningExport.Schema}). Unknown fields are ignored.");

            snapshot = snap;
            BuildRows();
        }

        static bool SceneReady(out string why)
        {
            why = null;
            if (!UnityEngine.Object.FindAnyObjectByType<Fighter>(FindObjectsInactive.Include))
            {
                why = "Open FightScene first: the values are written into its fighters, stage, input and CPU.";
                return false;
            }
            return true;
        }

        /// <summary>Compares every value in the file with the project as it is now.</summary>
        void BuildRows()
        {
            rows.Clear();
            if (snapshot == null) return;

            if (Application.isPlaying) warnings.Add("Play mode is on: exit Play mode to apply (Play mode values are temporary).");
            if (!SceneReady(out string why)) warnings.Add(why);
            string scene = SceneManager.GetActiveScene().name;
            if (!string.IsNullOrEmpty(snapshot.scene) && snapshot.scene != scene)
                warnings.Add($"The file was exported in {snapshot.scene}; the open scene is {scene}.");
            string player = TuningRegistry.PlayerName();
            bool otherPlayer = !string.IsNullOrEmpty(snapshot.player) && !string.IsNullOrEmpty(player) && snapshot.player != player;
            if (otherPlayer) warnings.Add($"The file's move values belong to '{snapshot.player}'; this scene's player is '{player}'.");

            var seen = new HashSet<string>();
            var duplicates = new List<string>();
            foreach (var e in snapshot.values)
            {
                if (e == null || string.IsNullOrEmpty(e.id)) continue;
                if (!seen.Add(e.id)) duplicates.Add(e.id);
                rows.Add(Compare(e));
            }
            // A repeated id: the last one wins (earlier rows are left out).
            for (int i = 0; i < rows.Count; i++)
                for (int j = i + 1; j < rows.Count; j++)
                    if (rows[i].entry.id == rows[j].entry.id) { rows[i].include = false; rows[i].status = Status.Invalid; rows[i].note = "Repeated id; the later entry is used"; break; }
            if (duplicates.Count > 0) warnings.Add("Repeated ids (the last one is used): " + string.Join(", ", duplicates));

            int missing = 0;
            foreach (var d in TuningRegistry.Defs) if (!seen.Contains(d.id)) missing++;
            if (missing > 0) warnings.Add($"{missing} value(s) of this project are not in the file; they are left as they are (older export).");
            Repaint();
        }

        static Row Compare(TuningEntry e)
        {
            var r = new Row { entry = e, def = TuningRegistry.Find(e.id) };
            if (r.def == null) { r.status = Status.UnknownId; r.note = "No such value in this project (renamed or removed)"; return r; }
            if (!e.inScene) { r.status = Status.NotInScene; r.note = "Not in the scene it was exported from"; return r; }
            if (double.IsNaN(e.value) || double.IsInfinity(e.value)) { r.status = Status.Invalid; r.note = "Not a number"; return r; }

            r.now = CurrentValue(r.def);
            if (float.IsNaN(r.now)) { r.status = Status.NotInScene; r.note = "Nothing in the open scene uses it"; return r; }

            // The tab's range, widened to include the project's own value (the tab allows that too).
            float low = Mathf.Min(r.def.min, r.now), high = Mathf.Max(r.def.max, r.now);
            double v = e.value;
            if (r.def.display == TuneDisplay.OnOff) v = v >= 0.5 ? 1 : 0;
            if (v < low || v > high)
            {
                r.note = $"{e.value:0.####} is outside {low:0.####}–{high:0.####}; clamped";
                v = Math.Max(low, Math.Min(high, v));
                r.status = Status.Clamped;
            }
            r.target = (float)v;

            if (Math.Abs(r.target - r.now) <= SameTolerance)
            {
                r.status = Status.Same;
                r.note = "";
                return r;
            }

            string asset = TuningRegistry.AssetName(r.def);
            if (!string.IsNullOrEmpty(e.asset) && !string.IsNullOrEmpty(asset) && e.asset != asset)
            {
                r.status = Status.AssetDiffers;
                r.note = $"Exported from {e.asset}; would write {asset} (tick to apply anyway)";
                r.include = false;
                return r;
            }
            if (r.status != Status.Clamped) r.status = Status.Change;
            r.include = true;
            return r;
        }

        /// <summary>The project's value now; item holding / throw speed fall back to the item prefabs.</summary>
        static float CurrentValue(TuneDef d)
        {
            float v = TuningRegistry.Current(d);
            if (!float.IsNaN(v)) return v;
            if (ItemPrefabField(d, out var weight, out string field))
                foreach (var c in ItemPrefabs(weight))
                    return ReadField(c, field);
            return float.NaN;
        }

        // ───────────── Applying ─────────────

        void Apply()
        {
            if (Application.isPlaying) { EditorUtility.DisplayDialog("Tuning Import", "Exit Play mode first.", "OK"); return; }
            if (!SceneReady(out string why)) { EditorUtility.DisplayDialog("Tuning Import", why, "OK"); return; }

            BuildRowsKeepingTicks(); // the project may have changed since the file was loaded
            var apply = rows.FindAll(r => r.include && r.Applicable);
            if (apply.Count == 0) { EditorUtility.DisplayDialog("Tuning Import", "Nothing to apply.", "OK"); return; }
            if (!EditorUtility.DisplayDialog("Tuning Import",
                    $"Apply {apply.Count} value(s) from '{snapshot.name}' to {SceneManager.GetActiveScene().name} and the data assets?\n\n" +
                    "Ctrl+Z undoes the whole import." + (writeBackup ? "\nA backup of the current values is saved to Docs/Tuning first." : ""),
                    "Apply", "Cancel"))
                return;

            string backupPath = null;
            if (writeBackup)
            {
                try
                {
                    Directory.CreateDirectory(DocsTuningFolder);
                    var backup = TuningExport.Snapshot("backup_before_" + Safe(snapshot.name), $"Project values before importing {snapshot.name}");
                    backupPath = Path.Combine(DocsTuningFolder, $"backup_before_{Safe(snapshot.name)}_{DateTime.Now:yyyyMMdd-HHmmss}.json");
                    File.WriteAllText(backupPath, TuningExport.ToJson(backup), new System.Text.UTF8Encoding(false));
                }
                catch (Exception ex)
                {
                    if (!EditorUtility.DisplayDialog("Tuning Import", "Couldn't write the backup: " + ex.Message + "\n\nApply anyway?", "Apply", "Cancel")) return;
                    backupPath = null;
                }
            }

            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName($"Import tuning {snapshot.name}");
            int group = Undo.GetCurrentGroup();
            var log = new List<string>();
            int writes = 0;

            foreach (var r in apply)
            {
                // Item holding / throw speed: the prefabs first, so scene instances that match them need no override.
                if (ItemPrefabField(r.def, out var weight, out string field))
                    foreach (var c in ItemPrefabs(weight))
                    {
                        var so = new SerializedObject(c);
                        var p = so.FindProperty(field);
                        if (p == null) continue;
                        if (p.propertyType == SerializedPropertyType.Boolean) p.boolValue = r.target >= 0.5f;
                        else if (p.propertyType == SerializedPropertyType.Integer) p.intValue = Mathf.RoundToInt(r.target);
                        else p.floatValue = r.target;
                        so.ApplyModifiedProperties();
                        writes++;
                    }

                foreach (var t in r.def.targets())
                {
                    Undo.RecordObject(t.owner, "Import tuning");
                    t.Write(r.target);
                    EditorUtility.SetDirty(t.owner);
                    if (t.owner is Component comp && PrefabUtility.IsPartOfPrefabInstance(comp))
                        PrefabUtility.RecordPrefabInstancePropertyModifications(comp);
                    writes++;
                }
                log.Add($"{r.def.id}: {TuningRegistry.Format(r.def, r.now)} → {TuningRegistry.Format(r.def, r.target)}");
            }

            Undo.CollapseUndoOperations(group);
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            if (clearSavedTabValues) TuningRegistry.ClearSaved();

            Debug.Log($"[Tuning] Imported '{snapshot.name}' ({sourceLabel}): {apply.Count} value(s), {writes} field(s) written." +
                      (backupPath != null ? $"\nBackup: {backupPath}" : "") + "\n  " + string.Join("\n  ", log) +
                      "\nSave the scene and project (Ctrl+S).");
            ShowNotification(new GUIContent($"Applied {apply.Count} value(s) — save with Ctrl+S"));
            BuildRows(); // everything applied now reads as Same
        }

        void BuildRowsKeepingTicks()
        {
            var ticks = new Dictionary<string, bool>();
            foreach (var r in rows) if (r.Applicable) ticks[r.entry.id] = r.include;
            BuildRows();
            foreach (var r in rows) if (r.Applicable && ticks.TryGetValue(r.entry.id, out bool on)) r.include = on;
        }

        static bool ItemPrefabField(TuneDef d, out ItemDurability.Weight weight, out string field)
        {
            weight = ItemDurability.Weight.Medium;
            field = null;
            if (d == null || !d.sweep || !d.id.StartsWith("items.")) return false;
            var parts = d.id.Split('.');
            if (parts.Length != 3) return false;
            if (parts[1] == "light") weight = ItemDurability.Weight.Light;
            else if (parts[1] == "heavy") weight = ItemDurability.Weight.Heavy;
            else return false;
            field = parts[2]; // throwSpeed / canMoveWhileHeld: the id ends with the Carryable field's name
            return true;
        }

        static IEnumerable<Carryable> ItemPrefabs(ItemDurability.Weight weight)
        {
            foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { ItemPrefabFolder }))
            {
                var go = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid));
                var c = go ? go.GetComponentInChildren<Carryable>(true) : null;
                var d = c ? c.GetComponent<ItemDurability>() : null;
                if (d && d.ItemWeight == weight) yield return c;
            }
        }

        static float ReadField(Carryable c, string field)
        {
            var p = new SerializedObject(c).FindProperty(field);
            if (p == null) return float.NaN;
            return p.propertyType == SerializedPropertyType.Boolean ? (p.boolValue ? 1f : 0f)
                 : p.propertyType == SerializedPropertyType.Integer ? p.intValue
                 : p.floatValue;
        }

        static string Safe(string s)
        {
            var chars = (s ?? "tune").ToCharArray();
            for (int i = 0; i < chars.Length; i++)
                if (!char.IsLetterOrDigit(chars[i]) && chars[i] != '_' && chars[i] != '-') chars[i] = '_';
            return new string(chars);
        }

        // ───────────── UI ─────────────

        static GUIStyle wrap;

        void OnGUI()
        {
            wrap ??= new GUIStyle(EditorStyles.label) { wordWrap = true, richText = true };

            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                if (GUILayout.Button("Open File…", EditorStyles.toolbarButton, GUILayout.Width(90))) OpenFile();
                if (GUILayout.Button("Paste from Clipboard", EditorStyles.toolbarButton, GUILayout.Width(140))) PasteClipboard();
                using (new EditorGUI.DisabledScope(snapshot == null))
                    if (GUILayout.Button("Compare Again", EditorStyles.toolbarButton, GUILayout.Width(100))) BuildRowsKeepingTicks();
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Export Folder", EditorStyles.toolbarButton, GUILayout.Width(90)))
                {
                    Directory.CreateDirectory(TuningExport.ExportFolder);
                    EditorUtility.RevealInFinder(TuningExport.ExportFolder);
                }
                if (GUILayout.Button("Docs/Tuning", EditorStyles.toolbarButton, GUILayout.Width(90)))
                {
                    Directory.CreateDirectory(DocsTuningFolder);
                    EditorUtility.RevealInFinder(DocsTuningFolder);
                }
            }

            if (!string.IsNullOrEmpty(error)) EditorGUILayout.HelpBox(error, MessageType.Error);
            if (snapshot == null)
            {
                if (string.IsNullOrEmpty(error))
                    EditorGUILayout.HelpBox("Open a tuning JSON exported from the TUNING tab (phone or Play mode), or paste it from the clipboard.\n" +
                                            "FightScene must be open; exit Play mode to apply.", MessageType.Info);
                return;
            }

            // File header
            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField($"<b>{snapshot.name}</b>   <color=#888888>({sourceLabel})</color>", wrap);
            if (!string.IsNullOrEmpty(snapshot.note)) EditorGUILayout.LabelField("Note: " + snapshot.note, wrap);
            EditorGUILayout.LabelField($"Exported {snapshot.exportedAt} · build {snapshot.build} · {snapshot.platform} · {snapshot.device} · " +
                                       $"scene {snapshot.scene} · player {snapshot.player} · {snapshot.changedCount} of {snapshot.valueCount} changed on the device", wrap);
            foreach (var w in warnings) EditorGUILayout.HelpBox(w, MessageType.Warning);

            int change = 0, same = 0, skipped = 0, ticked = 0;
            foreach (var r in rows)
            {
                if (r.Applicable) { change++; if (r.include) ticked++; }
                else if (r.status == Status.Same) same++;
                else skipped++;
            }
            EditorGUILayout.LabelField($"<b>{change}</b> differ from the project ({ticked} ticked) · {same} the same · {skipped} skipped", wrap);

            // Filters
            using (new EditorGUILayout.HorizontalScope())
            {
                changesOnly = GUILayout.Toggle(changesOnly, "Differences only", GUILayout.Width(130));
                GUILayout.Space(8);
                for (int i = 0; i < TuningRegistry.Sections.Length; i++)
                    sectionOn[i] = GUILayout.Toggle(sectionOn[i], TuningRegistry.Sections[i], EditorStyles.miniButton, GUILayout.Width(62));
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Tick all", GUILayout.Width(70))) SetTicks(true);
                if (GUILayout.Button("Untick all", GUILayout.Width(80))) SetTicks(false);
            }

            // Table
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                GUILayout.Label("", GUILayout.Width(20));
                GUILayout.Label("Section", EditorStyles.miniBoldLabel, GUILayout.Width(56));
                GUILayout.Label("Value", EditorStyles.miniBoldLabel, GUILayout.Width(250));
                GUILayout.Label("Project now", EditorStyles.miniBoldLabel, GUILayout.Width(80));
                GUILayout.Label("File", EditorStyles.miniBoldLabel, GUILayout.Width(80));
                GUILayout.Label("Result", EditorStyles.miniBoldLabel);
            }
            scroll = EditorGUILayout.BeginScrollView(scroll);
            foreach (var r in rows)
            {
                if (changesOnly && r.status == Status.Same) continue;
                int section = r.def != null ? Array.IndexOf(TuningRegistry.Sections, r.def.section) : -1;
                if (section >= 0 && !sectionOn[section]) continue;

                using (new EditorGUILayout.HorizontalScope())
                {
                    using (new EditorGUI.DisabledScope(!r.Applicable))
                        r.include = EditorGUILayout.Toggle(r.include && r.Applicable, GUILayout.Width(20));
                    GUILayout.Label(r.def != null ? r.def.section : r.entry.section, GUILayout.Width(56));
                    string name = r.def != null ? (string.IsNullOrEmpty(r.def.group) ? r.def.label : $"{r.def.group} · {r.def.label}") : r.entry.label;
                    GUILayout.Label(new GUIContent(name, r.entry.id), GUILayout.Width(250));
                    GUILayout.Label(r.def != null ? TuningRegistry.Format(r.def, r.now) : "—", GUILayout.Width(80));
                    string file = r.def != null && !float.IsNaN(r.target) ? TuningRegistry.Format(r.def, r.target)
                                : r.entry.value.ToString("0.####");
                    GUILayout.Label(file, GUILayout.Width(80));
                    GUILayout.Label(ResultText(r), wrap);
                }
            }
            EditorGUILayout.EndScrollView();

            // Apply
            EditorGUILayout.Space(4);
            using (new EditorGUILayout.HorizontalScope())
            {
                writeBackup = GUILayout.Toggle(writeBackup, new GUIContent("Back up current values first", "Docs/Tuning/backup_before_<name>_<time>.json"), GUILayout.Width(200));
                clearSavedTabValues = GUILayout.Toggle(clearSavedTabValues, new GUIContent("Forget this computer's saved tab values",
                    "So Play mode shows the imported values, not values saved earlier in the TUNING tab"), GUILayout.Width(260));
                GUILayout.FlexibleSpace();
                using (new EditorGUI.DisabledScope(Application.isPlaying || ticked == 0))
                    if (GUILayout.Button($"Apply {ticked} value(s)", GUILayout.Height(26), GUILayout.Width(160))) Apply();
            }
        }

        void SetTicks(bool on)
        {
            foreach (var r in rows) if (r.Applicable) r.include = on;
        }

        static string ResultText(Row r)
        {
            switch (r.status)
            {
                case Status.Change: return "<color=#E0A030>Change</color>";
                case Status.Clamped: return "<color=#E0A030>Change</color> · " + r.note;
                case Status.Same: return "<color=#888888>Same</color>";
                case Status.AssetDiffers: return "<color=#D06050>Check</color> · " + r.note;
                default: return "<color=#888888>Skipped</color> · " + r.note;
            }
        }
    }
}
