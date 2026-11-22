using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using KungFuCrashout.Core;
using KungFuCrashout.UI;

namespace KungFuCrashout.EditorTools
{
    /// <summary>
    /// Window → KungFu Crashout → GDD Build → Build Win-Lose Screen (Concepts 25 + 26)
    ///
    /// Builds the match-end screen in the open FightScene from GamConcepts_UI_25 (DEFEAT) and _26 (VICTORY), 2424 × 1080:
    ///   End Screen (EndScreenConcept, on the root canvas above the HUD, below the pause menu)
    ///     Screen (hidden until the match ends)
    ///       Blur        — the fight scene, captured and blurred at the end of the match (full screen)
    ///       Background  — the translucent Victory / Defeat background (envelopes the screen)
    ///       Safe Area   — Stage (StageFit 2424 × 1080, scaled to fit any phone) with the banner (CoverWidth: reaches
    ///                     both screen edges on wide phones), names + round coins, record / streak, START · CHANGE FIGHTER ·
    ///                     REMATCH (glowing).
    /// Text (fightHUD_info.txt): names Tillana Bold 30 (winner yellow, loser grey), record / streak Tillana Medium 40,
    /// buttons Tillana SemiBold 46. Positions measured from the concepts.
    /// The old Match Over panel is kept and hidden while the new screen is in use (End Screen → Use Concept End Screen).
    /// Re-running rebuilds the End Screen. Ctrl+Z undoes it. Save the scene afterwards.
    ///
    /// Place in: Assets/_Project/Editor/EndScreenSetup.cs
    /// </summary>
    public static class EndScreenSetup
    {
        const string Art = "Assets/_Project/Art/UI/EndScreen";
        const string HudArt = "Assets/_Project/Art/UI/FightHUD";
        const string Fonts = "Assets/_Project/Art/UI/Fonts";
        const string RootName = "End Screen";

        static readonly Color Ink = Hex("4E3B1D");       // button text
        static readonly Color Cream = Hex("F3E7CF");     // record / streak
        static readonly Color Winner = Hex("ECD998");
        static readonly Color Loser = Hex("C8CAC9");
        static readonly Color Brown = Hex("3A3028");     // callout outline / depth (as the fight HUD)

