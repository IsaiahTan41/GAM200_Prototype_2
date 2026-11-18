#if UNITY_EDITOR
using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;
using UnityEngine.UI;
using KungFuCrashout.UI;

namespace KungFuCrashout.EditorTools
{
    /// <summary>
    /// Menu: Window → KungFu Crashout → GDD Build → Apply Fight HUD Skin (Concept 27)
    /// Builds the fight HUD from the UI team's FightHUD assets in the style of GamConcepts_UI_27 (spec: fightHUD_info.txt):
    ///   1. Fonts: TextMesh Pro font assets (dynamic SDF, ASCII pre-filled) for Tillana Bold / SemiBold / Medium, Aladin and
    ///      Barlow SemiBold in Art/UI/Fonts, plus outline material presets (2 px outline: brown combo words and callouts,
    ///      orange combo numbers).
    ///   2. Sprites: Art/UI/FightHUD imported as UI sprites; the bar frames, bar backgrounds and status border get borders
    ///      so only their straight middle stretches (ornaments, name plates and slanted caps keep their shape).
    ///   3. "Concept HUD" under Canvas/SafeArea, laid out from the concept (2424 x 1080 = the Pixel 9a; the canvas is
    ///      1920 x 1080 matched on height, so 1 canvas unit = 1 concept pixel). Portraits, timer, coins, banners and text
    ///      keep their native size; the health bars stretch between the portrait and the timer, so every landscape width
    ///      from 4:3 up fits.
    ///   4. The old widgets (health sliders, names, pips, timer text, combo / move text, callout, popups) move, unchanged,
    ///      into a disabled "Legacy HUD" next to it. HudSkinSwitch on SafeArea → Use Concept Skin switches back.
    /// Re-running rebuilds Concept HUD (the legacy widgets are only moved once). Portraits: assign the Portrait images
    /// (Concept HUD → P1 / CPU Profile → Portrait Mask → Portrait) when they're supplied.
    /// Place in: Assets/_Project/Editor/FightHudSkinSetup.cs
    /// </summary>
    public static class FightHudSkinSetup
    {
        const string Title = "Fight HUD Skin";
        const string SpriteFolder = "Assets/_Project/Art/UI/FightHUD";
        const string FontFolder = "Assets/_Project/Art/UI/Fonts";
        const string ConceptName = "Concept HUD";
        const string LegacyName = "Legacy HUD";
        const string Ascii = " !\"#$%&'()*+,-./0123456789:;<=>?@ABCDEFGHIJKLMNOPQRSTUVWXYZ[\\]^_`abcdefghijklmnopqrstuvwxyz{|}~";

        // Old widgets replaced by the skin (moved into Legacy HUD, never deleted).
        static readonly string[] LegacyWidgets =
        {
            "HealthSlider", "EnemyHealthSlider", "P1 Name", "CPU Name", "P1 Pips", "CPU Pips", "P1 Combo", "CPU Combo",
            "P1 Move", "CPU Move", "Callout", "Timer (Pause)", "P1 Popup", "CPU Popup",
        };

        // Colours sampled from the concept.
        static readonly Color Cream = Hex("F2E3CA"), NameCream = Hex("F0E8D8"), TimerCream = Hex("F6EEDA");
        static readonly Color Brown = Hex("3A3028"), Orange = Hex("E8803F");
        static readonly Color NumTop = Hex("FAF3A6"), NumBottom = Hex("F7D34E");
        static readonly Color StatusP1 = Hex("E8D898"), StatusCpu = Hex("F88860");
        static readonly Color P1HealthAnchor = Hex("1E785F"), P1HealthLead = Hex("5EBF96");
        static readonly Color P1RedAnchor = Hex("90251D"), P1RedLead = Hex("AC382B");
        static readonly Color CpuHealthAnchor = Hex("A0434B"), CpuHealthLead = Hex("DF7373");
        static readonly Color CpuRedAnchor = Hex("90251F"), CpuRedLead = Hex("B53D2F");

        // Layout (canvas units = concept pixels, measured from the top-left / top-right).
        const float BarOuter = 147f, BarInner = 135f, BarTop = 48f, BarHeight = 138f;
        const float BannerTop = 508f, BannerStep = 97f;
        const float ComboY = 345f, ComboMargin = 27f;
        const float CoinY = 162f, CoinInner = 185f, CoinOuter = 240f;

