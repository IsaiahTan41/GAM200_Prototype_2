using UnityEngine;

namespace KungFuCrashout.Core
{
    /// <summary>
    /// Defines the left/right edges of the fight lane — THE walls. Fighters, knockback, launches and thrown
    /// items all stop here (and bounce off here). The camera never decides where the walls are.
    /// The stage is centred on this GameObject's position; its Y is the ground line.
    /// The stage can be wider than the screen: LandscapeCameraFit's Follow Fighters mode scrolls the
    /// camera and sets Max Separation so the fighters can't get further apart than the view can show.
    /// Place in: Assets/_Project/Scripts/Core/StageBounds.cs
    /// </summary>
    public class StageBounds : MonoBehaviour
    {
        [SerializeField, Min(1f)] float width = 10f;
        [Tooltip("Optional: the visible floor (e.g. the Ground sprite). Its X scale and position follow Width, so widening the stage is one number.")]
        [SerializeField] Transform groundVisual;

        public float Width => width;
        public float MinX => transform.position.x - width * 0.5f;
        public float MaxX => transform.position.x + width * 0.5f;
        public float GroundY => transform.position.y;

        /// <summary>Clamp an X position inside the stage, keeping `margin` away from each wall.</summary>
        public float ClampX(float x, float margin = 0f) => Mathf.Clamp(x, MinX + margin, MaxX - margin);

        /// <summary>
        /// Furthest the two fighters may be apart (centre to centre), so both always fit on screen.
        /// 0 = no limit. Set every frame by LandscapeCameraFit in Follow Fighters mode; Fighter respects it.
        /// </summary>
        public float MaxSeparation { get; set; }

        void OnValidate() => SyncGround();
        void Awake() => SyncGround();

        /// <summary>Stretch the floor sprite to the stage width and centre it on the stage.</summary>
        void SyncGround()
        {
            if (!groundVisual) return;
            var s = groundVisual.localScale;
            s.x = width;
            groundVisual.localScale = s;
            var p = groundVisual.position;
            p.x = transform.position.x;
            groundVisual.position = p;
        }

        void OnDrawGizmos()
        {
            Gizmos.color = Color.green;
            float y = GroundY;
            Gizmos.DrawLine(new Vector3(MinX, y), new Vector3(MaxX, y));
            Gizmos.DrawLine(new Vector3(MinX, y), new Vector3(MinX, y + 3f));
            Gizmos.DrawLine(new Vector3(MaxX, y), new Vector3(MaxX, y + 3f));
        }
    }
}