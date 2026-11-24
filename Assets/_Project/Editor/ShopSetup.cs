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
    /// Menu: Window → KungFu Crashout → GDD Build → Build Shop Screen (Roster layout)
    /// Builds ShopScene from claude/Shop_Screen_Plan.md: the Roster (Concept 14) layout, with existing art only, every
    /// piece at its Roster concept position, in StageFit layers inside the safe area (scaled uniformly to fit any phone):
    ///   • Centre: A'Qing's colour card first (Art/UI/Shop/UI_Shop_CharacterCard_AQing, from the PVP Character Select
    ///     folder); either side the "Coming soon" card (stand-in: the locked card with the character painted out, chains
    ///     kept — replace with the art team's card). Arrows as the Roster.
    ///   • Left: A'Qing featured (stand-in: her card enlarged, until there's an illustration) + green nameplate.
    ///   • Right: fighter info on the Leaderboard stat scroll; the price on the red nameplate.
    ///   • Top: back lantern, "Shop" + "New in the dojo" (where the Roster title / timer are), Jade balance on the
    ///     Online Lobby connection bar (top-right corner). Bottom: BUY on the Roster's ready scroll.
    /// Layout only: nothing but Back works (→ Start screen). Start screen: the bag (Shop) icon → ShopScene. Start screen
    /// music carries on (music player copied in). ShopScene goes in the build list; the camera gets an Audio Listener.
    /// Re-running rebuilds the screen. Saves both scenes. Place in: Assets/_Project/Editor/ShopSetup.cs
    /// </summary>
    public static class ShopSetup
    {
        const string Title = "Shop Screen";
        const string Art = "Assets/_Project/Art/UI/Shop";
        const string CsArt = "Assets/_Project/Art/UI/CharacterSelect";
        const string FontFolder = "Assets/_Project/Art/UI/Fonts";
        const string RootName = "Shop (Roster layout)";
        const string ScenePath = "Assets/_Project/Scenes/ShopScene.unity";
        const string StartPath = "Assets/_Project/Scenes/StartScreen.unity";
        const float W = 2424f, H = 1080f;

        static readonly Color Cream = Hex("ECD998");
        static readonly Color SkillCream = Hex("F0E6C4");
        static readonly Color ScrollInk = Hex("401D0E");
        static readonly Color InfoText = Hex("DDCEBB");
        static readonly Color Balance = Hex("F4E8CE");
        static readonly Color ShopTitle = Hex("F0DEB0");
        static readonly Color TimerTop = Hex("D99A4E"), TimerBottom = Hex("F4EBC0");

        [MenuItem("Window/KungFu Crashout/GDD Build/Build Shop Screen (Roster layout)", priority = 10070)]
        public static void Build()
        {
            if (EditorApplication.isPlaying) { EditorUtility.DisplayDialog(Title, "Stop Play mode first.", "OK"); return; }
            if (!System.IO.File.Exists(ScenePath)) { EditorUtility.DisplayDialog(Title, "No scene at " + ScenePath, "OK"); return; }
            var tBold = Font("Tillana-Bold");
            if (!tBold) { EditorUtility.DisplayDialog(Title, "Tillana-Bold SDF not found in Art/UI/Fonts.", "OK"); return; }
            var tMed = Font("Tillana-Medium") ?? tBold;
            var barlow = Font("Barlow-Medium") ?? tBold;
            var aladin = Font("Aladin-Regular") ?? tBold;
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

            var missing = new List<string>();
            Sprite S(string folder, string n, int max = 1024)
            {
                var sp = ImportSprite($"{folder}/{n}.png", max);
                if (!sp) missing.Add(n);
                return sp;
            }
            var bgSprite = S(CsArt, "UI_CharacterSelect_Background", 4096);
            var clouds = S(CsArt, "UI_CharacterSelect_Clouds", 4096);
            var wall = S(CsArt, "UI_CharacterSelect_Background_CenterWall", 2048);
            var plateG = S(CsArt, "UI_CharacterSelect_Nameplate_Green");
            var plateR = S(CsArt, "UI_CharacterSelect_Nameplate_Red");
            var arrow = S(CsArt, "UI_CharacterSelect_Arrow", 256);
            var scroll = S(CsArt, "UI_CharacterSelect_Scroll_Base_Ready");
            var back = S(CsArt, "UI_CharacterSelect_Back", 256);
            var aqing = S(Art, "UI_Shop_CharacterCard_AQing");
            var soon = S(Art, "UI_Shop_Card_ComingSoon_StandIn");
            var info = S("Assets/_Project/Art/UI/Leaderboard", "UI_Leaderboard_Stat_Scroll");
            var bar = S("Assets/_Project/Art/UI/OnlineLobby", "UI_OnlineLobby_Connection_Border");

            var canvas = EnsureCanvas(scene);
            EnsureEventSystem(scene);
            EnsureListener(scene);
            var old = canvas.transform.Find(RootName);
            if (old) Object.DestroyImmediate(old.gameObject);
            var root = Stretch(NewRect(RootName, canvas.transform));

            var bg = NewImage("Background", root, bgSprite);
            Stretch(bg.rectTransform);
            var fitter = bg.gameObject.AddComponent<AspectRatioFitter>();
            fitter.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
            fitter.aspectRatio = W / H;
            bg.raycastTarget = true;

            var safe = Stretch(NewRect("Safe Area", root));
            safe.gameObject.AddComponent<SafeAreaFitter>();
            var floor = NewStage("Stage (floor)", safe, new Vector2(0.5f, 0f));
            var top = NewStage("Stage (top)", safe, new Vector2(0.5f, 1f));
            var right = NewStage("Stage (right)", safe, new Vector2(1f, 1f));

            // ── Floor, back to front (as the Roster) ──
            var feature = Img("Featured (A'Qing card, stand-in for an illustration)", floor, aqing, 150, 40, 336 * 1.62f, 588 * 1.62f);
            feature.preserveAspect = true;
            Img("Info Scroll", floor, info, 1745, 120, 792 * 0.82f, 727 * 0.82f);
            Left("Info Name", floor, aladin, 60, InfoText, 1840, 235, "A'Qing");
            string[,] rows = { { "Class", "Coming soon" }, { "Style", "Mid-range" }, { "Unlocks", "Roster card" } };
            for (int i = 0; i < 3; i++)
            {
                Left("Info " + rows[i, 0], floor, tMed, 38, InfoText, 1840, 330 + i * 78, rows[i, 0]);
                Right("Info " + rows[i, 0] + " Value", floor, tMed, 38, InfoText, 2305, 330 + i * 78, rows[i, 1]);
            }
            Img("Clouds", floor, clouds, 0, H - 235, W, 235);
            Img("Feature Nameplate", floor, plateG, 36, 767, 709, 208);
            Left("Feature Class", floor, tBold, 40, Cream, 129, 816, "New Fighter");
            Left("Feature Name", floor, tBold, 50, Cream, 112, 868, "A'Qing");
            Left("Feature Hint", floor, barlow, 30, SkillCream, 101, 927, "Tap the card to preview");
            Img("Price Nameplate", floor, plateR, 1677, 767, 710, 208);
            Right("Price Label", floor, tBold, 40, Cream, 2302, 814, "Price");
            Right("Price", floor, tBold, 50, Cream, 2300, 868, "1,200 Jade");
            Right("Price Note", floor, barlow, 30, SkillCream, 2308, 929, "One-time unlock");

            Img("Centre Wall", floor, wall, 712, 0, 1000, H);
            Card("Card Left (coming soon)", floor, soon, 916, 577, 0.8f, true, "Coming soon", tBold);
            Card("Card Right (coming soon)", floor, soon, 1521, 577, 0.8f, true, "Coming soon", tBold);
            Card("Card Centre (A'Qing)", floor, aqing, 1212, 577, 1f, false, "New Fighter", tBold);
            MakeButton(Img("Arrow Left", floor, arrow, 730 - 54.5f, 572 - 54.5f, 109, 109));
            var ra = Img("Arrow Right", floor, arrow, 1694 - 54.5f, 572 - 54.5f, 109, 109);
            ra.rectTransform.localScale = new Vector3(-1f, 1f, 1f);
            MakeButton(ra);
            var buy = MakeButton(Img("Buy", floor, scroll, 1212 - 262.5f, 978 - 79.5f, 525, 159));
            Centre("Text", buy.GetComponent<RectTransform>(), aladin, 60, ScrollInk, 0, 0, "BUY", true);

            // ── Top ──
            var backBtn = MakeButton(Img("Back", top, back, 35, 0, 132, 218));
            backBtn.GetComponent<Image>().raycastPadding = new Vector4(-10f, -10f, -10f, -10f);
            var sb = backBtn.gameObject.AddComponent<SceneButton>();
            var sso = new SerializedObject(sb);
            sso.FindProperty("sceneName").stringValue = "StartScreen";
            sso.ApplyModifiedPropertiesWithoutUndo();
            Centre("Title", top, aladin, 80, ShopTitle, 1212, 112, "Shop");
            var sub = Centre("Subtitle", top, tBold, 46, Color.white, 1212, 215, "New in the dojo");
            sub.enableVertexGradient = true;
            sub.colorGradient = new VertexGradient(TimerTop, TimerTop, TimerBottom, TimerBottom);
            Img("Balance Bar", right, bar, 1868, 30, 556, 57);
            Left("Balance", right, barlow, 30, Balance, 2009, 58, "Jade · 2,450");

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            string startNote = HookStart(scene, out string musicNote);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AddToBuild();
            Debug.Log("[Shop] Built and saved (layout only; Back → Start screen). " + startNote + " " + musicNote +
                      " ShopScene added to the build list. Stand-ins: the coming-soon card and the featured A'Qing art." +
                      (missing.Count > 0 ? $" Missing art, left blank: {string.Join(", ", missing)}." : ""));
        }

        /// <summary>A Roster-style card at its centre (cx, cy), scaled; side cards get the Roster's 20 % black dim.</summary>
        static void Card(string name, RectTransform stage, Sprite sprite, float cx, float cy, float scale, bool dim, string label, TMP_FontAsset font)
        {
            var root = NewRect(name, stage);
            root.anchorMin = root.anchorMax = new Vector2(0f, 1f);
            root.pivot = new Vector2(0.5f, 0.5f);
            root.sizeDelta = new Vector2(335f, 588f);
            root.anchoredPosition = new Vector2(cx, -cy);
            root.localScale = new Vector3(scale, scale, 1f);
            var art = NewImage("Art", root, sprite);
            Stretch(art.rectTransform);
            art.preserveAspect = true;
            if (dim)
            {
                var d = NewImage("Dim", root, sprite);
                Stretch(d.rectTransform);
                d.preserveAspect = true;
                d.color = new Color(0f, 0f, 0f, 0.2f);
            }
            var t = Txt("Label", root, font, 34f, Cream, TextAlignmentOptions.Midline);
            var rt = t.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(320f, 50f);
            rt.anchoredPosition = new Vector2(0f, -251f);
            t.text = label;
        }

        // ───────────── Start screen: bag icon → shop, music copied ─────────────

        static string HookStart(UnityEngine.SceneManagement.Scene shop, out string musicNote)
        {
            musicNote = "";
            if (!System.IO.File.Exists(StartPath)) return "No StartScreen scene.";
            var start = EditorSceneManager.OpenScene(StartPath, OpenSceneMode.Additive);
            musicNote = CopyMusic(start, shop);
            string note = "Shop (bag) button not found in StartScreen.";
            foreach (var r in start.GetRootGameObjects())
            {
                var screen = r.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "Start Screen (Concept 29)");
                if (!screen) continue;
                var b = screen.GetComponentsInChildren<Button>(true).FirstOrDefault(x => x.name == "Shop");
                if (!b) break;
                var sb = b.GetComponent<SceneButton>() ?? b.gameObject.AddComponent<SceneButton>();
                var sso = new SerializedObject(sb);
                sso.FindProperty("sceneName").stringValue = "ShopScene";
                sso.ApplyModifiedPropertiesWithoutUndo();
                note = "Start screen bag icon → ShopScene.";
                break;
            }
            EditorSceneManager.MarkSceneDirty(start);
            EditorSceneManager.SaveScene(start);
            EditorSceneManager.CloseScene(start, true);
            return note;
        }

        static string CopyMusic(UnityEngine.SceneManagement.Scene start, UnityEngine.SceneManagement.Scene target)
        {
            KungFuCrashout.Sound.MusicPlayer src = null, dst = null;
            foreach (var r in start.GetRootGameObjects()) if (!src) src = r.GetComponentInChildren<KungFuCrashout.Sound.MusicPlayer>(true);
            if (!src) return "No music player in StartScreen to copy.";
            foreach (var r in target.GetRootGameObjects()) if (!dst) dst = r.GetComponentInChildren<KungFuCrashout.Sound.MusicPlayer>(true);
            if (!dst)
            {
                var go = new GameObject("Music");
                EditorSceneManager.MoveGameObjectToScene(go, target);
                dst = go.AddComponent<KungFuCrashout.Sound.MusicPlayer>();
            }
            EditorUtility.CopySerialized(src, dst);
            var so = new SerializedObject(dst);
            so.FindProperty("persistAcrossScenes").boolValue = true;
            so.ApplyModifiedPropertiesWithoutUndo();
            return "Start screen music carries over.";
        }

        static void AddToBuild()
        {
            var list = EditorBuildSettings.scenes.ToList();
            if (list.Any(s => s.path == ScenePath)) return;
            list.Add(new EditorBuildSettingsScene(ScenePath, true));
            EditorBuildSettings.scenes = list.ToArray();
        }

        static void EnsureListener(UnityEngine.SceneManagement.Scene scene)
        {
            Camera cam = null;
            foreach (var r in scene.GetRootGameObjects())
            {
                if (r.GetComponentInChildren<AudioListener>(true)) return;
                if (!cam) cam = r.GetComponentInChildren<Camera>(true);
            }
            if (cam) cam.gameObject.AddComponent<AudioListener>();
        }

        // ───────────── Text pieces (x / y = concept px; y = the line's centre) ─────────────

        static TextMeshProUGUI Left(string name, RectTransform stage, TMP_FontAsset f, float size, Color c, float x, float y, string s)
            => Place(name, stage, f, size, c, TextAlignmentOptions.MidlineLeft, new Vector2(0f, 0.5f), x, y, s);

        static TextMeshProUGUI Right(string name, RectTransform stage, TMP_FontAsset f, float size, Color c, float x, float y, string s)
            => Place(name, stage, f, size, c, TextAlignmentOptions.MidlineRight, new Vector2(1f, 0.5f), x, y, s);

        /// <summary>Centred text: on a stage at (x, y), or filling a button (inButton).</summary>
        static TextMeshProUGUI Centre(string name, RectTransform parent, TMP_FontAsset f, float size, Color c, float x, float y, string s, bool inButton = false)
        {
            if (!inButton) return Place(name, parent, f, size, c, TextAlignmentOptions.Midline, new Vector2(0.5f, 0.5f), x, y, s);
            var t = Txt(name, parent, f, size, c, TextAlignmentOptions.Midline);
            Stretch(t.rectTransform);
            t.text = s;
            return t;
        }

        static TextMeshProUGUI Place(string name, RectTransform stage, TMP_FontAsset f, float size, Color c,
                                     TextAlignmentOptions align, Vector2 pivot, float x, float y, string s)
        {
            var t = Txt(name, stage, f, size, c, align);
            var rt = t.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = pivot;
            rt.sizeDelta = new Vector2(900f, size * 1.6f);
            rt.anchoredPosition = new Vector2(x, -y);
            t.text = s;
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