        [MenuItem("Window/KungFu Crashout/GDD Build/Build Win-Lose Screen (Concepts 25 + 26)", priority = 10046)]
        static void Build()
        {
            var match = Object.FindAnyObjectByType<MatchManager>(FindObjectsInactive.Include);
            var canvas = FindCanvas();
            if (!match || !canvas)
            {
                EditorUtility.DisplayDialog("Win-Lose Screen", "Open FightScene first (needs the MatchManager and the UI canvas).", "OK");
                return;
            }

            // Art
            var victoryBg = ImportSprite($"{Art}/UI_FightHUD_VictoryScreen_Background.png", 4096);
            var defeatBg = ImportSprite($"{Art}/UI_FightHUD_DefeatScreen_Background.png", 4096);
            var victoryBanner = ImportSprite($"{Art}/UI_FightHUD_VictoryScreen_Banner.png", 4096);
            var defeatBanner = ImportSprite($"{Art}/UI_FightHUD_DefeatScreen_Banner.png", 4096);
            var plate1 = ImportSprite($"{Art}/UI_FightHUD_EndScreen_Border1.png", 512);
            var plate2 = ImportSprite($"{Art}/UI_FightHUD_EndScreen_Border2.png", 512);
            var plate3 = ImportSprite($"{Art}/UI_FightHUD_EndScreen_Border3.png", 512);
            var glow = ImportSprite($"{Art}/UI_FightHUD_EndScreen_Border3_Glow.png", 1024);
            var coinLit = AssetDatabase.LoadAssetAtPath<Sprite>($"{HudArt}/UI_FightHUD_Coin_Lit.png");
            var coinUnlit = AssetDatabase.LoadAssetAtPath<Sprite>($"{HudArt}/UI_FightHUD_Coin_Unlit.png");

            var tillanaBold = Font("Tillana-Bold");
            var tillanaMedium = Font("Tillana-Medium");
            var tillanaSemi = Font("Tillana-SemiBold");
            var aladin = Font("Aladin-Regular");
            var calloutMat = AssetDatabase.LoadAssetAtPath<Material>($"{Fonts}/Aladin-Regular SDF Callout.mat");

            Undo.SetCurrentGroupName("Build Win-Lose Screen");
            int undo = Undo.GetCurrentGroup();

            var old = canvas.transform.Find(RootName);
            if (old) Undo.DestroyObjectImmediate(old.gameObject);

            // Root (always on: listens for the match end) — above the HUD and stamina bars, below the pause menu.
            var root = Stretch(NewRect(RootName, canvas.transform));
            var options = Object.FindAnyObjectByType<OptionsMenu>(FindObjectsInactive.Include);
            if (options && options.transform.parent == canvas.transform) root.SetSiblingIndex(options.transform.GetSiblingIndex());
            else root.SetAsLastSibling();

            var screen = Stretch(NewRect("Screen", root));
            var group = screen.gameObject.AddComponent<CanvasGroup>();

            // Blurred fight scene (blocks touches to the fight underneath)
            var blurRt = Stretch(NewRect("Blur", screen));
            var blur = blurRt.gameObject.AddComponent<RawImage>();
            blur.color = Color.black;
            blur.raycastTarget = true;

            // Translucent background, covering the whole screen at its own shape
            var bgRt = NewRect("Background", screen);
            var bg = bgRt.gameObject.AddComponent<Image>();
            bg.sprite = defeatBg;
            bg.raycastTarget = false;
            var fitter = bgRt.gameObject.AddComponent<AspectRatioFitter>();
            fitter.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
            fitter.aspectRatio = 2424f / 1080f;

            // Content: safe area → concept-sized stage scaled to fit
            var safe = Stretch(NewRect("Safe Area", screen));
            safe.gameObject.AddComponent<SafeAreaFitter>();
            var stage = NewRect("Stage", safe);
            stage.anchorMin = stage.anchorMax = stage.pivot = new Vector2(0.5f, 0.5f);
            stage.sizeDelta = new Vector2(2424f, 1080f);
            stage.gameObject.AddComponent<StageFit>();

            // Banner (top-centre; size and top set per result at runtime)
            var bannerRt = NewRect("Banner", stage);
            bannerRt.anchorMin = bannerRt.anchorMax = bannerRt.pivot = new Vector2(0.5f, 1f);
            bannerRt.sizeDelta = new Vector2(2424f, 672f);
            bannerRt.anchoredPosition = new Vector2(0f, -46f);
            var banner = bannerRt.gameObject.AddComponent<Image>();
            banner.sprite = defeatBanner;
            banner.raycastTarget = false;
            bannerRt.gameObject.AddComponent<CoverWidth>();

            // Draw (no art): the callout style where the banner word would be
            var draw = Txt("Draw Text", stage, aladin, 160f, Color.white, TextAlignmentOptions.Midline, 1212f, 345f, 900f, 220f, 0.5f);
            draw.text = "DRAW";
            if (calloutMat) draw.fontSharedMaterial = calloutMat;
            var depth = Undo.AddComponent<HudTextDepth>(draw.gameObject);
            depth.Colour = Brown;
            draw.gameObject.SetActive(false);

            // Names + round coins (inner coin next to the name first)
            var pName = Txt("Player Name", stage, tillanaBold, 30f, Loser, TextAlignmentOptions.MidlineRight, 922f, 622f, 600f, 50f, 1f);
            pName.text = "Player name";
            var oName = Txt("Opponent Name", stage, tillanaBold, 30f, Winner, TextAlignmentOptions.MidlineLeft, 1527f, 622f, 600f, 50f, 0f);
            oName.text = "Opponent";
            var pCoins = new[] { Coin("Player Coin 1", stage, coinLit, 990f), Coin("Player Coin 2", stage, coinUnlit, 1042f) };
            var oCoins = new[] { Coin("Opponent Coin 1", stage, coinLit, 1460f), Coin("Opponent Coin 2", stage, coinUnlit, 1406f) };

            // Record / streak: two left-aligned lines, the block centred where the concept has it
            var block = NewRect("Record Block", stage);
            block.anchorMin = block.anchorMax = new Vector2(0f, 1f);
            block.pivot = new Vector2(0.5f, 0.5f);
            block.anchoredPosition = new Vector2(1224f, -734f);
            var vlg = block.gameObject.AddComponent<VerticalLayoutGroup>();
            vlg.childAlignment = TextAnchor.UpperLeft;
            vlg.childControlWidth = vlg.childControlHeight = true;
            vlg.childForceExpandWidth = vlg.childForceExpandHeight = false;
            vlg.spacing = 0f;
            var fit = block.gameObject.AddComponent<ContentSizeFitter>();
            fit.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var record = Line("Record", block, tillanaMedium, "Record: 15W · 0L");
            var streak = Line("Streak", block, tillanaMedium, "Streak: 15 (best 15)");

            // Buttons
            var start = Plate("Start", stage, plate1, 806f, tillanaSemi, new[] { ("START", -3f) });
            var change = Plate("Change Fighter", stage, plate2, 1212f, tillanaSemi, new[] { ("CHANGE", 23f), ("FIGHTER", -23f) });
            var glowImg = Img("Rematch Glow", stage, glow, 1618f, 935f, 814f, 396f);
            var rematch = Plate("Rematch", stage, plate3, 1618f, tillanaSemi, new[] { ("REMATCH", -1f) });

            // Controller
            var end = Undo.AddComponent<EndScreenConcept>(root.gameObject);
            var so = new SerializedObject(end);
            Set(so, "match", match);
            var hud = Object.FindAnyObjectByType<MatchHUD>(FindObjectsInactive.Include);
            if (hud) Set(so, "legacyPanel", new SerializedObject(hud).FindProperty("matchOverPanel").objectReferenceValue);
            Set(so, "screen", screen.gameObject);
            Set(so, "group", group);
            Set(so, "blur", blur);
            Set(so, "background", bg);
            Set(so, "content", stage);
            Set(so, "victoryBackground", victoryBg);
            Set(so, "defeatBackground", defeatBg);
            Set(so, "banner", banner);
            Set(so, "victoryBanner", victoryBanner);
            Set(so, "defeatBanner", defeatBanner);
            Set(so, "drawText", draw);
            Set(so, "playerName", pName);
            Set(so, "opponentName", oName);
            SetArray(so, "playerCoins", pCoins);
            SetArray(so, "opponentCoins", oCoins);
            Set(so, "coinLit", coinLit);
            Set(so, "coinUnlit", coinUnlit);
            Set(so, "recordText", record);
            Set(so, "streakText", streak);
            Set(so, "startButton", start);
            Set(so, "changeFighterButton", change);
            Set(so, "rematchButton", rematch);
            Set(so, "rematchGlow", glowImg);
            so.FindProperty("winnerColour").colorValue = Winner;
            so.FindProperty("loserColour").colorValue = Loser;
            so.FindProperty("drawColour").colorValue = Cream;
            so.ApplyModifiedPropertiesWithoutUndo();

            screen.gameObject.SetActive(false);
            Undo.CollapseUndoOperations(undo);
            EditorSceneManager.MarkSceneDirty(canvas.gameObject.scene);

            var missing = new System.Collections.Generic.List<string>();
            if (!victoryBg || !defeatBg || !victoryBanner || !defeatBanner || !plate1 || !plate2 || !plate3 || !glow) missing.Add("end-screen art (Art/UI/EndScreen)");
            if (!coinLit || !coinUnlit) missing.Add("coin sprites (Art/UI/FightHUD)");
            if (!tillanaBold || !tillanaMedium || !tillanaSemi || !aladin) missing.Add("fonts (run Apply Fight HUD Skin first)");
            Debug.Log("[GDD Build] Win-lose screen built (Concepts 25 + 26). Shows at the end of the match: VICTORY when the player wins, " +
                      "DEFEAT when the player loses, DRAW in the callout style for a draw. " +
                      (hud ? "The old Match Over panel is kept and hidden (End Screen → Use Concept End Screen). " : "") +
                      (missing.Count > 0 ? "Missing: " + string.Join(", ", missing) + ". " : "") +
                      "Preview in Play mode: right-click End Screen's component → Preview Victory / Defeat / Draw. Save the scene.");
            Selection.activeGameObject = root.gameObject;
        }

