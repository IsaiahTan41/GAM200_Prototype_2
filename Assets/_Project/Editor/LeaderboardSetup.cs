#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;
using KungFuCrashout.UI;

namespace KungFuCrashout.EditorTools
{
    /// <summary>
    /// Menu: Window → KungFu Crashout → GDD Build → Build Leaderboard Screen (Concept 19)
    /// Builds the Leaderboard scene from GamConcepts_UI_19 + Leaderboard UI.txt, the same way as the Start / Roster /
    /// Level Select screens, then hooks the Start screen's trophy to it:
    ///   • Art/UI/Leaderboard imported as UI sprites. Background = the Start screen mountains filling the whole screen,
    ///     under a black dim that is darker at the top (sampled from the concept: 66 % → 25 %).
    ///   • Concept layout at 2424 × 1080, every piece at its concept pixel position and size, in StageFit layers inside
    ///     the safe area (scaled uniformly to fit any phone):
    ///       Stage (top)  — hangs from the top: back lantern, "Leaderboard" title, This Phone / Online tabs + glow.
    ///       Stage (main) — centred: ranking header, scrolling ranking list (5 rows visible), the more-rankings glow,
    ///                      the pinned user row, and the Your Stats scroll.
    ///   • Fonts as the txt: Leaderboard Tillana Bold 43, header Barlow Medium 34, list Tillana Bold 40, tabs Tillana
    ///     SemiBold 40, Your Stats Aladin 60, stat details Tillana Medium 40.
    ///   • LeaderboardMenu runs it: static list, personal stats from PlayerStats, tabs, back → StartScreen.
    ///   • Start screen: the trophy now loads the Leaderboard scene (its old popup is unhooked, kept hidden), and the
    ///     Leaderboard scene is added to the build list.
    /// Title border: UI_OnlineLobby_Title_Frame (Art/UI/OnlineLobby) — the plank drawn behind "Leaderboard" in the concept.
    /// Re-running rebuilds the screen. Saves both scenes.
    /// Place in: Assets/_Project/Editor/LeaderboardSetup.cs
    /// </summary>
    public static class LeaderboardSetup
    {
        const string Title = "Leaderboard Screen";
        const string Art = "Assets/_Project/Art/UI/Leaderboard";
        const string StartArt = "Assets/_Project/Art/UI/StartScreen";
        const string CsArt = "Assets/_Project/Art/UI/CharacterSelect";
        const string FontFolder = "Assets/_Project/Art/UI/Fonts";
        const string RootName = "Leaderboard (Concept 19)";
        const string ScenePath = "Assets/_Project/Scenes/Leaderboard.unity";
        const string StartPath = "Assets/_Project/Scenes/StartScreen.unity";
        const float W = 2424f, H = 1080f, RowH = 103f;

        static readonly Color TitleText = Hex("EBD997");
        static readonly Color HeaderText = Hex("F4E8CE");
        static readonly Color OtherText = Hex("C8BAA0");
        static readonly Color TabText = Hex("4E3B1D");
        static readonly Color StatText = Hex("DDCEBB");

        // Column centres / left edge inside a 1466-wide row (concept px from the row's left edge).
        const float ColRank = 96f, ColName = 173f, ColW = 949f, ColL = 1143f, ColRate = 1336f;

