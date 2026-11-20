using UnityEngine;

namespace KungFuCrashout.UI
{
    /// <summary>
    /// Keeps a layout authored at one fixed size (the UI concepts are 2424 × 1080 = the Pixel 9a screen) whole on any screen:
    /// this RectTransform is sized to Design Size and uniformly SCALED to fit its parent (normally the safe area), so every
    /// child keeps its exact concept position and size relative to the others, text included.
    ///   • 20:9 phone (Pixel 9a): scale 1, pixel-for-pixel the concept.
    ///   • Other phones (19.5:9 … 21:9): a few % smaller or centred with background showing at the sides.
    ///   • 16:9 / tablets: fitted to the width; the spare height goes above or below depending on Pivot (set by the builder:
    ///     bottom-anchored layers keep characters on the floor, top-anchored ones keep the title / back button at the top).
    /// Place in: Assets/_Project/Scripts/UI/StageFit.cs
    /// </summary>
    [ExecuteAlways]
    [RequireComponent(typeof(RectTransform))]
    public class StageFit : MonoBehaviour
    {
        [SerializeField] Vector2 designSize = new Vector2(2424f, 1080f);
        [Tooltip("Never scale up past the concept size.")]
        [SerializeField] bool noUpscale = true;

        RectTransform rt;
        Vector2 lastParent;

        public float Scale { get; private set; } = 1f;

        void OnEnable()
        {
            rt = (RectTransform)transform;
            Fit();
        }

        void LateUpdate()
        {
            var p = transform.parent as RectTransform;
            if (p && p.rect.size != lastParent) Fit();
        }

        void OnRectTransformDimensionsChange()
        {
            if (rt) Fit();
        }

        public void Fit()
        {
            var p = transform.parent as RectTransform;
            if (!rt || !p) return;
            Vector2 size = p.rect.size;
            lastParent = size;
            if (size.x <= 0f || size.y <= 0f) return;

            float s = Mathf.Min(size.x / designSize.x, size.y / designSize.y);
            if (noUpscale) s = Mathf.Min(1f, s);
            Scale = s;

            // Anchor and pivot at the same point, so the scaled stage hugs that point of the parent.
            rt.anchorMin = rt.anchorMax = rt.pivot;
            rt.sizeDelta = designSize;
            rt.anchoredPosition = Vector2.zero;
            rt.localScale = new Vector3(s, s, 1f);
        }
    }
}
