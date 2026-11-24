#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using KungFuCrashout.UI;

namespace KungFuCrashout.EditorTools
{
    /// <summary>
    /// Menu: Window → KungFu Crashout → GDD Build → Skin Options Menu + Add To Start Screen
    /// 1. Reskins the pause / options menu (FightScene) with the General UI kit (GamConcepts_UI_21 + Generic UI.txt) and the
    ///    project's other UI, keeping every page, value and behaviour as it is:
    ///      • Window → General Scroll Frame (9-sliced so the rollers keep their shape; 1500 × 1000, contents moved inside
    ///        the paper), dim behind it black 20 % (as the 1v1 notes), close → General Close icon.
    ///      • Tabs, difficulty buttons, tuning section chips → the Leaderboard tab button (selected = bright, others shaded),
    ///        dark brown Tillana SemiBold labels. Other buttons (Resume, Quit, − / +, Reset, Export…) → the brown
    ///        Leaderboard button with cream labels.
    ///      • Toggles → General checkbox + tick. Sliders → General slider frame (track), bar (fill) and marker (handle).
    ///      • Text → Aladin title, Tillana Medium ink on the paper. Row backings → faint brown bands; scrollbars brown.
    /// 2. Copies the reskinned menu into the Start screen, opened by the gear (Settings) icon. There it doesn't pause the
    ///    game, Back / Esc only closes it (Back still quits from the Start screen otherwise), and the match-only buttons
    ///    (Quit to menu, Restart Match) are hidden. Settings are shared (saved on the device), so anything changed on the
    ///    Start screen applies in the fight.
    /// Re-running is safe (the Start-screen copy is replaced). Saves both scenes.
    /// Place in: Assets/_Project/Editor/OptionsMenuSkinSetup.cs
    /// </summary>
    public static class OptionsMenuSkinSetup
    {
        const string Title = "Options Menu Skin";
        const string General = "Assets/_Project/Art/UI/General";
        const string LbArt = "Assets/_Project/Art/UI/Leaderboard";
        const string FontFolder = "Assets/_Project/Art/UI/Fonts";
        const string FightPath = "Assets/_Project/Scenes/FightScene.unity";
        const string StartPath = "Assets/_Project/Scenes/StartScreen.unity";
        const string CopyName = "Options Menu";

        static readonly Color Ink = Hex("401D0E");
        static readonly Color Cream = Hex("F0DEB0");
        static readonly Color TabInk = Hex("4E3B1D");
        static readonly Color Shaded = new Color(0.78f, 0.62f, 0.45f, 1f);   // unselected tab / chip / difficulty
        static readonly Color Band = new Color(0.25f, 0.13f, 0.05f, 0.10f);   // row backings on the paper

        static Sprite inputBox, frame, close, boxEmpty, tick, sliderFrame, sliderBar, marker, tabSprite, buttonSprite;
        static TMP_FontAsset aladin, tSemi, tMed;

        [MenuItem("Window/KungFu Crashout/GDD Build/Skin Options Menu + Add To Start Screen", priority = 10069)]
        public static void Run()
        {
            if (EditorApplication.isPlaying) { EditorUtility.DisplayDialog(Title, "Stop Play mode first.", "OK"); return; }
            if (!LoadAssets(out string missing)) { EditorUtility.DisplayDialog(Title, "Missing: " + missing, "OK"); return; }
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            var fight = EditorSceneManager.OpenScene(FightPath, OpenSceneMode.Single);
            var menu = Find<OptionsMenu>(fight);
            if (!menu) { EditorUtility.DisplayDialog(Title, "No OptionsMenu in FightScene.", "OK"); return; }
            Skin(menu);
            EditorSceneManager.MarkSceneDirty(fight);
            EditorSceneManager.SaveScene(fight);

            var start = EditorSceneManager.OpenScene(StartPath, OpenSceneMode.Additive);
            string note = CopyToStart(menu, start);
            EditorSceneManager.MarkSceneDirty(start);
            EditorSceneManager.SaveScene(start);
            EditorSceneManager.CloseScene(start, true);
            Debug.Log("[Options] Pause menu reskinned with the General UI kit (FightScene saved). " + note);
        }

        // ───────────── Assets ─────────────

