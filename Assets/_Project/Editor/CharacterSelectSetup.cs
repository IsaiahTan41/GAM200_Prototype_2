#if UNITY_EDITOR
using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;
using UnityEngine.UI;
using KungFuCrashout.Fighters;
using KungFuCrashout.UI;

namespace KungFuCrashout.EditorTools
{
    /// <summary>
    /// Menu: Window → KungFu Crashout → GDD Build → Build Character Select (Concept 14)   (open the Roster scene first)
    /// Rebuilds the Roster as the UI team's "Choose your fighter" screen (GamConcepts_UI_13 / 14 + characterselectPVP_info):
    ///   • Art/UI/CharacterSelect imported as UI sprites (background / clouds at 4096 max so the 2424-wide art stays sharp;
    ///     the centre wall 9-sliced so only its plain middle stretches upward on narrow screens).
    ///   • Layout from the concept at 2424 × 1080 (= the Pixel 9a) in two StageFit layers inside the safe area: the floor
    ///     layer (illustrations, clouds, nameplates, centre wall, cards, arrows, scrolls) sits on the bottom, the top layer
    ///     (back lantern, title, timer) hangs from the top. Each scales uniformly to fit, so the concept proportions hold on
    ///     every phone; the background fills the whole screen behind.
    ///   • Fonts from the spec: Tillana Bold (class 40, name 50, P1/P2 34, timer 90 with the orange→cream gradient and a soft
    ///     white glow), Barlow Medium (skill 30), Aladin (READY 60, waiting 40 with a wave).
    ///   • The old roster (Background + SafeArea with RosterMenu) moves, unchanged, into a disabled "Legacy Roster";
    ///     Canvas → Hud Skin Switch → Use Concept Skin switches back.
    /// Fighters: A'Qing (no archetype yet: coming soon, no illustration), Monk Wu (Nam-pi archetype), Lord Yuan (Rushdown).
    /// Re-running rebuilds the concept screen. Place in: Assets/_Project/Editor/CharacterSelectSetup.cs
    /// </summary>
    public static class CharacterSelectSetup
    {
        const string Title = "Character Select";
        const string Art = "Assets/_Project/Art/UI/CharacterSelect";
        const string FontFolder = "Assets/_Project/Art/UI/Fonts";
        const string RootName = "Character Select (Concept 14)";
        const string LegacyName = "Legacy Roster";
        const string Ascii = " !\"#$%&'()*+,-./0123456789:;<=>?@ABCDEFGHIJKLMNOPQRSTUVWXYZ[\\]^_`abcdefghijklmnopqrstuvwxyz{|}~";
        const float W = 2424f, H = 1080f;

        static readonly Color Cream = Hex("ECD998");
        static readonly Color SkillCream = Hex("F0E6C4");
        static readonly Color ScrollInk = Hex("401D0E");
        static readonly Color TimerTop = Hex("D99A4E"), TimerBottom = Hex("F4EBC0");

