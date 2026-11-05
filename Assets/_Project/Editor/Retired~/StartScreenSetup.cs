#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using KungFuCrashout.UI;

namespace KungFuCrashout.EditorTools
{
    /// <summary>
    /// Menu: KungFu Crashout → Set Up Start Screen + Back Button
    ///  1. StartScreen.unity: HUD-style canvas (Overlay, 1920×1080, match height), dark panel, title, START and QUIT
    ///     buttons, StartMenu wired (START → FightScene, QUIT → close the app). Re-running rebuilds the menu.
    ///  2. FightScene.unity: adds SceneButton to "BackButton" → loads StartScreen (and keeps its taps out of the fight).
    ///  3. Build order: StartScreen first (the app opens on it), FightScene second, other scenes left disabled.
    /// Both scenes are saved. You're asked to save the scene you have open first; it's reopened at the end.
    /// Place in: Assets/_Project/Editor/StartScreenSetup.cs
    /// </summary>
    public static class StartScreenSetup
    {
        const string StartPath = "Assets/_Project/Scenes/StartScreen.unity";
        const string FightPath = "Assets/_Project/Scenes/FightScene.unity";
        const string MenuName = "Start Menu";
        static readonly Color Accent = new Color(0.85f, 0.25f, 0.2f);

        [MenuItem("KungFu Crashout/Set Up Start Screen + Back Button", priority = 70)]
        public static void SetUp()
        {
            if (EditorApplication.isPlaying) { EditorUtility.DisplayDialog("Start Screen", "Stop Play mode first.", "OK"); return; }
            if (!System.IO.File.Exists(StartPath) || !System.IO.File.Exists(FightPath))
            {
                EditorUtility.DisplayDialog("Start Screen", $"Need both {StartPath} and {FightPath}.", "OK");
                return;
            }
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            string returnTo = UnityEngine.SceneManagement.SceneManager.GetActiveScene().path;

            // ── 1. Start screen ──
            var start = EditorSceneManager.OpenScene(StartPath, OpenSceneMode.Single);
            BuildStartMenu();
            EditorSceneManager.MarkSceneDirty(start);
            EditorSceneManager.SaveScene(start);

            // ── 2. Back button in the fight ──
            var fight = EditorSceneManager.OpenScene(FightPath, OpenSceneMode.Single);
            string backReport = HookBackButton();
            EditorSceneManager.MarkSceneDirty(fight);
            EditorSceneManager.SaveScene(fight);

            // ── 3. Build order ──
            var list = EditorBuildSettings.scenes.Where(s => s.path != StartPath && s.path != FightPath).ToList();
            foreach (var s in list) s.enabled = false; // e.g. the template SampleScene
            list.Insert(0, new EditorBuildSettingsScene(FightPath, true));
            list.Insert(0, new EditorBuildSettingsScene(StartPath, true));
            EditorBuildSettings.scenes = list.ToArray();

            if (!string.IsNullOrEmpty(returnTo) && returnTo != FightPath) EditorSceneManager.OpenScene(returnTo, OpenSceneMode.Single);

            Debug.Log("[Start screen] StartScreen built (START → FightScene, QUIT → close app). " + backReport +
                      "\n  Build order: 0 = StartScreen, 1 = FightScene (other scenes disabled). Both scenes saved.");
        }

        // ───────────── Start screen ─────────────