        static bool LoadAssets(out string missing)
        {
            var miss = new List<string>();
            Sprite S(string folder, string n, Vector4 border, int max = 1024)
            {
                var sp = ImportSprite($"{folder}/{n}.png", max, border);
                if (!sp) miss.Add(n);
                return sp;
            }
            frame = S(General, "UI_General_Scroll_Frame", new Vector4(80, 70, 80, 70));
            close = S(General, "UI_General_Close_Icon", Vector4.zero, 256);
            boxEmpty = S(General, "UI_General_Checkbox_Empty", Vector4.zero, 128);
            tick = S(General, "UI_General_Checkbox_Tick", Vector4.zero, 128);
            sliderFrame = S(General, "UI_General_Slider_Frame", new Vector4(14, 0, 14, 0));
            sliderBar = S(General, "UI_General_Slider_Bar", new Vector4(12, 0, 12, 0));
            marker = S(General, "UI_General_Slider_Marker", Vector4.zero, 128);
            tabSprite = S(LbArt, "UI_Leaderboard_Button_Selected", new Vector4(24, 24, 24, 24), 512);
            buttonSprite = S(LbArt, "UI_Leaderboard_Button_Unselected", new Vector4(24, 24, 24, 24), 512);
            ImportSprite($"{General}/UI_General_Checkbox_Shade.png", 128, Vector4.zero);
            // Text input: the 1v1 message pop-up box (as Generic UI.txt suggests).
            inputBox = S("Assets/_Project/Art/UI/OnlineLobby", "UI_General_Message_Popup", new Vector4(30, 30, 30, 30));
            aladin = Font("Aladin-Regular");
            tSemi = Font("Tillana-SemiBold");
            tMed = Font("Tillana-Medium");
            if (!aladin) miss.Add("Aladin-Regular SDF");
            if (!tSemi) miss.Add("Tillana-SemiBold SDF");
            if (!tMed) miss.Add("Tillana-Medium SDF");
            missing = string.Join(", ", miss);
            return miss.Count == 0;
        }

        // ───────────── Skin ─────────────