        // ───────────── Pieces ─────────────

        /// <summary>A button plate at its native size, centred at (x, 935), with one or more lines of label.</summary>
        static Button Plate(string name, RectTransform stage, Sprite sprite, float x, TMP_FontAsset font, (string text, float dy)[] lines)
        {
            var img = Img(name, stage, sprite, x, 935f, sprite ? sprite.rect.width : 327f, sprite ? sprite.rect.height : 138f);
            img.raycastTarget = true;
            var b = Undo.AddComponent<Button>(img.gameObject);
            var colors = b.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1f, 0.97f, 0.9f);
            colors.pressedColor = new Color(0.82f, 0.78f, 0.7f);
            colors.selectedColor = Color.white;
            colors.disabledColor = new Color(0.8f, 0.8f, 0.8f, 0.85f);
            b.colors = colors;
            var nav = b.navigation;
            nav.mode = Navigation.Mode.None;
            b.navigation = nav;
            foreach (var (text, dy) in lines)
            {
                var t = Txt(lines.Length > 1 ? $"Label {text}" : "Label", img.rectTransform, font, 46f, Ink, TextAlignmentOptions.Midline, 0f, 0f, 400f, 60f, 0.5f);
                var rt = t.rectTransform;
                rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
                rt.anchoredPosition = new Vector2(0f, dy);
                t.text = text;
            }
            return b;
        }