        static void BuildStartMenu()
        {
            Canvas canvas = null;
            foreach (var c in Object.FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (c.isRootCanvas) { canvas = c; break; }
            if (!canvas)
            {
                var go = new GameObject("Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
                canvas = go.GetComponent<Canvas>();
            }
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvas.GetComponent<CanvasScaler>();
            if (!scaler) scaler = canvas.gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 1f;

            // Background: the existing Panel, made a solid dark backdrop.
            var panelT = canvas.transform.Find("Panel");
            var panel = panelT ? panelT.gameObject : NewUI("Panel", canvas.transform);
            Stretch(panel);
            var bg = panel.GetComponent<Image>();
            if (!bg) bg = panel.AddComponent<Image>();
            bg.color = new Color(0.07f, 0.05f, 0.05f, 1f);

            var old = panel.transform.Find(MenuName);
            if (old) Object.DestroyImmediate(old.gameObject);
            var menu = NewUI(MenuName, panel.transform);
            Stretch(menu);

            var title = Text("Title", menu.transform, "KUNGFU CRASHOUT", 120);
            Place(title.gameObject, new Vector2(0.5f, 0.7f), new Vector2(1600f, 180f));
            title.fontStyle = FontStyles.Bold;
            title.color = Accent;

            var sub = Text("Subtitle", menu.transform, "Team 11 · Je-gnomes   ·   prototype", 36);
            Place(sub.gameObject, new Vector2(0.5f, 0.58f), new Vector2(1200f, 60f));
            sub.color = new Color(1f, 1f, 1f, 0.6f);

            var startBtn = MakeButton("Start Button", menu.transform, "START", Accent);
            Place(startBtn.gameObject, new Vector2(0.5f, 0.38f), new Vector2(560f, 130f));
            var quitBtn = MakeButton("Quit Button", menu.transform, "QUIT", new Color(0.25f, 0.25f, 0.3f));
            Place(quitBtn.gameObject, new Vector2(0.5f, 0.22f), new Vector2(560f, 110f));

            var sm = menu.AddComponent<StartMenu>();
            var so = new SerializedObject(sm);
            so.FindProperty("startButton").objectReferenceValue = startBtn;
            so.FindProperty("quitButton").objectReferenceValue = quitBtn;
            so.FindProperty("fightScene").stringValue = System.IO.Path.GetFileNameWithoutExtension(FightPath);
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        // ───────────── Fight scene ─────────────

        static string HookBackButton()
        {
            Button back = null;
            foreach (var b in Object.FindObjectsByType<Button>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (b.name.Replace(" ", "").ToLowerInvariant() == "backbutton") { back = b; break; }
            if (!back) return "No 'BackButton' found in FightScene: add SceneButton (Scene Name = StartScreen) to your button by hand.";

            var sb = back.GetComponent<SceneButton>();
            if (!sb) sb = back.gameObject.AddComponent<SceneButton>();
            var so = new SerializedObject(sb);
            so.FindProperty("sceneName").stringValue = System.IO.Path.GetFileNameWithoutExtension(StartPath);
            so.ApplyModifiedPropertiesWithoutUndo();
            return $"'{back.name}' → StartScreen.";
        }

        // ───────────── Building blocks ─────────────

        static GameObject NewUI(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = parent.gameObject.layer;
            go.transform.SetParent(parent, false);
            return go;
        }

        static void Stretch(GameObject go)
        {
            var rt = (RectTransform)go.transform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        /// <summary>Centre the element at a point given as a fraction of the screen.</summary>
        static void Place(GameObject go, Vector2 anchor, Vector2 size)
        {
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = anchor;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = size;
        }

        static TextMeshProUGUI Text(string name, Transform parent, string text, float size)
        {
            var go = NewUI(name, parent);
            var t = go.AddComponent<TextMeshProUGUI>();
            t.text = text;
            t.fontSize = size;
            t.alignment = TextAlignmentOptions.Center;
            t.color = Color.white;
            t.raycastTarget = false;
            return t;
        }

        static Button MakeButton(string name, Transform parent, string label, Color colour)
        {
            var go = NewUI(name, parent);
            var img = go.AddComponent<Image>();
            img.sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
            img.type = Image.Type.Sliced;
            img.color = colour;
            var b = go.AddComponent<Button>();
            b.targetGraphic = img;
            var t = Text("Label", go.transform, label, 56);
            Stretch(t.gameObject);
            t.fontStyle = FontStyles.Bold;
            return b;
        }
    }
}
#endif
