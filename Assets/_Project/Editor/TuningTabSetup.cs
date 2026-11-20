using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using KungFuCrashout.UI;

namespace KungFuCrashout.EditorTools
{
    /// <summary>
    /// Window → KungFu Crashout → GDD Build → Add Tuning Tab + Scrolling Pages (pause menu)
    ///
    /// In the open scene's pause menu (FightScene):
    ///   1. Enlarges the window (1100 × 700 → 1500 × 900) and lays out three tabs: GAME · AUDIO · TUNING.
    ///   2. Puts the GAME and AUDIO pages' rows into a vertical scroll view (Resume / Quit stay pinned at the bottom).
    ///   3. Builds the TUNING page from TuningRegistry: section chips, one scrolling list per section (rows with
    ///      [−] slider [+] and the value, LIVE / NEXT ROUND under the name), and Reset Section / Reset All / Restart Match.
    ///      Above that, the export bar: Name (tune_v1, tune_v2…) · Note · EXPORT JSON (a full snapshot, TuningExport).
    /// Safe to run again: the TUNING page is rebuilt; pages already in a scroll view are left as they are.
    /// Undo (Ctrl+Z) reverts the whole step. Save the scene afterwards.
    ///
    /// Place in: Assets/_Project/Editor/TuningTabSetup.cs
    /// </summary>
    public static class TuningTabSetup
    {
        static readonly Vector2 WindowSize = new Vector2(1500f, 900f);
        const float RowHeight = 96f;
        const float StepperSize = 88f;
        const float ValueWidth = 170f;
        const float LabelShare = 0.42f; // fraction of the row width for the name

        [MenuItem("Window/KungFu Crashout/GDD Build/Add Tuning Tab + Scrolling Pages (pause menu)", priority = 10055)]
        static void Run()
        {
            var options = Object.FindAnyObjectByType<OptionsMenu>(FindObjectsInactive.Include);
            if (!options)
            {
                EditorUtility.DisplayDialog("Tuning Tab", "No OptionsMenu in the open scene. Open FightScene first.", "OK");
                return;
            }
            var so = new SerializedObject(options);
            var gamePage = so.FindProperty("gamePage").objectReferenceValue as GameObject;
            var audioPage = so.FindProperty("audioPage").objectReferenceValue as GameObject;
            var gameTab = so.FindProperty("gameTab").objectReferenceValue as Button;
            var audioTab = so.FindProperty("audioTab").objectReferenceValue as Button;
            if (!gamePage || !audioPage || !gameTab || !audioTab)
            {
                EditorUtility.DisplayDialog("Tuning Tab", "The OptionsMenu is missing its GAME / AUDIO tabs or pages.", "OK");
                return;
            }

            var window = (RectTransform)gamePage.transform.parent;
            Undo.SetCurrentGroupName("Add Tuning Tab");
            int group = Undo.GetCurrentGroup();
            Undo.RegisterFullObjectHierarchyUndo(window.gameObject, "Add Tuning Tab");
            var log = new List<string>();

            // 1. Window and tabs
            if (window.sizeDelta.x < WindowSize.x || window.sizeDelta.y < WindowSize.y)
            {
                window.sizeDelta = WindowSize;
                log.Add($"window {WindowSize.x:0} × {WindowSize.y:0}");
            }
            PlaceTab(gameTab, -310f);
            PlaceTab(audioTab, 0f);
            var tuningTab = window.Find("Tuning Tab") ? window.Find("Tuning Tab").GetComponent<Button>() : null;
            if (!tuningTab)
            {
                tuningTab = Object.Instantiate(audioTab, window);
                tuningTab.name = "Tuning Tab";
                Undo.RegisterCreatedObjectUndo(tuningTab.gameObject, "Add Tuning Tab");
            }
            var tabLabel = tuningTab.GetComponentInChildren<TMP_Text>(true);
            if (tabLabel) tabLabel.text = "TUNING";
            PlaceTab(tuningTab, 310f);
            tuningTab.transform.SetSiblingIndex(audioTab.transform.GetSiblingIndex() + 1);

            // 2. Scroll the existing pages
            if (MakeScrollable(gamePage.transform)) log.Add("GAME page scrolls");
            if (MakeScrollable(audioPage.transform)) log.Add("AUDIO page scrolls");

            // 3. TUNING page
            var tuningPage = BuildTuningPage(window, (RectTransform)audioPage.transform, out int rows);
            log.Add($"TUNING page: {rows} values in {TuningRegistry.Sections.Length} sections");

            so.FindProperty("tuningTab").objectReferenceValue = tuningTab;
            so.FindProperty("tuningPage").objectReferenceValue = tuningPage;
            so.ApplyModifiedProperties();

            Undo.CollapseUndoOperations(group);
            EditorSceneManager.MarkSceneDirty(window.gameObject.scene);
            Debug.Log("[GDD Build] Pause menu: " + string.Join("; ", log) + ". Save the scene (Ctrl+S).");
        }