        [MenuItem("Window/KungFu Crashout/GDD Build/Build Leaderboard Screen (Concept 19)", priority = 10066)]
        public static void Build()
        {
            if (EditorApplication.isPlaying) { EditorUtility.DisplayDialog(Title, "Stop Play mode first.", "OK"); return; }
            if (!System.IO.File.Exists(ScenePath)) { EditorUtility.DisplayDialog(Title, "No scene at " + ScenePath, "OK"); return; }
            var tillanaBold = Font("Tillana-Bold");
            var tillanaSemi = Font("Tillana-SemiBold") ?? tillanaBold;
            var tillanaMed = Font("Tillana-Medium") ?? tillanaBold;
            var barlow = Font("Barlow-Medium") ?? tillanaBold;
            var aladin = Font("Aladin-Regular") ?? tillanaBold;
            if (!tillanaBold) { EditorUtility.DisplayDialog(Title, "Tillana-Bold SDF not found in Art/UI/Fonts.", "OK"); return; }
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

            var missing = new List<string>();
            Sprite S(string folder, string n, int max = 2048)
            {
                var sp = ImportSprite($"{folder}/{n}.png", max);
                if (!sp) missing.Add(n);
                return sp;
            }
            var bgSprite = S(StartArt, "UI_StartScreen_Background", 4096);
            var back = S(CsArt, "UI_CharacterSelect_Back", 256);
            var tabSel = S(Art, "UI_Leaderboard_Button_Selected", 512);
            var tabUnsel = S(Art, "UI_Leaderboard_Button_Unselected", 512);
            var tabGlowSp = S(Art, "UI_Leaderboard_Button_Selected_Glow", 1024);
            var headerSp = S(Art, "UI_Leaderboard_Ranking_Title");
            var first = S(Art, "UI_Leaderboard_Ranking_First_Border");
            var second = S(Art, "UI_Leaderboard_Ranking_Second_Border");
            var third = S(Art, "UI_Leaderboard_Ranking_Third_Border");
            var lightB = S(Art, "UI_Leaderboard_Ranking_Light_Border");
            var darkB = S(Art, "UI_Leaderboard_Ranking_Dark_Border");
            var userB = S(Art, "UI_Leaderboard_User_Border");
            var icon1 = S(Art, "UI_Leaderboard_Ranking_First_Icon", 256);
            var icon2 = S(Art, "UI_Leaderboard_Ranking_Second_Icon", 256);
            var icon3 = S(Art, "UI_Leaderboard_Ranking_Third_Icon", 256);
            var gradient = S(Art, "UI_Leaderboard_Ranking_Gradient");
            var statScroll = S(Art, "UI_Leaderboard_Stat_Scroll", 1024);

            var canvas = EnsureCanvas(scene);
            EnsureEventSystem(scene);
            var old = canvas.transform.Find(RootName);
            if (old) Object.DestroyImmediate(old.gameObject);
            var root = Stretch(NewRect(RootName, canvas.transform));
            var menu = root.gameObject.AddComponent<LeaderboardMenu>();

            // ── Background + dim (darker at the top, as the concept) ──
            var bg = NewImage("Background (Start screen mountains)", root, bgSprite);
            Stretch(bg.rectTransform);
            var fitter = bg.gameObject.AddComponent<AspectRatioFitter>();
            fitter.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
            fitter.aspectRatio = W / H;
            bg.raycastTarget = true;
            var dim = NewImage("Dim", root, null);
            dim.enabled = true;
            dim.color = Color.white;
            Stretch(dim.rectTransform);
            dim.gameObject.AddComponent<UiVerticalGradient>();

            var safe = Stretch(NewRect("Safe Area", root));
            safe.gameObject.AddComponent<SafeAreaFitter>();
            var main = NewStage("Stage (main)", safe, new Vector2(0.5f, 0.5f));
            var top = NewStage("Stage (top)", safe, new Vector2(0.5f, 1f));

            // ── Ranking header ──
            var header = Img("Ranking Title", main, headerSp, 85, 266, 1466, 76);
            HeaderLabel(header.rectTransform, "#", barlow, ColRank, TextAlignmentOptions.Midline);
            HeaderLabel(header.rectTransform, "Player", barlow, ColName, TextAlignmentOptions.MidlineLeft);
            HeaderLabel(header.rectTransform, "W", barlow, ColW, TextAlignmentOptions.Midline);
            HeaderLabel(header.rectTransform, "L", barlow, ColL, TextAlignmentOptions.Midline);
            HeaderLabel(header.rectTransform, "Win%", barlow, ColRate, TextAlignmentOptions.Midline);

            // ── Scrolling ranking list: 5 rows visible (342 → 857) ──
            var viewportImg = Img("Rankings (scroll)", main, null, 85, 342, 1466, 5 * RowH);
            viewportImg.enabled = true;
            viewportImg.color = new Color(1f, 1f, 1f, 0f);   // invisible, catches drags
            viewportImg.raycastTarget = true;
            var viewport = viewportImg.rectTransform;
            viewport.gameObject.AddComponent<RectMask2D>();
            var content = NewRect("Content", viewport);
            content.anchorMin = new Vector2(0f, 1f); content.anchorMax = new Vector2(1f, 1f);
            content.pivot = new Vector2(0.5f, 1f);
            content.anchoredPosition = Vector2.zero;
            content.sizeDelta = new Vector2(0f, 5 * RowH);
            var sr = viewport.gameObject.AddComponent<ScrollRect>();
            sr.horizontal = false; sr.vertical = true;
            sr.viewport = viewport; sr.content = content;
            sr.movementType = ScrollRect.MovementType.Elastic;
            sr.scrollSensitivity = 30f;
            var template = MakeRow("Row Template", content, lightB, icon1, tillanaBold, 0f);
            template.root.gameObject.SetActive(false);

            // Glow above the rankings, between the second-last ranking and the user row.
            var glow = Img("More Rankings Glow", main, gradient, 86, 752, 1465, 164);

            // Pinned user row.
            var user = MakeRow("You", main, userB, null, tillanaBold, 857f);

            // ── Your Stats ──
            Img("Stat Scroll", main, statScroll, 1600, 247, 792, 727);
            TextAt("Your Stats", main, aladin, 60, StatText, TextAlignmentOptions.MidlineLeft, 1705, 362, 500).text = "Your Stats";
            string[] labels = { "Wins", "Losses", "Win rate", "Best streak", "Most played" };
            float[] ys = { 472, 567, 661, 754, 849 };
            var values = new TMP_Text[labels.Length];
            for (int i = 0; i < labels.Length; i++)
            {
                TextAt(labels[i], main, tillanaMed, 40, StatText, TextAlignmentOptions.MidlineLeft, 1703, ys[i], 330).text = labels[i];
                var v = TextAt(labels[i] + " Value", main, tillanaMed, 40, StatText, TextAlignmentOptions.MidlineRight, 2293 - 300, ys[i], 300);
                v.text = "-";
                values[i] = v;
            }

            // ── Top: back, title, tabs ──
            var backBtn = MakeButton(Img("Back", top, back, 35, 0, 132, 218));
            backBtn.GetComponent<Image>().raycastPadding = new Vector4(-10f, -10f, -10f, -10f);
            // Title border: the plank from the Online Lobby art (the same one, same place, in concepts 17 and 19).
            var border = Img("Title Border", top, S("Assets/_Project/Art/UI/OnlineLobby", "UI_OnlineLobby_Title_Frame", 1024), 128, 39, 524, 82);
            border.transform.SetSiblingIndex(0);
            TextAt("Title", top, tillanaBold, 43, TitleText, TextAlignmentOptions.Midline, 250, 80, 300).text = "Leaderboard";
            var tabGlow = Img("Tab Glow", top, tabGlowSp, 1915 - 349.5f, 100 - 127, 699, 254);
            var phone = Tab("This Phone", top, tabSel, tillanaSemi, 1770);
            var online = Tab("Online", top, tabUnsel, tillanaSemi, 2081);

            // ── Wire ──
            var so = new SerializedObject(menu);
            so.FindProperty("scroll").objectReferenceValue = sr;
            so.FindProperty("content").objectReferenceValue = content;
            WireRow(so.FindProperty("template"), template);
            WireRow(so.FindProperty("userRow"), user);
            so.FindProperty("moreGlow").objectReferenceValue = glow;
            so.FindProperty("firstBorder").objectReferenceValue = first;
            so.FindProperty("secondBorder").objectReferenceValue = second;
            so.FindProperty("thirdBorder").objectReferenceValue = third;
            so.FindProperty("lightBorder").objectReferenceValue = lightB;
            so.FindProperty("darkBorder").objectReferenceValue = darkB;
            so.FindProperty("firstIcon").objectReferenceValue = icon1;
            so.FindProperty("secondIcon").objectReferenceValue = icon2;
            so.FindProperty("thirdIcon").objectReferenceValue = icon3;
            so.FindProperty("winsValue").objectReferenceValue = values[0];
            so.FindProperty("lossesValue").objectReferenceValue = values[1];
            so.FindProperty("winRateValue").objectReferenceValue = values[2];
            so.FindProperty("bestStreakValue").objectReferenceValue = values[3];
            so.FindProperty("mostPlayedValue").objectReferenceValue = values[4];
            so.FindProperty("thisPhoneButton").objectReferenceValue = phone;
            so.FindProperty("onlineButton").objectReferenceValue = online;
            so.FindProperty("tabSelected").objectReferenceValue = tabSel;
            so.FindProperty("tabUnselected").objectReferenceValue = tabUnsel;
            so.FindProperty("tabGlow").objectReferenceValue = tabGlow.rectTransform;
            so.FindProperty("backButton").objectReferenceValue = backBtn;
            so.ApplyModifiedPropertiesWithoutUndo();

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);

