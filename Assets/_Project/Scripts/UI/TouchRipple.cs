using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem.EnhancedTouch;
using UnityEngine.UI;
using ETouch = UnityEngine.InputSystem.EnhancedTouch.Touch;
using TouchPhase = UnityEngine.InputSystem.TouchPhase;

namespace KungFuCrashout.UI
{
    /// <summary>
    /// UI Standards §7 "Touch": a fingertip-sized ring fades in under every finger as it lands, and a swipe leaves a short
    /// trail of fading dots, so every input visibly registers on the next frame. Two fingers landing together (the block)
    /// show two rings at once, which is exactly the shape the player needs to learn.
    /// Never blocks touches (no raycast targets). Makes its own overlay canvas and ring sprite; nothing to set up.
    /// Setup: KungFu Crashout → GDD Build → Upgrade Fight Scene adds it. Place in: Assets/_Project/Scripts/UI/TouchRipple.cs
    /// </summary>
    public class TouchRipple : MonoBehaviour
    {
        [SerializeField] Color colour = new Color(1f, 1f, 1f, 0.55f);
        [Tooltip("Ring size as a fraction of the screen's short side (a fingertip is about 0.09).")]
        [SerializeField, Range(0.03f, 0.2f)] float ringSize = 0.09f;
        [SerializeField, Min(0.05f)] float ringTime = 0.35f;
        [Tooltip("Trail dot every this many pixels of finger movement.")]
        [SerializeField, Min(4f)] float trailSpacing = 24f;
        [SerializeField, Min(0.05f)] float trailTime = 0.2f;
        [Tooltip("Ignore touches that start in the HUD band at the top (fraction of the screen from the bottom).")]
        [SerializeField, Range(0.2f, 1f)] float inputZoneTop = 0.85f;

        class Mark { public RectTransform rt; public Image img; public float born, life, size; public bool ring; }

        readonly List<Mark> live = new List<Mark>();
        readonly Stack<Mark> pool = new Stack<Mark>();
        readonly Dictionary<int, Vector2> lastTrail = new Dictionary<int, Vector2>();
        RectTransform root;
        Sprite ringSprite, dotSprite;

        void OnEnable()
        {
            EnhancedTouchSupport.Enable();
            if (!root) Build();
        }

        void OnDisable() => EnhancedTouchSupport.Disable();

        void Build()
        {
            var go = new GameObject("Touch Ripples", typeof(RectTransform), typeof(Canvas));
            go.transform.SetParent(transform, false);
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 500; // over everything; it never takes touches
            root = (RectTransform)go.transform;
            ringSprite = MakeSprite(ring: true);
            dotSprite = MakeSprite(ring: false);
        }

        void Update()
        {
            float shortSide = Mathf.Min(Screen.width, Screen.height);
            foreach (var t in ETouch.activeTouches)
            {
                if (t.startScreenPosition.y > Screen.height * inputZoneTop) continue;
                int id = t.touchId;
                Vector2 pos = t.screenPosition;
                if (t.phase == TouchPhase.Began)
                {
                    Spawn(pos, shortSide * ringSize, ringTime, ring: true);
                    lastTrail[id] = pos;
                }
                else if (t.phase == TouchPhase.Moved && lastTrail.TryGetValue(id, out var last) &&
                         (pos - last).magnitude >= trailSpacing)
                {
                    Spawn(pos, shortSide * ringSize * 0.3f, trailTime, ring: false);
                    lastTrail[id] = pos;
                }
                else if (t.phase == TouchPhase.Ended || t.phase == TouchPhase.Canceled) lastTrail.Remove(id);
            }

            float now = Time.unscaledTime;
            for (int i = live.Count - 1; i >= 0; i--)
            {
                var m = live[i];
                float k = (now - m.born) / m.life;
                if (k >= 1f)
                {
                    m.img.enabled = false;
                    pool.Push(m);
                    live.RemoveAt(i);
                    continue;
                }
                float grow = m.ring ? Mathf.Lerp(0.6f, 1.15f, 1f - (1f - k) * (1f - k)) : 1f;
                m.rt.sizeDelta = Vector2.one * m.size * grow;
                var c = colour;
                c.a *= 1f - k;
                m.img.color = c;
            }
        }

        void Spawn(Vector2 screenPos, float size, float life, bool ring)
        {
            var m = pool.Count > 0 ? pool.Pop() : NewMark();
            m.img.sprite = ring ? ringSprite : dotSprite;
            m.img.enabled = true;
            m.rt.position = screenPos;
            m.rt.sizeDelta = Vector2.one * size;
            m.born = Time.unscaledTime;
            m.life = life;
            m.size = size;
            m.ring = ring;
            live.Add(m);
        }

        Mark NewMark()
        {
            var go = new GameObject("Mark", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(root, false);
            var img = go.GetComponent<Image>();
            img.raycastTarget = false;
            return new Mark { rt = (RectTransform)go.transform, img = img };
        }

        static Sprite MakeSprite(bool ring)
        {
            const int n = 64;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[n * n];
            float c = (n - 1) * 0.5f;
            for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float d = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c)) / c; // 0 centre → 1 edge
                float a = ring ? Mathf.Clamp01(1f - Mathf.Abs(d - 0.82f) / 0.12f) : Mathf.Clamp01((1f - d) / 0.15f);
                px[y * n + x] = new Color32(255, 255, 255, (byte)(a * 255));
            }
            tex.SetPixels32(px);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), 100f);
        }
    }
}
