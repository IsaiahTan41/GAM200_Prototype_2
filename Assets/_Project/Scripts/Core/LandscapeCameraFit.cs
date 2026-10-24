using UnityEngine;
using KungFuCrashout.Fighters;

namespace KungFuCrashout.Core
{
    /// <summary>
    /// Frames the fight lane with the orthographic camera in LANDSCAPE, on any phone or tablet shape.
    ///
    /// Two modes (Mode dropdown, switchable while playing):
    ///
    ///   WHOLE STAGE (original)
    ///     • the whole stage width (plus padding) is always visible, so a wider stage = smaller fighters
    ///
    ///   FOLLOW FIGHTERS (default — stage width and view are separate)
    ///     • the stage can be much wider than the screen; the view is View Width wide, however wide the stage is
    ///     • the camera follows the midpoint between the Follow Targets (empty = every Fighter in the scene)
    ///     • when they move apart it zooms out so both stay on screen; when they close in it zooms back
    ///     • it never shows past the stage walls (plus Horizontal Padding)
    ///     • WALLS COME FROM StageBounds ONLY. The camera never stops a fighter — unless you tick Limit
    ///       Separation, which adds a classic "screen edge" limit: fighters can't get further apart than
    ///       Max View Width shows (the camera then never zooms out past Max View Width).
    ///
    /// Both modes:
    ///   • at least `minVisibleHeight` world units of height are always visible (very wide phones)
    ///   • the ground line sits at a chosen height on screen, leaving room for the HUD band above
    ///   • HitFeedback's screen shake still works (it's added on top afterwards)
    ///
    /// Output (camera hybrid):
    ///   CAMERA       — moves the Main Camera and sets its size directly (no Cinemachine needed)
    ///   VALUES ONLY  — works out the same framing but only publishes it (Target Position, Target Ortho
    ///                  Size). CinemachineFightRig copies it onto a Cinemachine Camera, which adds impulse
    ///                  shake and blends. The fighter leash (StageBounds.MaxSeparation) works in both.
    ///
    /// Place in: Assets/_Project/Scripts/Core/LandscapeCameraFit.cs
    /// </summary>
    [ExecuteAlways]
    [RequireComponent(typeof(Camera))]
    [DefaultExecutionOrder(60)] // after fighters (and their push-apart) and carried items have moved
    public class LandscapeCameraFit : MonoBehaviour
    {
        public enum FramingMode { WholeStage, FollowFighters }
        public enum OutputMode { Camera, ValuesOnly }

        [SerializeField] StageBounds stage;
        [SerializeField] FramingMode mode = FramingMode.FollowFighters;
        [Tooltip("Camera: move the Main Camera directly. Values Only: just publish the framing for CinemachineFightRig.")]
        [SerializeField] OutputMode output = OutputMode.Camera;

        [Tooltip("Whole Stage: extra world units visible beyond each wall. Follow Fighters: how far past a wall the view may go.")]
        [SerializeField, Min(0f)] float horizontalPadding = 0.5f;

        [Tooltip("Never show less than this many world units of height. Raise it if tall characters or jumps get cut off.")]
        [SerializeField, Min(1f)] float minVisibleHeight = 5f;

        [Tooltip("How far in front of the stage the camera sits (world units). The camera is always placed at the stage's Z minus this, " +
                 "so an accidental drag of the camera's Z can never put it behind the sprites.")]
        [SerializeField, Min(0.5f)] float cameraDistance = 10f;

        [Tooltip("Where the ground line sits on screen. 0 = bottom edge, 1 = top edge. " +
                 "~0.1–0.3 keeps the feet clear of the thumbs, with the HUD band above.")]
        [SerializeField, Range(0f, 1f)] float groundScreenHeight = 0.1f;

        [Header("Follow Fighters mode")]
        [Tooltip("The fighters to keep on screen (Player, Opponent). Empty = every Fighter in the scene.")]
        [SerializeField] Transform[] followTargets = new Transform[2];
        [Tooltip("World units visible across the screen when the fighters are close. Independent of the stage width: a stage narrower than this is centred, a wider one scrolls.")]
        [SerializeField, Min(2f)] float viewWidth = 11f;
        [Tooltip("Limit Separation ON: widest the view zooms out to, and the fighters can't get further apart than it shows.\n" +
                 "Limit Separation OFF: ignored — the view zooms out as far as needed (up to the whole stage) to keep both fighters on screen.")]
        [SerializeField, Min(2f)] float maxViewWidth = 13f;
        [Tooltip("Space kept between each fighter's centre and the screen edge (world units).")]
        [SerializeField, Min(0f)] float edgeMargin = 1.5f;
        [Tooltip("How quickly the camera slides to follow. Higher = snappier, lower = smoother.")]
        [SerializeField, Min(0.5f)] float followSharpness = 6f;
        [Tooltip("How quickly it zooms in and out. Lower = gentler.")]
        [SerializeField, Min(0.5f)] float zoomSharpness = 3f;
        [Tooltip("OFF (default): only the StageBounds walls stop the fighters; the camera zooms out to keep both in view.\n" +
                 "ON: also stop the fighters getting further apart than Max View Width shows — an invisible screen-edge wall.")]
        [SerializeField] bool limitSeparation = false;

        Camera cam;
        float currentX, currentWidth;
        bool following; // false = next follow frame snaps instead of easing

        public FramingMode Mode { get => mode; set => mode = value; }
        public OutputMode Output { get => output; set => output = value; }

