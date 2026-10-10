using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

namespace ValheimCliBridge
{
    public static class GameUi
    {
        public static void Execute(Request r)
        {
            var system = EventSystem.current;
            if (system == null) throw new InvalidOperationException("Game UI event system unavailable");
            var data = new PointerEventData(system)
            {
                position = new Vector2((float)r.pointerX.Value * (Screen.width - 1), (1 - (float)r.pointerY.Value) * (Screen.height - 1)),
                button = r.button == "right" ? PointerEventData.InputButton.Right : r.button == "middle" ? PointerEventData.InputButton.Middle : PointerEventData.InputButton.Left
            };
            var hits = new List<RaycastResult>();
            system.RaycastAll(data, hits);
            foreach (var hit in hits)
            {
                if (hit.gameObject == null || hit.gameObject.GetComponentInParent<Canvas>() == null) continue;
                data.pointerCurrentRaycast = hit;
                if (r.uiAction == "scroll")
                {
                    data.scrollDelta = new Vector2(0, r.scroll.Value);
                    if (ExecuteEvents.ExecuteHierarchy(hit.gameObject, data, ExecuteEvents.scrollHandler) == null)
                        throw new InvalidOperationException("UI does not accept scroll");
                    return;
                }
                var click = ExecuteEvents.GetEventHandler<IPointerClickHandler>(hit.gameObject);
                if (click == null) throw new InvalidOperationException("UI does not accept click");
                data.pressPosition = data.position; data.pointerPressRaycast = hit;
                data.eligibleForClick = true; data.clickCount = 1;
                data.pointerPress = ExecuteEvents.ExecuteHierarchy(hit.gameObject, data, ExecuteEvents.pointerDownHandler) ?? click;
                try { ExecuteEvents.Execute(data.pointerPress, data, ExecuteEvents.pointerUpHandler); ExecuteEvents.Execute(click, data, ExecuteEvents.pointerClickHandler); }
                finally { data.eligibleForClick = false; }
                return;
            }
            throw new InvalidOperationException("No game UI at the requested point");
        }
    }
}