        [MenuItem("Window/KungFu Crashout/GDD Build/Build Character Select (Concept 14)", priority = 10061)]
        public static void Build()
        {
            if (EditorApplication.isPlaying) { EditorUtility.DisplayDialog(Title, "Stop Play mode first.", "OK"); return; }
            var canvas = FindCanvas();
            var oldMenu = Object.FindFirstObjectByType<RosterMenu>(FindObjectsInactive.Include);
            if (!canvas) { EditorUtility.DisplayDialog(Title, "Open the Roster scene first (needs its Canvas).", "OK"); return; }

            // Fonts.
            var tillana = EnsureFont("Tillana-Bold");
            var aladin = EnsureFont("Aladin-Regular");
            var barlowMed = EnsureFont("Barlow-Medium");
            var barlowSemi = EnsureFont("Barlow-SemiBold");
            if (!tillana || !aladin || !barlowMed) return;
            var timerMat = TimerMaterial(tillana);

            // Sprites.
            Sprite S(string n, Vector4 border = default, int max = 2048) => ImportSprite(n, border, max);
            var bg = S("UI_CharacterSelect_Background", default, 4096);
            var wall = S("UI_CharacterSelect_Background_CenterWall", new Vector4(0, 170, 0, 250), 2048);
            var clouds = S("UI_CharacterSelect_Clouds", default, 4096);
            var back = S("UI_CharacterSelect_Back");
            var title = S("UI_ChooseYourFighter");
            S("UI_CharacterSelect_ChooseYourFighter"); // the green version, imported for later
            var arrow = S("UI_CharacterSelect_Arrow");
            var cardMonk = S("UI_CharacterSelect_CharacterCard_MonkWu");
            var cardYuan = S("UI_CharacterSelect_CharacterCard_LordYuan");
            var cardAQing = S("UI_CharacterSelect_CharacterCard_AQing");
            var illMonk = S("UI_CharacterSelect_CharacterIllustration_MonkWu");
            var illYuan = S("UI_CharacterSelect_CharacterIllustration_LordYuan");
            var plateG = S("UI_CharacterSelect_Nameplate_Green");
            var plateR = S("UI_CharacterSelect_Nameplate_Red");
            var bigG = S("UI_CharacterSelect_PlayerMarker_Large_Green");
            var bigR = S("UI_CharacterSelect_PlayerMarker_Large_Red");
            var smallG = S("UI_CharacterSelect_PlayerMarker_Small_Green");
            var smallR = S("UI_CharacterSelect_PlayerMarker_Small_Red");
            var scrollReady = S("UI_CharacterSelect_Scroll_Base_Ready");
            var scrollWait = S("UI_CharacterSelect_Scroll_Base_Waiting");
            if (!bg || !wall || !cardMonk) return;

            Undo.SetCurrentGroupName("Build Character Select");
            int group = Undo.GetCurrentGroup();

            // Old roster → Legacy Roster (moved once, unchanged).
            var legacy = canvas.transform.Find(LegacyName) as RectTransform;
            if (!legacy) legacy = Stretch(NewRect(LegacyName, canvas.transform));
            foreach (var n in new[] { "Background", "SafeArea" })
            {
                var t = canvas.transform.Find(n);
                if (t) Undo.SetTransformParent(t, legacy, false, "Legacy Roster");
            }

            var oldRoot = canvas.transform.Find(RootName);
            if (oldRoot) Undo.DestroyObjectImmediate(oldRoot.gameObject);
            var root = Stretch(NewRect(RootName, canvas.transform));
            root.SetAsLastSibling();
            var menu = Undo.AddComponent<CharacterSelectMenu>(root.gameObject);

            // Background: fills the whole screen (cover), behind everything.
            var bgImg = NewImage("Background", root, bg);
            Stretch(bgImg.rectTransform);
            var fitter = Undo.AddComponent<AspectRatioFitter>(bgImg.gameObject);
            fitter.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
            fitter.aspectRatio = W / H;

            var safe = Stretch(NewRect("Safe Area", root));
            Undo.AddComponent<SafeAreaFitter>(safe.gameObject);
            var floor = NewStage("Stage (floor)", safe, new Vector2(0.5f, 0f));
            var top = NewStage("Stage (top)", safe, new Vector2(0.5f, 1f));

            // ── Floor layer, back to front ──
            var p1Ill = Img("P1 Illustration", floor, illMonk, 0, 29, 1211, 1051);
            var p2Ill = Img("P2 Illustration", floor, illYuan, 1713, 61, 656, 1019);
            p2Ill.raycastTarget = true;
            var p2Cycle = Undo.AddComponent<Button>(p2Ill.gameObject);
            p2Cycle.transition = Selectable.Transition.None;
            Img("Clouds", floor, clouds, 0, H - 235, W, 235);

            var p1Plate = Img("P1 Nameplate", floor, plateG, 36, 767, 709, 208);
            var p1Class = Txt("Class", floor, tillana, 40, Cream, TextAlignmentOptions.MidlineLeft, 129, 816, true);
            var p1Name = Txt("Name", floor, tillana, 50, Cream, TextAlignmentOptions.MidlineLeft, 112, 868, true);
            var p1Skill = Txt("Skill", floor, barlowMed, 30, SkillCream, TextAlignmentOptions.MidlineLeft, 101, 927, true);
            Marker("P1 Marker", floor, bigG, 541, 894, 160, 80, "P1", tillana, 34, new Vector2(-21f, -3f));

            var p2Plate = Img("P2 Nameplate", floor, plateR, 1677, 767, 710, 208);
            var p2Class = Txt("Class", floor, tillana, 40, Cream, TextAlignmentOptions.MidlineRight, 2302, 814, false);
            var p2Name = Txt("Name", floor, tillana, 50, Cream, TextAlignmentOptions.MidlineRight, 2300, 868, false);
            var p2Skill = Txt("Skill", floor, barlowMed, 30, SkillCream, TextAlignmentOptions.MidlineRight, 2308, 929, false);
            Marker("P2 Marker", floor, bigR, 1883, 894, 160, 80, "P2", tillana, 34, new Vector2(20f, -3f));
            p1Class.name = "P1 Class"; p1Name.name = "P1 Name"; p1Skill.name = "P1 Skill";
            p2Class.name = "P2 Class"; p2Name.name = "P2 Name"; p2Skill.name = "P2 Skill";

            var wallImg = Img("Centre Wall", floor, wall, 712, 0, 1000, H);
            wallImg.type = Image.Type.Sliced;
            Undo.AddComponent<ExtendToCanvasTop>(wallImg.gameObject);

            var leftCard = Card("Card Left", floor, cardAQing, 916, 577, 0.8f, tillana, smallG, smallR);
            var rightCard = Card("Card Right", floor, cardYuan, 1521, 577, 0.8f, tillana, smallG, smallR);
            var centreCard = Card("Card Centre", floor, cardMonk, 1212, 577, 1f, tillana, smallG, smallR);

            var leftArrow = Img("Arrow Left", floor, arrow, 730 - 54.5f, 572 - 54.5f, 109, 109);
            var rightArrow = Img("Arrow Right", floor, arrow, 1694 - 54.5f, 572 - 54.5f, 109, 109);
            rightArrow.rectTransform.localScale = new Vector3(-1f, 1f, 1f); // mirrored: points right
            var la = MakeButton(leftArrow);
            var ra = MakeButton(rightArrow);

            var ready = Img("Ready", floor, scrollReady, 1212 - 262.5f, 978 - 79.5f, 525, 159);
            var readyBtn = MakeButton(ready);
            var readyText = Txt("Text", ready.rectTransform, aladin, 60, ScrollInk, TextAlignmentOptions.Midline, 0, 0, true);
            Centre(readyText.rectTransform, Vector2.zero, new Vector2(400f, 100f));
            readyText.text = "READY";

            var waiting = Img("Waiting", floor, scrollWait, 1214 - 358f, 980 - 79.5f, 716, 159);
            var waitText = Txt("Text", waiting.rectTransform, aladin, 40, ScrollInk, TextAlignmentOptions.Midline, 0, 0, true);
            Centre(waitText.rectTransform, Vector2.zero, new Vector2(560f, 90f));
            waitText.text = "Waiting for the other player...";
            Undo.AddComponent<TmpWave>(waitText.gameObject);
            waiting.gameObject.SetActive(false);

            var toast = Txt("Toast", floor, barlowSemi ? barlowSemi : barlowMed, 30, Cream, TextAlignmentOptions.Midline, 1212, 270, true);
            Centre(toast.rectTransform, new Vector2(1212f, -270f), new Vector2(900f, 50f), floor);
            toast.text = "";

            // ── Top layer ──
            var backImg = Img("Back", top, back, 35, 0, 132, 218);
            var backBtn = MakeButton(backImg);
            Img("Title", top, title, 924, 63, 578, 98);
            var timer = Txt("Timer", top, tillana, 90, Color.white, TextAlignmentOptions.Midline, 1212, 226, true);
            Centre(timer.rectTransform, new Vector2(1212f, -226f), new Vector2(300f, 120f), top);
            timer.fontSharedMaterial = timerMat;
            timer.enableVertexGradient = true;
            timer.colorGradient = new VertexGradient(TimerTop, TimerTop, TimerBottom, TimerBottom);
            timer.text = "90";

            // ── Wire the menu ──
            var so = new SerializedObject(menu);
            var entries = so.FindProperty("entries");
            entries.arraySize = 3;
            var archetypes = FindArchetypes();
            SetEntry(entries.GetArrayElementAtIndex(0), null, "A'Qing", "", "", cardAQing, null);
            SetEntry(entries.GetArrayElementAtIndex(1), archetypes.Get("Archetype_1_NamPi"), "Monk Wu", "Shoto", "Hold: Palm Strike", cardMonk, illMonk);
            SetEntry(entries.GetArrayElementAtIndex(2), archetypes.Get("Archetype_2_Rushdown"), "Lord Yuan", "Rushdown", "Close-ranged combat", cardYuan, illYuan);

            WireCard(so.FindProperty("leftCard"), leftCard);
            WireCard(so.FindProperty("centreCard"), centreCard);
            WireCard(so.FindProperty("rightCard"), rightCard);
            so.FindProperty("leftArrow").objectReferenceValue = la;
            so.FindProperty("rightArrow").objectReferenceValue = ra;
            WireSide(so.FindProperty("p1"), p1Ill, p1Class, p1Name, p1Skill);
            WireSide(so.FindProperty("p2"), p2Ill, p2Class, p2Name, p2Skill);
            so.FindProperty("p2CycleButton").objectReferenceValue = p2Cycle;
            so.FindProperty("timerText").objectReferenceValue = timer;
            so.FindProperty("readyButton").objectReferenceValue = readyBtn;
            so.FindProperty("readyScroll").objectReferenceValue = ready.gameObject;
            so.FindProperty("waitingScroll").objectReferenceValue = waiting.gameObject;
            so.FindProperty("backButton").objectReferenceValue = backBtn;
            so.FindProperty("toastText").objectReferenceValue = toast;
            if (oldMenu)
            {
                // Keep the old roster's playtest lock and scene names.
                var old = new SerializedObject(oldMenu);
                so.FindProperty("lockMatchup").boolValue = old.FindProperty("lockMatchup").boolValue;
                so.FindProperty("lockedPlayer").objectReferenceValue = old.FindProperty("lockedPlayer").objectReferenceValue;
                so.FindProperty("lockedCpu").objectReferenceValue = old.FindProperty("lockedCpu").objectReferenceValue;
                so.FindProperty("fightScene").stringValue = old.FindProperty("fightScene").stringValue;
                so.FindProperty("backScene").stringValue = old.FindProperty("backScene").stringValue;
            }
            so.ApplyModifiedProperties();

            // Switch between this and the old roster.
            var sw = canvas.GetComponent<HudSkinSwitch>();
            if (!sw) sw = Undo.AddComponent<HudSkinSwitch>(canvas.gameObject);
            var sso = new SerializedObject(sw);
            sso.FindProperty("useConceptSkin").boolValue = true;
            sso.FindProperty("conceptHud").objectReferenceValue = root.gameObject;
            sso.FindProperty("legacyHud").objectReferenceValue = legacy.gameObject;
            sso.ApplyModifiedProperties();
            sw.Apply();

            Undo.CollapseUndoOperations(group);
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            Debug.Log("[Character Select] Concept 14 screen built. Missing art left blank: A'Qing's illustration (and her class / " +
                      "skill text; she has no archetype yet, so she shows as coming soon). Canvas → Hud Skin Switch → Use Concept " +
                      "Skin switches back to the old roster. Save the scene (Ctrl+S).");
        }