        static void PlaceTab(Button tab, float x)
        {
            var rt = (RectTransform)tab.transform;
            rt.anchoredPosition = new Vector2(x, rt.anchoredPosition.y);
        }

        // ───────────── Scroll view ─────────────

        /// <summary>A stretched scroll view with a masked viewport and a slim scrollbar on the right.</summary>
        static ScrollRect NewScrollView(Transform parent, out RectTransform viewport)
        {
            var sv = GddUi.New("Scroll View", parent);
            GddUi.Stretch(sv);
            Undo.RegisterCreatedObjectUndo(sv, "Add Tuning Tab");

            var vp = GddUi.New("Viewport", sv.transform);
            viewport = GddUi.Stretch(vp);
            viewport.offsetMax = new Vector2(-26f, 0f); // room for the scrollbar
            var vpImage = vp.AddComponent<Image>();
            vpImage.color = new Color(0f, 0f, 0f, 0f); // invisible, but catches drags in the gaps between rows
            vp.AddComponent<RectMask2D>();

            var barGo = DefaultControls.CreateScrollbar(GddUi.Res);
            barGo.name = "Scrollbar";
            barGo.transform.SetParent(sv.transform, false);
            var barRt = (RectTransform)barGo.transform;
            barRt.anchorMin = new Vector2(1f, 0f);
            barRt.anchorMax = new Vector2(1f, 1f);
            barRt.pivot = new Vector2(1f, 0.5f);
            barRt.anchoredPosition = Vector2.zero;
            barRt.sizeDelta = new Vector2(16f, 0f);
            var bar = barGo.GetComponent<Scrollbar>();
            bar.direction = Scrollbar.Direction.BottomToTop;
            barGo.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0.08f);
            if (bar.handleRect) bar.handleRect.GetComponent<Image>().color = new Color(1f, 0.78f, 0.2f, 0.85f);

            var scroll = sv.AddComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Elastic;
            scroll.elasticity = 0.08f;
            scroll.inertia = true;
            scroll.decelerationRate = 0.12f;
            scroll.scrollSensitivity = 60f;
            scroll.viewport = viewport;
            scroll.verticalScrollbar = bar;
            scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
            return scroll;
        }

