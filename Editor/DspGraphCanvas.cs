namespace WallstopStudios.DxAudio.Editor
{
    using System;
    using System.Collections.Generic;
    using UnityEditor;
    using UnityEngine;
    using UnityEngine.UIElements;

    internal sealed class DspGraphCanvas : VisualElement
    {
        private const float NodeWidth = 170f;
        private const float NodeHeight = 68f;
        private const float MinimumZoom = 0.12f;
        private const float MaximumZoom = 1.8f;
        private const int WireSegments = 20;
        private readonly DspGraphAsset _asset;
        private readonly DspGraphEdits _edits;
        private readonly Action<int> _selectNode;
        private readonly Action<int> _selectEdge;
        private readonly Action<string> _report;
        private readonly HashSet<int> _selection = new();
        private readonly VisualElement _cards = new() { pickingMode = PickingMode.Ignore };
        private readonly Label _empty = new("Right-click or choose Add Node to start a signal path.") { pickingMode = PickingMode.Ignore };
        private readonly Label _connectionFeedback = new() { pickingMode = PickingMode.Ignore };
        private readonly Color _accent;
        private readonly string _viewKey;
        private Vector2[] _positions = Array.Empty<Vector2>();
        private (int from, int to)[] _links = Array.Empty<(int, int)>();
        private Vector2 _pan = new(38f, 48f);
        private float _zoom = 1f;
        private int _edge = -1;
        private int _pointerId = -1;
        private int _source = -1;
        private int _target = -1;
        private bool _validTarget;
        private bool _panning;
        private bool _draggingNodes;
        private bool _boxing;
        private Vector2 _down;
        private Vector2 _pointer;
        private Vector2 _initialPan;
        private VisualElement _picker;

        internal DspGraphCanvas(DspGraphAsset asset, DspGraphEdits edits, Action<int> selectNode, Action<int> selectEdge, Action<string> report)
        {
            _asset = asset;
            _edits = edits;
            _selectNode = selectNode;
            _selectEdge = selectEdge;
            _report = report;
            bool dark = EditorGUIUtility.isProSkin;
            _accent = dark ? new Color(0.36f, 0.8f, 0.74f) : new Color(0.03f, 0.36f, 0.32f);
            style.backgroundColor = dark ? new Color(0.12f, 0.14f, 0.16f) : new Color(0.82f, 0.85f, 0.87f);
            style.flexGrow = 1;
            style.minWidth = 200;
            style.minHeight = 180;
            style.overflow = Overflow.Hidden;
            focusable = true;
            tooltip = "Middle-drag to pan. Scroll to zoom. Right-click to add a node or insert on a wire.";
            string guid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(asset));
            _viewKey = string.IsNullOrEmpty(guid) ? null : "DxAudio.GraphView." + guid;
            Vector3 view = _viewKey == null ? Vector3.zero : SessionState.GetVector3(_viewKey, Vector3.zero);
            if (0f < view.z)
            {
                _pan = new Vector2(view.x, view.y);
                _zoom = Mathf.Clamp(view.z, MinimumZoom, MaximumZoom);
            }
            else
            {
                RegisterCallback<GeometryChangedEvent>(FrameOnOpen);
            }

            Add(_cards);
            _empty.style.marginLeft = 16;
            _empty.style.marginTop = 18;
            Add(_empty);
            _connectionFeedback.style.position = Position.Absolute;
            _connectionFeedback.style.left = 12f;
            _connectionFeedback.style.bottom = 12f;
            _connectionFeedback.style.maxWidth = Length.Percent(90f);
            _connectionFeedback.style.paddingLeft = 8f;
            _connectionFeedback.style.paddingRight = 8f;
            _connectionFeedback.style.paddingTop = 4f;
            _connectionFeedback.style.paddingBottom = 4f;
            _connectionFeedback.style.whiteSpace = WhiteSpace.Normal;
            _connectionFeedback.style.backgroundColor = dark ? new Color(0.22f, 0.25f, 0.28f) : new Color(0.95f, 0.96f, 0.97f);
            _connectionFeedback.style.display = DisplayStyle.None;
            Add(_connectionFeedback);
            generateVisualContent += Draw;
            RegisterCallback<PointerDownEvent>(PointerDown);
            RegisterCallback<PointerMoveEvent>(PointerMove);
            RegisterCallback<PointerUpEvent>(PointerUp);
            RegisterCallback<PointerCaptureOutEvent>(_ => CancelGesture());
            RegisterCallback<WheelEvent>(Zoom);
            RegisterCallback<KeyDownEvent>(KeyDown);
            Refresh();
        }

        private static Rect Box(Vector2 a, Vector2 b)
        {
            return Rect.MinMaxRect(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y), Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y));
        }

        private static Vector2 Curve(Vector2 start, Vector2 end, float t)
        {
            float reach = Mathf.Max(45f, Mathf.Abs(end.x - start.x) * 0.46f);
            float a = 1f - t;
            return a * a * a * start + 3f * a * a * t * (start + new Vector2(reach, 0f)) + 3f * a * t * t * (end - new Vector2(reach, 0f)) + t * t * t * end;
        }

        private static void DrawWire(MeshGenerationContext context, Vector2 start, Vector2 end, Color color, float width)
        {
            MeshWriteData mesh = context.Allocate(WireSegments * 4, WireSegments * 6);
            Vector2 previous = start;
            for (int segment = 1; segment <= WireSegments; segment++)
            {
                Vector2 next = Curve(start, end, (float)segment / WireSegments);
                Vector2 axis = next - previous;
                Vector2 offset = new Vector2(-axis.y, axis.x).normalized * (width * 0.5f);
                Quad(mesh, previous - offset, next - offset, next + offset, previous + offset, color, (ushort)((segment - 1) * 4));
                previous = next;
            }
        }

        private static void DrawRect(MeshGenerationContext context, Rect rect, Color color)
        {
            MeshWriteData mesh = context.Allocate(4, 6);
            Quad(mesh, rect.min, new Vector2(rect.xMax, rect.yMin), rect.max, new Vector2(rect.xMin, rect.yMax), color, 0);
        }

        private static void Quad(MeshWriteData mesh, Vector2 a, Vector2 b, Vector2 c, Vector2 d, Color color, ushort index)
        {
            WriteVertex(mesh, a, color);
            WriteVertex(mesh, b, color);
            WriteVertex(mesh, c, color);
            WriteVertex(mesh, d, color);
            mesh.SetNextIndex(index);
            mesh.SetNextIndex((ushort)(index + 1));
            mesh.SetNextIndex((ushort)(index + 2));
            mesh.SetNextIndex(index);
            mesh.SetNextIndex((ushort)(index + 2));
            mesh.SetNextIndex((ushort)(index + 3));
        }

        private static void WriteVertex(MeshWriteData mesh, Vector2 point, Color color)
        {
            mesh.SetNextVertex(new Vertex { position = new Vector3(point.x, point.y, Vertex.nearZ), tint = color });
        }

        internal void Refresh()
        {
            CancelGesture();
            ClosePicker();
            _selection.RemoveWhere(index => _asset.NodeCount <= index);
            if (_asset.EdgeCount <= _edge)
            {
                _edge = -1;
            }

            _links = new (int, int)[_asset.EdgeCount];
            int[] incoming = new int[_asset.NodeCount];
            for (int index = 0; index < _links.Length; index++)
            {
                DspGraphEdge edge = _asset.Edges[index];
                int from = _edits.Resolve(edge?.From);
                int to = _edits.Resolve(edge?.To);
                _links[index] = (from, to);
                if (0 <= from && 0 <= to)
                {
                    incoming[to]++;
                }
            }

            _positions = AutomaticPositions();
            _cards.Clear();
            _empty.style.display = _asset.NodeCount == 0 ? DisplayStyle.Flex : DisplayStyle.None;
            int input = _edits.Resolve(_asset.InputNode);
            int output = _edits.Resolve(_asset.OutputNode);
            for (int index = 0; index < _asset.NodeCount; index++)
            {
                DspGraphNode node = _asset.Nodes[index];
                if (
                    node != null
                    && node.HasEditorPosition
                    && !float.IsNaN(node.EditorPosition.x)
                    && !float.IsInfinity(node.EditorPosition.x)
                    && !float.IsNaN(node.EditorPosition.y)
                    && !float.IsInfinity(node.EditorPosition.y)
                )
                {
                    _positions[index] = node.EditorPosition;
                }

                string title = string.IsNullOrWhiteSpace(node?.Name) ? $"Unnamed node {index + 1}" : node.Name;
                DspChainAsset chain = node?.Chain;
                string subtitle = chain != null ? chain.name : "Junction";
                string ports = (index == input ? "Input     " : "") + (index == output ? "Output     " : "");
                if (1 < incoming[index])
                {
                    ports += $"Sum {incoming[index]} inputs";
                }

                _cards.Add(MakeCard(title, subtitle, ports));
            }

            UpdateCards();
        }

        internal void SelectNode(int index)
        {
            _selection.Clear();
            _edge = -1;
            if (0 <= index && index < _positions.Length)
            {
                _selection.Add(index);
            }

            UpdateCards();
            _selectNode(index);
        }

        internal void SelectEdge(int index)
        {
            _selection.Clear();
            _edge = index;
            UpdateCards();
            _selectEdge(index);
        }

        internal void FrameSelection()
        {
            Rect bounds = default;
            bool hasBounds = false;
            for (int index = 0; index < _positions.Length; index++)
            {
                bool endpoint = 0 <= _edge && _edge < _links.Length && (_links[_edge].from == index || _links[_edge].to == index);
                if (0 < _selection.Count && !_selection.Contains(index) || _selection.Count == 0 && 0 <= _edge && !endpoint)
                {
                    continue;
                }

                Rect card = new(_positions[index], new Vector2(NodeWidth, NodeHeight));
                bounds = hasBounds
                    ? Rect.MinMaxRect(Mathf.Min(bounds.xMin, card.xMin), Mathf.Min(bounds.yMin, card.yMin), Mathf.Max(bounds.xMax, card.xMax), Mathf.Max(bounds.yMax, card.yMax))
                    : card;
                hasBounds = true;
            }

            if (!hasBounds || contentRect.width <= 0f || contentRect.height <= 0f)
            {
                return;
            }

            _zoom = Mathf.Clamp(Mathf.Min((contentRect.width - 64f) / bounds.width, (contentRect.height - 64f) / bounds.height), MinimumZoom, 1.15f);
            _pan = contentRect.center - bounds.center * _zoom;
            SaveView();
            UpdateCards();
        }

        internal void Arrange()
        {
            Vector2[] positions = AutomaticPositions();
            Dictionary<int, Vector2> changes = new();
            for (int index = 0; index < positions.Length; index++)
            {
                if (_asset.Nodes[index] != null)
                {
                    changes.Add(index, positions[index]);
                }
            }

            if (0 < changes.Count && !_edits.TryMove(changes, out string error))
            {
                _report(error);
            }
            else
            {
                SelectNode(-1);
                FrameSelection();
            }
        }

        internal void AddNode()
        {
            ShowPicker(contentRect.center, -1, -1);
        }

        internal void FindNode()
        {
            OpenPicker(
                contentRect.center,
                "Find node",
                (query, result) =>
                {
                    for (int index = 0; index < _asset.NodeCount; index++)
                    {
                        DspGraphNode node = _asset.Nodes[index];
                        string title = string.IsNullOrWhiteSpace(node?.Name) ? $"Unnamed node {index + 1}" : node.Name;
                        DspChainAsset chain = node?.Chain;
                        string chainName = chain == null ? "Junction" : chain.name;
                        if (title.IndexOf(query, StringComparison.OrdinalIgnoreCase) < 0 && chainName.IndexOf(query, StringComparison.OrdinalIgnoreCase) < 0)
                        {
                            continue;
                        }

                        int row = index;
                        result(
                            title,
                            chainName,
                            () =>
                            {
                                SelectNode(row);
                                FrameSelection();
                            }
                        );
                    }
                }
            );
        }

        private void FrameOnOpen(GeometryChangedEvent evt)
        {
            if (0f < evt.newRect.width && 0f < evt.newRect.height)
            {
                UnregisterCallback<GeometryChangedEvent>(FrameOnOpen);
                FrameSelection();
            }
        }

        private Vector2[] AutomaticPositions()
        {
            int count = _asset.NodeCount;
            int[] incoming = new int[count];
            int[] depth = new int[count];
            Queue<int> ready = new();
            foreach ((int from, int to) in _links)
            {
                if (0 <= from && 0 <= to)
                {
                    incoming[to]++;
                }
            }

            for (int index = 0; index < count; index++)
            {
                if (incoming[index] == 0)
                {
                    ready.Enqueue(index);
                }
            }

            while (ready.TryDequeue(out int node))
            {
                foreach ((int from, int to) in _links)
                {
                    if (from != node || to < 0)
                    {
                        continue;
                    }

                    depth[to] = Mathf.Max(depth[to], depth[node] + 1);
                    if (--incoming[to] == 0)
                    {
                        ready.Enqueue(to);
                    }
                }
            }

            int[] rows = new int[count];
            Vector2[] positions = new Vector2[count];
            for (int index = 0; index < count; index++)
            {
                int column = depth[index];
                positions[index] = new Vector2(column * 244f, rows[column]++ * 104f);
            }

            return positions;
        }

        private VisualElement MakeCard(string title, string subtitle, string ports)
        {
            bool dark = EditorGUIUtility.isProSkin;
            VisualElement card = new() { tooltip = $"{title}\n{subtitle}\n{ports}" };
            card.style.position = Position.Absolute;
            card.style.paddingLeft = 12;
            card.style.paddingRight = 12;
            card.style.justifyContent = Justify.Center;
            card.style.backgroundColor = dark ? new Color(0.22f, 0.25f, 0.28f) : new Color(0.96f, 0.97f, 0.98f);
            card.style.borderTopLeftRadius = 7;
            card.style.borderTopRightRadius = 7;
            card.style.borderBottomLeftRadius = 7;
            card.style.borderBottomRightRadius = 7;
            card.style.borderLeftWidth = 1;
            card.style.borderRightWidth = 1;
            card.style.borderTopWidth = 2;
            card.style.borderBottomWidth = 1;
            card.style.overflow = Overflow.Hidden;
            foreach (string text in new[] { title, subtitle, ports })
            {
                Label label = new(text) { pickingMode = PickingMode.Ignore };
                label.style.whiteSpace = WhiteSpace.NoWrap;
                label.style.textOverflow = TextOverflow.Ellipsis;
                label.style.overflow = Overflow.Hidden;
                if (card.childCount == 0)
                {
                    label.style.unityFontStyleAndWeight = FontStyle.Bold;
                }

                card.Add(label);
            }

            return card;
        }

        private void UpdateCards()
        {
            Color neutralBorder = EditorGUIUtility.isProSkin ? new Color(0.36f, 0.43f, 0.47f) : new Color(0.48f, 0.54f, 0.58f);
            for (int index = 0; index < _positions.Length && index < _cards.childCount; index++)
            {
                Rect rect = CardRect(index);
                VisualElement card = _cards[index];
                card.style.left = rect.x;
                card.style.top = rect.y;
                card.style.width = rect.width;
                card.style.height = rect.height;
                card.style.fontSize = Mathf.Clamp(13f * _zoom, 8f, 19f);
                Color edge = _selection.Contains(index) ? _accent : neutralBorder;
                card.style.borderTopColor = edge;
                card.style.borderBottomColor = edge;
                card.style.borderLeftColor = edge;
                card.style.borderRightColor = edge;
                card[0].style.display = _zoom < 0.27f ? DisplayStyle.None : DisplayStyle.Flex;
                card[1].style.display = _zoom < 0.58f ? DisplayStyle.None : DisplayStyle.Flex;
                card[2].style.display = _zoom < 0.75f ? DisplayStyle.None : DisplayStyle.Flex;
            }

            MarkDirtyRepaint();
        }

        private Rect CardRect(int index)
        {
            Vector2 delta = _draggingNodes && _selection.Contains(index) ? _pointer - _down : Vector2.zero;
            return new Rect(_positions[index] * _zoom + _pan + delta, new Vector2(NodeWidth, NodeHeight) * _zoom);
        }

        private Vector2 Port(int index, bool output)
        {
            Rect rect = CardRect(index);
            return new Vector2(output ? rect.xMax + 6f : rect.xMin - 6f, rect.center.y);
        }

        private void PointerDown(PointerDownEvent evt)
        {
            if (_picker != null && evt.target is VisualElement target && (target == _picker || _picker.Contains(target)))
            {
                return;
            }

            ClosePicker();
            if (_pointerId != -1)
            {
                return;
            }

            Vector2 point = this.WorldToLocal(evt.position);
            int node = HitNode(point);
            int edge = node < 0 ? HitEdge(point) : -1;
            if (evt.button == 1)
            {
                if (node < 0)
                {
                    ShowPicker(point, -1, edge);
                }

                evt.StopPropagation();
                return;
            }

            if (evt.button != 0 && evt.button != 2)
            {
                return;
            }

            Focus();
            _pointerId = evt.pointerId;
            _down = point;
            _pointer = point;
            _initialPan = _pan;
            _panning = evt.button == 2;
            if (!_panning)
            {
                _source = HitPort(point, output: true);
                if (_source < 0)
                {
                    if (0 <= node)
                    {
                        if (!evt.shiftKey && !_selection.Contains(node))
                        {
                            _selection.Clear();
                        }

                        _selection.Add(node);
                        _edge = -1;
                        _draggingNodes = true;
                        _selectNode(node);
                    }
                    else
                    {
                        _selection.Clear();
                        _edge = edge;
                        _boxing = edge < 0;
                        if (_boxing)
                        {
                            _selectNode(-1);
                        }
                        else
                        {
                            SelectEdge(edge);
                        }
                    }
                }
            }

            this.CapturePointer(evt.pointerId);
            UpdateCards();
            evt.StopPropagation();
        }

        private void PointerMove(PointerMoveEvent evt)
        {
            if (evt.pointerId != _pointerId)
            {
                return;
            }

            _pointer = this.WorldToLocal(evt.position);
            if (_panning)
            {
                _pan = _initialPan + _pointer - _down;
            }
            else if (0 <= _source)
            {
                int target = HitPort(_pointer, output: false);
                if (target != _target)
                {
                    _target = target;
                    string reason = string.Empty;
                    _validTarget = 0 <= target && _edits.CanConnect(_source, target, out reason);
                    _connectionFeedback.text = _validTarget ? "Release to connect" : reason;
                    _connectionFeedback.style.display = 0 <= target ? DisplayStyle.Flex : DisplayStyle.None;
                }
            }

            UpdateCards();
            evt.StopPropagation();
        }

        private void PointerUp(PointerUpEvent evt)
        {
            if (evt.pointerId != _pointerId)
            {
                return;
            }

            Vector2 point = this.WorldToLocal(evt.position);
            Vector2 delta = point - _down;
            int source = _source;
            bool showPicker = false;
            if (_panning)
            {
                SaveView();
            }
            else if (0 <= source)
            {
                int to = HitPort(point, output: false);
                if (0 <= to)
                {
                    if (!_edits.TryConnect(source, to, out string error))
                    {
                        _report(error);
                    }
                }
                else if (HitNode(point) < 0 && 25f < delta.sqrMagnitude && contentRect.Contains(point))
                {
                    showPicker = true;
                }
            }
            else if (_draggingNodes && 9f < delta.sqrMagnitude)
            {
                Dictionary<int, Vector2> changes = new();
                foreach (int index in _selection)
                {
                    if (_asset.Nodes[index] != null)
                    {
                        changes.Add(index, _positions[index] + delta / _zoom);
                    }
                }

                if (0 < changes.Count && !_edits.TryMove(changes, out string error))
                {
                    _report(error);
                }
            }
            else if (_boxing && 9f < delta.sqrMagnitude)
            {
                Rect box = Box(_down, point);
                int first = -1;
                for (int index = 0; index < _positions.Length; index++)
                {
                    if (box.Overlaps(CardRect(index)))
                    {
                        _selection.Add(index);
                        if (first < 0)
                        {
                            first = index;
                        }
                    }
                }

                _selectNode(first);
            }

            CancelGesture();
            if (showPicker)
            {
                ShowPicker(point, source, -1);
            }

            evt.StopPropagation();
        }

        private void CancelGesture()
        {
            int previous = _pointerId;
            _pointerId = -1;
            _panning = false;
            _draggingNodes = false;
            _boxing = false;
            _source = -1;
            _target = -1;
            _validTarget = false;
            _connectionFeedback.style.display = DisplayStyle.None;
            if (0 <= previous && this.HasPointerCapture(previous))
            {
                this.ReleasePointer(previous);
            }

            UpdateCards();
        }

        private void Zoom(WheelEvent evt)
        {
            if (_picker != null || _pointerId != -1)
            {
                return;
            }

            Vector2 point = this.WorldToLocal(evt.mousePosition);
            float zoom = Mathf.Clamp(_zoom * Mathf.Pow(1.1f, -evt.delta.y / 3f), MinimumZoom, MaximumZoom);
            _pan = point - (point - _pan) * (zoom / _zoom);
            _zoom = zoom;
            SaveView();
            UpdateCards();
            evt.StopPropagation();
        }

        private void SaveView()
        {
            if (_viewKey != null)
            {
                SessionState.SetVector3(_viewKey, new Vector3(_pan.x, _pan.y, _zoom));
            }
        }

        private void KeyDown(KeyDownEvent evt)
        {
            if (evt.keyCode == KeyCode.Escape)
            {
                ClosePicker();
                CancelGesture();
                Focus();
            }
            else if (_picker == null && evt.keyCode == KeyCode.Home)
            {
                FrameSelection();
            }
            else if (_picker == null && evt.keyCode == KeyCode.F && evt.actionKey)
            {
                FindNode();
            }
            else if (_picker == null && evt.keyCode == KeyCode.A && !evt.actionKey)
            {
                AddNode();
            }
            else if (_picker == null && (evt.keyCode == KeyCode.Delete || evt.keyCode == KeyCode.Backspace))
            {
                if (!_edits.TryDelete(_selection, 0 <= _edge ? new[] { _edge } : Array.Empty<int>(), out string error))
                {
                    _report(error);
                }
                else
                {
                    _selection.Clear();
                    _edge = -1;
                    _selectNode(-1);
                    UpdateCards();
                }
            }
            else
            {
                return;
            }

            evt.StopPropagation();
        }

        private void ShowPicker(Vector2 point, int source, int edge)
        {
            Vector2 position = (point - _pan) / _zoom;
            string[] guids = AssetDatabase.FindAssets("t:DspChainAsset");
            List<string> paths = new(guids.Length);
            foreach (string guid in guids)
            {
                paths.Add(AssetDatabase.GUIDToAssetPath(guid));
            }

            paths.Sort(StringComparer.OrdinalIgnoreCase);
            OpenPicker(
                point,
                0 <= edge ? "Insert node on wire" : "Add node",
                (query, result) =>
                {
                    void AddResult(string title, DspChainAsset chain, string path)
                    {
                        result(title, path, () => CreateNode(title, chain, position, source, edge));
                    }

                    if (0 <= "Junction".IndexOf(query, StringComparison.OrdinalIgnoreCase))
                    {
                        AddResult("Junction", null, string.Empty);
                    }

                    int visible = 0;
                    foreach (string path in paths)
                    {
                        if (path.IndexOf(query, StringComparison.OrdinalIgnoreCase) < 0)
                        {
                            continue;
                        }

                        DspChainAsset chain = AssetDatabase.LoadAssetAtPath<DspChainAsset>(path);
                        if (chain != null)
                        {
                            AddResult(chain.name, chain, path);
                            if (60 <= ++visible)
                            {
                                break;
                            }
                        }
                    }
                }
            );
        }

        private void CreateNode(string title, DspChainAsset chain, Vector2 position, int source, int edge)
        {
            string name = NextName(title.Trim());
            bool changed;
            string error;
            if (0 <= edge)
            {
                changed = _edits.TryInsert(edge, name, chain, position, out error);
            }
            else if (0 <= source)
            {
                changed = _edits.TryAddConnected(source, name, chain, position, out error);
            }
            else
            {
                changed = _edits.TryAdd(name, chain, position, out error);
            }

            if (changed)
            {
                int selected = _edits.Resolve(name);
                SelectNode(selected);
            }
            else
            {
                _report(error);
            }
        }

        private void OpenPicker(Vector2 point, string title, Action<string, Action<string, string, Action>> populate)
        {
            ClosePicker();
            _picker = new VisualElement();
            _picker.style.position = Position.Absolute;
            _picker.style.left = Mathf.Clamp(point.x, 0f, Mathf.Max(0f, contentRect.width - 245f));
            _picker.style.top = Mathf.Clamp(point.y, 0f, Mathf.Max(0f, contentRect.height - 215f));
            _picker.style.width = Mathf.Min(240f, contentRect.width);
            _picker.style.height = Mathf.Min(210f, contentRect.height);
            _picker.style.paddingTop = 6;
            _picker.style.paddingLeft = 6;
            _picker.style.paddingRight = 6;
            _picker.style.backgroundColor = EditorGUIUtility.isProSkin ? new Color(0.22f, 0.25f, 0.28f) : new Color(0.95f, 0.96f, 0.97f);
            TextField search = new() { tooltip = "Type to filter. Use Up and Down to select, Enter to confirm, and Escape to close." };
            ScrollView results = new();
            results.style.flexGrow = 1f;
            results.style.minHeight = 0f;
            _picker.Add(new Label(title));
            _picker.Add(search);
            _picker.Add(results);
            Add(_picker);
            List<Action> choices = new();
            int selected = -1;
            Color selectedBackground = EditorGUIUtility.isProSkin ? new Color(0.23f, 0.42f, 0.43f) : new Color(0.68f, 0.83f, 0.81f);
            void SelectResult(int index, bool scroll)
            {
                if (0 <= selected)
                {
                    results.contentContainer[selected].style.backgroundColor = StyleKeyword.Null;
                }

                selected = index;
                VisualElement row = results.contentContainer[selected];
                row.style.backgroundColor = selectedBackground;
                if (scroll)
                {
                    results.ScrollTo(row);
                }
            }

            void Rebuild(string query)
            {
                results.Clear();
                choices.Clear();
                selected = -1;
                populate(
                    query.Trim(),
                    (name, details, choose) =>
                    {
                        void Finish()
                        {
                            ClosePicker();
                            choose();
                            Focus();
                        }

                        choices.Add(Finish);
                        results.Add(new Button(Finish) { text = name, tooltip = details });
                    }
                );

                if (choices.Count == 0)
                {
                    results.Add(new Label("No matching nodes."));
                }
                else
                {
                    results.scrollOffset = Vector2.zero;
                    SelectResult(0, scroll: false);
                }
            }

            search.RegisterValueChangedCallback(evt => Rebuild(evt.newValue));
            _picker.RegisterCallback<KeyDownEvent>(
                evt =>
                {
                    if (evt.keyCode == KeyCode.Escape)
                    {
                        ClosePicker();
                        Focus();
                        evt.StopImmediatePropagation();
                    }
                },
                TrickleDown.TrickleDown
            );
            search.RegisterCallback<KeyDownEvent>(
                evt =>
                {
                    if (evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter)
                    {
                        if (0 <= selected)
                        {
                            choices[selected]();
                        }

                        evt.StopImmediatePropagation();
                    }
                    else if (evt.keyCode == KeyCode.UpArrow || evt.keyCode == KeyCode.DownArrow)
                    {
                        if (0 <= selected)
                        {
                            int direction = evt.keyCode == KeyCode.UpArrow ? -1 : 1;
                            SelectResult(Mathf.Clamp(selected + direction, 0, choices.Count - 1), scroll: true);
                        }

                        evt.StopImmediatePropagation();
                    }
                },
                TrickleDown.TrickleDown
            );
            Rebuild(string.Empty);
            search.Focus();
        }

        private string NextName(string basename)
        {
            string name = basename;
            int suffix = 2;
            while (_asset.IndexOfNode(name) != -1)
            {
                name = $"{basename} {suffix++}";
            }

            return name;
        }

        private void ClosePicker()
        {
            if (_picker != null)
            {
                _picker.RemoveFromHierarchy();
                _picker = null;
            }
        }

        private int HitNode(Vector2 point)
        {
            for (int index = _positions.Length - 1; 0 <= index; index--)
            {
                if (CardRect(index).Contains(point))
                {
                    return index;
                }
            }

            return -1;
        }

        private int HitPort(Vector2 point, bool output)
        {
            for (int index = _positions.Length - 1; 0 <= index; index--)
            {
                if ((Port(index, output) - point).sqrMagnitude <= 121f)
                {
                    return index;
                }
            }

            return -1;
        }

        private int HitEdge(Vector2 point)
        {
            int result = -1;
            float best = 64f;
            for (int index = 0; index < _links.Length; index++)
            {
                (int from, int to) = _links[index];
                if (from < 0 || to < 0)
                {
                    continue;
                }

                Vector2 start = Port(from, output: true);
                Vector2 end = Port(to, output: false);
                Vector2 previous = start;
                for (int segment = 1; segment <= WireSegments; segment++)
                {
                    Vector2 next = Curve(start, end, (float)segment / WireSegments);
                    Vector2 direction = next - previous;
                    float factor = Mathf.Clamp01(Vector2.Dot(point - previous, direction) / Mathf.Max(0.001f, direction.sqrMagnitude));
                    float distance = (point - previous - factor * direction).sqrMagnitude;
                    if (distance <= best)
                    {
                        best = distance;
                        result = index;
                    }

                    previous = next;
                }
            }

            return result;
        }

        private void Draw(MeshGenerationContext context)
        {
            bool dark = EditorGUIUtility.isProSkin;
            Color wire = dark ? new Color(0.46f, 0.57f, 0.62f) : new Color(0.35f, 0.44f, 0.50f);
            Color rejected = dark ? new Color(0.85f, 0.32f, 0.30f) : new Color(0.70f, 0.18f, 0.17f);
            for (int index = 0; index < _links.Length; index++)
            {
                (int from, int to) = _links[index];
                if (0 <= from && 0 <= to)
                {
                    DrawWire(context, Port(from, output: true), Port(to, output: false), index == _edge ? _accent : wire, index == _edge ? 4f : 2f);
                }
            }

            if (0 <= _source)
            {
                Vector2 end = 0 <= _target ? Port(_target, output: false) : _pointer;
                Color candidate = 0 <= _target && !_validTarget ? rejected : _accent;
                DrawWire(context, Port(_source, output: true), end, candidate, 3f);
            }

            for (int index = 0; index < _positions.Length; index++)
            {
                float inputRadius = index == _target ? 6f : 4f;
                Color inputColor = index == _target ? (_validTarget ? _accent : rejected) : wire;
                DrawRect(context, new Rect(Port(index, output: false) - Vector2.one * inputRadius, Vector2.one * (2f * inputRadius)), inputColor);
                DrawRect(context, new Rect(Port(index, output: true) - Vector2.one * 4f, Vector2.one * 8f), _accent);
            }

            if (_boxing)
            {
                Color shade = _accent;
                shade.a = 0.24f;
                DrawRect(context, Box(_down, _pointer), shade);
            }
        }
    }
}
