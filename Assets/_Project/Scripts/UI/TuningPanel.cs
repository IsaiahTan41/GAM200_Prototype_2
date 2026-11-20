using System.IO;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using KungFuCrashout.Core;

namespace KungFuCrashout.UI
{
    /// <summary>
    /// The pause menu's TUNING tab (M2D02): section chips (Rules · Moves · Items · Stage · Input · CPU), a scrolling list
    /// of TuningRows for the chosen section, and Reset Section / Reset All / Restart Match.
    /// EXPORT JSON writes a full snapshot of every value (TuningExport) named by the Name field (tune_v1, tune_v2…) with
    /// an optional note, and copies the JSON to the clipboard.
    /// Changes are remembered on the device like the other options and applied on every load (TuningRuntime).
    /// LIVE values take effect on the next action; NEXT ROUND values when the next round starts (or Restart Match).
    ///
    /// Setup: KungFu Crashout → GDD Build → Add Tuning Tab + Scrolling Pages (pause menu).
    /// Place in: Assets/_Project/Scripts/UI/TuningPanel.cs
    /// </summary>
    public class TuningPanel : MonoBehaviour
    {
        [SerializeField] ScrollRect scroll;
        [Tooltip("One chip per TuningRegistry section, in the same order as Section Contents.")]
        [SerializeField] Button[] sectionButtons = new Button[0];
        [SerializeField] RectTransform[] sectionContents = new RectTransform[0];
        [SerializeField] Button resetSectionButton;
        [SerializeField] Button resetAllButton;
        [SerializeField] Button restartButton;
        [SerializeField] TMP_Text footer;

        [Header("Export (JSON snapshot)")]
        [SerializeField] TMP_InputField nameField;
        [SerializeField] TMP_InputField noteField;
        [SerializeField] Button exportButton;
        [Tooltip("Also put the exported JSON on the clipboard (paste it anywhere if the file can't be reached).")]
        [SerializeField] bool copyToClipboard = true;
        [Tooltip("On the first launch of a newly installed build, forget tab values saved with the older build.")]
        [SerializeField] bool clearOnNewBuild = true;
        [SerializeField, Min(1f)] float statusSeconds = 8f;
        [SerializeField] Color selected = new Color(1f, 0.78f, 0.2f);
        [SerializeField] Color normal = new Color(0.25f, 0.25f, 0.3f);
        [SerializeField] Color selectedText = Color.black;
        [SerializeField] Color normalText = Color.white;

        int current;
        string status;
        float statusUntil;
        bool newBuildShown;

        /// <summary>Called by the OptionsMenu at start (this page is hidden until opened).</summary>
        public void ApplySaved() => TuningRuntime.Ensure(clearOnNewBuild);

        void Awake()
        {
            for (int i = 0; i < sectionButtons.Length; i++)
            {
                int index = i;
                if (sectionButtons[i]) sectionButtons[i].onClick.AddListener(() => ShowSection(index));
            }
            if (resetSectionButton) resetSectionButton.onClick.AddListener(ResetSection);
            if (resetAllButton) resetAllButton.onClick.AddListener(ResetAll);
            if (restartButton) restartButton.onClick.AddListener(RestartMatch);
            if (exportButton) exportButton.onClick.AddListener(Export);
        }

        void OnEnable()
        {
            TuningRuntime.Ensure(clearOnNewBuild);
            if (nameField && string.IsNullOrWhiteSpace(nameField.text)) nameField.text = TuningExport.NextName;
            if (TuningRuntime.ClearedForNewBuild && !newBuildShown)
            {
                newBuildShown = true;
                SetStatus("New build installed: tuning from the old build was cleared");
            }
            ShowSection(current);
        }

        void Export()
        {
            string name = nameField ? nameField.text : null;
            string note = noteField ? noteField.text : null;
            if (TuningExport.Export(name, note, out string path, out string json, out string error))
            {
                if (copyToClipboard) GUIUtility.systemCopyBuffer = json;
                SetStatus($"Exported {Path.GetFileNameWithoutExtension(path)}{(copyToClipboard ? " · copied to clipboard" : "")}\n" +
                          $"<size=80%>{TuningExport.DisplayPath(path)}</size>");
                if (nameField) nameField.text = TuningExport.NextName;
                if (noteField) noteField.text = "";
            }
            else
            {
                SetStatus("<color=#FF6B6B>Export failed: " + error + "</color>");
            }
        }

        void SetStatus(string text)
        {
            status = text;
            statusUntil = Time.unscaledTime + statusSeconds;
            UpdateFooter();
        }

        public void ShowSection(int index)
        {
            if (sectionContents.Length == 0) return;
            current = Mathf.Clamp(index, 0, sectionContents.Length - 1);
            for (int i = 0; i < sectionContents.Length; i++)
                if (sectionContents[i]) sectionContents[i].gameObject.SetActive(i == current);

            for (int i = 0; i < sectionButtons.Length; i++)
            {
                var b = sectionButtons[i];
                if (!b) continue;
                if (b.targetGraphic) b.targetGraphic.color = i == current ? selected : normal;
                var t = b.GetComponentInChildren<TMP_Text>(true);
                if (t) t.color = i == current ? selectedText : normalText;
            }

            if (scroll && sectionContents[current])
            {
                scroll.StopMovement();
                scroll.content = sectionContents[current];
                LayoutRebuilder.ForceRebuildLayoutImmediate(sectionContents[current]);
                scroll.verticalNormalizedPosition = 1f;
            }
            UpdateFooter();
        }

        void ResetSection()
        {
            foreach (var row in Rows(current))
            {
                var def = TuningRegistry.Find(row.ParamId);
                if (def != null) TuningRegistry.Reset(def);
            }
            PlayerPrefs.Save();
            RefreshRows();
        }

        void ResetAll()
        {
            TuningRegistry.ResetAll();
            RefreshRows();
        }

        void RestartMatch()
        {
            PlayerPrefs.Save();
            var match = FindAnyObjectByType<MatchManager>();
            var menu = GetComponentInParent<OptionsMenu>(true);
            if (menu) menu.Close();
            if (match) match.Rematch();
        }

        void RefreshRows()
        {
            for (int i = 0; i < sectionContents.Length; i++)
                foreach (var row in Rows(i)) row.Refresh();
            UpdateFooter();
        }

        TuningRow[] Rows(int section) =>
            section >= 0 && section < sectionContents.Length && sectionContents[section]
                ? sectionContents[section].GetComponentsInChildren<TuningRow>(true)
                : new TuningRow[0];

        void UpdateFooter()
        {
            if (!footer) return;
            int changed = 0;
            foreach (var d in TuningRegistry.Defs) if (TuningRegistry.IsChanged(d)) changed++;
            if (!string.IsNullOrEmpty(status) && Time.unscaledTime < statusUntil)
            {
                footer.text = "<color=#FFC733>" + status + "</color>";
                return;
            }
            footer.text = $"{changed} value(s) changed · saved on this device\n" +
                          "<color=#9AA0A6>LIVE = next action · NEXT ROUND = next round</color>";
        }

        void LateUpdate()
        {
            // Cheap: only while the page is open.
            if (Time.frameCount % 15 == 0) UpdateFooter();
        }
    }
}
