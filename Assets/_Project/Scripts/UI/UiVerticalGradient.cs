using UnityEngine;
using UnityEngine.UI;

namespace KungFuCrashout.UI
{
    /// <summary>
    /// A plain two-colour vertical gradient filling its rect (no texture). Used as a stand-in screen background where the
    /// art isn't supplied yet (Level Select). Place in: Assets/_Project/Scripts/UI/UiVerticalGradient.cs
    /// </summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public class UiVerticalGradient : MaskableGraphic
    {
        [SerializeField] Color top = new Color32(0x45, 0x44, 0x39, 0xFF);
        [SerializeField] Color bottom = new Color32(0x91, 0x81, 0x60, 0xFF);

        public void SetColours(Color t, Color b) { top = t; bottom = b; SetVerticesDirty(); }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            Rect r = GetPixelAdjustedRect();
            var v = UIVertex.simpleVert;
            v.color = bottom * color; v.position = new Vector3(r.xMin, r.yMin); vh.AddVert(v);
            v.color = top * color;    v.position = new Vector3(r.xMin, r.yMax); vh.AddVert(v);
            v.color = top * color;    v.position = new Vector3(r.xMax, r.yMax); vh.AddVert(v);
            v.color = bottom * color; v.position = new Vector3(r.xMax, r.yMin); vh.AddVert(v);
            vh.AddTriangle(0, 1, 2);
            vh.AddTriangle(2, 3, 0);
        }
    }
}
