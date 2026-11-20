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
    /// Menu: Window → KungFu Crashout → GDD Build → Build Level Select (Concept 20)   (open the LevelSelect scene first)
    /// Builds "Choose your level" from GamConcepts_UI_20 + Level Select UI.txt, the same way as the Roster:
    ///   • Art/UI/LevelSelect imported as UI sprites; the Roster's back lantern, arrows and small P1 / P2 markers reused
    ///     (markers rotated upright as in the concept).
    ///   • Concept layout at 2424 × 1080 in StageFit layers inside the safe area (main layer centred, back + title hanging
    ///     from the top), scaled uniformly to fit any phone.
    ///   • Canvas (1920 × 1080, match height, like the other scenes) and an EventSystem are created if the scene has none.
    ///   • Stand-ins until their own art arrives: background = the Roster's mountains, Restaurant scroll illustration = the
    ///     fight scene's background (GamConcepts_Restaurant_08_WIP_2), READY = the Roster's scroll buttons (short / long).
    ///   • Stages, in card order: Restaurant (playable → FightScene), Street Market, Dojo, Ship, Theatre (coming soon).
    ///   • Adds LevelSelect to Build Settings after the Roster, and points the Roster's READY here (CharacterSelectMenu and
    ///     the legacy RosterMenu → Fight Scene = "LevelSelect"), saving the Roster scene.
    /// Missing art is left blank (see the Console line). Re-running rebuilds the screen.
    /// Place in: Assets/_Project/Editor/LevelSelectSetup.cs
    /// </summary>
    public static class LevelSelectSetup
    {
        const string Title = "Level Select";
        const string Art = "Assets/_Project/Art/UI/LevelSelect";
        const string CsArt = "Assets/_Project/Art/UI/CharacterSelect";
        const string FontFolder = "Assets/_Project/Art/UI/Fonts";
        const string RootName = "Level Select (Concept 20)";
        const string ScenePath = "Assets/_Project/Scenes/LevelSelect.unity";
        const string RosterPath = "Assets/_Project/Scenes/Roster.unity";
        const float W = 2424f, H = 1080f;

        static readonly Color Cream = Hex("ECD998");
        static readonly Color LocationCream = Hex("E6D8C2");

        // Card order in the concept, with the location names (only the Restaurant's is in the concept; the rest are placeholders).
        // Restaurant: the fight scene's background, framed as in the concept (25 % of full size = window width × 1.066,
        // showing its lower part: focus 0.24).
        static readonly (string file, string name, string id, bool playable, float focus, float zoom)[] Stages =
        {
            ("Restaurant",   "Fancy Chinese Restaurant", "Restaurant",   true,  0.24f, 1.066f),
            ("StreetMarket", "Street Market",            "StreetMarket", false, 0.45f, 1f),
            ("Dojo",         "Dojo",                     "Dojo",         false, 0.45f, 1f),
            ("Ship",         "Ship",                     "Ship",         false, 0.45f, 1f),
            ("Theatre",      "Theatre",                  "Theatre",      false, 0.45f, 1f),
        };
        // Stand-ins (reused, not re-imported, so the fight scene's import settings stay as they are).
        const string RestaurantDisplay = "Assets/_Project/Art/GamConcepts_Restaurant_08_WIP_2.jpg";

        [MenuItem("Window/KungFu Crashout/GDD Build/Build Level Select (Concept 20)", priority = 10062)]
        public static void Build()
        {
            if (EditorApplication.isPlaying) { EditorUtility.DisplayDialog(Title, "Stop Play mode first.", "OK"); return; }
            var scene = EditorSceneManager.GetActiveScene();
            if (scene.path != ScenePath &&
                !EditorUtility.DisplayDialog(Title, $"The open scene is {scene.name}, not LevelSelect. Build here anyway?", "Build here", "Cancel"))
                return;

            var tillana = Font("Tillana-Bold");
            var aladin = Font("Aladin-Regular");
            var barlow = Font("Barlow-SemiBold");
            if (!tillana || !aladin)
            {
                EditorUtility.DisplayDialog(Title, "Tillana-Bold SDF / Aladin-Regular SDF not found in Art/UI/Fonts. Run Apply Fight HUD Skin " +
                                                   "or Build Character Select once first (they create the font assets).", "OK");
                return;
            }

            var missing = new List<string>();
            Sprite S(string folder, string n, bool note = true)
            {
                var sp = ImportSprite($"{folder}/{n}.png", n.EndsWith("Placeholder") ? 4096 : 2048);
                if (!sp && note) missing.Add(n);
                return sp;
            }
            var back = S(CsArt, "UI_CharacterSelect_Back");
            var arrow = S(CsArt, "UI_CharacterSelect_Arrow");
            var smallG = S(CsArt, "UI_CharacterSelect_PlayerMarker_Small_Green");
            var smallR = S(CsArt, "UI_CharacterSelect_PlayerMarker_Small_Red");
            var title = S(Art, "UI_LevelSelect_ChooseYourLevel");
            var backing = S(Art, "UI_LevelSelect_Scroll_Backing");
            var frame = S(Art, "UI_LevelSelect_Scroll_Frame");
            var glow = S(Art, "UI_LevelSelect_Card_Glow");

            // ── Canvas + EventSystem ──
            var canvas = EnsureCanvas();
            EnsureEventSystem();

            Undo.SetCurrentGroupName("Build Level Select");
            int group = Undo.GetCurrentGroup();
            var old = canvas.transform.Find(RootName);
            if (old) Undo.DestroyObjectImmediate(old.gameObject);
            var root = Stretch(NewRect(RootName, canvas.transform));
            var menu = Undo.AddComponent<LevelSelectMenu>(root.gameObject);

            // Background: the Roster's mountain background (stand-in), filling the whole screen.
            var bgSprite = S(CsArt, "UI_CharacterSelect_Background");
            var bg = NewImage("Background (Roster mountains, stand-in)", root, bgSprite);
            Stretch(bg.rectTransform);
            var fitter = Undo.AddComponent<AspectRatioFitter>(bg.gameObject);
            fitter.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
            fitter.aspectRatio = W / H;

            var safe = Stretch(NewRect("Safe Area", root));
            Undo.AddComponent<SafeAreaFitter>(safe.gameObject);
            var main = NewStage("Stage (main)", safe, new Vector2(0.5f, 0.5f));
            var top = NewStage("Stage (top)", safe, new Vector2(0.5f, 1f));

            // ── Scroll ──
            Img("Scroll Backing", main, backing, 172, 210, 2080, 560);
            var window = NewRect("Window", main);
            Box(window, 232, 218, 1958, 520);
            Undo.AddComponent<RectMask2D>(window.gameObject);
            var display = NewImage("Display", window, null);
            Stretch(display.rectTransform);
            Img("Scroll Frame", main, frame, 171, 160, 2082, 657);

            var location = Txt("Location", main, tillana, 30, LocationCream, TextAlignmentOptions.MidlineRight);
            Anchor(location.rectTransform, new Vector2(1f, 0.5f), new Vector2(1953f, -700f), new Vector2(900f, 50f));
            location.text = "Fancy Chinese Restaurant";

            var scrollP2 = Marker("Scroll P2 Marker", main, smallR, new Vector2(404f, -296f), 45f, "P2", tillana, 30f, true);
            var scrollP1 = Marker("Scroll P1 Marker", main, smallG, new Vector2(307f, -364f), 55f, "P1", tillana, 30f, true);

            var la = MakeButton(Img("Arrow Left", main, arrow, 166 - 54.5f, 476 - 54.5f, 109, 109));
            var raImg = Img("Arrow Right", main, arrow, 2258 - 54.5f, 476 - 54.5f, 109, 109);
            raImg.rectTransform.localScale = new Vector3(-1f, 1f, 1f);
            var ra = MakeButton(raImg);

            // ── Cards ──
            var cardsRoot = NewRect("Cards", main);
            Stretch(cardsRoot);
            float[] xs = { 854f, 1037f, 1222f, 1404f, 1587f };
            var cardParts = new List<(RectTransform root, Image art, Image glow, RectTransform p1, RectTransform p2, Button b)>();
            var cardSprites = new List<(Sprite sel, Sprite unsel, Sprite display)>();
            for (int i = 0; i < Stages.Length; i++)
            {
                var st = Stages[i];
                var sel = S(Art, $"UI_LevelSelect_Card_{st.file}_Selected");
                var unsel = S(Art, $"UI_LevelSelect_Card_{st.file}_Unselected");
                var disp = st.file == "Restaurant" ? AssetDatabase.LoadAssetAtPath<Sprite>(RestaurantDisplay)
                                                   : S(Art, $"UI_LevelSelect_{st.file}_Placeholder", false);
                if (!disp) missing.Add($"{st.file} display illustration (UI_LevelSelect_{st.file}_Placeholder)");
                cardSprites.Add((sel, unsel, disp));

                var card = NewRect($"Card {st.file}", cardsRoot);
                Anchor(card, new Vector2(0.5f, 0.5f), new Vector2(xs[i], -922f), new Vector2(170f, 233f));
                var g = NewImage("Glow", card, glow);
                Anchor(g.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(292f, 336f), centred: true);
                var art = NewImage("Art", card, unsel);
                Stretch(art.rectTransform);
                var b = MakeButton(art);
                var p1 = Marker("P1 Marker", card, smallG, new Vector2(0f, -68f), 55f, "P1", tillana, 26f, false);
                var p2 = Marker("P2 Marker", card, smallR, new Vector2(0f, -68f), 45f, "P2", tillana, 26f, false);
                cardParts.Add((card, art, g, p1, p2, b));
            }

            // ── Ready: the Roster's scroll buttons stand in for the Online 1v1 READY art (short = READY, long = waiting) ──
            var readyArt = S(CsArt, "UI_CharacterSelect_Scroll_Base_Ready");
            var waitingArt = S(CsArt, "UI_CharacterSelect_Scroll_Base_Waiting");
            var ready = NewImage("Ready", main, readyArt);
            // Right edge where the concept's button ends (x 2354), 120 high; the width follows the art.
            Anchor(ready.rectTransform, new Vector2(1f, 0.5f), new Vector2(2354f, -968f), new Vector2(396f, 120f));
            ready.enabled = true;
            var readyBtn = MakeButton(ready);
            var readyText = Txt("Text", ready.rectTransform, aladin, 60, Hex("401D0E"), TextAlignmentOptions.Midline);
            Anchor(readyText.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(520f, 110f), centred: true);
            readyText.text = "READY";
            var wave = Undo.AddComponent<TmpWave>(readyText.gameObject);
            wave.enabled = false;

            var toast = Txt("Toast", main, barlow ? barlow : tillana, 30, Cream, TextAlignmentOptions.Midline);
            Anchor(toast.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(1212f, -770f), new Vector2(1200f, 50f));
            toast.text = "";

            // ── Top layer ──
            var backBtn = MakeButton(Img("Back", top, back, 35, 0, 132, 218));
            Img("Title", top, title, 881, 75, 664, 98);

            // ── Wire ──
            var so = new SerializedObject(menu);
            var stagesProp = so.FindProperty("stages");
            stagesProp.arraySize = Stages.Length;
            var cardsProp = so.FindProperty("cards");
            cardsProp.arraySize = Stages.Length;
            for (int i = 0; i < Stages.Length; i++)
            {
                var e = stagesProp.GetArrayElementAtIndex(i);
                e.FindPropertyRelative("displayName").stringValue = Stages[i].name;
                e.FindPropertyRelative("id").stringValue = Stages[i].id;
                e.FindPropertyRelative("playable").boolValue = Stages[i].playable;
                e.FindPropertyRelative("cardSelected").objectReferenceValue = cardSprites[i].sel;
                e.FindPropertyRelative("cardUnselected").objectReferenceValue = cardSprites[i].unsel;
                e.FindPropertyRelative("display").objectReferenceValue = cardSprites[i].display;
                e.FindPropertyRelative("displayFocus").floatValue = Stages[i].focus;
                e.FindPropertyRelative("displayZoom").floatValue = Stages[i].zoom;

                var c = cardsProp.GetArrayElementAtIndex(i);
                var p = cardParts[i];
                c.FindPropertyRelative("root").objectReferenceValue = p.root;
                c.FindPropertyRelative("art").objectReferenceValue = p.art;
                c.FindPropertyRelative("glow").objectReferenceValue = p.glow;
                c.FindPropertyRelative("p1Marker").objectReferenceValue = p.p1;
                c.FindPropertyRelative("p2Marker").objectReferenceValue = p.p2;
                c.FindPropertyRelative("button").objectReferenceValue = p.b;
            }
            so.FindProperty("window").objectReferenceValue = window;
            so.FindProperty("display").objectReferenceValue = display;
            so.FindProperty("locationText").objectReferenceValue = location;
            so.FindProperty("scrollP1Marker").objectReferenceValue = scrollP1.gameObject;
            so.FindProperty("scrollP2Marker").objectReferenceValue = scrollP2.gameObject;
            so.FindProperty("leftArrow").objectReferenceValue = la;
            so.FindProperty("rightArrow").objectReferenceValue = ra;
            so.FindProperty("readyButton").objectReferenceValue = readyBtn;
            so.FindProperty("readyImage").objectReferenceValue = ready;
            so.FindProperty("readyText").objectReferenceValue = readyText;
            so.FindProperty("readyFont").objectReferenceValue = aladin;
            so.FindProperty("readySprite").objectReferenceValue = readyArt;
            so.FindProperty("waitingSprite").objectReferenceValue = waitingArt;
            so.FindProperty("readyTextColour").colorValue = Hex("401D0E");
            so.FindProperty("waitingWave").objectReferenceValue = wave;
            so.FindProperty("backButton").objectReferenceValue = backBtn;
            so.FindProperty("toastText").objectReferenceValue = toast;
            so.ApplyModifiedProperties();

            Undo.CollapseUndoOperations(group);
            EditorSceneManager.MarkSceneDirty(scene);
            if (scene.path == ScenePath) EditorSceneManager.SaveScene(scene);

            AddToBuildSettings();
            RouteRosterHere();

            Debug.Log("[Level Select] Concept 20 screen built (Restaurant → FightScene; the other stages are coming soon). " +
                      (missing.Count > 0 ? $"Missing art, left blank: {string.Join("; ", missing.Distinct())}. " : "") +
                      "Stand-ins: background = the Roster's mountains, Restaurant illustration = the fight background, READY = " +
                      "the Roster's scroll buttons. Location names other than the Restaurant's are placeholders.");
        }

        // ───────────── Scene plumbing ─────────────

        static Canvas EnsureCanvas()
        {
            foreach (var c in Object.FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (c.isRootCanvas && c.gameObject.scene == EditorSceneManager.GetActiveScene()) return c;
            var go = new GameObject("Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            Undo.RegisterCreatedObjectUndo(go, "Canvas");
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

        static void EnsureEventSystem()
        {
            if (Object.FindFirstObjectByType<EventSystem>(FindObjectsInactive.Include)) return;
            var go = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
            Undo.RegisterCreatedObjectUndo(go, "EventSystem");
        }

        static void AddToBuildSettings()
        {
            var list = EditorBuildSettings.scenes.ToList();
            if (list.Any(s => s.path == ScenePath)) return;
            int at = list.FindIndex(s => s.path == RosterPath);
            list.Insert(at >= 0 ? at + 1 : list.Count, new EditorBuildSettingsScene(ScenePath, true));
            EditorBuildSettings.scenes = list.ToArray();
            Debug.Log("[Level Select] Added LevelSelect to Build Settings after the Roster.");
        }

        /// <summary>The Roster's READY now loads LevelSelect (CharacterSelectMenu and the legacy RosterMenu).</summary>
        static void RouteRosterHere()
        {
            var active = EditorSceneManager.GetActiveScene();
            if (active.path == RosterPath) { Route(active); return; }
            var roster = EditorSceneManager.OpenScene(RosterPath, OpenSceneMode.Additive);
            Route(roster);
            EditorSceneManager.CloseScene(roster, true);
            EditorSceneManager.SetActiveScene(active);
        }

        static void Route(UnityEngine.SceneManagement.Scene scene)
        {
            int n = 0;
            foreach (var go in scene.GetRootGameObjects())
            {
                foreach (var m in go.GetComponentsInChildren<CharacterSelectMenu>(true)) n += SetScene(m);
                foreach (var m in go.GetComponentsInChildren<RosterMenu>(true)) n += SetScene(m);
            }
            if (n > 0)
            {
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
                Debug.Log($"[Level Select] Roster READY → LevelSelect ({n} menu(s)); Roster scene saved.");
            }
        }

        static int SetScene(Object menu)
        {
            var so = new SerializedObject(menu);
            var p = so.FindProperty("fightScene");
            if (p == null || p.stringValue == "LevelSelect") return 0;
            p.stringValue = "LevelSelect";
            so.ApplyModifiedPropertiesWithoutUndo();
            return 1;
        }

        // ───────────── Layout helpers (concept px, y down from the top-left of the 2424 × 1080 stage) ─────────────

        static RectTransform NewStage(string name, RectTransform parent, Vector2 pivot)
        {
            var rt = NewRect(name, parent);
            rt.pivot = pivot;
            rt.anchorMin = rt.anchorMax = pivot;
            rt.sizeDelta = new Vector2(W, H);
            Undo.AddComponent<StageFit>(rt.gameObject);
            return rt;
        }

        static Image Img(string name, RectTransform stage, Sprite sprite, float x, float y, float w, float h)
        {
            var img = NewImage(name, stage, sprite);
            Box(img.rectTransform, x, y, w, h);
            return img;
        }

        /// <summary>By top-left corner and size; pivot in the middle (mirroring / scaling stay in place).</summary>
        static void Box(RectTransform rt, float x, float y, float w, float h)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(w, h);
            rt.anchoredPosition = new Vector2(x + w * 0.5f, -(y + h * 0.5f));
        }

        /// <summary>Pivot at pos: in a stage (anchor top-left, y down) or, centred = true, inside a parent (anchor centre).</summary>
        static void Anchor(RectTransform rt, Vector2 pivot, Vector2 pos, Vector2 size, bool centred = false)
        {
            var a = centred ? new Vector2(0.5f, 0.5f) : new Vector2(0f, 1f);
            rt.anchorMin = rt.anchorMax = a;
            rt.pivot = pivot;
            rt.sizeDelta = size;
            rt.anchoredPosition = pos;
        }

        /// <summary>A small P1 / P2 marker: the leaf rotated upright, its label straight.</summary>
        static RectTransform Marker(string name, RectTransform parent, Sprite sprite, Vector2 pos, float rotation, string label, TMP_FontAsset font, float size, bool inStage)
        {
            var m = NewRect(name, parent);
            var a = inStage ? new Vector2(0f, 1f) : new Vector2(0.5f, 0.5f);
            m.anchorMin = m.anchorMax = a;
            m.pivot = new Vector2(0.5f, 0.5f);
            m.sizeDelta = new Vector2(90f, 90f);
            m.anchoredPosition = pos;
            var leaf = NewImage("Leaf", m, sprite);
            leaf.rectTransform.anchorMin = leaf.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            leaf.rectTransform.sizeDelta = sprite ? sprite.rect.size : new Vector2(84f, 84f);
            leaf.rectTransform.anchoredPosition = Vector2.zero;
            leaf.rectTransform.localRotation = Quaternion.Euler(0f, 0f, rotation);
            var t = Txt("Text", m, font, size, Cream, TextAlignmentOptions.Midline);
            t.rectTransform.anchorMin = t.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            t.rectTransform.sizeDelta = new Vector2(90f, 60f);
            t.rectTransform.anchoredPosition = new Vector2(0f, -4f);
            t.text = label;
            return m;
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
            var b = Undo.AddComponent<Button>(img.gameObject);
            var colors = b.colors;
            colors.pressedColor = new Color(0.85f, 0.85f, 0.85f);
            colors.disabledColor = Color.white;
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
