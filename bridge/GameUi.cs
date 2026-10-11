using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ValheimCliBridge
{
    public static class GameUi
    {
        private sealed class Target
        {
            public GameObject Object;
            public UiControl Description;
        }
        private static readonly Dictionary<string, Target> targets = new Dictionary<string, Target>();
        private static float listedAt;
        public static void Invalidate() => targets.Clear();
        public static void List(Response response, int offset)
        {
            Invalidate();
            var objects = UnityEngine.Object.FindObjectsOfType<MonoBehaviour>()
                .Where(c => c != null && c.isActiveAndEnabled &&
                    Supported(c))
                .Select(c => c.gameObject).Distinct()
                .Where(o => o.GetComponentInParent<Canvas>() != null)
                .OrderBy(o => o.GetInstanceID()).ToArray();
            response.uiTotal = objects.Length;
            response.uiNextOffset = offset + 64 < objects.Length ? (int?)(offset + 64) : null;
            response.ui = objects.Skip(offset).Take(64).Select(o =>
            {
                var description = Describe(o);
                description.id = Guid.NewGuid().ToString();
                targets.Add(description.id, new Target { Object = o, Description = description });
                return description;
            }).ToArray();
            listedAt = Time.realtimeSinceStartup;
        }
        private static bool Supported(MonoBehaviour c) => c is Button || c is Toggle || c is IScrollHandler || !(c is Selectable) && (c is IPointerClickHandler || c is ISubmitHandler);
        private static UiControl Describe(GameObject o)
        {
            var components = o.GetComponents<MonoBehaviour>().Where(c => c != null && c.isActiveAndEnabled).ToArray();
            var selectable = o.GetComponent<Selectable>();
            var actions = new List<string>();
            if (components.Any(c => Supported(c) && (c is IPointerClickHandler || c is ISubmitHandler))) actions.Add("click");
            if (components.Any(c => c is IScrollHandler)) actions.Add("scroll");
            var path = o.name;
            for (var parent = o.transform.parent; parent != null; parent = parent.parent) path = parent.name + "/" + path;
            // shortcut: IMGUI/UI Toolkit controls need a separate adapter if a profile uses them.
            var label = string.Join(" ", o.GetComponentsInChildren<Component>()
                .Where(c => c is Text || c.GetType().FullName == "TMPro.TextMeshProUGUI")
                .Select(c => c is Text text ? text.text : c.GetType().GetProperty("text").GetValue(c, null) as string)
                .Where(s => !string.IsNullOrWhiteSpace(s)).Take(8));
            var visible = AvailablePoint(o, out _);
            var enabled = selectable == null || selectable.IsInteractable();
            return new UiControl
            {
                path = Limit(path, 240), label = Limit(label, 160),
                type = Limit(string.Join(",", components.Where(c => c is IPointerClickHandler || c is ISubmitHandler || c is IScrollHandler).Select(c => c.GetType().FullName)), 160),
                actions = actions.ToArray(), interactable = visible && enabled,
                reason = !enabled ? "disabled" : !visible ? "hidden_or_obstructed" : null
            };
        }
        private static string Limit(string value, int length) => value.Length > length ? value.Substring(0, length) : value;
        private static bool AvailablePoint(GameObject target, out Vector2 point)
        {
            point = default;
            var system = EventSystem.current;
            var rect = target.transform as RectTransform;
            var canvas = target.GetComponentInParent<Canvas>();
            if (system == null || rect == null || canvas == null || !canvas.isActiveAndEnabled || !target.activeInHierarchy || target.GetComponentsInParent<CanvasGroup>().Any(g => g.alpha <= 0.01f)) return false;
            var corners = new Vector3[4];
            rect.GetWorldCorners(corners);
            var camera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
            var lo = RectTransformUtility.WorldToScreenPoint(camera, corners[0]);
            var hi = RectTransformUtility.WorldToScreenPoint(camera, corners[2]);
            foreach (var fraction in new[] { new Vector2(.5f, .5f), new Vector2(.1f, .1f), new Vector2(.9f, .1f), new Vector2(.1f, .9f), new Vector2(.9f, .9f) })
            {
                var p = new Vector2(lo.x + (hi.x - lo.x) * fraction.x, lo.y + (hi.y - lo.y) * fraction.y);
                if (p.x < 0 || p.y < 0 || p.x >= Screen.width || p.y >= Screen.height) continue;
                var data = new PointerEventData(system) { position = p };
                var hits = new List<RaycastResult>();
                system.RaycastAll(data, hits);
                var first = hits.FirstOrDefault(h => h.gameObject != null && h.gameObject.GetComponentInParent<Canvas>() != null).gameObject;
                if (first == null) continue;
                if (first == target || first.transform.IsChildOf(target.transform)) { point = p; return true; }
            }
            return false;
        }
        public static void Execute(Request r)
        {
            if (!targets.TryGetValue(r.target, out var target) || target.Object == null || Time.realtimeSinceStartup - listedAt > 30)
                throw new ActionRefusedException("Unknown or expired UI target; run ui-list again");
            var current = Describe(target.Object);
            if (current.path != target.Description.path || current.label != target.Description.label || current.type != target.Description.type || !current.interactable || !current.actions.Contains(r.uiAction) || !AvailablePoint(target.Object, out var point))
                throw new ActionRefusedException("UI target changed, disabled or obstructed; run ui-list again");
            var system = EventSystem.current;
            var data = new PointerEventData(system)
            {
                position = point, pressPosition = point, eligibleForClick = true, clickCount = 1,
                button = r.button == "right" ? PointerEventData.InputButton.Right : r.button == "middle" ? PointerEventData.InputButton.Middle : PointerEventData.InputButton.Left
            };
            var o = target.Object;
            Invalidate();
            if (r.uiAction == "scroll")
            {
                data.scrollDelta = new Vector2(0, r.scroll.Value);
                ExecuteEvents.Execute(o, data, ExecuteEvents.scrollHandler);
            }
            else if (data.button == PointerEventData.InputButton.Left && (o.GetComponent<Button>() != null || ExecuteEvents.GetEventHandler<IPointerClickHandler>(o) != o && ExecuteEvents.GetEventHandler<ISubmitHandler>(o) == o))
                ExecuteEvents.Execute(o, new BaseEventData(system), ExecuteEvents.submitHandler);
            else
            {
                if (o.GetComponent<Button>() != null && data.button != PointerEventData.InputButton.Left)
                    throw new ActionRefusedException("Standard buttons only accept left activation");
                data.pointerPress = o;
                ExecuteEvents.Execute(o, data, ExecuteEvents.pointerDownHandler);
                try { ExecuteEvents.Execute(o, data, ExecuteEvents.pointerUpHandler); ExecuteEvents.Execute(o, data, ExecuteEvents.pointerClickHandler); }
                finally { data.eligibleForClick = false; }
            }
        }
    }
}
