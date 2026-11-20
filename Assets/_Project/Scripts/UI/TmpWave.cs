using TMPro;
using UnityEngine;

namespace KungFuCrashout.UI
{
    /// <summary>
    /// Loading-style wave: each letter bobs up and down a little behind the one before it ("Waiting for the other player...").
    /// Works on the text's mesh, so it costs nothing when the object is hidden. Real time (keeps moving while paused).
    /// Place in: Assets/_Project/Scripts/UI/TmpWave.cs
    /// </summary>
    [RequireComponent(typeof(TMP_Text))]
    public class TmpWave : MonoBehaviour
    {
        [Tooltip("How high each letter rises (canvas pixels).")]
        [SerializeField] float amplitude = 7f;
        [Tooltip("Waves per second.")]
        [SerializeField] float speed = 1.6f;
        [Tooltip("Delay between neighbouring letters, as a fraction of a wave.")]
        [SerializeField] float letterOffset = 0.07f;

        TMP_Text text;

        void Awake() => text = GetComponent<TMP_Text>();

        void LateUpdate()
        {
            if (!text) return;
            text.ForceMeshUpdate();
            var info = text.textInfo;
            float t = Time.unscaledTime * speed;
            for (int i = 0; i < info.characterCount; i++)
            {
                var c = info.characterInfo[i];
                if (!c.isVisible) continue;
                var verts = info.meshInfo[c.materialReferenceIndex].vertices;
                float y = Mathf.Sin((t - i * letterOffset) * Mathf.PI * 2f) * amplitude;
                for (int v = 0; v < 4; v++) verts[c.vertexIndex + v].y += y;
            }
            for (int m = 0; m < info.meshInfo.Length; m++)
            {
                info.meshInfo[m].mesh.vertices = info.meshInfo[m].vertices;
                text.UpdateGeometry(info.meshInfo[m].mesh, m);
            }
        }
    }
}
