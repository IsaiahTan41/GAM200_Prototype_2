using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace KungFuCrashout.UI
{
    /// <summary>
    /// A stepper button (− / +) that fires once on a tap and repeats while held. Uses unscaled time (the fight is
    /// paused while the menu is open). A finger that starts on it and scrolls the list does not count as a tap.
    /// Place in: Assets/_Project/Scripts/UI/RepeatButton.cs
    /// </summary>
    public class RepeatButton : MonoBehaviour, IPointerDownHandler, IPointerUpHandler
    {
        [SerializeField, Min(0.05f)] float repeatDelay = 0.4f;
        [SerializeField, Min(0.02f)] float repeatInterval = 0.07f;

        public event Action Pressed;

        Selectable selectable;
        PointerEventData held;
        float nextAt;
        bool repeating;

        void Awake() => selectable = GetComponent<Selectable>();

        public void OnPointerDown(PointerEventData e)
        {
            if (selectable && !selectable.IsInteractable()) return;
            held = e;
            repeating = false;
            nextAt = Time.unscaledTime + repeatDelay;
        }

        public void OnPointerUp(PointerEventData e)
        {
            if (held == null) return;
            bool tap = !repeating && !e.dragging;
            held = null;
            if (tap) Pressed?.Invoke();
        }

        void Update()
        {
            if (held == null) return;
            if (held.dragging) { held = null; return; } // it became a scroll
            if (Time.unscaledTime < nextAt) return;
            repeating = true;
            nextAt = Time.unscaledTime + repeatInterval;
            Pressed?.Invoke();
        }

        void OnDisable() => held = null;
    }
}
