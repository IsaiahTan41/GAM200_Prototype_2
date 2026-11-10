#if UNITY_EDITOR
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace KungFuCrashout.EditorTools
{
    /// <summary>
    /// Small uGUI building blocks shared by the GDD Build setup (same look as Prototype 2's Options / Start screen builders).
    /// Positions are in Canvas reference units (1920 × 1080, match height). Place in: Assets/_Project/Editor/GddUi.cs
    /// </summary>
    public static class GddUi
    {
        public static readonly Color Window = new Color(0.09f, 0.09f, 0.11f, 0.96f);
        public static readonly Color Dim = new Color(0f, 0f, 0f, 0.7f);
        public static readonly Color ButtonColour = new Color(0.22f, 0.22f, 0.26f);
        public static readonly Color Disabled = new Color(0.3f, 0.3f, 0.32f, 0.6f);
        public static readonly Color Gold = new Color(1f, 0.78f, 0.2f);
        public static readonly Color Title = new Color(0.85f, 0.25f, 0.2f);

        static Sprite uiSprite;
        public static Sprite UISprite => uiSprite ? uiSprite : uiSprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");

        static DefaultControls.Resources res;
        public static DefaultControls.Resources Res
        {
            get
            {
                if (res.standard) return res;
                res = new DefaultControls.Resources
                {
                    standard = UISprite,
                    background = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Background.psd"),
                    inputField = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/InputFieldBackground.psd"),
                    knob = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Knob.psd"),
                    checkmark = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Checkmark.psd"),
                    dropdown = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/DropdownArrow.psd"),
                    mask = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UIMask.psd"),
                };
                return res;
            }
        }

        /// <summary>An outlined TMP material (dark rim), so text reads over the stage art (UI Standards: contrast).</summary>
        public static Material Outlined { get; set; }

        public static GameObject New(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = parent ? parent.gameObject.layer : 5;
            go.transform.SetParent(parent, false);
            return go;
        }

        public static RectTransform Stretch(GameObject go, float inset = 0f)
        {
            var rt = (RectTransform)go.transform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(inset, inset);
            rt.offsetMax = new Vector2(-inset, -inset);
            return rt;
        }

        /// <summary>Anchor at a point (fraction of the parent), pivot as given, offset and size in reference units.</summary>
        public static RectTransform Place(GameObject go, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size)
        {
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = anchor;
            rt.pivot = pivot;
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            return rt;
        }

        public static RectTransform Place(GameObject go, Vector2 anchor, Vector2 pos, Vector2 size) =>
            Place(go, anchor, new Vector2(0.5f, 0.5f), pos, size);

        public static Image Img(GameObject go, Color colour, bool sliced = true, bool raycast = false)
        {
            var img = go.GetComponent<Image>();
            if (!img) img = go.AddComponent<Image>();
            img.color = colour;
            if (sliced) { img.sprite = UISprite; img.type = Image.Type.Sliced; }
            img.raycastTarget = raycast;
            return img;
        }

        public static TextMeshProUGUI Text(string name, Transform parent, string text, float size,
                                           TextAlignmentOptions align = TextAlignmentOptions.Center, bool bold = false, bool outlined = false)
        {
            var go = New(name, parent);
            var t = go.AddComponent<TextMeshProUGUI>();
            t.text = text;
            t.fontSize = size;
            t.alignment = align;
            t.color = Color.white;
            t.raycastTarget = false;
            if (bold) t.fontStyle = FontStyles.Bold;
            if (outlined && Outlined) t.fontSharedMaterial = Outlined;
            return t;
        }

        public static Button MakeButton(string name, Transform parent, string label, float fontSize, Color fill, Color textColour)
        {
            var go = New(name, parent);
            var img = Img(go, fill, true, true);
            var b = go.AddComponent<UnityEngine.UI.Button>();
            b.targetGraphic = img;
            var t = Text("Label", go.transform, label, fontSize, TextAlignmentOptions.Center, bold: true);
            Stretch(t.gameObject);
            t.color = textColour;
            return b;
        }

        /// <summary>Primary action (UI Standards): the largest, filled button. On the dark menus it's filled white with black text.</summary>
        public static Button Primary(string name, Transform parent, string label, float fontSize = 60f) =>
            MakeButton(name, parent, label, fontSize, Color.white, Color.black);

        public static Button Secondary(string name, Transform parent, string label, float fontSize = 44f) =>
            MakeButton(name, parent, label, fontSize, ButtonColour, Color.white);

        public static Toggle MakeToggle(string name, Transform parent, string label, float fontSize = 38f)
        {
            var go = DefaultControls.CreateToggle(Res);
            go.name = name;
            go.transform.SetParent(parent, false);
            var toggle = go.GetComponent<Toggle>();

            var legacy = go.transform.Find("Label");
            if (legacy) Object.DestroyImmediate(legacy.gameObject);

            var bg = (RectTransform)go.transform.Find("Background");
            bg.anchorMin = bg.anchorMax = new Vector2(0f, 0.5f);
            bg.pivot = new Vector2(0f, 0.5f);
            bg.anchoredPosition = new Vector2(20f, 0f);
            bg.sizeDelta = new Vector2(64f, 64f);
            var check = (RectTransform)bg.Find("Checkmark");
            if (check)
            {
                check.anchorMin = Vector2.zero;
                check.anchorMax = Vector2.one;
                check.offsetMin = new Vector2(6f, 6f);
                check.offsetMax = new Vector2(-6f, -6f);
                var ci = check.GetComponent<Image>();
                if (ci) ci.color = Gold;
            }

            var t = Text("Label", go.transform, label, fontSize, TextAlignmentOptions.Left);
            var trt = (RectTransform)t.transform;
            trt.anchorMin = Vector2.zero;
            trt.anchorMax = Vector2.one;
            trt.offsetMin = new Vector2(110f, 0f);
            trt.offsetMax = Vector2.zero;

            Img(go, new Color(1f, 1f, 1f, 0.06f), true, true); // the whole row is tappable
            toggle.targetGraphic = bg.GetComponent<Image>();
            return toggle;
        }

        public static void Set(SerializedObject so, string path, Object value)
        {
            var p = so.FindProperty(path);
            if (p != null) p.objectReferenceValue = value;
            else Debug.LogWarning($"[GDD Build] {so.targetObject.GetType().Name} has no field '{path}'.");
        }

        public static void SetArray(SerializedObject so, string path, Object[] values)
        {
            var p = so.FindProperty(path);
            if (p == null) { Debug.LogWarning($"[GDD Build] {so.targetObject.GetType().Name} has no field '{path}'."); return; }
            p.arraySize = values.Length;
            for (int i = 0; i < values.Length; i++) p.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
        }
    }
}
#endif
