using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace FlickCurling.Game
{
    /// <summary>
    /// A sideways swipe over this UI rect (it needs a raycast target, e.g. a clear Image): the drag's distance from where it
    /// began while it goes on, then that distance and how fast it was moving when let go, all in this rect's own units.
    /// </summary>
    public sealed class SwipeArea : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        /// <summary>The finger is this far (x) from where the drag began.</summary>
        public event Action<float> Moved;
        /// <summary>Let go: the distance (x) and the speed (x, units a second) at the end.</summary>
        public event Action<float, float> Released;

        float startX, lastX, lastT, speed;
        bool dragging;

        float LocalX(PointerEventData e)
        {
            RectTransformUtility.ScreenPointToLocalPointInRectangle((RectTransform)transform, e.position, e.pressEventCamera, out var p);
            return p.x;
        }

        public void OnBeginDrag(PointerEventData e)
        {
            dragging = true;
            startX = lastX = LocalX(e);
            lastT = Time.unscaledTime;
            speed = 0;
        }

        public void OnDrag(PointerEventData e)
        {
            if (!dragging) return;
            float x = LocalX(e), t = Time.unscaledTime;
            if (t > lastT) speed = Mathf.Lerp(speed, (x - lastX) / (t - lastT), 0.6f);
            lastX = x; lastT = t;
            Moved?.Invoke(x - startX);
        }

        public void OnEndDrag(PointerEventData e)
        {
            if (!dragging) return;
            dragging = false;
            if (Time.unscaledTime - lastT > 0.1f) speed = 0;   // held still before letting go: no fling
            Released?.Invoke(LocalX(e) - startX, speed);
        }

        void OnDisable() => dragging = false;
    }
}
