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
    /// Menu: Window → KungFu Crashout → GDD Build → Build Online Lobby (Concept 17)
    /// Builds the LobbyScreen scene from GamConcepts_UI_17 + Online 1v1 UI Notes.txt, the same way as the other screens:
    ///   • Art/UI/OnlineLobby imported as UI sprites. Background = UI_OnlineLobby_ScreenBackground, whole screen (envelope).
    ///   • Concept layout at 2424 × 1080, every piece at its concept pixel position and size, in StageFit layers inside
    ///     the safe area (scaled uniformly to fit any phone):
    ///       Stage (top)   — hangs from the top: back lantern + "Online 1v1" title frame.
    ///       Stage (right) — hangs from the top-right corner: the connection bar (flush with the right edge).
    ///       Stage (main)  — centred: player borders, VS, status bars + P1 / P2 markers (from Character Select), room code
    ///                       frames, Copied / Join buttons (+ Join glow), READY.
    ///   • Shown as concept 17 (layout only, no networking). READY → concept 18 (selected art + "Waiting for opponent"
    ///     wave) → Roster. Back lantern → Start screen. Start screen music carries on (music player copied in).
    ///   • Start screen: Multiplayer → LobbyScreen. LobbyScreen is added to the build list. The camera gets an Audio
    ///     Listener if it has none.
    /// Re-running rebuilds the screen. Saves both scenes.
    /// Place in: Assets/_Project/Editor/LobbySetup.cs
    /// </summary>
    public static class LobbySetup
    {
        const string Title = "Online Lobby";
        const string Art = "Assets/_Project/Art/UI/OnlineLobby";
        const string CsArt = "Assets/_Project/Art/UI/CharacterSelect";
        const string FontFolder = "Assets/_Project/Art/UI/Fonts";
        const string RootName = "Online Lobby (Concept 17)";
        const string ScenePath = "Assets/_Project/Scenes/LobbyScreen.unity";
        const string StartPath = "Assets/_Project/Scenes/StartScreen.unity";
        const float W = 2424f, H = 1080f;

        static readonly Color Gold = Hex("EBD997");
        static readonly Color Cream = Hex("F4E8CE");
        static readonly Color Peach = Hex("DEB8A1");
        static readonly Color CopiedText = Hex("F5DAA5");
        static readonly Color DarkBrown = Hex("4E3B1D");
        static readonly Color ReadyText = Hex("F3E7CF");

        [MenuItem("Window/KungFu Crashout/GDD Build/Build Online Lobby (Concept 17)", priority = 10067)]
        public static void Build()
        {
            if (EditorApplication.isPlaying) { EditorUtility.DisplayDialog(Title, "Stop Play mode first.", "OK"); return; }
            if (!System.IO.File.Exists(ScenePath)) { EditorUtility.DisplayDialog(Title, "No scene at " + ScenePath, "OK"); return; }
            var tBold = Font("Tillana-Bold");
            if (!tBold) { EditorUtility.DisplayDialog(Title, "Tillana-Bold SDF not found in Art/UI/Fonts.", "OK"); return; }
            var tSemi = Font("Tillana-SemiBold") ?? tBold;
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
            var bgSprite = S(Art, "UI_OnlineLobby_ScreenBackground", 4096);
            var back = S(CsArt, "UI_CharacterSelect_Back", 256);
            var titleFrame = S(Art, "UI_OnlineLobby_Title_Frame");
            var connBorder = S(Art, "UI_OnlineLobby_Connection_Border");
            var connIcon = S(Art, "UI_OnlineLobby_Connection_Icon", 128);
            var borderGreen = S(Art, "UI_OnlineLobby_Player_Border_Green");
            var borderRed = S(Art, "UI_OnlineLobby_Player_Border_Red");
            S(Art, "UI_OnlineLobby_Player_Border_Empty");   // imported for the "no opponent yet" state later
            var statusGreen = S(Art, "UI_OnlineLobby_Player_Status_Green", 512);
            var statusRed = S(Art, "UI_OnlineLobby_Player_Status_Red", 512);
            var tick = S(Art, "UI_OnlineLobby_tick", 128);
            var vs = S(Art, "UI_OnlineLobby_VS", 512);
            var desc = S(Art, "UI_OnlineLobby_Description_Frame");
            var displayCode = S(Art, "UI_OnlineLobby_DisplayRoomCode_Frame", 512);
            var enterCode = S(Art, "UI_OnlineLobby_EnterRoomCode_Frame", 512);
            S(Art, "UI_OnlineLobby_Copy_Frame", 256);
            var copied = S(Art, "UI_OnlineLobby_Copied_Frame", 256);
            var joinOn = S(Art, "UI_OnlineLobby_Join_Available_Frame", 256);
            S(Art, "UI_OnlineLobby_Join_Unavailable_Frame", 256);
            var joinGlow = S(Art, "UI_OnlineLobby_Join_Available_Glow");
            var readyUnsel = S(Art, "UI_OnlineLobby_Ready_Unselected", 512);
            var readySel = S(Art, "UI_OnlineLobby_Ready_Selected", 512);
            S(Art, "UI_OnlineLobby_Ready_Unavailable", 512);
            S(Art, "UI_General_Message_Popup");
            var markerG = S(CsArt, "UI_CharacterSelect_PlayerMarker_Large_Green", 256);
            var markerR = S(CsArt, "UI_CharacterSelect_PlayerMarker_Large_Red", 256);

            var canvas = EnsureCanvas(scene);
            EnsureEventSystem(scene);
            EnsureListener(scene);
            var old = canvas.transform.Find(RootName);
            if (old) Object.DestroyImmediate(old.gameObject);
            var root = Stretch(NewRect(RootName, canvas.transform));
            var menu = root.gameObject.AddComponent<LobbyMenu>();

            var bg = NewImage("Background", root, bgSprite);
            Stretch(bg.rectTransform);
            var fitter = bg.gameObject.AddComponent<AspectRatioFitter>();
            fitter.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
            fitter.aspectRatio = W / H;
            bg.raycastTarget = true;

            var safe = Stretch(NewRect("Safe Area", root));
            safe.gameObject.AddComponent<SafeAreaFitter>();
            var main = NewStage("Stage (main)", safe, new Vector2(0.5f, 0.5f));
            var top = NewStage("Stage (top)", safe, new Vector2(0.5f, 1f));
            var right = NewStage("Stage (right)", safe, new Vector2(1f, 1f));

            // ── Players ──
            Img("P1 Border", main, borderGreen, 96, 276, 889, 371);
            Img("P1 Status", main, statusGreen, 270, 540, 408, 51);
            Img("P1 Tick", main, tick, 402, 550, 43, 36);
            MarkerText(Img("P1 Marker", main, markerG, 634, 522, 160, 80), tBold, "P1", new Vector2(-21f, -3f));
            Left("Player 1 Label", main, tBold, 40, Gold, 292, 357, "Player 1");
            Left("Player 1 Name", main, tBold, 50, Gold, 292, 412, "Jeromeisntthere");
            Left("Player 1 Record", main, tMed, 40, Gold, 292, 482, "12W · 8L");
            Left("Player 1 Status", main, tMed, 30, Cream, 292, 566, "READY");

            Img("P2 Border", main, borderRed, 1439, 276, 889, 371);
            Img("P2 Status", main, statusRed, 1745, 540, 408, 51);
            MarkerText(Img("P2 Marker", main, markerR, 1630, 522, 160, 80), tBold, "P2", new Vector2(20f, -3f));
            Right("Player 2 Label", main, tBold, 40, Gold, 2133, 357, "Player 2");
            Right("Player 2 Name", main, tBold, 50, Gold, 2133, 412, "Matcha.spice_59626");
            Right("Player 2 Record", main, tMed, 40, Gold, 2133, 482, "1W · 20L");
            Right("Player 2 Status", main, tMed, 30, Cream, 2132, 566, "Waiting . . .");

            Img("VS", main, vs, 979, 393, 465, 131);

            // ── Room codes ──
            Img("Host Description", main, desc, 149, 684, 784, 57);
            var joinDesc = Img("Join Description", main, desc, 1491, 684, 784, 57);
            joinDesc.rectTransform.localScale = new Vector3(-1f, 1f, 1f);   // mirrored, as the concept
            Left("Host Text", main, barlow, 30, Peach, 215, 712, "Host: Enter your room code");
            Left("Join Text", main, barlow, 30, Peach, 1587, 712, "Join: Enter your friend's code");

            Img("Room Code Frame", main, displayCode, 201, 758, 362, 134);
            Centre("Room Code", main, tSemi, 80, Color.black, 382, 826, "K7Q2");
            var copyBtn = MakeButton(Img("Copy", main, copied, 601, 758, 211, 138));
            Centre("Copy Label", copyBtn.GetComponent<RectTransform>(), tSemi, 50, CopiedText, 0, 0, "COPIED", true);

            Img("Enter Code Frame", main, enterCode, 1574, 758, 362, 134);
            var entry = Centre("Entered Code", main, tSemi, 80, Color.black, 1755, 826, "____");
            entry.characterSpacing = 30f;
            var glow = Img("Join Glow", main, joinGlow, 1816, 581.5f, 527, 491);
            var joinBtn = MakeButton(Img("Join", main, joinOn, 1974, 758, 211, 138));
            Centre("Join Label", joinBtn.GetComponent<RectTransform>(), tSemi, 50, DarkBrown, 0, 0, "JOIN", true);

            // ── Ready ──
            var readyBtn = MakeButton(Img("Ready", main, readyUnsel, 977, 851, 473, 167));
            var readyLabel = Centre("Ready Label", readyBtn.GetComponent<RectTransform>(), aladin, 80, ReadyText, 0, 0, "READY", true);
            var waiting = Centre("Waiting Label", readyBtn.GetComponent<RectTransform>(), aladin, 50, ReadyText, 0, 0, "Waiting for opponent", true);
            waiting.gameObject.AddComponent<TmpWave>();
            waiting.gameObject.SetActive(false);

            // ── Top: title + back; right: connection ──
            Img("Title Frame", top, titleFrame, 128, 39, 524, 82);
            Centre("Title", top, tBold, 43, Gold, 362, 80, "Online 1v1");
            var backBtn = MakeButton(Img("Back", top, back, 35, 0, 132, 218));
            backBtn.GetComponent<Image>().raycastPadding = new Vector4(-10f, -10f, -10f, -10f);
            Img("Connection Bar", right, connBorder, 1868, 53, 556, 57);
            Img("Connection Icon", right, connIcon, 1938, 62, 39, 38);
            Left("Connection Text", right, barlow, 30, Cream, 2009, 81, "Connected · 42ms");

            var so = new SerializedObject(menu);
            so.FindProperty("readyButton").objectReferenceValue = readyBtn;
            so.FindProperty("readySelected").objectReferenceValue = readySel;
            so.FindProperty("readyText").objectReferenceValue = readyLabel;
            so.FindProperty("waitingText").objectReferenceValue = waiting;
            so.FindProperty("joinGlow").objectReferenceValue = glow;
            so.FindProperty("backButton").objectReferenceValue = backBtn;
            so.ApplyModifiedPropertiesWithoutUndo();

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            string startNote = HookStart(scene, out string musicNote);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AddToBuild();
            Debug.Log("[Online Lobby] Concept 17 built and saved. READY → waiting (concept 18) → Roster; back → Start screen. " +
                      startNote + " " + musicNote + " LobbyScreen added to the build list." +
                      (missing.Count > 0 ? $" Missing art, left blank: {string.Join(", ", missing)}." : ""));
        }

        // ───────────── Start screen: Multiplayer → lobby, music copied ─────────────

        static string HookStart(UnityEngine.SceneManagement.Scene lobby, out string musicNote)
        {
            musicNote = "";
            if (!System.IO.File.Exists(StartPath)) return "No StartScreen scene.";
            var start = EditorSceneManager.OpenScene(StartPath, OpenSceneMode.Additive);
            musicNote = CopyMusic(start, lobby);
            string note = "Multiplayer button not found in StartScreen.";
            foreach (var r in start.GetRootGameObjects())
            {
                var screen = r.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "Start Screen (Concept 29)");
                if (!screen) continue;
                var mp = screen.GetComponentsInChildren<Button>(true).FirstOrDefault(b => b.name == "Multiplayer");
                if (!mp) break;
                var sb = mp.GetComponent<SceneButton>() ?? mp.gameObject.AddComponent<SceneButton>();
                var sso = new SerializedObject(sb);
                sso.FindProperty("sceneName").stringValue = "LobbyScreen";
                sso.ApplyModifiedPropertiesWithoutUndo();
                note = "Start screen Multiplayer → LobbyScreen.";
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

        /// <summary>"P1" / "P2" on the marker, as the Roster's large markers (Tillana Bold 34, same offset).</summary>
        static void MarkerText(Image marker, TMP_FontAsset font, string s, Vector2 offset)
        {
            var t = Txt("Text", marker.rectTransform, font, 34f, Hex("ECD998"), TextAlignmentOptions.Midline);
            Stretch(t.rectTransform);
            t.rectTransform.anchoredPosition = offset;
            t.text = s;
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
