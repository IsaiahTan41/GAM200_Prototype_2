using UnityEngine;

namespace KungFuCrashout.Fighters
{
    /// <summary>
    /// Outline by underlay: a duplicate of this object's sprite, drawn just BEHIND it, slightly bigger and tinted
    /// (red by default), so a coloured rim shows around the character. It copies the current pose sprite and facing
    /// every frame, so it follows pose swaps, flips and the hit squash automatically.
    ///
    ///   • Silhouette Material: a material using "GUI/Text Shader" makes the duplicate a SOLID colour (a clean outline).
    ///     Without it, the duplicate is the sprite tinted red (the art's darker areas give a darker, uneven rim).
    ///     KungFu Crashout → Add Opponent Outline creates that material and sets everything up.
    ///   • The duplicate is scaled about the sprite's centre so the rim is the same Thickness on every side.
    ///     It's a scaled copy, so deep inward corners (e.g. between arm and body) get less rim than the edges.
    ///
    /// Put on the object with the fighter's SpriteRenderer (the "Visual" child with FighterPoseView).
    /// Place in: Assets/_Project/Scripts/Fighters/SpriteOutline.cs
    /// </summary>
    [RequireComponent(typeof(SpriteRenderer))]
    [DefaultExecutionOrder(95)] // after FighterPoseView (90) has picked this frame's pose
    public class SpriteOutline : MonoBehaviour
    {
        [Tooltip("Outline colour (alpha = opacity).")]
        [SerializeField] Color colour = new Color(1f, 0f, 0f, 0.9f);
        [Tooltip("Rim width around the sprite (world units).")]
        [SerializeField, Range(0f, 0.3f)] float thickness = 0.05f;
        [Tooltip("A material using \"GUI/Text Shader\" for a solid-colour silhouette. Empty = tinted copy of the sprite.")]
        [SerializeField] Material silhouetteMaterial;
        [Tooltip("Fade with the character (e.g. the invincibility flicker after getting up).")]
        [SerializeField] bool followArtAlpha = true;
        [Tooltip("Turn the outline on/off (also works while playing).")]
        [SerializeField] bool showOutline = true;

        SpriteRenderer art;
        SpriteRenderer outline;

        public bool ShowOutline { get => showOutline; set => showOutline = value; }
        public Color Colour { get => colour; set => colour = value; }

        void Awake()
        {
            art = GetComponent<SpriteRenderer>();

            var go = new GameObject("Outline");
            go.transform.SetParent(transform, false);
            outline = go.AddComponent<SpriteRenderer>();
            if (silhouetteMaterial) outline.sharedMaterial = silhouetteMaterial;
        }

        void OnDisable()
        {
            if (outline) outline.enabled = false;
        }

        void LateUpdate()
        {
            if (!outline) return;

            var sprite = art.sprite;
            bool on = showOutline && art.enabled && sprite && thickness > 0f && colour.a > 0f;
            outline.enabled = on;
            if (!on) return;

            outline.sprite = sprite;
            outline.flipX = art.flipX;
            outline.flipY = art.flipY;
            outline.sortingLayerID = art.sortingLayerID;
            outline.sortingOrder = art.sortingOrder - 1; // just behind the character

            var c = colour;
            if (followArtAlpha) c.a *= art.color.a;
            outline.color = c;

            // Scale about the sprite's centre (not its feet pivot) so the rim is even all round.
            Vector3 size = sprite.bounds.size;
            Vector3 centre = sprite.bounds.center;
            if (art.flipX) centre.x = -centre.x;
            if (art.flipY) centre.y = -centre.y;

            float sx = size.x > 0.0001f ? 1f + 2f * thickness / size.x : 1f;
            float sy = size.y > 0.0001f ? 1f + 2f * thickness / size.y : 1f;
            var t = outline.transform;
            t.localScale = new Vector3(sx, sy, 1f);
            t.localPosition = new Vector3(centre.x * (1f - sx), centre.y * (1f - sy), 0f);
        }
    }
}