        static void Skin(OptionsMenu menu)
        {
            var root = menu.transform;
            var so = new SerializedObject(menu);
            var panel = so.FindProperty("panel").objectReferenceValue as GameObject;
            var closeBtn = so.FindProperty("closeButton").objectReferenceValue as Button;
            var tabs = new[] { "gameTab", "audioTab", "tuningTab" }
                .Select(n => so.FindProperty(n).objectReferenceValue as Button).Where(b => b).ToList();
            so.FindProperty("tabSelectedColour").colorValue = Color.white;
            so.FindProperty("tabNormalColour").colorValue = Shaded;
            so.ApplyModifiedPropertiesWithoutUndo();

            // Buttons coloured by their scripts: tabs, difficulty, tuning sections.
            var stateButtons = new HashSet<Button>(tabs);
            foreach (var ai in root.GetComponentsInChildren<AISettingsPanel>(true))
            {
                var aso = new SerializedObject(ai);
                var arr = aso.FindProperty("difficultyButtons");
                for (int i = 0; i < arr.arraySize; i++)
                    if (arr.GetArrayElementAtIndex(i).objectReferenceValue is Button b) stateButtons.Add(b);
                aso.FindProperty("selected").colorValue = Color.white;
                aso.FindProperty("normal").colorValue = Shaded;
                aso.ApplyModifiedPropertiesWithoutUndo();
            }
            foreach (var tp in root.GetComponentsInChildren<TuningPanel>(true))
            {
                var tso = new SerializedObject(tp);
                var arr = tso.FindProperty("sectionButtons");
                for (int i = 0; i < arr.arraySize; i++)
                    if (arr.GetArrayElementAtIndex(i).objectReferenceValue is Button b) stateButtons.Add(b);
                tso.FindProperty("selected").colorValue = Color.white;
                tso.FindProperty("normal").colorValue = Shaded;
                tso.FindProperty("selectedText").colorValue = TabInk;
                tso.FindProperty("normalText").colorValue = TabInk;
                tso.ApplyModifiedPropertiesWithoutUndo();
            }
            foreach (var row in root.GetComponentsInChildren<TuningRow>(true))
            {
                var rso = new SerializedObject(row);
                rso.FindProperty("normalColour").colorValue = Ink;
                rso.FindProperty("changedColour").colorValue = new Color(0.72f, 0.22f, 0.05f);
                rso.FindProperty("unusedColour").colorValue = new Color(Ink.r, Ink.g, Ink.b, 0.35f);
                rso.ApplyModifiedPropertiesWithoutUndo();
            }

            // ── Dim + window ──
            if (panel && panel.TryGetComponent(out Image dim)) dim.color = new Color(0f, 0f, 0f, 0.2f);
            var window = panel ? panel.transform.Find("Window") as RectTransform : null;
            if (window)
            {
                var wi = window.GetComponent<Image>();
                if (wi) { wi.sprite = frame; wi.type = Image.Type.Sliced; wi.color = Color.white; wi.pixelsPerUnitMultiplier = 1f; }
                window.sizeDelta = new Vector2(1500f, 1000f);
                // Contents inside the paper (rollers top / bottom 70, rods 80 each side).
                foreach (Transform c in window)
                {
                    var rt = (RectTransform)c;
                    if (c.name == "Title") rt.anchoredPosition = new Vector2(0f, -108f);
                    else if (c.name.EndsWith(" Page")) { rt.anchoredPosition = new Vector2(0f, -90f); rt.sizeDelta = new Vector2(-160f, -340f); }
                    else if (c.GetComponent<Button>() && tabs.Contains(c.GetComponent<Button>()))
                    {
                        rt.anchoredPosition = new Vector2(rt.anchoredPosition.x, -200f);
                        rt.sizeDelta = new Vector2(290f, 98f);
                    }
                }
            }
            if (closeBtn)
            {
                var rt = (RectTransform)closeBtn.transform;
                rt.anchorMin = rt.anchorMax = new Vector2(1f, 1f);
                rt.anchoredPosition = new Vector2(-120f, -110f);
                rt.sizeDelta = new Vector2(104f, 104f);
                if (closeBtn.targetGraphic is Image ci) { ci.sprite = close; ci.type = Image.Type.Simple; ci.color = Color.white; ci.preserveAspect = true; }
                foreach (var t in closeBtn.GetComponentsInChildren<TMP_Text>(true)) t.gameObject.SetActive(false);
                closeBtn.transition = Selectable.Transition.ColorTint;
            }

            // ── Text ──
            foreach (var t in root.GetComponentsInChildren<TMP_Text>(true))
            {
                if (window && t.transform.parent == window && t.name == "Title")
                {
                    t.font = aladin; t.fontSize = 70f; t.color = Ink; t.fontStyle = FontStyles.Normal;
                    continue;
                }
                var btn = t.GetComponentInParent<Button>(true);
                if (btn && btn != closeBtn)
                {
                    t.font = tSemi;
                    t.color = stateButtons.Contains(btn) ? TabInk : Cream;
                    continue;
                }
                t.font = tMed;
                t.color = Ink;
            }

            // ── Toggles ──
            foreach (var tg in root.GetComponentsInChildren<Toggle>(true))
            {
                if (tg.targetGraphic is Image bg && bg.gameObject != tg.gameObject)
                { bg.sprite = boxEmpty; bg.type = Image.Type.Simple; bg.color = Color.white; bg.preserveAspect = true; }
                if (tg.graphic is Image ck)
                {
                    ck.sprite = tick; ck.type = Image.Type.Simple; ck.color = Color.white; ck.preserveAspect = true;
                    var rt = ck.rectTransform;
                    rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
                    rt.sizeDelta = new Vector2(8f, 0f);
                    rt.anchoredPosition = new Vector2(4f, 4f);  // the tick pokes out of the box, as in the concept
                }
                if (tg.TryGetComponent(out Image own) && own != tg.targetGraphic) own.color = new Color(1f, 1f, 1f, 0f);
            }

            // ── Sliders ──
            foreach (var s in root.GetComponentsInChildren<Slider>(true))
            {
                if (s.GetComponentInParent<Scrollbar>(true)) continue;
                var back = s.transform.Find("Background")?.GetComponent<Image>();
                if (back) { back.sprite = sliderFrame; back.type = Image.Type.Sliced; back.color = Color.white; }
                if (s.fillRect && s.fillRect.TryGetComponent(out Image fill)) { fill.sprite = sliderBar; fill.type = Image.Type.Sliced; fill.color = Color.white; }
                if (s.handleRect && s.handleRect.TryGetComponent(out Image h))
                {
                    h.sprite = marker; h.type = Image.Type.Simple; h.color = Color.white; h.preserveAspect = true;
                    float sh = ((RectTransform)s.transform).rect.height;
                    if (sh <= 1f) sh = ((RectTransform)s.transform).sizeDelta.y;
                    float hh = Mathf.Max(30f, sh * 1.3f), hw = hh * 29f / 61f;
                    var rt = s.handleRect;
                    rt.anchorMin = new Vector2(rt.anchorMin.x, 0f); rt.anchorMax = new Vector2(rt.anchorMax.x, 1f);
                    rt.sizeDelta = new Vector2(hw, hh - sh);
                }
            }

            // ── Scrollbars ──
            foreach (var sb in root.GetComponentsInChildren<Scrollbar>(true))
            {
                if (sb.TryGetComponent(out Image track)) { track.sprite = null; track.color = new Color(0.25f, 0.13f, 0.05f, 0.15f); }
                if (sb.handleRect && sb.handleRect.TryGetComponent(out Image h)) { h.sprite = null; h.color = new Color(0.35f, 0.2f, 0.1f, 0.85f); }
            }

            // ── Buttons ──
            foreach (var b in root.GetComponentsInChildren<Button>(true))
            {
                if (b == closeBtn || !(b.targetGraphic is Image img)) continue;
                if (b.GetComponentInParent<Scrollbar>(true)) continue;
                img.sprite = stateButtons.Contains(b) ? tabSprite : buttonSprite;
                img.type = Image.Type.Sliced;
                img.pixelsPerUnitMultiplier = 1.5f;     // thinner borders on the small buttons
                if (!stateButtons.Contains(b)) img.color = Color.white;
                var c = b.colors;
                c.normalColor = Color.white; c.highlightedColor = Color.white; c.selectedColor = Color.white;
                c.pressedColor = new Color(0.8f, 0.8f, 0.8f); c.disabledColor = new Color(1f, 1f, 1f, 0.5f);
                b.colors = c;
            }

            // ── Text inputs (export name / note) ──
            foreach (var f in root.GetComponentsInChildren<TMP_InputField>(true))
            {
                if (f.targetGraphic is Image box)
                { box.sprite = inputBox; box.type = Image.Type.Sliced; box.pixelsPerUnitMultiplier = 1.5f; box.color = Color.white; }
                if (f.textComponent) { f.textComponent.font = tMed; f.textComponent.color = Cream; }
                if (f.placeholder is TMP_Text ph) { ph.font = tMed; ph.color = new Color(Cream.r, Cream.g, Cream.b, 0.55f); }
            }

            // ── Plain backings (rows, viewports) ──
            foreach (var img in root.GetComponentsInChildren<Image>(true))
            {
                if (panel && img.gameObject == panel) continue;
                if (window && img.transform == window) continue;
                if (img.sprite && AssetDatabase.GetAssetPath(img.sprite).StartsWith("Assets/")) continue; // skinned above (built-in Unity sprites are replaced)
                img.sprite = null;
                if (img.GetComponentInParent<Scrollbar>(true)) continue;
                if (img.GetComponent<Button>() || img.GetComponent<Toggle>()) continue;
                bool viewport = img.GetComponent<RectMask2D>() || img.GetComponent<Mask>();
                if (img.GetComponent<Mask>()) continue;                      // a stencil mask needs its graphic
                img.color = viewport ? new Color(1f, 1f, 1f, 0f) : Band;
            }
        }

