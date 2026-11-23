using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace KungFuCrashout.UI
{
    /// <summary>One exported tuning snapshot (every value, not just the changed ones).</summary>
    [Serializable]
    public class TuningSnapshot
    {
        public string format = TuningExport.Format;
        public int schema = TuningExport.Schema;
        public string name;
        public string note;
        public string exportedAt;
        public string build;      // Application.version
        public string buildId;    // Application.buildGUID (empty in the Editor)
        public string platform;
        public string device;
        public string scene;
        public string player;     // the fighter whose move set the Moves values belong to
        public int changedCount;
        public int valueCount;
        public List<TuningEntry> values = new List<TuningEntry>();
    }

    /// <summary>One value in a snapshot. Readable on its own: section, name, unit, the value and the project's value.</summary>
    [Serializable]
    public class TuningEntry
    {
        public string id;
        public string section;
        public string group;
        public string label;
        public string unit;
        public double value;
        public double projectValue;
        public bool changed;
        public bool inScene = true; // false: nothing in this scene uses it (value and projectValue are 0)
        public string asset;        // the data asset it came from (moves, launch, item hit data); empty for scene values
    }

    /// <summary>
    /// Writes the TUNING tab's values as a JSON snapshot (M2D02), in a build or in the Editor:
    ///   Application.persistentDataPath/Tuning/&lt;name&gt;_&lt;yyyyMMdd-HHmmss&gt;.json
    /// On the phone that's Android/data/&lt;package&gt;/files/Tuning (reachable over USB). The JSON is also put on the
    /// clipboard by the TUNING tab, so it can be pasted anywhere if the folder can't be reached.
    /// Format: see TuningSnapshot / TuningEntry. Ids are the TuningRegistry ids (e.g. moves.up.launch.speed).
    /// Place in: Assets/_Project/Scripts/UI/TuningExport.cs
    /// </summary>
    public static class TuningExport
    {
        public const string Format = "kfc-tuning";
        public const int Schema = 1;
        public const string FolderName = "Tuning";
        const string IndexKey = "kfc.tuning.meta.exportIndex"; // not under kfc.tune.* (those are the values)

        public static string ExportFolder => Path.Combine(Application.persistentDataPath, FolderName);

        /// <summary>The suggested name for the next export: tune_v1, tune_v2…</summary>
        public static string NextName => "tune_v" + (PlayerPrefs.GetInt(IndexKey, 0) + 1);

        public static TuningSnapshot Snapshot(string name, string note)
        {
            var s = new TuningSnapshot
            {
                name = string.IsNullOrWhiteSpace(name) ? NextName : name.Trim(),
                note = note?.Trim() ?? "",
                exportedAt = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:sszzz", CultureInfo.InvariantCulture),
                build = Application.version,
                buildId = Application.buildGUID,
                platform = Application.platform.ToString(),
                device = SystemInfo.deviceModel,
                scene = SceneManager.GetActiveScene().name,
                player = TuningRegistry.PlayerName(),
            };

            foreach (var d in TuningRegistry.Defs)
            {
                if (d.testOnly) continue; // testing toggles are never exported
                float now = TuningRegistry.Current(d);
                float project = TuningRegistry.Default(d);
                bool inScene = !float.IsNaN(now);
                var e = new TuningEntry
                {
                    id = d.id,
                    section = d.section,
                    group = d.group ?? "",
                    label = d.label,
                    unit = d.unit ?? "",
                    inScene = inScene,
                    value = inScene ? Round(now) : 0,
                    projectValue = inScene && !float.IsNaN(project) ? Round(project) : 0,
                    asset = TuningRegistry.AssetName(d),
                };
                e.changed = inScene && Math.Abs(e.value - e.projectValue) > 0.00005;
                if (e.changed) s.changedCount++;
                s.values.Add(e);
            }
            s.valueCount = s.values.Count;
            return s;
        }

        public static string ToJson(TuningSnapshot s) => JsonUtility.ToJson(s, true);

        /// <summary>Writes the snapshot file. False (with the reason) if it couldn't be written.</summary>
        public static bool Export(string name, string note, out string path, out string json, out string error)
        {
            path = null;
            json = null;
            error = null;
            try
            {
                var snapshot = Snapshot(name, note);
                json = ToJson(snapshot);
                Directory.CreateDirectory(ExportFolder);
                string file = $"{Safe(snapshot.name)}_{DateTime.Now:yyyyMMdd-HHmmss}.json";
                path = Path.Combine(ExportFolder, file);
                File.WriteAllText(path, json, new UTF8Encoding(false));

                PlayerPrefs.SetInt(IndexKey, PlayerPrefs.GetInt(IndexKey, 0) + 1);
                PlayerPrefs.Save();
                Debug.Log($"[Tuning] Exported {snapshot.changedCount} changed of {snapshot.valueCount} values → {path}");
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                Debug.LogError("[Tuning] Export failed: " + ex);
                return false;
            }
        }

        /// <summary>Where the file is, as a person would find it (on Android: from the phone's storage root).</summary>
        public static string DisplayPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return "";
            if (Application.platform == RuntimePlatform.Android)
                return $"Android/data/{Application.identifier}/files/{FolderName}/{Path.GetFileName(path)}";
            return path;
        }

        static double Round(float v) => Math.Round((double)v, 4);

        static string Safe(string name)
        {
            var sb = new StringBuilder();
            foreach (char c in name)
                sb.Append(char.IsLetterOrDigit(c) || c == '_' || c == '-' ? c : '_');
            return sb.Length > 0 ? sb.ToString() : "tune";
        }
    }
}
