using UnityEngine;
using UnityEngine.UI;

namespace KungFuCrashout.UI
{
    /// <summary>
    /// One segment of a fight-HUD health bar, drawn as a slanted quad with a two-colour gradient (Concept 27 HUD).
    ///   • The bar runs from its ANCHORED end (full health, next to the timer) to its OUTER end (by the portrait).
    ///     From / To are fractions along it (0 = anchored end): green = 0…health, red = health…health+recoverable, etc.
    ///   • Both ends are slanted like the frame: Cap Slant is the anchored end (matches the frame's rounded cap), Lead Slant
    ///     every other edge (the health / damage boundaries). Slant = horizontal pixels per vertical pixel.
    ///   • Gradient along the segment: Anchor Colour at its anchored-side edge, Lead Colour at its moving edge.
    ///   • Anchor Right = P1 (bar fills from the right, leans "\"); off = CPU (fills from the left, leans "/").
    /// No texture: it scales with the canvas at any resolution. Place in: Assets/_Project/Scripts/UI/SlantedBar.cs
    /// </summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public class SlantedBar : MaskableGraphic
    {
        [SerializeField, Range(0f, 1f)] float from = 0f;
        [SerializeField, Range(0f, 1f)] float to = 1f;
        [Tooltip("P1: the full end is on the RIGHT (by the timer). Off for the CPU bar (mirrored).")]
        [SerializeField] bool anchorRight = true;
        [Tooltip("Slant of the health / damage edges (horizontal px per vertical px).")]
        [SerializeField, Min(0f)] float leadSlant = 0.83f;
        [Tooltip("Slant of the anchored end, to sit inside the frame's cap.")]
        [SerializeField, Min(0f)] float capSlant = 1.6f;
        [SerializeField] Color anchorColour = Color.white;
        [SerializeField] Color leadColour = Color.white;

        public float From => from;
        public float To => to;

        public void SetRange(float a, float b)
        {
            a = Mathf.Clamp01(a); b = Mathf.Clamp01(b);
            if (Mathf.Approximately(a, from) && Mathf.Approximately(b, to)) return;
            from = a; to = b;
            SetVerticesDirty();
        }

        public void SetColours(Color anchor, Color lead)
        {
            if (anchor == anchorColour && lead == leadColour) return;
            anchorColour = anchor; leadColour = lead;
            SetVerticesDirty();
        }

        public void Configure(bool anchoredRight, float lead, float cap)
        {
            anchorRight = anchoredRight; leadSlant = lead; capSlant = cap;
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            if (to <= from + 0.0001f) return;

            Rect r = GetPixelAdjustedRect();
            float h = r.height;
            float halfCap = capSlant * h * 0.5f, halfLead = leadSlant * h * 0.5f;
            // Centre line of the anchored end and of the outer end, inset so the slanted edges stay inside the rect.
            float xA = anchorRight ? r.xMax - halfCap : r.xMin + halfCap;
            float xO = anchorRight ? r.xMin + halfLead : r.xMax - halfLead;
            float lean = anchorRight ? 1f : -1f; // "\" for P1: the top of each edge sits further toward the outer end

            float Edge(float u, bool top)
            {
                float xc = Mathf.Lerp(xA, xO, u);
                float half = (u <= 0.0001f ? capSlant : leadSlant) * h * 0.5f;
                return xc + (top ? -lean : lean) * half;
            }

            Color ca = anchorColour * color, cl = leadColour * color;
            float yT = r.yMax, yB = r.yMin;
            var v = UIVertex.simpleVert;
            v.color = ca; v.position = new Vector3(Edge(from, true), yT);  vh.AddVert(v);
            v.color = ca; v.position = new Vector3(Edge(from, false), yB); vh.AddVert(v);
            v.color = cl; v.position = new Vector3(Edge(to, false), yB);   vh.AddVert(v);
            v.color = cl; v.position = new Vector3(Edge(to, true), yT);    vh.AddVert(v);
            vh.AddTriangle(0, 1, 2);
            vh.AddTriangle(2, 3, 0);
        }
    }
}