        /// <summary>Where the camera should be this frame (world space, keeps the camera's Z).</summary>
        public Vector3 TargetPosition { get; private set; }
        /// <summary>The orthographic size the camera should have this frame.</summary>
        public float TargetOrthoSize { get; private set; } = 5f;
        /// <summary>The fight camera's aspect ratio, for anything that needs the visible width.</summary>
        public float Aspect => cam ? cam.aspect : 16f / 9f;

        void OnEnable() => cam = GetComponent<Camera>();

        void OnDisable()
        {
            if (stage && Application.isPlaying) stage.MaxSeparation = 0f;
            following = false;
        }

        void LateUpdate()
        {
            if (!stage || !cam) return;
            cam.orthographic = true;

            if (mode == FramingMode.FollowFighters && TryGetSpan(out float minX, out float maxX))
                FollowFighters(minX, maxX);
            else
                FitWholeStage();
        }

        // ───────────── Whole stage (original) ─────────────

        void FitWholeStage()
        {
            following = false;
            if (Application.isPlaying) stage.MaxSeparation = 0f;

            // Size needed to show the whole stage width at this screen shape…
            float halfVisibleWidth = stage.Width * 0.5f + horizontalPadding;
            float size = SizeForWidth(halfVisibleWidth * 2f);

            Place(stage.transform.position.x, size);
        }

        // ───────────── Follow fighters ─────────────

        void FollowFighters(float minX, float maxX)
        {
            // The view width comes from View Width only — the stage width never changes it.
            // It only grows past View Width when the fighters are further apart than it can show:
            //   Limit Separation ON  → up to Max View Width (and the fighters are held within it)
            //   Limit Separation OFF → as far as needed to keep both fighters on screen (at most the whole stage)
            float wholeStage = stage.Width + horizontalPadding * 2f;
            float closest = viewWidth;
            float widest = limitSeparation
                ? Mathf.Max(viewWidth, maxViewWidth)
                : Mathf.Max(viewWidth, wholeStage);

            // Zoom: wide enough to show both fighters plus the edge margin, within [View Width, Max View Width].
            float targetWidth = Mathf.Clamp((maxX - minX) + edgeMargin * 2f, closest, widest);
            float targetX = (minX + maxX) * 0.5f;

            // Ease toward the targets (snap on the first frame and in edit mode).
            // Uses scaled time, so the camera holds still during hitstop and slows with slow motion.
            bool snap = !following || !Application.isPlaying;
            float dt = Time.deltaTime;
            currentWidth = snap ? targetWidth : Mathf.Lerp(currentWidth, targetWidth, 1f - Mathf.Exp(-zoomSharpness * dt));
            currentX = snap ? targetX : Mathf.Lerp(currentX, targetX, 1f - Mathf.Exp(-followSharpness * dt));
            following = true;

            float size = SizeForWidth(currentWidth);

            // Keep the view inside the stage (plus padding). Stage narrower than the view: centre it
            // (the background art past the walls fills the rest — the walls stay where StageBounds puts them).
            float halfW = size * cam.aspect;
            float left = stage.MinX - horizontalPadding + halfW;
            float right = stage.MaxX + horizontalPadding - halfW;
            currentX = left <= right ? Mathf.Clamp(currentX, left, right) : stage.transform.position.x;

            Place(currentX, size);

            // Leash: the fighters may be at most as far apart as the widest view shows, minus the margins.
            if (Application.isPlaying)
            {
                float widestShown = SizeForWidth(widest) * cam.aspect * 2f;
                stage.MaxSeparation = limitSeparation ? Mathf.Max(1f, widestShown - edgeMargin * 2f) : 0f;
            }
        }

        bool TryGetSpan(out float minX, out float maxX)
        {
            minX = float.MaxValue;
            maxX = float.MinValue;
            if (!HasAnyTarget()) AutoFindTargets();
            if (followTargets == null) return false;

            foreach (var t in followTargets)
            {
                if (!t || !t.gameObject.activeInHierarchy) continue;
                minX = Mathf.Min(minX, t.position.x);
                maxX = Mathf.Max(maxX, t.position.x);
            }
            return minX <= maxX;
        }

        bool HasAnyTarget()
        {
            if (followTargets == null) return false;
            foreach (var t in followTargets) if (t) return true;
            return false;
        }

        /// <summary>Follow Targets left empty: follow every Fighter in the scene.</summary>
        void AutoFindTargets()
        {
            var found = FindObjectsByType<Fighter>(FindObjectsSortMode.None);
            if (found.Length == 0) return;
            followTargets = new Transform[found.Length];
            for (int i = 0; i < found.Length; i++) followTargets[i] = found[i].transform;
        }

        // ───────────── Shared ─────────────

        /// <summary>Orthographic size that shows `width` world units across, but never less than Min Visible Height.</summary>
        float SizeForWidth(float width) => Mathf.Max(width * 0.5f / cam.aspect, minVisibleHeight * 0.5f);

        /// <summary>Work out the final framing (ground line at the chosen screen height) and publish or apply it.</summary>
        void Place(float x, float size)
        {
            float viewHeight = size * 2f;
            float camY = stage.GroundY + viewHeight * (0.5f - groundScreenHeight);

            TargetOrthoSize = size;
            // Z is fixed in front of the stage: an orthographic camera looks along +Z, so it must be at a LOWER Z
            // than the sprites (z = 0) or it sees nothing.
            TargetPosition = new Vector3(x, camY, stage.transform.position.z - cameraDistance);

            if (output != OutputMode.Camera) return; // CinemachineFightRig applies it instead

            cam.orthographicSize = size;
            transform.position = TargetPosition;
        }
    }
}