        // ───────────── Pieces ─────────────

        class Archetypes : Dictionary<string, FighterArchetype>
        {
            public FighterArchetype Get(string n) => TryGetValue(n, out var a) ? a : null;
        }

        static Archetypes FindArchetypes()
        {
            var d = new Archetypes();
            foreach (var guid in AssetDatabase.FindAssets("t:FighterArchetype"))
            {
                var a = AssetDatabase.LoadAssetAtPath<FighterArchetype>(AssetDatabase.GUIDToAssetPath(guid));
                if (a) d[a.name] = a;
            }
            return d;
        }

        static void SetEntry(SerializedProperty e, FighterArchetype a, string name, string cls, string skill, Sprite card, Sprite ill)
        {
            e.FindPropertyRelative("archetype").objectReferenceValue = a;
            e.FindPropertyRelative("displayName").stringValue = name;
            e.FindPropertyRelative("classLabel").stringValue = cls;
            e.FindPropertyRelative("skill").stringValue = skill;
            e.FindPropertyRelative("card").objectReferenceValue = card;
            e.FindPropertyRelative("illustration").objectReferenceValue = ill;
        }

        struct CardParts { public RectTransform root; public Image art, dim; public TMP_Text label; public GameObject p1, p2; public Button button; }

        /// <summary>A card at its concept centre (cx, cy), scaled (side cards 0.8): art, class label, P1 / P2 markers, dim overlay.</summary>
        static CardParts Card(string name, RectTransform stage, Sprite card, float cx, float cy, float scale, TMP_FontAsset font, Sprite g, Sprite r)
        {
            var c = new CardParts();
            c.root = NewRect(name, stage);
            Centre(c.root, new Vector2(cx, -cy), new Vector2(335f, 588f), stage);
            c.root.localScale = new Vector3(scale, scale, 1f);

            c.art = NewImage("Art", c.root, card);
            Stretch(c.art.rectTransform);
            c.art.raycastTarget = true;
            c.art.preserveAspect = true;
            c.button = MakeButton(c.art);

            c.label = Txt("Label", c.root, font, 34, Cream, TextAlignmentOptions.Midline, 0, 0, true);
            Centre(c.label.rectTransform, new Vector2(0f, -251f), new Vector2(320f, 50f));
            c.label.text = "Shoto";

            c.p2 = MarkerIn("P2", c.root, r, new Vector2(92f, 221f), 86f, font).gameObject;
            c.p1 = MarkerIn("P1", c.root, g, new Vector2(-82f, -171f), 81f, font).gameObject;

            c.dim = NewImage("Dim (side cards)", c.root, card);
            Stretch(c.dim.rectTransform);
            c.dim.preserveAspect = true;
            c.dim.color = new Color(0f, 0f, 0f, 0.2f);
            c.dim.raycastTarget = false;
            return c;
        }