        /// <summary>
        /// Moves a page's rows into a scroll view sized to fit them. Buttons named Resume / Quit stay outside, pinned to
        /// the bottom. False if the page already scrolls.
        /// </summary>
        static bool MakeScrollable(Transform page)
        {
            if (page.Find("Scroll View")) return false;

            var pinned = new List<Transform>();
            foreach (var n in new[] { "Resume", "Quit" })
            {
                var t = FindDeep(page, n);
                if (t) pinned.Add(t);
            }

            float height = Extent(page) + 30f;
            var children = new List<Transform>();
            foreach (Transform c in page) children.Add(c);

            var scroll = NewScrollView(page, out var viewport);
            var svRt = (RectTransform)scroll.transform;
            if (pinned.Count > 0) svRt.offsetMin = new Vector2(0f, 115f); // above Resume / Quit

            var content = GddUi.New("Content", viewport);
            var crt = (RectTransform)content.transform;
            crt.anchorMin = new Vector2(0f, 1f);
            crt.anchorMax = new Vector2(1f, 1f);
            crt.pivot = new Vector2(0.5f, 1f);
            crt.anchoredPosition = Vector2.zero;
            crt.sizeDelta = new Vector2(0f, height);
            scroll.content = crt;

            foreach (var c in children) Undo.SetTransformParent(c, crt, false, "Add Tuning Tab");
            foreach (var c in pinned)
            {
                Undo.SetTransformParent(c, page, false, "Add Tuning Tab");
                c.SetAsLastSibling();
            }
            return true;
        }

        /// <summary>How far below the top the page's top-anchored rows reach (looks inside stretched groups).</summary>
        static float Extent(Transform parent)
        {
            float bottom = 0f;
            foreach (Transform c in parent)
            {
                if (!c.gameObject.activeSelf || !(c is RectTransform rt)) continue;
                bool stretched = rt.anchorMin == Vector2.zero && rt.anchorMax == Vector2.one;
                if (stretched) { bottom = Mathf.Max(bottom, Extent(rt)); continue; }
                if (!Mathf.Approximately(rt.anchorMin.y, 1f) || !Mathf.Approximately(rt.anchorMax.y, 1f)) continue;
                bottom = Mathf.Max(bottom, -rt.anchoredPosition.y + rt.sizeDelta.y * rt.pivot.y);
            }
            return bottom;
        }

        static Transform FindDeep(Transform root, string name)
        {
            foreach (Transform c in root)
            {
                if (c.name == name) return c;
                var f = FindDeep(c, name);
                if (f) return f;
            }
            return null;
        }

        // ───────────── TUNING page ─────────────

        static GameObject BuildTuningPage(RectTransform window, RectTransform pageTemplate, out int rowCount)
        {
            var old = window.Find("Tuning Page");
            if (old) Undo.DestroyObjectImmediate(old.gameObject);

            var page = GddUi.New("Tuning Page", window);
            Undo.RegisterCreatedObjectUndo(page, "Add Tuning Tab");
            var prt = (RectTransform)page.transform;
            prt.anchorMin = pageTemplate.anchorMin;
            prt.anchorMax = pageTemplate.anchorMax;
            prt.pivot = pageTemplate.pivot;
            prt.anchoredPosition = pageTemplate.anchoredPosition;
            prt.sizeDelta = pageTemplate.sizeDelta;
            page.transform.SetSiblingIndex(pageTemplate.GetSiblingIndex() + 1);

            // Section chips
            var chips = GddUi.New("Sections", page.transform);
            GddUi.Place(chips, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), Vector2.zero, Vector2.zero);
            var chipsRt = (RectTransform)chips.transform;
            chipsRt.anchorMin = new Vector2(0f, 1f);
            chipsRt.anchorMax = new Vector2(1f, 1f);
            chipsRt.sizeDelta = new Vector2(0f, 72f);
            var hlg = chips.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = 10f;
            hlg.childControlWidth = hlg.childControlHeight = true;
            hlg.childForceExpandWidth = hlg.childForceExpandHeight = true;

            var sections = TuningRegistry.Sections;
            var buttons = new Button[sections.Length];
            for (int i = 0; i < sections.Length; i++)
                buttons[i] = GddUi.MakeButton(sections[i] + " Chip", chips.transform, sections[i].ToUpperInvariant(), 30f, GddUi.ButtonColour, Color.white);