        [MenuItem("Window/KungFu Crashout/GDD Build/Apply Fight HUD Skin (Concept 27)", priority = 10060)]
        public static void Apply()
        {
            if (EditorApplication.isPlaying) { EditorUtility.DisplayDialog(Title, "Stop Play mode first.", "OK"); return; }

            var safeArea = FindByName<RectTransform>("SafeArea");
            var gddHud = FindByName<RectTransform>("GDD HUD");
            if (!safeArea || !gddHud)
            {
                EditorUtility.DisplayDialog(Title, "Open FightScene first (needs Canvas → SafeArea → GDD HUD).", "OK");
                return;
            }

            // 1. Fonts and materials.
            var tillanaBold = EnsureFont("Tillana-Bold");
            var tillanaSemi = EnsureFont("Tillana-SemiBold");
            var tillanaMedium = EnsureFont("Tillana-Medium");
            var aladin = EnsureFont("Aladin-Regular");
            var barlow = EnsureFont("Barlow-SemiBold");
            if (!tillanaBold || !aladin || !barlow) return;
            var comboWordMat = Preset(tillanaBold, "Combo Word", Brown, 40f, 2f);
            var comboNumMat = Preset(tillanaBold, "Combo Number", Orange, 100f, 2f);
            var calloutMat = Preset(aladin, "Callout", Brown, 160f, 2f);

            // 2. Sprites.
            var userFrame = ImportSprite("UI_FightHUD_UserProfile_Frame", Vector4.zero);
            var enemyFrame = ImportSprite("UI_FightHUD_EnemyProfile_Frame", Vector4.zero);
            var circle = ImportSprite("UI_FightHUD_EnemyProfile_Background", Vector4.zero);
            var userBar = ImportSprite("UI_FightHUD_UserProfile_HealthBar_Frame", new Vector4(270, 0, 60, 0));
            var enemyBar = ImportSprite("UI_FightHUD_EnemyProfile_HealthBar_Frame", new Vector4(60, 0, 270, 0));
            var userBarBg = ImportSprite("UI_FightHUD_UserProfile_HealthBar_Frame_Background", new Vector4(10, 0, 60, 0));
            var enemyBarBg = ImportSprite("UI_FightHUD_HealthBar_Frame_Background", new Vector4(60, 0, 10, 0));
            var timerFrame = ImportSprite("UI_FightHUD_Time_Frame", Vector4.zero);
            var coinLit = ImportSprite("UI_FightHUD_Coin_Lit", Vector4.zero);
            var coinUnlit = ImportSprite("UI_FightHUD_Coin_Unlit", Vector4.zero);
            var status = ImportSprite("UI_FightHUD_Status_Border", new Vector4(10, 0, 40, 0));
            if (!userFrame || !userBar || !timerFrame || !status) return;

            Undo.SetCurrentGroupName("Apply Fight HUD Skin");
            int group = Undo.GetCurrentGroup();

            // 3. Legacy widgets → Legacy HUD (moved once, layout unchanged: both parents fill the safe area).
            var legacy = safeArea.Find(LegacyName) as RectTransform;
            if (!legacy) legacy = Stretch(NewRect(LegacyName, safeArea));
            var moved = new List<string>();
            Button pauseButton = null;
            foreach (var n in LegacyWidgets)
            {
                var t = FindChildDeep(safeArea, n, legacy);
                if (t) { Undo.SetTransformParent(t, legacy, false, "Legacy HUD"); moved.Add(n); }
            }
            var timerOld = legacy.Find("Timer (Pause)");
            if (timerOld) pauseButton = timerOld.GetComponent<Button>();

            // 4. Concept HUD (rebuilt each run).
            var old = safeArea.Find(ConceptName);
            if (old) Undo.DestroyObjectImmediate(old.gameObject);
            var root = Stretch(NewRect(ConceptName, safeArea));
            root.SetSiblingIndex(gddHud.GetSiblingIndex()); // under GDD HUD: the match-over panel stays on top
            var hud = Undo.AddComponent<FightHudConcept>(root.gameObject);
            var so = new SerializedObject(hud);

            // Bars, behind the profiles.
            BuildBar(root, true, userBar, userBarBg, tillanaBold, so.FindProperty("left"));
            BuildBar(root, false, enemyBar, enemyBarBg, tillanaBold, so.FindProperty("right"));
            BuildProfile(root, true, userFrame, circle, so.FindProperty("left"));
            BuildProfile(root, false, enemyFrame, circle, so.FindProperty("right"));
            BuildCoins(root, true, coinUnlit, so.FindProperty("left"));
            BuildCoins(root, false, coinUnlit, so.FindProperty("right"));

            // Timer: the diamond frame is the pause button now.
            var timer = NewImage("Timer", root, timerFrame);
            Place(timer.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -19f), new Vector2(188f, 188f));
            timer.raycastTarget = true;
            var timerButton = Undo.AddComponent<Button>(timer.gameObject);
            timerButton.transition = Selectable.Transition.None;
            var time = NewText("Time", timer.rectTransform, tillanaBold, null, 70f, TimerCream, TextAlignmentOptions.Midline);
            Place(time.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(1f, 3f), new Vector2(170f, 110f));
            time.text = "90";