        // ───────────── Start screen copy ─────────────

        static string CopyToStart(OptionsMenu source, Scene start)
        {
            Canvas canvas = null;
            Button gear = null;
            foreach (var r in start.GetRootGameObjects())
            {
                if (!canvas) { var c = r.GetComponentInChildren<Canvas>(true); if (c && c.isRootCanvas) canvas = c; }
                var screen = r.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "Start Screen (Concept 29)");
                if (screen && !gear) gear = screen.GetComponentsInChildren<Button>(true).FirstOrDefault(b => b.name == "Settings");
            }
            if (!canvas) return "Start screen has no Canvas: menu not copied.";
            var old = canvas.transform.Find(CopyName);
            if (old) Object.DestroyImmediate(old.gameObject);

            var copy = Object.Instantiate(source.gameObject);
            copy.name = CopyName;
            SceneManager.MoveGameObjectToScene(copy, start);
            copy.transform.SetParent(canvas.transform, false);
            copy.transform.SetAsLastSibling();

            // References to FightScene objects (HUD button, CPU, item director…) can't cross scenes: clear them.
            foreach (var comp in copy.GetComponentsInChildren<Component>(true))
            {
                if (!comp) continue;
                var cso = new SerializedObject(comp);
                var it = cso.GetIterator();
                bool changed = false;
                while (it.Next(true))
                {
                    if (it.propertyType != SerializedPropertyType.ObjectReference) continue;
                    var o = it.objectReferenceValue;
                    GameObject go = o is GameObject g ? g : o is Component c ? c.gameObject : null;
                    if (go && go.scene.IsValid() && go.scene != start) { it.objectReferenceValue = null; changed = true; }
                }
                if (changed) cso.ApplyModifiedPropertiesWithoutUndo();
            }