            // List
            var scroll = NewScrollView(page.transform, out var viewport);
            var svRt = (RectTransform)scroll.transform;
            svRt.offsetMin = new Vector2(0f, 96f + ExportBarHeight + 10f); // above the export bar and the bottom bar
            svRt.offsetMax = new Vector2(0f, -86f);  // below the chips

            var contents = new RectTransform[sections.Length];
            rowCount = 0;
            for (int i = 0; i < sections.Length; i++)
            {
                var content = GddUi.New(sections[i] + " Content", viewport);
                var crt = (RectTransform)content.transform;
                crt.anchorMin = new Vector2(0f, 1f);
                crt.anchorMax = new Vector2(1f, 1f);
                crt.pivot = new Vector2(0.5f, 1f);
                crt.anchoredPosition = Vector2.zero;
                crt.sizeDelta = Vector2.zero;
                var vlg = content.AddComponent<VerticalLayoutGroup>();
                vlg.padding = new RectOffset(4, 4, 4, 16);
                vlg.spacing = 8f;
                vlg.childControlWidth = vlg.childControlHeight = true;
                vlg.childForceExpandWidth = true;
                vlg.childForceExpandHeight = false;
                var fitter = content.AddComponent<ContentSizeFitter>();
                fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
                contents[i] = crt;

                string lastGroup = null;
                foreach (var def in TuningRegistry.Defs)
                {
                    if (def.section != sections[i]) continue;
                    if (def.group != lastGroup && !string.IsNullOrEmpty(def.group)) Header(content.transform, def.group);
                    lastGroup = def.group;
                    Row(content.transform, def);
                    rowCount++;
                }
                content.SetActive(i == 0);
            }
            scroll.content = contents[0];

            // Bottom bar
            var bottom = GddUi.New("Bottom Bar", page.transform);
            var brt = (RectTransform)bottom.transform;
            brt.anchorMin = new Vector2(0f, 0f);
            brt.anchorMax = new Vector2(1f, 0f);
            brt.pivot = new Vector2(0.5f, 0f);
            brt.anchoredPosition = Vector2.zero;
            brt.sizeDelta = new Vector2(0f, 84f);
            var bar = bottom.AddComponent<HorizontalLayoutGroup>();
            bar.spacing = 14f;
            bar.childControlWidth = bar.childControlHeight = true;
            bar.childForceExpandWidth = false;
            bar.childForceExpandHeight = true;

            var footer = GddUi.Text("Footer", bottom.transform, "", 22f, TextAlignmentOptions.MidlineLeft);
            footer.enableAutoSizing = true;
            footer.fontSizeMin = 14f;
            footer.fontSizeMax = 22f;
            Flexible(footer.gameObject, 2.2f);
            var resetSection = GddUi.Secondary("Reset Section", bottom.transform, "RESET SECTION", 28f);
            Flexible(resetSection.gameObject, 1f);
            var resetAll = GddUi.Secondary("Reset All", bottom.transform, "RESET ALL", 28f);
            Flexible(resetAll.gameObject, 1f);
            var restart = GddUi.Secondary("Restart Match", bottom.transform, "RESTART MATCH", 28f);
            Flexible(restart.gameObject, 1f);

            // Export bar: Name · Note · EXPORT JSON
            var export = GddUi.New("Export Bar", page.transform);
            var ert = (RectTransform)export.transform;
            ert.anchorMin = new Vector2(0f, 0f);
            ert.anchorMax = new Vector2(1f, 0f);
            ert.pivot = new Vector2(0.5f, 0f);
            ert.anchoredPosition = new Vector2(0f, 94f);
            ert.sizeDelta = new Vector2(0f, ExportBarHeight);
            var ebar = export.AddComponent<HorizontalLayoutGroup>();
            ebar.spacing = 12f;
            ebar.childControlWidth = ebar.childControlHeight = true;
            ebar.childForceExpandWidth = false;
            ebar.childForceExpandHeight = true;