            BuildCombo(root, true, tillanaBold, comboWordMat, comboNumMat, so.FindProperty("left"));
            BuildCombo(root, false, tillanaBold, comboWordMat, comboNumMat, so.FindProperty("right"));
            BuildBanners(root, true, status, barlow, so.FindProperty("left"));
            BuildBanners(root, false, status, barlow, so.FindProperty("right"));

            var callout = NewText("Callout", root, aladin, calloutMat, 160f, Cream, TextAlignmentOptions.Midline);
            Place(callout.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, -47f), new Vector2(2200f, 260f));
            callout.text = "ROUND 1";
            AddDepth(callout, Brown);

            so.FindProperty("match").objectReferenceValue = Object.FindFirstObjectByType<Core.MatchManager>();
            so.FindProperty("timerText").objectReferenceValue = time;
            so.FindProperty("timerButton").objectReferenceValue = timerButton;
            so.FindProperty("pauseButton").objectReferenceValue = pauseButton;
            so.FindProperty("coinLit").objectReferenceValue = coinLit;
            so.FindProperty("coinUnlit").objectReferenceValue = coinUnlit;
            so.FindProperty("calloutText").objectReferenceValue = callout;
            so.ApplyModifiedProperties();

            // 5. The switch.
            var sw = safeArea.GetComponent<HudSkinSwitch>();
            if (!sw) sw = Undo.AddComponent<HudSkinSwitch>(safeArea.gameObject);
            var sso = new SerializedObject(sw);
            sso.FindProperty("useConceptSkin").boolValue = true;
            sso.FindProperty("conceptHud").objectReferenceValue = root.gameObject;
            sso.FindProperty("legacyHud").objectReferenceValue = legacy.gameObject;
            sso.ApplyModifiedProperties();
            sw.Apply();

