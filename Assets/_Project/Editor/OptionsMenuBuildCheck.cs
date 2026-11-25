#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace KungFuCrashout.EditorTools
{
    /// <summary>
    /// Build check (2026-11-25): the pause menu stopped working in a build because its "Options Menu" object had been
    /// switched OFF in FightScene while its UI was being edited. An OptionsMenu on an inactive object never runs, so the
    /// pause / gear button does nothing (and edits made inside that menu never show). Before every build this scans the
    /// scenes in the build list; if any OptionsMenu sits on an inactive object it asks whether to cancel the build.
    /// (Its inner "Panel" may be off: the menu hides it itself. Only the object WITH the OptionsMenu must be on.)
    /// Place in: Assets/_Project/Editor/OptionsMenuBuildCheck.cs
    /// </summary>
    public class OptionsMenuBuildCheck : IPreprocessBuildWithReport
    {
        public int callbackOrder => 0;

        public void OnPreprocessBuild(BuildReport report)
        {
            var problems = Find();
            if (problems.Count == 0) return;
            string msg = "The pause / options menu is switched OFF in:\n  " + string.Join("\n  ", problems) +
                         "\n\nIt won't open in this build. Select that scene's 'Options Menu' object and tick it on (leave its Panel off), " +
                         "or run Window → KungFu Crashout → GDD Build → Check Options Menus (switch on).";
            Debug.LogError("[Build check] " + msg);
            if (!Application.isBatchMode &&
                EditorUtility.DisplayDialog("Pause menu is off", msg, "Cancel build", "Build anyway"))
                throw new BuildFailedException("Cancelled: Options Menu switched off (see Console).");
        }

        [MenuItem("Window/KungFu Crashout/GDD Build/Check Options Menus (switch on)", priority = 10056)]
        static void FixMenu()
        {
            var problems = Find();
            if (problems.Count == 0) { EditorUtility.DisplayDialog("Options menus", "All options / pause menus are switched on.", "OK"); return; }
            int fixedCount = 0;
            foreach (var path in EnabledScenes())
            {
                string text = File.ReadAllText(path);
                string updated = SwitchOn(text);
                if (updated != text) { File.WriteAllText(path, updated); fixedCount++; }
            }
            AssetDatabase.Refresh();
            EditorUtility.DisplayDialog("Options menus", $"Switched the Options Menu back on in {fixedCount} scene(s). " +
                "If one of them is open, reload it when Unity asks.", "OK");
        }

        static IEnumerable<string> EnabledScenes()
        {
            foreach (var s in EditorBuildSettings.scenes) if (s.enabled && File.Exists(s.path)) yield return s.path;
        }

        static string Guid() => AssetDatabase.AssetPathToGUID("Assets/_Project/Scripts/UI/OptionsMenu.cs");

        /// <summary>"Scene: object" for every OptionsMenu on an inactive GameObject in the build's scenes (read from the files).</summary>
        static List<string> Find()
        {
            var list = new List<string>();
            string guid = Guid();
            if (string.IsNullOrEmpty(guid)) return list;
            foreach (var path in EnabledScenes())
                foreach (var name in InactiveMenus(File.ReadAllText(path), guid))
                    list.Add($"{Path.GetFileNameWithoutExtension(path)}: {name}");
            return list;
        }

        static IEnumerable<string> InactiveMenus(string text, string guid)
        {
            foreach (Match m in Regex.Matches(text, @"--- !u!114 &-?\d+\nMonoBehaviour:\n(?:  .*\n)*?  m_GameObject: \{fileID: (-?\d+)\}\n(?:  .*\n)*?  m_Script: \{fileID: 11500000, guid: " + guid))
            {
                var go = Regex.Match(text, @"--- !u!1 &" + m.Groups[1].Value + @"\nGameObject:\n(?:  .*\n)*?  m_Name: (.*)\n(?:  .*\n)*?  m_IsActive: (\d)");
                if (go.Success && go.Groups[2].Value == "0") yield return go.Groups[1].Value;
            }
        }

        static string SwitchOn(string text)
        {
            string guid = Guid();
            foreach (Match m in Regex.Matches(text, @"--- !u!114 &-?\d+\nMonoBehaviour:\n(?:  .*\n)*?  m_GameObject: \{fileID: (-?\d+)\}\n(?:  .*\n)*?  m_Script: \{fileID: 11500000, guid: " + guid))
                text = Regex.Replace(text, @"(--- !u!1 &" + m.Groups[1].Value + @"\nGameObject:\n(?:  .*\n)*?  m_IsActive: )0", "${1}1");
            return text;
        }
    }
}
#endif