        static Image Coin(string name, RectTransform stage, Sprite sprite, float x) => Img(name, stage, sprite, x, 620f, 40f, 40f);

        static TextMeshProUGUI Line(string name, RectTransform block, TMP_FontAsset font, string text)
        {
            var rt = NewRect(name, block);
            var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
            t.font = font;
            t.fontSize = 40f;
            t.color = Cream;
            t.alignment = TextAlignmentOptions.MidlineLeft;
            t.textWrappingMode = TextWrappingModes.NoWrap;
            t.raycastTarget = false;
            t.text = text;
            var le = rt.gameObject.AddComponent<LayoutElement>();
            le.preferredHeight = 55f; // the concept's line spacing (centres 707 and 761)
            return t;
        }

        /// <summary>Image centred at concept (cx, cy) from the stage's top-left, size w × h.</summary>
        static Image Img(string name, RectTransform stage, Sprite sprite, float cx, float cy, float w, float h)
        {
            var rt = NewRect(name, stage);
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(w, h);
            rt.anchoredPosition = new Vector2(cx, -cy);
            var img = rt.gameObject.AddComponent<Image>();
            img.sprite = sprite;
            img.raycastTarget = false;
            if (!sprite) img.color = new Color(1f, 1f, 1f, 0f); // missing art: blank
            return img;
        }

        /// <summary>Text at concept (x, cy): pivotX 1 = right edge at x, 0 = left edge at x, 0.5 = centred.</summary>
        static TextMeshProUGUI Txt(string name, RectTransform parent, TMP_FontAsset font, float size, Color colour,
                                   TextAlignmentOptions align, float x, float cy, float w, float h, float pivotX)
        {
            var rt = NewRect(name, parent);
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(pivotX, 0.5f);
            rt.sizeDelta = new Vector2(w, h);
            rt.anchoredPosition = new Vector2(x, -cy);
            var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
            if (font) t.font = font;
            t.fontSize = size;
            t.color = colour;
            t.alignment = align;
            t.textWrappingMode = TextWrappingModes.NoWrap;
            t.overflowMode = TextOverflowModes.Overflow;
            t.raycastTarget = false;
            return t;
        }

        // ───────────── Helpers ─────────────

        static Sprite ImportSprite(string path, int max)
        {
            var ti = AssetImporter.GetAtPath(path) as TextureImporter;
            if (!ti) { Debug.LogWarning($"[Win-Lose Screen] Missing {path}: left blank."); return null; }
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

        static TMP_FontAsset Font(string file)
        {
            var fa = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>($"{Fonts}/{file} SDF.asset");
            if (!fa) Debug.LogWarning($"[Win-Lose Screen] Missing {Fonts}/{file} SDF.asset (run Apply Fight HUD Skin once).");
            return fa;
        }

        static Canvas FindCanvas()
        {
            var options = Object.FindAnyObjectByType<OptionsMenu>(FindObjectsInactive.Include);
            if (options)
            {
                var c = options.GetComponentInParent<Canvas>(true);
                if (c) return c.rootCanvas;
            }
            foreach (var c in Object.FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (c.isRootCanvas && c.gameObject.scene.IsValid()) return c;
            return null;
        }

        static RectTransform NewRect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            Undo.RegisterCreatedObjectUndo(go, name);
            go.layer = parent.gameObject.layer;
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        static RectTransform Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            return rt;
        }

        static void Set(SerializedObject so, string prop, Object value)
        {
            var p = so.FindProperty(prop);
            if (p != null) p.objectReferenceValue = value;
            else Debug.LogWarning($"[Win-Lose Screen] No field '{prop}' on {so.targetObject.GetType().Name}.");
        }

        static void SetArray(SerializedObject so, string prop, Object[] values)
        {
            var p = so.FindProperty(prop);
            if (p == null) return;
            p.arraySize = values.Length;
            for (int i = 0; i < values.Length; i++) p.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
        }

        static Color Hex(string hex) => ColorUtility.TryParseHtmlString("#" + hex, out var c) ? c : Color.white;
    }
}
