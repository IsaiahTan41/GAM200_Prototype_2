using UnityEngine;

namespace KungFuCrashout.UI
{
    /// <summary>
    /// Stretches this element's top edge up to the top of the screen, keeping its bottom where it is. For tall art inside a
    /// scaled StageFit (the character-select centre wall): on screens narrower than the concept the stage is scaled down and
    /// sits on the floor, and this keeps the wall reaching the top instead of stopping short. Use a 9-sliced sprite so only
    /// its plain middle stretches. Place in: Assets/_Project/Scripts/UI/ExtendToCanvasTop.cs
    /// </summary>
    [ExecuteAlways]
    [RequireComponent(typeof(RectTransform))]
    public class ExtendToCanvasTop : MonoBehaviour
    {
        RectTransform rt;
        Canvas canvas;
        readonly Vector3[] corners = new Vector3[4];

        void OnEnable()
        {
            rt = (RectTransform)transform;
            canvas = GetComponentInParent<Canvas>();
        }

        void LateUpdate()
        {
            if (!rt || !canvas || !(rt.parent is RectTransform parent)) return;
            var root = canvas.rootCanvas.transform as RectTransform;
            if (!root) return;
            root.GetWorldCorners(corners);                       // 1 = top-left
            float topLocal = parent.InverseTransformPoint(corners[1]).y;
            rt.GetWorldCorners(corners);
            float bottomLocal = parent.InverseTransformPoint(corners[0]).y;
            float want = Mathf.Max(0f, topLocal - bottomLocal);
            // Grow from the bottom: pivot y at the bottom edge, height = distance to the screen top.
            if (!Mathf.Approximately(rt.pivot.y, 0f))
            {
                var pos = rt.anchoredPosition;
                pos.y -= rt.rect.height * rt.pivot.y;
                rt.pivot = new Vector2(rt.pivot.x, 0f);
                rt.anchoredPosition = pos;
            }
            if (Mathf.Abs(rt.sizeDelta.y - want) > 0.5f && rt.anchorMin.y == rt.anchorMax.y)
                rt.sizeDelta = new Vector2(rt.sizeDelta.x, want);
        }
    }
}