            string startNote = HookTrophy(scene, out string musicNote);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AddToBuild();

            Debug.Log("[Leaderboard] Concept 19 built and saved. " + startNote + " " + musicNote +
                      " Leaderboard added to the build list." +
                      (missing.Count > 0 ? $" Missing art, left blank: {string.Join(", ", missing)}." : ""));
        }

        // ───────────── Start screen trophy ─────────────

        static string HookTrophy(UnityEngine.SceneManagement.Scene board, out string musicNote)
        {
            musicNote = "";
            if (!System.IO.File.Exists(StartPath)) return "No StartScreen scene; trophy not hooked.";
            var start = EditorSceneManager.OpenScene(StartPath, OpenSceneMode.Additive);
            string note = "Trophy button not found in StartScreen.";
            musicNote = CopyMusic(start, board);
            foreach (var r in start.GetRootGameObjects())
            {
                var screen = r.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "Start Screen (Concept 29)");
                if (!screen) continue;
                var trophy = screen.GetComponentsInChildren<Button>(true).FirstOrDefault(b => b.name == "Leaderboard");
                if (!trophy) break;
                // The old popup no longer opens from the trophy (kept, hidden).
                foreach (var rp in screen.GetComponentsInChildren<RecordPanel>(true))
                {
                    var rso = new SerializedObject(rp);
                    if (rso.FindProperty("openButton").objectReferenceValue == trophy)
                    {
                        rso.FindProperty("openButton").objectReferenceValue = null;
                        rso.ApplyModifiedPropertiesWithoutUndo();
                    }
                }
                var sb = trophy.GetComponent<SceneButton>() ?? trophy.gameObject.AddComponent<SceneButton>();
                var sso = new SerializedObject(sb);
                sso.FindProperty("sceneName").stringValue = "Leaderboard";
                sso.ApplyModifiedPropertiesWithoutUndo();
                note = "Start screen trophy → Leaderboard scene (old popup unhooked).";
                break;
            }
            EditorSceneManager.MarkSceneDirty(start);
            EditorSceneManager.SaveScene(start);
            EditorSceneManager.CloseScene(start, true);
            return note;
        }

        /// <summary>
        /// The Start screen's music player, copied into the Leaderboard scene (same track, persistent): coming from the
        /// Start screen the music just carries on (same track = no restart); opened on its own, it starts the same music.
        /// </summary>
        static string CopyMusic(UnityEngine.SceneManagement.Scene start, UnityEngine.SceneManagement.Scene board)
        {
            KungFuCrashout.Sound.MusicPlayer src = null, dst = null;
            foreach (var r in start.GetRootGameObjects()) if (!src) src = r.GetComponentInChildren<KungFuCrashout.Sound.MusicPlayer>(true);
            if (!src) return "No music player in StartScreen to copy.";
            foreach (var r in board.GetRootGameObjects()) if (!dst) dst = r.GetComponentInChildren<KungFuCrashout.Sound.MusicPlayer>(true);
            if (!dst)
            {
                var go = new GameObject("Music");
                EditorSceneManager.MoveGameObjectToScene(go, board);
                dst = go.AddComponent<KungFuCrashout.Sound.MusicPlayer>();
            }
            EditorUtility.CopySerialized(src, dst);
            var so = new SerializedObject(dst);
            so.FindProperty("persistAcrossScenes").boolValue = true;
            so.ApplyModifiedPropertiesWithoutUndo();
            return "Start screen music carries over (Music copied into Leaderboard).";
        }

        static void AddToBuild()
        {
            var list = EditorBuildSettings.scenes.ToList();
            if (list.Any(s => s.path == ScenePath)) return;
            list.Add(new EditorBuildSettingsScene(ScenePath, true));
            EditorBuildSettings.scenes = list.ToArray();
        }

        // ───────────── Pieces ─────────────

        struct RowParts { public RectTransform root; public Image border, icon; public TMP_Text rank, name, w, l, rate; }

        static RowParts MakeRow(string name, RectTransform parent, Sprite border, Sprite icon, TMP_FontAsset font, float y)
        {
            var p = new RowParts();
            p.root = NewRect(name, parent);
            p.root.anchorMin = p.root.anchorMax = new Vector2(0f, 1f);
            p.root.pivot = new Vector2(0f, 1f);
            p.root.sizeDelta = new Vector2(1466f, RowH);
            p.root.anchoredPosition = parent.name.StartsWith("Stage") ? new Vector2(85f, -y) : Vector2.zero;
            p.border = NewImage("Border", p.root, border);
            Stretch(p.border.rectTransform);
            p.icon = Img("Icon", p.root, icon, 38, 7, 124, 89);
            p.icon.enabled = false;
            p.rank = RowText(p.root, "Rank", font, ColRank, TextAlignmentOptions.Midline);
            p.name = RowText(p.root, "Name", font, ColName, TextAlignmentOptions.MidlineLeft);
            p.w = RowText(p.root, "W", font, ColW, TextAlignmentOptions.Midline);
            p.l = RowText(p.root, "L", font, ColL, TextAlignmentOptions.Midline);
            p.rate = RowText(p.root, "Win%", font, ColRate, TextAlignmentOptions.Midline);
            return p;
        }

        static TMP_Text RowText(RectTransform row, string name, TMP_FontAsset font, float x, TextAlignmentOptions align)
        {
            bool left = align == TextAlignmentOptions.MidlineLeft;
            var t = Txt(name, row, font, 40f, OtherText, align);
            var rt = t.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(left ? 0f : 0.5f, 0.5f);
            rt.sizeDelta = new Vector2(left ? 640f : 180f, RowH);
            rt.anchoredPosition = new Vector2(x, -RowH * 0.5f - 1f);
            t.text = "-";
            return t;
        }

        static void HeaderLabel(RectTransform strip, string label, TMP_FontAsset font, float x, TextAlignmentOptions align)
        {
            bool left = align == TextAlignmentOptions.MidlineLeft;
            var t = Txt(label, strip, font, 34f, HeaderText, align);
            var rt = t.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(left ? 0f : 0.5f, 0.5f);
            rt.sizeDelta = new Vector2(left ? 400f : 180f, 76f);
            rt.anchoredPosition = new Vector2(x, -42f);
            t.text = label;
        }

        static Button Tab(string label, RectTransform stage, Sprite sprite, TMP_FontAsset font, float x)
        {
            var img = Img(label, stage, sprite, x, 51, 290, 98);
            var b = MakeButton(img);
            var t = Txt("Label", img.rectTransform, font, 40f, TabText, TextAlignmentOptions.Midline);
            Stretch(t.rectTransform);
            t.text = label.ToUpperInvariant();
            return b;
        }

        static void WireRow(SerializedProperty p, RowParts r)
        {
            p.FindPropertyRelative("root").objectReferenceValue = r.root;
            p.FindPropertyRelative("border").objectReferenceValue = r.border;
            p.FindPropertyRelative("icon").objectReferenceValue = r.icon;
            p.FindPropertyRelative("rank").objectReferenceValue = r.rank;
            p.FindPropertyRelative("playerName").objectReferenceValue = r.name;
            p.FindPropertyRelative("wins").objectReferenceValue = r.w;
            p.FindPropertyRelative("losses").objectReferenceValue = r.l;
            p.FindPropertyRelative("rate").objectReferenceValue = r.rate;
        }

        /// <summary>Text box on a stage: x = left edge (or the left of the box for right / centre alignment), y = centre.</summary>
        static TextMeshProUGUI TextAt(string name, RectTransform stage, TMP_FontAsset font, float size, Color colour,
                                      TextAlignmentOptions align, float x, float y, float w)
        {
            var t = Txt(name, stage, font, size, colour, align);
            var rt = t.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(w, size * 1.6f);
            rt.anchoredPosition = new Vector2(x + w * 0.5f, -y);
            return t;
        }

        // ───────────── Scene plumbing ─────────────

        static Canvas EnsureCanvas(UnityEngine.SceneManagement.Scene scene)
        {
            foreach (var r in scene.GetRootGameObjects())
            {
                var c = r.GetComponentInChildren<Canvas>(true);
                if (c && c.isRootCanvas) return c;
            }
            var go = new GameObject("Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            go.layer = LayerMask.NameToLayer("UI");
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 1f;
            return canvas;
        }

        static void EnsureEventSystem(UnityEngine.SceneManagement.Scene scene)
        {
            foreach (var r in scene.GetRootGameObjects())
                if (r.GetComponentInChildren<EventSystem>(true)) return;
            new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
        }

        // ───────────── Layout helpers (concept px, y down from the top-left of the 2424 × 1080 stage) ─────────────

        static RectTransform NewStage(string name, RectTransform parent, Vector2 pivot)
        {
            var rt = NewRect(name, parent);
            rt.pivot = pivot;
            rt.anchorMin = rt.anchorMax = pivot;
            rt.sizeDelta = new Vector2(W, H);
            rt.gameObject.AddComponent<StageFit>();
            return rt;
        }

        static Image Img(string name, RectTransform parent, Sprite sprite, float x, float y, float w, float h)
        {
            var img = NewImage(name, parent, sprite);
            var rt = img.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(w, h);
            rt.anchoredPosition = new Vector2(x + w * 0.5f, -(y + h * 0.5f));
            return img;
        }

        static TextMeshProUGUI Txt(string name, RectTransform parent, TMP_FontAsset font, float size, Color colour, TextAlignmentOptions align)
        {
            var t = NewRect(name, parent).gameObject.AddComponent<TextMeshProUGUI>();
            t.font = font;
            t.fontSize = size;
            t.color = colour;
            t.alignment = align;
            t.textWrappingMode = TextWrappingModes.NoWrap;
            t.overflowMode = TextOverflowModes.Overflow;
            t.raycastTarget = false;
            return t;
        }

        static Button MakeButton(Image img)
        {
            img.raycastTarget = true;
            var b = img.gameObject.AddComponent<Button>();
            var colors = b.colors;
            colors.highlightedColor = Color.white;
            colors.selectedColor = Color.white;
            colors.pressedColor = new Color(0.8f, 0.8f, 0.8f);
            colors.disabledColor = Color.white;
            b.colors = colors;
            return b;
        }

        static RectTransform NewRect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = parent.gameObject.layer;
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        static Image NewImage(string name, Transform parent, Sprite sprite)
        {
            var img = NewRect(name, parent).gameObject.AddComponent<Image>();
            img.sprite = sprite;
            img.raycastTarget = false;
            img.enabled = sprite;
            return img;
        }

        static RectTransform Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            return rt;
        }

        // ───────────── Assets ─────────────

        static Sprite ImportSprite(string path, int max)
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
