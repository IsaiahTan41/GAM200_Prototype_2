using UnityEngine;

namespace KungFuCrashout.UI
{
    /// <summary>
    /// Full-bleed art inside a StageFit layout (e.g. the win / lose banners): on screens wider than the concept it scales up
    /// uniformly, around its pivot, until it reaches both edges of the canvas, so the band never ends short of the screen.
    /// At the concept's shape (Pixel 9a) or narrower it stays at scale 1.
    /// Place in: Assets/_Project/Scripts/UI/CoverWidth.cs
    /// </summary>
    [ExecuteAlways]
    [RequireComponent(typeof(RectTransform))]
    public class CoverWidth : MonoBehaviour
    {
        RectTransform rt, canvasRt;

        void OnEnable()
        {
            rt = (RectTransform)transform;
            var canvas = GetComponentInParent<Canvas>();
            canvasRt = canvas ? (RectTransform)canvas.rootCanvas.transform : null;
        }

        void LateUpdate()
        {
            if (!rt || !canvasRt || !rt.parent) return;
            float parentScale = rt.parent.lossyScale.x;
            float canvasWidth = canvasRt.rect.width * canvasRt.lossyScale.x; // in world units
            float myWidth = rt.rect.width * parentScale;
            float s = myWidth > 0.0001f ? Mathf.Max(1f, canvasWidth / myWidth) : 1f;
            if (!Mathf.Approximately(rt.localScale.x, s)) rt.localScale = new Vector3(s, s, 1f);
        }
    }
}