        static RectTransform MarkerIn(string text, RectTransform parent, Sprite sprite, Vector2 pos, float size, TMP_FontAsset font)
        {
            var m = NewImage(text + " Marker", parent, sprite);
            Centre(m.rectTransform, pos, new Vector2(size, size));
            var t = Txt("Text", m.rectTransform, font, 26, Cream, TextAlignmentOptions.Midline, 0, 0, true);
            Centre(t.rectTransform, Vector2.zero, new Vector2(size, size));
            t.text = text;
            return m.rectTransform;
        }

        static void Marker(string name, RectTransform stage, Sprite sprite, float cx, float cy, float w, float h, string text, TMP_FontAsset font, float size, Vector2 textOffset)
        {
            var m = NewImage(name, stage, sprite);
            Centre(m.rectTransform, new Vector2(cx, -cy), new Vector2(w, h), stage);
            var t = Txt("Text", m.rectTransform, font, size, Cream, TextAlignmentOptions.Midline, 0, 0, true);
            Centre(t.rectTransform, textOffset, new Vector2(w, h));
            t.text = text;
        }

        static void WireCard(SerializedProperty p, CardParts c)
        {
            p.FindPropertyRelative("root").objectReferenceValue = c.root;
            p.FindPropertyRelative("art").objectReferenceValue = c.art;
            p.FindPropertyRelative("label").objectReferenceValue = c.label;
            p.FindPropertyRelative("p1Marker").objectReferenceValue = c.p1;
            p.FindPropertyRelative("p2Marker").objectReferenceValue = c.p2;
            p.FindPropertyRelative("dim").objectReferenceValue = c.dim;
            p.FindPropertyRelative("button").objectReferenceValue = c.button;
        }

