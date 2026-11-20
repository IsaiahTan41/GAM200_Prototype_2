using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace KungFuCrashout.UI
{
    /// <summary>
    /// One row of the TUNING tab: name · [−] slider [+] · value. Bound to a TuningRegistry value by Param Id.
    /// The value turns gold when it differs from the project's value (saved on this device).
    /// Built by KungFu Crashout → GDD Build → Add Tuning Tab + Scrolling Pages.
    /// Place in: Assets/_Project/Scripts/UI/TuningRow.cs
    /// </summary>
    public class TuningRow : MonoBehaviour
    {
        [SerializeField] string paramId;
        [SerializeField] TMP_Text label;
        [SerializeField] TMP_Text value;
        [SerializeField] Slider slider;
        [SerializeField] RepeatButton minus;
        [SerializeField] RepeatButton plus;
        [SerializeField] Color changedColour = new Color(1f, 0.78f, 0.2f);
        [SerializeField] Color normalColour = Color.white;
        [SerializeField] Color unusedColour = new Color(1f, 1f, 1f, 0.35f);

        public string ParamId => paramId;

        TuneDef def;
        float low;
        int steps;

        void Awake()
        {
            if (slider) slider.onValueChanged.AddListener(OnSlider);
            if (minus) minus.Pressed += () => Nudge(-1);
            if (plus) plus.Pressed += () => Nudge(1);
        }

        void OnEnable() => Refresh();

        public void Refresh()
        {
            def = TuningRegistry.Find(paramId);
            float current = def != null ? TuningRegistry.Current(def) : float.NaN;
            bool used = !float.IsNaN(current);

            if (slider) slider.interactable = used;
            SetInteractable(minus, used);
            SetInteractable(plus, used);
            if (label) label.alpha = used ? 1f : 0.45f;
            if (!used)
            {
                if (value) { value.text = "—"; value.color = unusedColour; }
                return;
            }

            // Slider steps on the value's grid; widen the range if the project's value sits outside it.
            low = def.min;
            float high = def.max;
            if (current < low) low -= Mathf.Ceil((low - current) / def.step) * def.step;
            if (current > high) high += Mathf.Ceil((current - high) / def.step) * def.step;
            steps = Mathf.Max(1, Mathf.RoundToInt((high - low) / def.step));
            if (slider)
            {
                slider.wholeNumbers = true;
                slider.minValue = 0;
                slider.maxValue = steps;
                slider.SetValueWithoutNotify(Mathf.Clamp(Mathf.RoundToInt((current - low) / def.step), 0, steps));
            }
            Show(current);
        }

        void OnSlider(float index)
        {
            if (def == null) return;
            float v = Mathf.Round((low + index * def.step) * 10000f) / 10000f;
            TuningRegistry.Set(def, v);
            Show(v);
        }

        void Nudge(int direction)
        {
            if (def == null || !slider) return;
            slider.value = Mathf.Clamp(Mathf.Round(slider.value) + direction, 0, steps); // calls OnSlider
        }

        void Show(float v)
        {
            if (!value) return;
            value.text = TuningRegistry.Format(def, v);
            value.color = TuningRegistry.IsChanged(def) ? changedColour : normalColour;
        }

        static void SetInteractable(RepeatButton b, bool on)
        {
            if (b && b.TryGetComponent(out Selectable s)) s.interactable = on;
        }
    }
}
