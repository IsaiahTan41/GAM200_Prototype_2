#if UNITY_EDITOR
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using KungFuCrashout.UI;

namespace KungFuCrashout.EditorTools
{
    /// <summary>
    /// Menu: KungFu Crashout → Build Options Menu
    /// Builds the Options popup (Game / Audio tabs) on the HUD canvas and hooks it to the scene's "OptionsButton":
    ///   GAME  — toggle: Enemy AI = State Machine (on) / Practice Dummy (off)
    ///   AUDIO — Master, Music and Sound Effects sliders
    /// Re-running rebuilds the popup from scratch (your OptionsMenu Inspector settings are reset). Undoable.
    /// Save the scene afterwards. Place in: Assets/_Project/Editor/OptionsMenuSetup.cs
    /// </summary>
    public static class OptionsMenuSetup
    {
        const string RootName = "Options Menu";
        static readonly Color WindowColour = new Color(0.09f, 0.09f, 0.11f, 0.96f);
        static readonly Color DimColour = new Color(0f, 0f, 0f, 0.6f);
        static readonly Color RowColour = new Color(1f, 1f, 1f, 0.06f);
        static readonly Color Accent = new Color(0.85f, 0.25f, 0.2f);

        static DefaultControls.Resources res;

        [MenuItem("KungFu Crashout/Build Options Menu", priority = 69)]
        public static void Build()
        {
            Button optionsButton = null;
            foreach (var b in Object.FindObjectsByType<Button>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (b.name == "OptionsButton") { optionsButton = b; break; }

            Canvas canvas = optionsButton ? optionsButton.GetComponentInParent<Canvas>().rootCanvas : null;
            if (!canvas)
                foreach (var c in Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None))
                    if (c.isRootCanvas) { canvas = c; break; }
            if (!canvas)
            {
                EditorUtility.DisplayDialog("Build Options Menu", "No UI Canvas in the open scene.", "OK");
                return;
            }

            res = new DefaultControls.Resources
            {
                standard = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd"),
                background = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Background.psd"),
                inputField = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/InputFieldBackground.psd"),
                knob = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Knob.psd"),
                checkmark = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Checkmark.psd"),
                dropdown = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/DropdownArrow.psd"),
                mask = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UIMask.psd"),
            };

            Undo.SetCurrentGroupName("Build Options Menu");
            int group = Undo.GetCurrentGroup();

            var old = canvas.transform.Find(RootName);
            if (old) Undo.DestroyObjectImmediate(old.gameObject);

            // Root (always active, holds the script) → Panel (dim, hidden until opened) → Window
            var root = New(RootName, canvas.transform);
            Undo.RegisterCreatedObjectUndo(root, "Create Options Menu");
            Stretch(root);
            root.transform.SetAsLastSibling();

            var panel = New("Panel", root.transform);
            Stretch(panel);
            AddImage(panel, DimColour, null); // also blocks touches to the HUD underneath

            var window = New("Window", panel.transform);
            Place(window, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1100f, 700f));
            AddImage(window, WindowColour, res.standard);

            var title = Text("Title", window.transform, "OPTIONS", 56, TextAlignmentOptions.Center);
            Place(title.gameObject, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -60f), new Vector2(600f, 80f));
            title.fontStyle = FontStyles.Bold;