        static void WireSide(SerializedProperty p, Image ill, TMP_Text cls, TMP_Text name, TMP_Text skill)
        {
            p.FindPropertyRelative("illustration").objectReferenceValue = ill;
            p.FindPropertyRelative("classText").objectReferenceValue = cls;
            p.FindPropertyRelative("nameText").objectReferenceValue = name;
            p.FindPropertyRelative("skillText").objectReferenceValue = skill;
        }

        // ───────────── Layout helpers (concept pixels, y down from the top-left of the 2424 × 1080 stage) ─────────────

        static RectTransform NewStage(string name, RectTransform parent, Vector2 pivot)
        {
            var rt = NewRect(name, parent);
            rt.pivot = pivot;
            rt.anchorMin = rt.anchorMax = pivot;
            rt.sizeDelta = new Vector2(W, H);
            Undo.AddComponent<StageFit>(rt.gameObject);
            return rt;
        }

        /// <summary>Image by its concept top-left corner and size.</summary>
        static Image Img(string name, RectTransform stage, Sprite sprite, float x, float y, float w, float h)
        {
            var img = NewImage(name, stage, sprite);
            var rt = img.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.sizeDelta = new Vector2(w, h);
            // Pivot in the middle so mirroring (scale -1) and pops stay in place.
            var centre = new Vector2(x + w * 0.5f, -(y + h * 0.5f));
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = centre;
            return img;
        }