            var menu = copy.GetComponent<OptionsMenu>();
            var so = new SerializedObject(menu);
            so.FindProperty("optionsButton").objectReferenceValue = gear;
            so.FindProperty("alsoOpenedBy").arraySize = 0;
            so.FindProperty("pauseWhileOpen").boolValue = false;
            so.FindProperty("escapeOpens").boolValue = false;
            so.ApplyModifiedPropertiesWithoutUndo();

            // Match-only buttons: Quit to menu (AI page) and Restart Match (tuning) make no sense on the Start screen.
            var hidden = new List<string>();
            foreach (var ai in copy.GetComponentsInChildren<AISettingsPanel>(true))
            {
                var q = new SerializedObject(ai).FindProperty("quitButton").objectReferenceValue as Button;
                if (q) { q.gameObject.SetActive(false); hidden.Add(q.name); }
            }
            foreach (var tp in copy.GetComponentsInChildren<TuningPanel>(true))
            {
                var r = new SerializedObject(tp).FindProperty("restartButton").objectReferenceValue as Button;
                if (r) { r.gameObject.SetActive(false); hidden.Add(r.name); }
            }
            return (gear ? "Start screen gear → Options (copy added; " : "Copy added to the Start screen but no Settings icon found; ") +
                   $"no pause, Back only closes it; hidden there: {(hidden.Count > 0 ? string.Join(", ", hidden) : "none")}). StartScreen saved.";
        }

        // ───────────── Helpers ─────────────

        static T Find<T>(Scene s) where T : Component
        {
            foreach (var r in s.GetRootGameObjects())
            {
                var c = r.GetComponentInChildren<T>(true);
                if (c) return c;
            }
            return null;
        }

        static Sprite ImportSprite(string path, int max, Vector4 border)
        {
            var ti = AssetImporter.GetAtPath(path) as TextureImporter;
            if (!ti) return null;
            ti.textureType = TextureImporterType.Sprite;
            ti.spriteImportMode = SpriteImportMode.Single;
            ti.spritePixelsPerUnit = 100f;
            ti.mipmapEnabled = false;
            ti.alphaIsTransparency = true;
            ti.wrapMode = TextureWrapMode.Clamp;
            ti.filterMode = FilterMode.Bilinear;
            ti.textureCompression = TextureImporterCompression.CompressedHQ;
            ti.maxTextureSize = max;
            var s = new TextureImporterSettings();
            ti.ReadTextureSettings(s);
            s.spriteMeshType = SpriteMeshType.FullRect;
            ti.SetTextureSettings(s);
            if (border != Vector4.zero) ti.spriteBorder = border;
            ti.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }

        static TMP_FontAsset Font(string file) => AssetDatabase.LoadAssetAtPath<TMP_FontAsset>($"{FontFolder}/{file} SDF.asset");

        static Color Hex(string hex)
        {
            ColorUtility.TryParseHtmlString("#" + hex, out var c);
            return c;
        }
    }
}
#endif
