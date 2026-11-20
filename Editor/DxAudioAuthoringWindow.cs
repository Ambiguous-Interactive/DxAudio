namespace WallstopStudios.DxAudio.Editor
{
    using System;
    using System.Collections.Generic;
    using UnityEditor;
    using UnityEditor.UIElements;
    using UnityEngine;
    using UnityEngine.UIElements;
    using Object = UnityEngine.Object;

    internal sealed class DxAudioAuthoringWindow : EditorWindow
    {
        private static readonly Type[] AssetTypes =
        {
            typeof(DspGraphAsset),
            typeof(DspChainAsset),
            typeof(DspEffectAsset),
            typeof(SfxCue),
            typeof(MusicTrack),
            typeof(MusicDuck),
            typeof(SfxLibrary),
            typeof(MusicLibrary),
        };

        [SerializeField]
        private Object _target;

        private readonly List<Object> _assets = new();
        private readonly List<Object> _matches = new();
        private ToolbarSearchField _search;
        private ListView _list;
        private VisualElement _work;
        private ScrollView _details;
        private VisualElement _findings;
        private Label _status;
        private DspAuditionPanel _audition;
        private DspGraphAsset _graph;
        private DspGraphEdits _edits;
        private DspGraphCanvas _canvas;
        private DspGraph _prepared;
        private int _selectedNode = -1;
        private int _selectedEdge = -1;

        internal static void Open(Object asset)
        {
            DxAudioAuthoringWindow window = GetWindow<DxAudioAuthoringWindow>();
            window.titleContent = new GUIContent("DxAudio Authoring");
            window.minSize = new Vector2(680, 400);
            window.Show();
            if (asset != null)
            {
                window.SetTarget(asset);
            }
        }

        [MenuItem("Tools/DxAudio/Authoring")]
        private static void OpenWindow()
        {
            Object selected = Selection.activeObject;
            if (selected is GameObject gameObject && gameObject != null)
            {
                selected = gameObject.GetComponent<AudioMixBus>();
            }

            Open(selected is DspGraphAsset || selected is DspChainAsset || DxAudioAssetDetails.Supports(selected) ? selected : null);
        }

        private static string TypeLabel(Object asset)
        {
            if (asset == null)
            {
                return string.Empty;
            }

            return ObjectNames.NicifyVariableName(asset.GetType().Name).Replace(" Asset", string.Empty);
        }

        private static string DisplayNodeName(string name)
        {
            return string.IsNullOrEmpty(name) ? "Unassigned" : name;
        }

        public void CreateGUI()
        {
            VisualElement root = rootVisualElement;
            root.Clear();

            Toolbar toolbar = new();
            toolbar.Add(new ToolbarButton(NewAssetMenu) { text = "Create" });
            toolbar.Add(
                new ToolbarButton(() =>
                {
                    if (_target != null)
                    {
                        EditorGUIUtility.PingObject(_target);
                    }
                })
                {
                    text = "Ping Asset",
                }
            );
            toolbar.Add(
                new ToolbarButton(() =>
                {
                    if (_target != null && AssetDatabase.Contains(_target))
                    {
                        AssetDatabase.SaveAssetIfDirty(_target);
                    }
                })
                {
                    text = "Save Asset",
                }
            );
            root.Add(toolbar);

            TwoPaneSplitView body = new(0, 210, TwoPaneSplitViewOrientation.Horizontal);
            body.style.flexGrow = 1;
            body.style.minHeight = 0;
            root.Add(body);

            VisualElement browser = new();
            browser.style.minWidth = 145;
            _search = new ToolbarSearchField { tooltip = "Search DxAudio assets by name or type" };
            _search.style.marginTop = 4;
            _search.style.marginBottom = 4;
            _search.RegisterValueChangedCallback(_ => Filter());
            browser.Add(_search);
            _list = new ListView
            {
                itemsSource = _matches,
                fixedItemHeight = 24f,
                makeItem = () => new Label(),
                bindItem = (item, index) => BindAsset((Label)item, index),
            };
            _list.style.flexGrow = 1;
#if UNITY_2022_3_OR_NEWER
            _list.selectionChanged += OnAssetSelected;
#else
            _list.onSelectionChange += OnAssetSelected;
#endif
            browser.Add(_list);
            body.Add(browser);

            VisualElement content = new();
            content.style.minWidth = 0;
            _work = new VisualElement();
            _work.style.flexGrow = 1;
            _work.style.minHeight = 0;
            content.Add(_work);
            _findings = new VisualElement();
            content.Add(_findings);
            _status = new Label();
            _status.style.whiteSpace = WhiteSpace.Normal;
            content.Add(_status);
            _audition = new DspAuditionPanel(SetTarget, ShowCueVariation);
            content.Add(_audition);
            body.Add(content);

            RefreshAssets();
            if (_target != null)
            {
                ShowTarget();
            }
        }

        private void OnEnable()
        {
            EditorApplication.projectChanged += RefreshAssets;
            Undo.undoRedoPerformed += RefreshGraph;
        }

        private void OnDisable()
        {
            EditorApplication.projectChanged -= RefreshAssets;
            Undo.undoRedoPerformed -= RefreshGraph;
            if (_edits != null)
            {
                _edits.Changed -= RefreshGraph;
            }

            _audition?.Stop();
        }

        private void BindAsset(Label label, int index)
        {
            Object asset = _matches[index];
            string type = TypeLabel(asset);
            label.text = asset == null ? "Deleted asset" : $"{asset.name}  ·  {type}";
            label.tooltip = asset == null ? string.Empty : AssetDatabase.GetAssetPath(asset);
            label.style.paddingLeft = 6;
        }

        private void RefreshAssets()
        {
            if (_list == null)
            {
                return;
            }

            _assets.Clear();
            foreach (Type type in AssetTypes)
            {
                foreach (string guid in AssetDatabase.FindAssets($"t:{type.Name}"))
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    foreach (Object item in AssetDatabase.LoadAllAssetsAtPath(path))
                    {
                        if (item != null && type.IsInstanceOfType(item) && !_assets.Contains(item))
                        {
                            _assets.Add(item);
                        }
                    }
                }
            }

            _assets.Sort(
                (left, right) =>
                {
                    int byName = StringComparer.OrdinalIgnoreCase.Compare(left.name, right.name);
                    return byName == 0 ? StringComparer.Ordinal.Compare(AssetDatabase.GetAssetPath(left), AssetDatabase.GetAssetPath(right)) : byName;
                }
            );
            Filter();
            if (_target == null)
            {
                ShowTarget();
            }
        }

        private void Filter()
        {
            string query = _search.value.Trim();
            _matches.Clear();
            foreach (Object asset in _assets)
            {
                if (
                    asset != null
                    && (0 <= asset.name.IndexOf(query, StringComparison.OrdinalIgnoreCase) || 0 <= TypeLabel(asset).IndexOf(query, StringComparison.OrdinalIgnoreCase))
                )
                {
                    _matches.Add(asset);
                }
            }

            _list.Rebuild();
            UpdateBrowserSelection();
        }

        private void UpdateBrowserSelection()
        {
            _list.SetSelectionWithoutNotify(_target == null || !_matches.Contains(_target) ? Array.Empty<int>() : new[] { _matches.IndexOf(_target) });
        }

        private void OnAssetSelected(IEnumerable<object> selected)
        {
            foreach (object item in selected)
            {
                if (item is Object asset && asset != null)
                {
                    SetTarget(asset);
                    break;
                }
            }
        }

        private void SetTarget(Object asset)
        {
            if (_target == asset)
            {
                return;
            }

            _target = asset;
            if (_work != null)
            {
                UpdateBrowserSelection();
                ShowTarget();
            }
        }

        private void ShowTarget()
        {
            if (_edits != null)
            {
                _edits.Changed -= RefreshGraph;
            }

            _selectedNode = -1;
            _selectedEdge = -1;
            _canvas = null;
            _graph = _target as DspGraphAsset;
            _edits = null;
            _work.Unbind();
            _work.Clear();
            _findings.Clear();
            Report(string.Empty);
            _audition.SetTarget(_target);

            if (_target == null)
            {
                _work.Add(new HelpBox("Select an audio asset or create a DSP graph or chain.", HelpBoxMessageType.Info));
                return;
            }

            if (_graph != null)
            {
                ShowGraph();
                return;
            }

            ScrollView scroll = new();
            scroll.style.flexGrow = 1;
            _work.Add(scroll);
            if (_target is DspChainAsset chain)
            {
                scroll.Add(new DspChainRack(chain));
            }
            else
            {
                scroll.Add(DxAudioAssetDetails.Build(_target, SetTarget));
            }
        }

        private void ShowCueVariation()
        {
            VisualElement variation = _work.Q<VisualElement>("DxAudioSfxVariation");
            if (variation != null)
            {
                variation.GetFirstAncestorOfType<ScrollView>()?.ScrollTo(variation);
                VisualElement field = variation.Q<VisualElement>(className: "unity-base-field");
                field?.Focus();
            }
        }

        private void ShowGraph()
        {
            _edits = new DspGraphEdits(_graph);
            _edits.Changed += RefreshGraph;
            TwoPaneSplitView split = new(1, 260, TwoPaneSplitViewOrientation.Horizontal);
            split.style.flexGrow = 1;
            split.style.minHeight = 0;
            _work.Add(split);

            _canvas = new DspGraphCanvas(_graph, _edits, SelectNode, SelectEdge, Report);
            _canvas.style.minWidth = 160;
            split.Add(_canvas);
            _details = new ScrollView();
            _details.style.minWidth = 200;
            _details.contentContainer.style.paddingLeft = 8;
            _details.contentContainer.style.paddingRight = 8;
            _details.contentContainer.style.paddingTop = 8;
            split.Add(_details);

            _work.TrackSerializedObjectValue(new SerializedObject(_graph), _ => RefreshGraph());
            RefreshGraph();
        }

        private void SelectNode(int index)
        {
            _selectedNode = index;
            _selectedEdge = -1;
            BuildGraphDetails();
        }

        private void SelectEdge(int index)
        {
            _selectedNode = -1;
            _selectedEdge = index;
            BuildGraphDetails();
        }

        private void RefreshGraph()
        {
            if (_graph == null || _canvas == null)
            {
                return;
            }

            _canvas.Refresh();
            _findings.Clear();
            int count = 0;
            ScrollView problems = new();
            problems.style.maxHeight = 94;
            _prepared = _graph.CollectProblems(
                (node, edge, message) =>
                {
                    count++;
                    if (node < 0 && edge < 0)
                    {
                        problems.Add(new HelpBox(message, HelpBoxMessageType.Warning));
                    }
                    else
                    {
                        problems.Add(new Button(() => FocusFinding(node, edge)) { text = message });
                    }
                }
            );
            if (0 < count)
            {
                _findings.Add(problems);
            }

            BuildGraphDetails();
        }

        private void FocusFinding(int node, int edge)
        {
            if (0 <= node)
            {
                _canvas.SelectNode(node);
            }
            else
            {
                _canvas.SelectEdge(edge);
            }

            _canvas.FrameSelection();
        }

        private void BuildGraphDetails()
        {
            _details.Clear();
            Label title = new(_graph.name);
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.marginBottom = 6;
            title.style.whiteSpace = WhiteSpace.Normal;
            _details.Add(title);
            VisualElement actions = new();
            actions.style.flexDirection = FlexDirection.Row;
            actions.style.flexWrap = Wrap.Wrap;
            actions.style.marginBottom = 6;
            actions.Add(new Button(() => _canvas.AddNode()) { text = "Add Node…" });
            actions.Add(new Button(() => _canvas.FindNode()) { text = "Find Node…" });
            actions.Add(new Button(() => _canvas.FrameSelection()) { text = "Frame Selection" });
            actions.Add(new Button(() => _canvas.Arrange()) { text = "Arrange Nodes" });
            _details.Add(actions);

            List<string> uniqueNames = new() { string.Empty };
            for (int index = 0; index < _graph.NodeCount; index++)
            {
                DspGraphNode node = _graph.Nodes[index];
                if (node != null && _edits.Resolve(node.Name) == index)
                {
                    uniqueNames.Add(node.Name);
                }
            }

            AddEndpoint("Input", _graph.InputNode, uniqueNames, true);
            AddEndpoint("Output", _graph.OutputNode, uniqueNames, false);
            if (0 <= _selectedNode && _selectedNode < _graph.NodeCount)
            {
                BuildNodeDetails(_selectedNode, uniqueNames);
            }

            if (0 <= _selectedEdge && _selectedEdge < _graph.EdgeCount)
            {
                BuildConnectionDetails(uniqueNames);
            }

            Foldout nodes = new() { text = $"Nodes ({_graph.NodeCount})", value = false };
            for (int index = 0; index < _graph.NodeCount; index++)
            {
                int row = index;
                string name = _graph.Nodes[index]?.Name;
                nodes.Add(new Button(() => FocusFinding(row, -1)) { text = string.IsNullOrWhiteSpace(name) ? $"Unnamed node {row + 1}" : name });
            }

            _details.Add(nodes);
            Foldout connections = new() { text = $"Connections ({_graph.EdgeCount})", value = false };
            for (int index = 0; index < _graph.EdgeCount; index++)
            {
                int row = index;
                DspGraphEdge edge = _graph.Edges[index];
                connections.Add(new Button(() => FocusFinding(-1, row)) { text = edge == null ? $"Empty connection {row + 1}" : $"{edge.From} → {edge.To}" });
            }

            _details.Add(connections);
            if (_prepared != null)
            {
                _details.Add(new Label($"{_prepared.LatencySamples} samples · {_prepared.PreparedBytes:N0} bytes"));
                Label reference = new("Reference layout · 48 kHz · stereo · 1,024 frames");
                reference.style.whiteSpace = WhiteSpace.Normal;
                _details.Add(reference);
                if (_prepared.TryGetNodeLatency(_selectedNode, out int nodeLatency))
                {
                    _details.Add(new Label($"Selected branch · {nodeLatency} samples"));
                }
            }
        }

        private void AddEndpoint(string label, string current, List<string> names, bool input)
        {
            PopupField<string> selector = new(label, names, Mathf.Max(0, names.IndexOf(current)), DisplayNodeName, DisplayNodeName);
            selector.RegisterValueChangedCallback(evt =>
            {
                if (!_edits.TrySetEndpoint(input, selector.index == 0 ? -1 : _edits.Resolve(evt.newValue), out string error))
                {
                    Report(error);
                }
            });
            _details.Add(selector);
            if (!string.IsNullOrEmpty(current) && _edits.Resolve(current) < 0)
            {
                _details.Add(
                    new Button(() =>
                    {
                        if (!_edits.TrySetEndpoint(input, -1, out string error))
                        {
                            Report(error);
                        }
                    })
                    {
                        text = $"Clear {label} Reference",
                    }
                );
            }
        }

        private void BuildNodeDetails(int index, List<string> names)
        {
            DspGraphNode node = _graph.Nodes[index];
            if (node == null)
            {
                return;
            }

            TextField name = new("Name") { value = node.Name, isDelayed = true };
            name.RegisterValueChangedCallback(evt =>
            {
                if (!_edits.TryRename(index, evt.newValue, out string error))
                {
                    name.SetValueWithoutNotify(evt.previousValue);
                    Report(error);
                }
            });
            _details.Add(name);
            ObjectField chain = new("Chain")
            {
                objectType = typeof(DspChainAsset),
                value = node.Chain,
                allowSceneObjects = false,
            };
            chain.RegisterValueChangedCallback(evt =>
            {
                if (!_edits.TrySetChain(index, evt.newValue as DspChainAsset, out string error))
                {
                    chain.SetValueWithoutNotify(evt.previousValue);
                    Report(error);
                }
            });
            _details.Add(chain);

            if (_edits.Resolve(node.Name) == index)
            {
                PopupField<string> destination = new("Connect To", names, 0, DisplayNodeName, DisplayNodeName);
                _details.Add(destination);
                Button connect = new(() =>
                {
                    if (!_edits.TryConnect(index, _edits.Resolve(destination.value), out string error))
                    {
                        Report(error);
                    }
                })
                {
                    text = "Connect",
                };
                connect.SetEnabled(false);
                destination.RegisterValueChangedCallback(_ => connect.SetEnabled(0 < destination.index));
                _details.Add(connect);
            }

            _details.Add(
                new Button(() =>
                {
                    if (_edits.TryDelete(new[] { index }, Array.Empty<int>(), out string error))
                    {
                        _canvas.SelectNode(-1);
                    }
                    else
                    {
                        Report(error);
                    }
                })
                {
                    text = "Remove Node",
                }
            );

            if (node.Chain != null)
            {
                _details.Add(new Button(() => SetTarget(node.Chain)) { text = "Open Chain" });
                Foldout effects = new() { text = "Chain Effects", value = false };
                effects.Add(new DspChainRack(node.Chain, showTitle: false));
                _details.Add(effects);
            }
        }

        private void BuildConnectionDetails(List<string> names)
        {
            int edgeIndex = _selectedEdge;
            DspGraphEdge edge = _graph.Edges[edgeIndex];
            PopupField<string> from = new("From", names, Mathf.Max(0, names.IndexOf(edge?.From)), DisplayNodeName, DisplayNodeName);
            PopupField<string> to = new("To", names, Mathf.Max(0, names.IndexOf(edge?.To)), DisplayNodeName, DisplayNodeName);
            _details.Add(from);
            _details.Add(to);
            _details.Add(
                new Button(() =>
                {
                    if (!_edits.TrySetEdge(edgeIndex, from.index == 0 ? -1 : _edits.Resolve(from.value), to.index == 0 ? -1 : _edits.Resolve(to.value), out string error))
                    {
                        Report(error);
                    }
                })
                {
                    text = "Apply Connection",
                }
            );
            _details.Add(
                new Button(() =>
                {
                    _selectedEdge = -1;
                    if (!_edits.TryDelete(Array.Empty<int>(), new[] { edgeIndex }, out string error))
                    {
                        Report(error);
                    }
                })
                {
                    text = "Remove Connection",
                }
            );
        }

        private void Report(string message)
        {
            _status.text = message;
            _status.style.display = string.IsNullOrEmpty(message) ? DisplayStyle.None : DisplayStyle.Flex;
        }

        private void NewAssetMenu()
        {
            GenericMenu menu = new();
            menu.AddItem(new GUIContent("DSP Graph"), false, () => CreateAsset<DspGraphAsset>("DSP Graph"));
            menu.AddItem(new GUIContent("DSP Chain"), false, () => CreateAsset<DspChainAsset>("DSP Chain"));
            menu.AddItem(new GUIContent("DSP Effect/Bit Crusher"), false, () => CreateAsset<DspBitCrusherAsset>("Bit Crusher"));
            menu.AddItem(new GUIContent("DSP Effect/Compressor"), false, () => CreateAsset<DspCompressorAsset>("Compressor"));
            menu.AddItem(new GUIContent("DSP Effect/Distortion"), false, () => CreateAsset<DspDistortionAsset>("Distortion"));
            menu.AddItem(new GUIContent("DSP Effect/Filter"), false, () => CreateAsset<DspFilterAsset>("Filter"));
            menu.AddItem(new GUIContent("DSP Effect/Limiter"), false, () => CreateAsset<DspLimiterAsset>("Limiter"));
            menu.AddItem(new GUIContent("DSP Effect/Pitch Shifter"), false, () => CreateAsset<DspPitchShifterAsset>("Pitch Shifter"));
            menu.AddSeparator(string.Empty);
            menu.AddItem(new GUIContent("SFX Cue"), false, () => CreateAsset<SfxCue>("SFX Cue"));
            menu.AddItem(new GUIContent("SFX Library"), false, () => CreateAsset<SfxLibrary>("SFX Library"));
            menu.AddItem(new GUIContent("Music Track"), false, () => CreateAsset<MusicTrack>("Music Track"));
            menu.AddItem(new GUIContent("Music Library"), false, () => CreateAsset<MusicLibrary>("Music Library"));
            menu.AddItem(new GUIContent("Music Duck"), false, () => CreateAsset<MusicDuck>("Music Duck"));
            menu.ShowAsContext();
        }

        private void CreateAsset<T>(string name)
            where T : ScriptableObject
        {
            string path = EditorUtility.SaveFilePanelInProject($"Create {name}", name, "asset", string.Empty);
            if (string.IsNullOrEmpty(path))
            {
                return;
            }

            T asset = CreateInstance<T>();
            AssetDatabase.CreateAsset(asset, AssetDatabase.GenerateUniqueAssetPath(path));
            RefreshAssets();
            SetTarget(asset);
        }
    }
}
