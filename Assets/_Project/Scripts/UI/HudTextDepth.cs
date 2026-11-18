using TMPro;
using UnityEngine;

namespace KungFuCrashout.UI
{
    /// <summary>
    /// The "3D" fight-HUD text from the UI spec: a copy of this text in one solid colour, Offset pixels lower, drawn behind
    /// it (combo words / numbers and ROUND 1 / FIGHT: brown or orange, 4 px down). The copy shares the text's font and
    /// material (so it gets the same outline, in its own colour) and follows its text, size, position, scale and alpha.
    /// It is rebuilt when the scene loads and never saved (nothing to keep in sync by hand). Turn the component off = no depth.
    /// Place in: Assets/_Project/Scripts/UI/HudTextDepth.cs
    /// </summary>
    [ExecuteAlways]
    [RequireComponent(typeof(TMP_Text))]
    public class HudTextDepth : MonoBehaviour
    {
        [SerializeField] Vector2 offset = new Vector2(0f, -4f);
        [SerializeField] Color colour = new Color32(0x3A, 0x30, 0x28, 0xFF);

        TMP_Text src;
        TextMeshProUGUI copy;
        string CopyName => name + " (depth)";

        public Color Colour { get => colour; set => colour = value; }

        void OnEnable()
        {
            src = GetComponent<TMP_Text>();
            Ensure();
            if (copy) copy.gameObject.SetActive(true);
            Sync();
        }

        void OnDisable()
        {
            if (copy) copy.gameObject.SetActive(false);
        }

        void OnDestroy()
        {
            if (!copy) return;
            var go = copy.gameObject;
            if (Application.isPlaying) { Destroy(go); return; }
#if UNITY_EDITOR
            // The copy is often being destroyed in the same pass (its parent is): only clean up if it survived.
            UnityEditor.EditorApplication.delayCall += () => { if (go) DestroyImmediate(go); };
#endif
        }

        void LateUpdate() => Sync();

        void Ensure()
        {
            if (copy || !transform.parent) return;
            // After a script reload the old copy is still there: reuse it instead of making a second one.
            foreach (Transform sibling in transform.parent)
                if (sibling.name == CopyName && (sibling.gameObject.hideFlags & HideFlags.DontSave) != 0)
                {
                    copy = sibling.GetComponent<TextMeshProUGUI>();
                    if (copy) return;
                }
            var go = new GameObject(CopyName, typeof(RectTransform));
            go.hideFlags = HideFlags.DontSave | HideFlags.NotEditable;
            go.layer = gameObject.layer;
            go.transform.SetParent(transform.parent, false);
            copy = go.AddComponent<TextMeshProUGUI>();
            copy.raycastTarget = false;
        }

        void Sync()
        {
            if (!src || !copy) return;
            var t = (RectTransform)transform;
            var c = copy.rectTransform;

            int want = t.GetSiblingIndex();
            int have = c.GetSiblingIndex();
            if (have > want) c.SetSiblingIndex(want);               // behind the text: just before it
            else if (have < want - 1) c.SetSiblingIndex(want - 1);

            c.anchorMin = t.anchorMin; c.anchorMax = t.anchorMax; c.pivot = t.pivot;
            c.sizeDelta = t.sizeDelta;
            c.anchoredPosition = t.anchoredPosition + offset;
            c.localScale = t.localScale; c.localRotation = t.localRotation;

            if (copy.font != src.font) copy.font = src.font;
            if (copy.fontSharedMaterial != src.fontSharedMaterial) copy.fontSharedMaterial = src.fontSharedMaterial;
            copy.fontSize = src.fontSize;
            copy.fontStyle = src.fontStyle;
            copy.alignment = src.alignment;
            copy.textWrappingMode = src.textWrappingMode;
            copy.overflowMode = src.overflowMode;
            copy.characterSpacing = src.characterSpacing;
            copy.wordSpacing = src.wordSpacing;
            copy.enableAutoSizing = false;
            copy.enableVertexGradient = false;
            copy.richText = src.richText;
            copy.margin = src.margin;
            if (copy.text != src.text) copy.text = src.text;

            var col = colour;
            col.a *= src.color.a;
            if (copy.color != col) copy.color = col;
        }
    }
}