            Undo.CollapseUndoOperations(group);
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            Debug.Log($"[Fight HUD] Concept 27 skin built under {safeArea.name}/{ConceptName}. " +
                      (moved.Count > 0 ? $"Moved to {LegacyName}: {string.Join(", ", moved)}. " : "") +
                      (pauseButton ? "" : "No old Timer (Pause) button found: the timer won't open the pause menu. ") +
                      $"Fonts: {tillanaBold.name}, {tillanaSemi?.name}, {tillanaMedium?.name}, {aladin.name}, {barlow.name}. " +
                      "SafeArea → Hud Skin Switch → Use Concept Skin switches back. Save the scene (Ctrl+S).");
        }

        // ───────────── Pieces ─────────────

        static void BuildBar(RectTransform root, bool p1, Sprite frame, Sprite bg, TMP_FontAsset font, SerializedProperty side)
        {
            var bar = NewRect(p1 ? "P1 Bar" : "CPU Bar", root);
            bar.anchorMin = new Vector2(p1 ? 0f : 0.5f, 1f);
            bar.anchorMax = new Vector2(p1 ? 0.5f : 1f, 1f);
            bar.pivot = new Vector2(p1 ? 0f : 1f, 1f);
            bar.offsetMin = new Vector2(p1 ? BarOuter : BarInner, -BarTop - BarHeight);
            bar.offsetMax = new Vector2(p1 ? -BarInner : -BarOuter, -BarTop);

            var back = NewImage("Bar Background", bar, bg);
            back.type = Image.Type.Sliced;
            Band(back.rectTransform, p1 ? 54f : 5f, p1 ? 5f : 54f, 44f, 41f);

            var fillArea = NewRect("Fill Area", bar);
            Band(fillArea, p1 ? 50f : 3f, p1 ? 3f : 50f, 52f, 28f);
            var trailing = NewBar("Trailing", fillArea, p1);
            var red = NewBar("Recoverable", fillArea, p1);
            var health = NewBar("Health", fillArea, p1);

            var img = NewImage("Frame", bar, frame);
            img.type = Image.Type.Sliced;
            Stretch(img.rectTransform);

            var name = NewText("Name", bar, font, null, 25f, NameCream, p1 ? TextAlignmentOptions.MidlineLeft : TextAlignmentOptions.MidlineRight);
            Place(name.rectTransform, new Vector2(p1 ? 0f : 1f, 1f), new Vector2(p1 ? 0f : 1f, 1f), new Vector2(p1 ? 53f : -53f, -112f), new Vector2(420f, 40f));
            name.rectTransform.pivot = new Vector2(p1 ? 0f : 1f, 0.5f);
            name.text = p1 ? "Player" : "CPU";

            side.FindPropertyRelative("health").objectReferenceValue = health;
            side.FindPropertyRelative("recoverable").objectReferenceValue = red;
            side.FindPropertyRelative("trailing").objectReferenceValue = trailing;
            side.FindPropertyRelative("nameText").objectReferenceValue = name;
            side.FindPropertyRelative("healthAnchor").colorValue = p1 ? P1HealthAnchor : CpuHealthAnchor;
            side.FindPropertyRelative("healthLead").colorValue = p1 ? P1HealthLead : CpuHealthLead;
            side.FindPropertyRelative("redAnchor").colorValue = p1 ? P1RedAnchor : CpuRedAnchor;
            side.FindPropertyRelative("redLead").colorValue = p1 ? P1RedLead : CpuRedLead;

            health.SetColours(p1 ? P1HealthAnchor : CpuHealthAnchor, p1 ? P1HealthLead : CpuHealthLead);
            health.SetRange(0f, 1f);
            red.SetRange(0f, 0f);
            trailing.SetRange(0f, 0f);
        }

        static SlantedBar NewBar(string name, RectTransform parent, bool p1)
        {
            var rt = Stretch(NewRect(name, parent));
            var bar = Undo.AddComponent<SlantedBar>(rt.gameObject);
            bar.raycastTarget = false;
            bar.Configure(p1, 0.83f, 1.6f);
            return bar;
        }

        static void BuildProfile(RectTransform root, bool p1, Sprite frame, Sprite circle, SerializedProperty side)
        {
            var profile = NewRect(p1 ? "P1 Profile" : "CPU Profile", root);
            var corner = new Vector2(p1 ? 0f : 1f, 1f);
            Place(profile, corner, corner, new Vector2(p1 ? 6f : -6f, -30f), new Vector2(432f, 169f));
            profile.pivot = corner;
            profile.anchoredPosition = new Vector2(p1 ? 6f : -6f, -30f);

            var mask = NewImage("Portrait Mask", profile, circle);
            Place(mask.rectTransform, corner, corner, new Vector2(p1 ? 106f : -107f, -77f), new Vector2(154f, 154f));
            Undo.AddComponent<Mask>(mask.gameObject).showMaskGraphic = true;
            var portrait = NewImage("Portrait", mask.rectTransform, null);
            Stretch(portrait.rectTransform);
            portrait.preserveAspect = true;
            portrait.enabled = false; // empty until the portraits are supplied

            var img = NewImage("Frame", profile, frame);
            Stretch(img.rectTransform);
            side.FindPropertyRelative("portrait").objectReferenceValue = portrait;
        }

        static void BuildCoins(RectTransform root, bool p1, Sprite unlit, SerializedProperty side)
        {
            var coins = side.FindPropertyRelative("coins");
            coins.arraySize = 2;
            float sign = p1 ? -1f : 1f;
            for (int i = 0; i < 2; i++)
            {
                var c = NewImage($"{(p1 ? "P1" : "CPU")} Coin {i + 1}", root, unlit);
                Place(c.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(sign * (i == 0 ? CoinOuter : CoinInner), -CoinY), new Vector2(40f, 40f));
                coins.GetArrayElementAtIndex(i).objectReferenceValue = c;
            }
        }

        static void BuildCombo(RectTransform root, bool p1, TMP_FontAsset font, Material wordMat, Material numMat, SerializedProperty side)
        {
            var combo = NewRect(p1 ? "P1 Combo" : "CPU Combo", root);
            var a = new Vector2(p1 ? 0f : 1f, 1f);
            Place(combo, a, a, new Vector2(p1 ? ComboMargin : -ComboMargin, -ComboY), new Vector2(900f, 150f));
            combo.pivot = new Vector2(p1 ? 0f : 1f, 0.5f);
            var inner = new Vector2(p1 ? 0f : 1f, 0.5f);
            var align = p1 ? TextAlignmentOptions.MidlineLeft : TextAlignmentOptions.MidlineRight;

            var word = NewText("Word", combo, font, wordMat, 40f, Cream, align);
            Place(word.rectTransform, inner, inner, Vector2.zero, new Vector2(620f, 70f));
            word.rectTransform.pivot = inner;
            word.text = "Light combo";
            AddDepth(word, Brown);

            var num = NewText("Number", combo, font, numMat, 100f, Color.white, align);
            Place(num.rectTransform, inner, inner, new Vector2(p1 ? 300f : 0f, 0f), new Vector2(180f, 150f));
            num.rectTransform.pivot = inner;
            num.enableVertexGradient = true;
            num.colorGradient = new VertexGradient(NumTop, NumTop, NumBottom, NumBottom);
            num.text = "3";
            AddDepth(num, Orange);

            side.FindPropertyRelative("comboRoot").objectReferenceValue = combo;
            side.FindPropertyRelative("comboWord").objectReferenceValue = word;
            side.FindPropertyRelative("comboNumber").objectReferenceValue = num;
        }

        static void BuildBanners(RectTransform root, bool p1, Sprite border, TMP_FontAsset font, SerializedProperty side)
        {
            var list = side.FindPropertyRelative("banners");
            list.arraySize = 3;
            var a = new Vector2(p1 ? 0f : 1f, 1f);
            for (int i = 0; i < 3; i++)
            {
                var b = NewRect($"{(p1 ? "P1" : "CPU")} Status {i + 1}", root);
                Place(b, a, a, new Vector2(0f, -(BannerTop + i * BannerStep)), new Vector2(458f, 74f));
                b.pivot = a;
                b.anchoredPosition = new Vector2(0f, -(BannerTop + i * BannerStep));

                var img = NewImage("Border", b, border);
                img.type = Image.Type.Sliced;
                Stretch(img.rectTransform);
                if (!p1) img.rectTransform.localScale = new Vector3(-1f, 1f, 1f); // notch toward the middle

                var t = NewText("Text", b, font, null, 40f, p1 ? StatusP1 : StatusCpu, p1 ? TextAlignmentOptions.MidlineLeft : TextAlignmentOptions.MidlineRight);
                var rt = Stretch(t.rectTransform);
                rt.offsetMin = new Vector2(p1 ? 27f : 60f, 0f);
                rt.offsetMax = new Vector2(p1 ? -60f : -30f, 0f);
                t.text = p1 ? "BLOCKSTUN" : "BLOCK";
                list.GetArrayElementAtIndex(i).objectReferenceValue = b;
            }
        }

        static void AddDepth(TMP_Text text, Color colour)
        {
            var d = Undo.AddComponent<HudTextDepth>(text.gameObject);
            d.Colour = colour;
            EditorUtility.SetDirty(d);
        }

        // ───────────── Fonts ─────────────

        static TMP_FontAsset EnsureFont(string file)
        {
            string path = $"{FontFolder}/{file} SDF.asset";
            var fa = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);
            if (fa) return fa;
            var font = AssetDatabase.LoadAssetAtPath<Font>($"{FontFolder}/{file}.ttf");
            if (!font) { EditorUtility.DisplayDialog(Title, $"Missing {FontFolder}/{file}.ttf.", "OK"); return null; }

            fa = TMP_FontAsset.CreateFontAsset(font, 90, 9, GlyphRenderMode.SDFAA, 1024, 1024, AtlasPopulationMode.Dynamic, true);
            if (!fa) { EditorUtility.DisplayDialog(Title, $"Couldn't make a font asset from {file}.ttf.", "OK"); return null; }
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

        /// <summary>
        /// Outline preset: px of outline OUTSIDE the letters at this font size, face kept at its normal width
        /// (the outline is centred on the edge, so the face is dilated by the same amount).
        /// </summary>
        static Material Preset(TMP_FontAsset fa, string label, Color outline, float size, float px)
        {
            string path = $"{FontFolder}/{fa.name} {label}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            bool created = !mat;
            if (created) mat = new Material(fa.material);
            else mat.CopyPropertiesFromMaterial(fa.material);
            mat.name = $"{fa.name} {label}";

            float gradient = fa.atlasPadding + 1f;
            float pxPerUnit = gradient * size / Mathf.Max(1f, fa.faceInfo.pointSize);
            float w = Mathf.Clamp(px / pxPerUnit, 0f, 1f);
            mat.EnableKeyword(ShaderUtilities.Keyword_Outline);
            mat.SetFloat(ShaderUtilities.ID_OutlineWidth, w);
            mat.SetColor(ShaderUtilities.ID_OutlineColor, outline);
            mat.SetFloat(ShaderUtilities.ID_FaceDilate, w);
            ShaderUtilities.UpdateShaderRatios(mat);

            if (created) AssetDatabase.CreateAsset(mat, path);
            else EditorUtility.SetDirty(mat);
            return mat;
        }

        // ───────────── Sprites ─────────────

        static Sprite ImportSprite(string file, Vector4 border)
        {
            string path = $"{SpriteFolder}/{file}.png";
            var ti = AssetImporter.GetAtPath(path) as TextureImporter;
            if (!ti) { EditorUtility.DisplayDialog(Title, $"Missing {path}.", "OK"); return null; }
            ti.textureType = TextureImporterType.Sprite;
            ti.spriteImportMode = SpriteImportMode.Single;
            ti.spritePixelsPerUnit = 100f;
            ti.mipmapEnabled = false;
            ti.alphaIsTransparency = true;
            ti.wrapMode = TextureWrapMode.Clamp;
            ti.filterMode = FilterMode.Bilinear;
            ti.textureCompression = TextureImporterCompression.CompressedHQ;
            ti.maxTextureSize = 1024;
            ti.spriteBorder = border;
            var s = new TextureImporterSettings();
            ti.ReadTextureSettings(s);
            s.spriteMeshType = SpriteMeshType.FullRect;
            ti.SetTextureSettings(s);
            ti.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }

        // ───────────── UI helpers ─────────────

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
            var rt = NewRect(name, parent);
            var img = rt.gameObject.AddComponent<Image>();
            img.sprite = sprite;
            img.raycastTarget = false; // the HUD never eats a touch (gestures), except the timer
            if (sprite) { img.SetNativeSize(); }
            return img;
        }

        static TextMeshProUGUI NewText(string name, Transform parent, TMP_FontAsset font, Material mat, float size, Color colour, TextAlignmentOptions align)
        {
            var rt = NewRect(name, parent);
            var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
            t.font = font;
            if (mat) t.fontSharedMaterial = mat;
            t.fontSize = size;
            t.color = colour;
            t.alignment = align;
            t.textWrappingMode = TextWrappingModes.NoWrap;
            t.overflowMode = TextOverflowModes.Overflow;
            t.raycastTarget = false;
            return t;
        }

        static RectTransform Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            return rt;
        }

        /// <summary>Horizontal stretch with left / right insets; top inset and height from the parent's top.</summary>
        static void Band(RectTransform rt, float leftInset, float rightInset, float top, float height)
        {
            rt.anchorMin = new Vector2(0f, 1f); rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.offsetMin = new Vector2(leftInset, -top - height);
            rt.offsetMax = new Vector2(-rightInset, -top);
        }

        static void Place(RectTransform rt, Vector2 anchorMin, Vector2 anchorMax, Vector2 pos, Vector2 size)
        {
            rt.anchorMin = anchorMin; rt.anchorMax = anchorMax;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = size;
            rt.anchoredPosition = pos;
        }

        static T FindByName<T>(string name) where T : Component
        {
            foreach (var c in Object.FindObjectsByType<T>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (c.name == name && c.gameObject.scene.IsValid()) return c;
            return null;
        }

        /// <summary>A descendant with this name, not already inside skip (the Legacy HUD) or the Concept HUD.</summary>
        static Transform FindChildDeep(Transform root, string name, Transform skip)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                if (t.name != name || t == root) continue;
                if (skip && t.IsChildOf(skip)) continue;
                var concept = root.Find(ConceptName);
                if (concept && t.IsChildOf(concept)) continue;
                if (t.parent != root && t.parent.name != "GDD HUD") continue; // only the HUD's own widgets
                return t;
            }
            return null;
        }

        static Color Hex(string hex)
        {
            ColorUtility.TryParseHtmlString("#" + hex, out var c);
            return c;
        }
    }
}
#endif
