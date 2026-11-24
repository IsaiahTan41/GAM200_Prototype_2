using UnityEngine;
using UnityEngine.UI;

namespace KungFuCrashout.UI
{
    /// <summary>
    /// Colours a UI Graphic top-to-bottom (vertex colours), e.g. a plain black Image as a dim that is darker at the top.
    /// The Graphic's own colour multiplies in. No texture needed.
    /// Place in: Assets/_Project/Scripts/UI/UiVerticalGradient.cs
    /// </summary>
    [RequireComponent(typeof(Graphic))]
    public class UiVerticalGradient : BaseMeshEffect
    {
        [SerializeField] Color top = new Color(0f, 0f, 0f, 0.65f);
        [SerializeField] Color bottom = new Color(0f, 0f, 0f, 0.25f);

        public override void ModifyMesh(VertexHelper vh)
        {
            if (!IsActive() || vh.currentVertCount == 0) return;
            var v = new UIVertex();
            float min = float.MaxValue, max = float.MinValue;
            for (int i = 0; i < vh.currentVertCount; i++)
            {
                vh.PopulateUIVertex(ref v, i);
                min = Mathf.Min(min, v.position.y);
                max = Mathf.Max(max, v.position.y);
            }
            float h = Mathf.Max(0.0001f, max - min);
            for (int i = 0; i < vh.currentVertCount; i++)
            {
                vh.PopulateUIVertex(ref v, i);
                v.color = (Color)v.color * Color.Lerp(bottom, top, (v.position.y - min) / h);
                vh.SetUIVertex(v, i);
            }
        }
    }
}
