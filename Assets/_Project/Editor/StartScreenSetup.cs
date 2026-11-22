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
    /// Menu: Window → KungFu Crashout → GDD Build → Build Start Screen (Concept 29)   (open the StartScreen scene first)
    /// Builds the title screen from GamConcepts_UI_29 + Start screen UI.txt, the same way as the Roster / Level Select:
    ///   • Art/UI/StartScreen imported as UI sprites. Background fills the whole screen (envelope, no bars).
    ///   • Concept layout at 2424 × 1080 in StageFit layers inside the safe area, scaled uniformly to fit any phone:
    ///       Stage (main)  — centred: logo (1201 × 548, as drawn in the concept) and Singleplayer / Multiplayer / Tutorial
    ///                       (Tillana Bold 50, brown buttons, grey Tutorial).
    ///       Stage (right) — hangs from the top-right corner: Settings, Shop, Leaderboard (trophy), Quit, 39 px from the edge,
    ///                       so on wider phones they stay at the screen edge.
    ///   • Singleplayer → Roster (StartMenu). Trophy → the existing Leaderboard popup (RecordPanel, moved into this screen).
    ///     Multiplayer, Tutorial, Settings, Shop and Quit are live buttons (press tint) with nothing hooked up yet.
    ///     Android Back / Esc still quits from this screen (StartMenu).
    ///   • The previous GDD Start Menu is kept, hidden. Restore Previous Start Screen puts it back.
    /// Re-running rebuilds the screen. Ctrl+Z undoes it. The scene is saved when it is the StartScreen.
    /// Place in: Assets/_Project/Editor/StartScreenSetup.cs
    /// </summary>
    public static class StartScreenSetup
    {
        const string Title = "Start Screen";
        const string Art = "Assets/_Project/Art/UI/StartScreen";
        const string FontFolder = "Assets/_Project/Art/UI/Fonts";
        const string RootName = "Start Screen (Concept 29)";
        const string OldRootName = "GDD Start Menu";
        const string PopupName = "Leaderboard Popup";
        const string ScenePath = "Assets/_Project/Scenes/StartScreen.unity";
        const string RosterScene = "Roster";
        const float W = 2424f, H = 1080f;

        static readonly Color BrownText = Hex("F0DEB0");   // sampled from the concept
        static readonly Color GreyText = Hex("F4E8CE");

        [MenuItem("Window/KungFu Crashout/GDD Build/Build Start Screen (Concept 29)", priority = 10064)]
        public static void Build()
        {
            if (EditorApplication.isPlaying) { EditorUtility.DisplayDialog(Title, "Stop Play mode first.", "OK"); return; }
            var scene = EditorSceneManager.GetActiveScene();
            if (scene.path != ScenePath &&
                !EditorUtility.DisplayDialog(Title, $"The open scene is {scene.name}, not StartScreen. Build here anyway?", "Build here", "Cancel"))
                return;

            var tillana = Font("Tillana-Bold");
            if (!tillana)
            {
                EditorUtility.DisplayDialog(Title, "Tillana-Bold SDF not found in Art/UI/Fonts. Run Apply Fight HUD Skin or Build Character " +
                                                   "Select once first (they create the font assets).", "OK");
                return;
            }

            var missing = new List<string>();
            Sprite S(string n, int max = 1024)
            {
                var sp = ImportSprite($"{Art}/{n}.png", max);
                if (!sp) missing.Add(n);
                return sp;
            }
            var bgSprite = S("UI_StartScreen_Background", 4096);
            var logo = S("UI_StartScreen_Logo", 2048);
            var brown = S("UI_StartScreen_Brown_Text_Button");
            var grey = S("UI_StartScreen_Grey_Text_Button");
            var settingsIcon = S("UI_StartScreen_Settings_Button", 256);
            var shopIcon = S("UI_StartScreen_Shop_Button", 256);
            var trophyIcon = S("UI_StartScreen_Trophy_Button", 256);
            var quitIcon = S("UI_StartScreen_Quit_Button", 256);

            var canvas = EnsureCanvas();
            EnsureEventSystem();

            Undo.SetCurrentGroupName("Build Start Screen");
            int group = Undo.GetCurrentGroup();

            // Previous start menu: keep it, hidden. Its leaderboard popup is reused (moved out first so a rebuild keeps it).
            var oldRoot = canvas.transform.Find(OldRootName);
            var existing = canvas.transform.Find(RootName);
            var popup = (existing ? existing.Find(PopupName) : null) ?? (oldRoot ? oldRoot.Find(PopupName) : null);
            if (popup && existing && popup.parent == existing)
                Undo.SetTransformParent(popup, oldRoot ? oldRoot : canvas.transform, "Keep Leaderboard Popup");
            if (existing) Undo.DestroyObjectImmediate(existing.gameObject);
            if (oldRoot)
            {
                Undo.RecordObject(oldRoot.gameObject, "Hide old start menu");
                oldRoot.gameObject.SetActive(false);
            }

            var root = Stretch(NewRect(RootName, canvas.transform));
            root.SetAsLastSibling();

            // Background: whole screen, concept shape kept (envelope).
            var bg = NewImage("Background", root, bgSprite);
            Stretch(bg.rectTransform);
            var fitter = Undo.AddComponent<AspectRatioFitter>(bg.gameObject);
            fitter.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
            fitter.aspectRatio = W / H;
            bg.raycastTarget = true; // nothing behind reacts to taps

            var safe = Stretch(NewRect("Safe Area", root));
            Undo.AddComponent<SafeAreaFitter>(safe.gameObject);
            var main = NewStage("Stage (main)", safe, new Vector2(0.5f, 0.5f));
            var right = NewStage("Stage (right)", safe, new Vector2(1f, 1f));

            // ── Logo + menu buttons (concept px, top-left) ──
            var logoImg = Img("Logo", main, logo, 580, 36, 1201, 548);
            logoImg.preserveAspect = true;
            var single = TextButton("Singleplayer", main, brown, tillana, BrownText, 588);
            var multi = TextButton("Multiplayer", main, brown, tillana, BrownText, 730);
            var tutorial = TextButton("Tutorial", main, grey, tillana, GreyText, 872);

            // ── Side icons (right edge) ──
            var settings = IconButton("Settings", right, settingsIcon, 44);
            var shop = IconButton("Shop", right, shopIcon, 194);
            var trophy = IconButton("Leaderboard", right, trophyIcon, 346);
            var quit = IconButton("Quit", right, quitIcon, 502);

            // Singleplayer → Roster. Quit is left empty for now (drag the Quit button into Quit Button to make it close the app).
            var sm = Undo.AddComponent<StartMenu>(root.gameObject);
            var so = new SerializedObject(sm);
            so.FindProperty("startButton").objectReferenceValue = single;
            so.FindProperty("quitButton").objectReferenceValue = null;
            so.FindProperty("fightScene").stringValue = RosterScene;
            so.ApplyModifiedPropertiesWithoutUndo();

            // Trophy → the existing leaderboard popup, on top of this screen.
            string boardNote;
            if (popup)
            {
                Undo.SetTransformParent(popup, root, "Move Leaderboard Popup");
                popup.SetAsLastSibling();
                var rec = popup.GetComponentsInChildren<Transform>(true);
                var rp = Undo.AddComponent<RecordPanel>(root.gameObject);
                var rso = new SerializedObject(rp);
                rso.FindProperty("openButton").objectReferenceValue = trophy;
                rso.FindProperty("panel").objectReferenceValue = popup.gameObject;
                rso.FindProperty("closeButton").objectReferenceValue = FindIn<Button>(rec, "Close");
                rso.FindProperty("statsText").objectReferenceValue = FindIn<TMP_Text>(rec, "Stats");
                rso.FindProperty("resetButton").objectReferenceValue = FindIn<Button>(rec, "Reset");
                rso.ApplyModifiedPropertiesWithoutUndo();
                Undo.RecordObject(popup.gameObject, "Hide popup");
                popup.gameObject.SetActive(false);
                boardNote = "Trophy → Leaderboard popup.";
            }
            else boardNote = "No Leaderboard Popup found (run GDD Build → 3. Build Roster + Start Screens once) — trophy left empty.";

            Undo.CollapseUndoOperations(group);
            EditorSceneManager.MarkSceneDirty(scene);
            if (scene.path == ScenePath) EditorSceneManager.SaveScene(scene);

            Debug.Log("[Start Screen] Concept 29 built: Singleplayer → Roster; " + boardNote +
                      " Multiplayer, Tutorial, Settings, Shop and Quit are interactable but not hooked up yet. Previous GDD Start Menu kept, hidden." +
                      (missing.Count > 0 ? $" Missing art, left blank: {string.Join("; ", missing)}." : ""));
        }

        [MenuItem("Window/KungFu Crashout/GDD Build/Restore Previous Start Screen", priority = 10065)]
        public static void Restore()
        {
            if (EditorApplication.isPlaying) { EditorUtility.DisplayDialog(Title, "Stop Play mode first.", "OK"); return; }
            var canvas = EnsureCanvasOrNull();
            var root = canvas ? canvas.transform.Find(RootName) : null;
            if (!root) { EditorUtility.DisplayDialog(Title, "No Start Screen (Concept 29) in the open scene.", "OK"); return; }

            Undo.SetCurrentGroupName("Restore Previous Start Screen");
            int group = Undo.GetCurrentGroup();
            var oldRoot = canvas.transform.Find(OldRootName);
            var popup = root.Find(PopupName);
            if (popup && oldRoot) Undo.SetTransformParent(popup, oldRoot, "Return Leaderboard Popup");
            Undo.DestroyObjectImmediate(root.gameObject);
            if (oldRoot)
            {
                Undo.RecordObject(oldRoot.gameObject, "Show old start menu");
                oldRoot.gameObject.SetActive(true);
            }
            Undo.CollapseUndoOperations(group);
            var scene = EditorSceneManager.GetActiveScene();
            EditorSceneManager.MarkSceneDirty(scene);
            if (scene.path == ScenePath) EditorSceneManager.SaveScene(scene);
            Debug.Log("[Start Screen] Previous GDD Start Menu restored.");
        }

        // ───────────── Pieces ─────────────

        /// <summary>A 566 × 109 scroll button at x 928 (centred on the 2424 stage) with its label centred.</summary>
        static Button TextButton(string label, RectTransform stage, Sprite sprite, TMP_FontAsset font, Color colour, float y)
        {
            var img = Img(label, stage, sprite, 928, y, 566, 109);
            var b = MakeButton(img);
            var t = Txt("Label", img.rectTransform, font, 50f, colour, TextAlignmentOptions.Center);
            Stretch(t.rectTransform);
            t.rectTransform.anchoredPosition = new Vector2(0f, 2f);
            t.text = label;
            return b;
        }

        /// <summary>A 131 × 127 icon 39 px from the right edge; the touch area reaches 10 px past the art on each side.</summary>
        static Button IconButton(string name, RectTransform stage, Sprite sprite, float y)
        {
            var img = Img(name, stage, sprite, 2254, y, 131, 127);
            img.raycastPadding = new Vector4(-10f, -10f, -10f, -10f);
            return MakeButton(img);
        }

        static T FindIn<T>(Transform[] all, string name) where T : Component =>
            all.Where(t => t.name == name).Select(t => t.GetComponent<T>()).FirstOrDefault(c => c);

        // ───────────── Scene plumbing ─────────────

        static Canvas EnsureCanvasOrNull()
        {
            foreach (var c in Object.FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (c.isRootCanvas && c.gameObject.scene == EditorSceneManager.GetActiveScene()) return c;
            return null;
        }

        static Canvas EnsureCanvas()
        {
            var existing = EnsureCanvasOrNull();
            if (existing) return existing;
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
            var b = Undo.AddComponent<Button>(img.gameObject);
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