        /// <summary>Centre an element: in a stage (anchor top-left, y down) or inside a parent (anchor centre).</summary>
        static void Centre(RectTransform rt, Vector2 pos, Vector2 size, RectTransform stage = null)
        {
            var a = stage ? new Vector2(0f, 1f) : new Vector2(0.5f, 0.5f);
            rt.anchorMin = rt.anchorMax = a;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = size;
            rt.anchoredPosition = pos;
        }

        /// <summary>Text with its left (alignLeft) or right edge at x and its middle at y, in stage pixels. (0, 0) = place later.</summary>
        static TextMeshProUGUI Txt(string name, RectTransform parent, TMP_FontAsset font, float size, Color colour, TextAlignmentOptions align, float x, float y, bool alignLeft)
        {
            var rt = NewRect(name, parent);
            var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
            t.font = font;
            t.fontSize = size;
            t.color = colour;
            t.alignment = align;
            t.textWrappingMode = TextWrappingModes.NoWrap;
            t.overflowMode = TextOverflowModes.Overflow;
            t.raycastTarget = false;
            if (x != 0f || y != 0f)
            {
                rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
                rt.pivot = new Vector2(alignLeft ? 0f : 1f, 0.5f);
                rt.sizeDelta = new Vector2(700f, size * 1.5f);
                rt.anchoredPosition = new Vector2(x, -y);
            }
            return t;
        }

        static Button MakeButton(Image img)
        {
            img.raycastTarget = true;
            var b = Undo.AddComponent<Button>(img.gameObject);
            var colors = b.colors;
            colors.pressedColor = new Color(0.85f, 0.85f, 0.85f);
            colors.disabledColor = Color.white; // committed: keep the art as it is
            b.colors = colors;
            return b;
        }

