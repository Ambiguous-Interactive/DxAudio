namespace WallstopStudios.DxAudio.Editor
{
    using System;
    using UnityEngine;
    using UnityEngine.UIElements;

    internal sealed class DspGraphMinimap : VisualElement
    {
        private readonly Func<int, Rect> _card;
        private readonly Action<Vector2> _center;
        private Rect _viewport;
        private Rect _map;
        private int _count;
        private int _input;
        private int _output;
        private Vector2 _origin;
        private float _scale;
        private Color _accent;
        private Color _node;
        private Color _outline;

        internal DspGraphMinimap(Func<int, Rect> card, Action<Vector2> center)
        {
            _card = card;
            _center = center;
            name = "DxAudioGraphMinimap";
            tooltip = "Click to center the canvas at that point in the graph.";
            AddToClassList("dxaudio-graph-minimap");
            Label caption = new("Minimap") { pickingMode = PickingMode.Ignore };
            caption.AddToClassList("dxaudio-graph-minimap-title");
            Add(caption);
            generateVisualContent += Draw;
            RegisterCallback<GeometryChangedEvent>(_ => UpdateProjection());
            RegisterCallback<PointerDownEvent>(Recenter);
            RegisterCallback<WheelEvent>(evt => evt.StopPropagation());
        }

        internal static bool TryFit(Rect graph, Rect map, out Vector2 origin, out float scale)
        {
            if (!(0f < graph.width && 0f < graph.height && 0f < map.width && 0f < map.height))
            {
                origin = default;
                scale = 0f;
                return false;
            }

            float fit = Mathf.Min(map.width / graph.width, map.height / graph.height);
            Vector2 offset = map.center - graph.center * fit;
            if (!AudioMath.IsFinite(offset.x) || !AudioMath.IsFinite(offset.y) || !(0f < fit && AudioMath.IsFinite(fit)))
            {
                origin = default;
                scale = 0f;
                return false;
            }

            origin = offset;
            scale = fit;
            return true;
        }

        internal void Refresh(int count, Rect viewport, int input, int output)
        {
            // One quad per overview card and four for the viewport outline.
            _count = Math.Min(count, ushort.MaxValue / 4 - 4);
            _viewport = viewport;
            _input = input;
            _output = output;
            style.display = count == 0 ? DisplayStyle.None : DisplayStyle.Flex;
            UpdateProjection();
        }

        internal void SetColors(Color accent, Color node, Color outline)
        {
            _accent = accent;
            _node = node;
            _outline = outline;
            MarkDirtyRepaint();
        }

        private void UpdateProjection()
        {
            _scale = 0f;
            if (_count == 0)
            {
                return;
            }

            Rect rect = contentRect;
            _map = Rect.MinMaxRect(rect.xMin + 6f, rect.yMin + 21f, rect.xMax - 6f, rect.yMax - 6f);
            Rect bounds = _card(0);
            for (int index = 1; index < _count; ++index)
            {
                Rect card = _card(index);
                bounds = Rect.MinMaxRect(
                    Mathf.Min(bounds.xMin, card.xMin),
                    Mathf.Min(bounds.yMin, card.yMin),
                    Mathf.Max(bounds.xMax, card.xMax),
                    Mathf.Max(bounds.yMax, card.yMax)
                );
            }

            _ = TryFit(bounds, _map, out _origin, out _scale);
            MarkDirtyRepaint();
        }

        private Rect Project(Rect rect)
        {
            return new Rect(_origin + rect.position * _scale, rect.size * _scale);
        }

        private void Recenter(PointerDownEvent evt)
        {
            Vector2 point = this.WorldToLocal(evt.position);
            if (evt.button == 0 && 0f < _scale && _map.Contains(point))
            {
                _center((point - _origin) / _scale);
            }

            evt.StopPropagation();
        }

        private void Draw(MeshGenerationContext context)
        {
            if (_scale <= 0f)
            {
                return;
            }

            Rect viewport = Project(_viewport);
            Rect visible = Rect.MinMaxRect(
                Mathf.Max(viewport.xMin, _map.xMin),
                Mathf.Max(viewport.yMin, _map.yMin),
                Mathf.Min(viewport.xMax, _map.xMax),
                Mathf.Min(viewport.yMax, _map.yMax)
            );
            bool intersects = 0f < visible.width && 0f < visible.height;
            int quads = _count + (intersects ? 4 : 0);
            MeshWriteData mesh = context.Allocate(quads * 4, quads * 6);
            for (int index = 0; index < _count; ++index)
            {
                DspGraphCanvas.WriteRect(mesh, Project(_card(index)), index == _input || index == _output ? _accent : _node, (ushort)(index * 4));
            }

            if (intersects)
            {
                float x = Mathf.Min(1f, visible.width);
                float y = Mathf.Min(1f, visible.height);
                int first = _count * 4;
                DspGraphCanvas.WriteRect(mesh, new Rect(visible.xMin, visible.yMin, visible.width, y), _outline, (ushort)first);
                DspGraphCanvas.WriteRect(mesh, new Rect(visible.xMin, visible.yMax - y, visible.width, y), _outline, (ushort)(first + 4));
                DspGraphCanvas.WriteRect(mesh, new Rect(visible.xMin, visible.yMin, x, visible.height), _outline, (ushort)(first + 8));
                DspGraphCanvas.WriteRect(mesh, new Rect(visible.xMax - x, visible.yMin, x, visible.height), _outline, (ushort)(first + 12));
            }
        }
    }
}