            var close = MakeButton("Close", window.transform, "X", 44);
            Place(close.gameObject, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-60f, -60f), new Vector2(80f, 80f));

            var gameTab = MakeButton("Game Tab", window.transform, "GAME", 40);
            Place(gameTab.gameObject, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(-150f, -160f), new Vector2(280f, 80f));
            var audioTab = MakeButton("Audio Tab", window.transform, "AUDIO", 40);
            Place(audioTab.gameObject, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(150f, -160f), new Vector2(280f, 80f));

            // Pages fill the area under the tabs.
            var gamePage = Page("Game Page", window.transform);
            var audioPage = Page("Audio Page", window.transform);

            // ── GAME ──
            var toggle = MakeToggle("State Machine Toggle", gamePage.transform, "Enemy AI uses the State Machine");
            Place(toggle.gameObject, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -70f), new Vector2(860f, 90f));
            var modeLabel = Text("Enemy Mode", gamePage.transform, "", 32, TextAlignmentOptions.Center);
            Place(modeLabel.gameObject, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -170f), new Vector2(960f, 60f));
            modeLabel.color = new Color(1f, 1f, 1f, 0.75f);
            var hint = Text("Hint", gamePage.transform, "Off = Practice Dummy (uses the Training Dummy settings in the Inspector)",
                            26, TextAlignmentOptions.Center);
            Place(hint.gameObject, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -230f), new Vector2(960f, 50f));
            hint.color = new Color(1f, 1f, 1f, 0.45f);

            // ── AUDIO ──
            var master = SliderRow("Master", audioPage.transform, "Master", 0, out var masterValue);
            var music = SliderRow("Music", audioPage.transform, "Music", 1, out var musicValue);
            var sfx = SliderRow("SFX", audioPage.transform, "Sound Effects", 2, out var sfxValue);
            audioPage.SetActive(false);
            panel.SetActive(false);

            // ── Script ──
            var menu = root.AddComponent<OptionsMenu>();
            var so = new SerializedObject(menu);
            Set(so, "optionsButton", optionsButton);
            Set(so, "panel", panel);
            Set(so, "closeButton", close);
            Set(so, "gameTab", gameTab);
            Set(so, "audioTab", audioTab);
            Set(so, "gamePage", gamePage);
            Set(so, "audioPage", audioPage);
            Set(so, "stateMachineToggle", toggle);
            Set(so, "enemyModeLabel", modeLabel);
            Set(so, "masterSlider", master);
            Set(so, "musicSlider", music);
            Set(so, "sfxSlider", sfx);
            Set(so, "masterValue", masterValue);
            Set(so, "musicValue", musicValue);
            Set(so, "sfxValue", sfxValue);
            so.ApplyModifiedProperties();

            Undo.CollapseUndoOperations(group);
            EditorSceneManager.MarkSceneDirty(canvas.gameObject.scene);
            Selection.activeObject = root;

            Debug.Log("[Options] Built 'Options Menu' (Game / Audio tabs)" +
                      (optionsButton ? $", opened by '{optionsButton.name}'." : ". NO 'OptionsButton' found: drag your button into Options Menu → Options Button.") +
                      " Save the scene (Ctrl+S).");
        }

        // ───────────── Building blocks ─────────────

        static GameObject New(string name, Transform parent)
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

        static void Place(GameObject go, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size)
        {
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = anchor;
            rt.pivot = new Vector2(0.5f, 0.5f); // positions are the element's centre
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
        }

        static Image AddImage(GameObject go, Color colour, Sprite sprite)
        {
            var img = go.AddComponent<Image>();
            img.color = colour;
            if (sprite) { img.sprite = sprite; img.type = Image.Type.Sliced; }
            return img;
        }

        static TextMeshProUGUI Text(string name, Transform parent, string text, float size, TextAlignmentOptions align)
        {
            var go = New(name, parent);
            var t = go.AddComponent<TextMeshProUGUI>();
            t.text = text;
            t.fontSize = size;
            t.alignment = align;
            t.color = Color.white;
            t.raycastTarget = false;
            return t;
        }

        static Button MakeButton(string name, Transform parent, string label, float size)
        {
            var go = New(name, parent);
            var img = AddImage(go, new Color(0.25f, 0.25f, 0.3f), res.standard);
            var b = go.AddComponent<Button>();
            b.targetGraphic = img;
            var t = Text("Label", go.transform, label, size, TextAlignmentOptions.Center);
            Stretch(t.gameObject);
            t.fontStyle = FontStyles.Bold;
            return b;
        }

        static GameObject Page(string name, Transform window)
        {
            var go = New(name, window);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = new Vector2(0f, 0f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.offsetMin = new Vector2(60f, 40f);
            rt.offsetMax = new Vector2(-60f, -220f);
            return go;
        }

        static Toggle MakeToggle(string name, Transform parent, string label)
        {
            var go = DefaultControls.CreateToggle(res);
            go.name = name;
            go.transform.SetParent(parent, false);
            var toggle = go.GetComponent<Toggle>();

            // Swap the legacy label for TextMeshPro.
            var legacy = go.transform.Find("Label");
            if (legacy) Object.DestroyImmediate(legacy.gameObject);

            var bg = (RectTransform)go.transform.Find("Background");
            bg.anchorMin = bg.anchorMax = new Vector2(0f, 0.5f);
            bg.pivot = new Vector2(0f, 0.5f);
            bg.anchoredPosition = new Vector2(20f, 0f);
            bg.sizeDelta = new Vector2(70f, 70f);
            var check = (RectTransform)bg.Find("Checkmark");
            if (check)
            {
                check.anchorMin = Vector2.zero;
                check.anchorMax = Vector2.one;
                check.offsetMin = new Vector2(6f, 6f);
                check.offsetMax = new Vector2(-6f, -6f);
                var ci = check.GetComponent<Image>();
                if (ci) ci.color = Accent;
            }

            var t = Text("Label", go.transform, label, 40, TextAlignmentOptions.Left);
            var trt = (RectTransform)t.transform;
            trt.anchorMin = new Vector2(0f, 0f);
            trt.anchorMax = new Vector2(1f, 1f);
            trt.offsetMin = new Vector2(120f, 0f);
            trt.offsetMax = Vector2.zero;

            AddImage(go, RowColour, res.standard).raycastTarget = true; // the whole row is tappable
            toggle.targetGraphic = bg.GetComponent<Image>();
            return toggle;
        }

        static Slider SliderRow(string name, Transform parent, string label, int index, out TextMeshProUGUI value)
        {
            float y = -60f - index * 130f;

            var row = New(name + " Row", parent);
            Place(row, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, y), new Vector2(960f, 110f));
            AddImage(row, RowColour, res.standard).raycastTarget = false;

            var l = Text("Label", row.transform, label, 38, TextAlignmentOptions.Left);
            Place(l.gameObject, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(170f, 0f), new Vector2(300f, 90f));

            var go = DefaultControls.CreateSlider(res);
            go.name = name + " Slider";
            go.transform.SetParent(row.transform, false);
            Place(go, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(80f, 0f), new Vector2(480f, 40f));
            var slider = go.GetComponent<Slider>();
            if (slider.handleRect) slider.handleRect.sizeDelta = new Vector2(50f, 30f);
            var fill = slider.fillRect ? slider.fillRect.GetComponent<Image>() : null;
            if (fill) fill.color = Accent;

            value = Text("Value", row.transform, "100%", 36, TextAlignmentOptions.Right);
            Place(value.gameObject, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-80f, 0f), new Vector2(140f, 90f));
            return slider;
        }

        static void Set(SerializedObject so, string field, Object value)
        {
            var p = so.FindProperty(field);
            if (p != null) p.objectReferenceValue = value;
        }
    }
}
#endif
