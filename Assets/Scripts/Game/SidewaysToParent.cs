using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace FlickCurling.Game
{
    /// <summary>
    /// On a vertical scroll view inside a <see cref="SwipeArea"/> (a page of the rules that scrolls, among pages that are
    /// swiped sideways): a drag that starts more sideways than up or down goes to the swipe (and the view holds still
    /// meanwhile); any other drag scrolls the view as usual.
    /// </summary>
    [RequireComponent(typeof(ScrollRect))]
    public sealed class SidewaysToParent : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        ScrollRect scroll;
        SwipeArea swipe;
        bool sideways;

        void Awake() => scroll = GetComponent<ScrollRect>();

        public void OnBeginDrag(PointerEventData e)
        {
            sideways = Mathf.Abs(e.delta.x) > Mathf.Abs(e.delta.y);
            if (!sideways) return;
            if (!swipe) swipe = transform.parent ? transform.parent.GetComponentInParent<SwipeArea>() : null;
            if (!swipe) { sideways = false; return; }
            scroll.vertical = false;   // (the scroll view takes this drag too: it holds still)
            scroll.StopMovement();
            swipe.OnBeginDrag(e);
        }

        public void OnDrag(PointerEventData e)
        {
            if (sideways) swipe.OnDrag(e);
        }

        public void OnEndDrag(PointerEventData e)
        {
            if (!sideways) return;
            sideways = false;
            scroll.vertical = true;
            swipe.OnEndDrag(e);
        }

        void OnDisable()
        {
            if (sideways && scroll) scroll.vertical = true;
            sideways = false;
        }
    }
}