            var nameField = InputField("Name Field", export.transform, "Name (tune_v1…)", 32);
            Flexible(nameField.gameObject, 1.1f);
            var noteField = InputField("Note Field", export.transform, "Note: what changed and why (optional)", 140);
            Flexible(noteField.gameObject, 2.6f);
            var exportButton = GddUi.MakeButton("Export JSON", export.transform, "EXPORT JSON", 28f, GddUi.Gold, Color.black);
            Flexible(exportButton.gameObject, 1f);

            // Controller
            var panel = page.AddComponent<TuningPanel>();
            var pso = new SerializedObject(panel);
            GddUi.Set(pso, "scroll", scroll);
            GddUi.SetArray(pso, "sectionButtons", buttons);
            GddUi.SetArray(pso, "sectionContents", contents);
            GddUi.Set(pso, "resetSectionButton", resetSection);
            GddUi.Set(pso, "resetAllButton", resetAll);
            GddUi.Set(pso, "restartButton", restart);
            GddUi.Set(pso, "footer", footer);
            GddUi.Set(pso, "nameField", nameField);
            GddUi.Set(pso, "noteField", noteField);
            GddUi.Set(pso, "exportButton", exportButton);
            pso.ApplyModifiedPropertiesWithoutUndo();

            page.SetActive(false);
            return page;
        }

        const float ExportBarHeight = 76f;

        static TMP_InputField InputField(string name, Transform parent, string placeholder, int limit)
        {
            var res = GddUi.Res;
            var go = TMP_DefaultControls.CreateInputField(new TMP_DefaultControls.Resources
            {
                standard = res.standard, background = res.background, inputField = res.inputField,
                knob = res.knob, checkmark = res.checkmark, dropdown = res.dropdown, mask = res.mask,
            });
            go.name = name;
            go.transform.SetParent(parent, false);
            var field = go.GetComponent<TMP_InputField>();
            go.GetComponent<Image>().color = new Color(0.16f, 0.16f, 0.19f);
            field.characterLimit = limit;
            field.lineType = TMP_InputField.LineType.SingleLine;
            field.pointSize = 28f;
            if (field.textComponent) field.textComponent.color = Color.white;
            if (field.placeholder is TMP_Text ph)
            {
                ph.text = placeholder;
                ph.color = new Color(1f, 1f, 1f, 0.35f);
                ph.fontStyle = FontStyles.Italic;
            }
            var area = go.transform.Find("Text Area") as RectTransform;
            if (area)
            {
                area.offsetMin = new Vector2(16f, 6f);
                area.offsetMax = new Vector2(-16f, -6f);
            }
            return field;
        }

        [MenuItem("Window/KungFu Crashout/Tuning/Open Export Folder", priority = 10056)]
        static void OpenExportFolder()
        {
            System.IO.Directory.CreateDirectory(TuningExport.ExportFolder);
            EditorUtility.RevealInFinder(TuningExport.ExportFolder);
        }

        static void Flexible(GameObject go, float weight)
        {
            var le = go.AddComponent<LayoutElement>();
            le.flexibleWidth = weight;
            le.minWidth = 120f;
        }

        static void Header(Transform parent, string text)
        {
            var t = GddUi.Text(text + " Header", parent, text.ToUpperInvariant(), 30f, TextAlignmentOptions.BottomLeft, bold: true);
            t.color = GddUi.Gold;
            t.margin = new Vector4(12f, 0f, 0f, 4f);
            var le = t.gameObject.AddComponent<LayoutElement>();
            le.preferredHeight = 58f;
            le.minHeight = 58f;
        }

        static void Row(Transform parent, TuneDef def)
        {
            var row = GddUi.New(def.id, parent);
            GddUi.Img(row, new Color(1f, 1f, 1f, 0.05f), true, false);
            var le = row.AddComponent<LayoutElement>();
            le.preferredHeight = RowHeight;
            le.minHeight = RowHeight;

            // Name, with when it applies underneath
            string when = def.apply == TuneApply.Live
                ? "<color=#9AA0A6>LIVE</color>"
                : "<color=#FFC733>NEXT ROUND</color>";
            var label = GddUi.Text("Label", row.transform, $"{def.label}\n<size=21>{when}</size>", 30f, TextAlignmentOptions.MidlineLeft);
            label.enableAutoSizing = true;
            label.fontSizeMin = 20f;
            label.fontSizeMax = 30f;
            var lrt = (RectTransform)label.transform;
            lrt.anchorMin = new Vector2(0f, 0f);
            lrt.anchorMax = new Vector2(LabelShare, 1f);
            lrt.offsetMin = new Vector2(22f, 4f);
            lrt.offsetMax = new Vector2(-6f, -4f);

            // [−]
            var minus = GddUi.MakeButton("Minus", row.transform, "-", 52f, GddUi.ButtonColour, Color.white);
            GddUi.Place(minus.gameObject, new Vector2(LabelShare, 0.5f), new Vector2(0f, 0.5f), new Vector2(6f, 0f), new Vector2(StepperSize, 80f));
            var minusRepeat = minus.gameObject.AddComponent<RepeatButton>();

            // [+]
            var plus = GddUi.MakeButton("Plus", row.transform, "+", 52f, GddUi.ButtonColour, Color.white);
            GddUi.Place(plus.gameObject, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-(ValueWidth + 14f), 0f), new Vector2(StepperSize, 80f));
            var plusRepeat = plus.gameObject.AddComponent<RepeatButton>();

            // Slider between them
            var sliderGo = DefaultControls.CreateSlider(GddUi.Res);
            sliderGo.name = "Slider";
            sliderGo.transform.SetParent(row.transform, false);
            var srt = (RectTransform)sliderGo.transform;
            srt.anchorMin = new Vector2(LabelShare, 0.5f);
            srt.anchorMax = new Vector2(1f, 0.5f);
            srt.pivot = new Vector2(0.5f, 0.5f);
            srt.offsetMin = new Vector2(6f + StepperSize + 26f, -22f);
            srt.offsetMax = new Vector2(-(ValueWidth + 14f + StepperSize + 26f), 22f);
            StyleSlider(sliderGo);
            var slider = sliderGo.GetComponent<Slider>();
            slider.wholeNumbers = true;

            // Value
            var value = GddUi.Text("Value", row.transform, "—", 34f, TextAlignmentOptions.MidlineRight, bold: true);
            value.enableAutoSizing = true;
            value.fontSizeMin = 22f;
            value.fontSizeMax = 34f;
            GddUi.Place(value.gameObject, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-12f, 0f), new Vector2(ValueWidth, 80f));

            var comp = row.AddComponent<TuningRow>();
            var so = new SerializedObject(comp);
            so.FindProperty("paramId").stringValue = def.id;
            GddUi.Set(so, "label", label);
            GddUi.Set(so, "value", value);
            GddUi.Set(so, "slider", slider);
            GddUi.Set(so, "minus", minusRepeat);
            GddUi.Set(so, "plus", plusRepeat);
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        static void StyleSlider(GameObject slider)
        {
            var bg = slider.transform.Find("Background");
            if (bg) bg.GetComponent<Image>().color = new Color(0.2f, 0.2f, 0.24f);
            var fill = slider.transform.Find("Fill Area/Fill");
            if (fill) fill.GetComponent<Image>().color = GddUi.Gold;
            var area = slider.transform.Find("Handle Slide Area") as RectTransform;
            if (area) area.sizeDelta = new Vector2(-44f, 0f);
            var handle = slider.transform.Find("Handle Slide Area/Handle") as RectTransform;
            if (handle)
            {
                handle.sizeDelta = new Vector2(44f, 20f); // a thumb-sized knob
                handle.GetComponent<Image>().color = Color.white;
            }
        }
    }
}