        static RectTransform NewRect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            Undo.RegisterCreatedObjectUndo(go, name);
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

        static Canvas FindCanvas()
        {
            foreach (var c in Object.FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (c.isRootCanvas && c.gameObject.scene.IsValid()) return c;
            return null;
        }

        // ───────────── Assets ─────────────

        static Sprite ImportSprite(string file, Vector4 border, int max)
        {
            string path = $"{Art}/{file}.png";
            var ti = AssetImporter.GetAtPath(path) as TextureImporter;
            if (!ti) { Debug.LogWarning($"[Character Select] Missing {path}: left blank."); return null; }
            ti.textureType = TextureImporterType.Sprite;
            ti.spriteImportMode = SpriteImportMode.Single;
            ti.spritePixelsPerUnit = 100f;
            ti.mipmapEnabled = false;
            ti.alphaIsTransparency = true;
            ti.wrapMode = TextureWrapMode.Clamp;
            ti.filterMode = FilterMode.Bilinear;
            ti.textureCompression = TextureImporterCompression.CompressedHQ;
            ti.maxTextureSize = max;
            ti.spriteBorder = border;
            var s = new TextureImporterSettings();
            ti.ReadTextureSettings(s);
            s.spriteMeshType = SpriteMeshType.FullRect;
            ti.SetTextureSettings(s);
            ti.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }

        static TMP_FontAsset EnsureFont(string file)
        {
            string path = $"{FontFolder}/{file} SDF.asset";
            var fa = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);
            if (fa) return fa;
            var font = AssetDatabase.LoadAssetAtPath<Font>($"{FontFolder}/{file}.ttf");
            if (!font) { Debug.LogWarning($"[Character Select] Missing {FontFolder}/{file}.ttf."); return null; }
            fa = TMP_FontAsset.CreateFontAsset(font, 90, 9, GlyphRenderMode.SDFAA, 1024, 1024, AtlasPopulationMode.Dynamic, true);
            if (!fa) return null;
            fa.name = $"{file} SDF";
            AssetDatabase.CreateAsset(fa, path);
            fa.material.name = $"{file} SDF Material";
            AssetDatabase.AddObjectToAsset(fa.material, fa);
            foreach (var tex in fa.atlasTextures)
            {
                if (!tex) continue;
                tex.name = $"{file} SDF Atlas";
                AssetDatabase.AddObjectToAsset(tex, fa);
            }
            fa.TryAddCharacters(Ascii, out _);
            EditorUtility.SetDirty(fa);
            AssetDatabase.SaveAssets();
            return fa;
        }

        /// <summary>The timer's soft white glow (the spec's "white blur behind the countdown"): a wide, soft TMP underlay.</summary>
        static Material TimerMaterial(TMP_FontAsset fa)
        {
            string path = $"{FontFolder}/{fa.name} Timer Glow.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            bool created = !mat;
            if (created) mat = new Material(fa.material);
            else mat.CopyPropertiesFromMaterial(fa.material);
            mat.name = $"{fa.name} Timer Glow";
            mat.EnableKeyword(ShaderUtilities.Keyword_Underlay);
            mat.SetColor(ShaderUtilities.ID_UnderlayColor, new Color(1f, 1f, 1f, 0.85f));
            mat.SetFloat(ShaderUtilities.ID_UnderlayOffsetX, 0f);
            mat.SetFloat(ShaderUtilities.ID_UnderlayOffsetY, 0f);
            mat.SetFloat(ShaderUtilities.ID_UnderlayDilate, 0.6f);
            mat.SetFloat(ShaderUtilities.ID_UnderlaySoftness, 1f);
            ShaderUtilities.UpdateShaderRatios(mat);
            if (created) AssetDatabase.CreateAsset(mat, path);
            else EditorUtility.SetDirty(mat);
            return mat;
        }

        static Color Hex(string hex)
        {
            ColorUtility.TryParseHtmlString("#" + hex, out var c);
            return c;
        }
    }
}
#endif
