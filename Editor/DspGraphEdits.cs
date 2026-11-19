namespace WallstopStudios.DxAudio.Editor
{
    using System;
    using System.Collections.Generic;
    using UnityEditor;
    using UnityEngine;

    internal sealed class DspGraphEdits
    {
        private readonly DspGraphAsset _asset;

        internal DspGraphEdits(DspGraphAsset asset)
        {
            _asset = asset;
        }

        internal event Action Changed;

        internal bool TryAdd(string name, DspChainAsset chain, Vector2 position, out string error)
        {
            return TryAddNode(name, chain, position, -1, -1, out error);
        }

        internal bool TryAddConnected(int from, string name, DspChainAsset chain, Vector2 position, out string error)
        {
            return TryAddNode(name, chain, position, from, -1, out error);
        }

        internal bool TryInsert(int edge, string name, DspChainAsset chain, Vector2 position, out string error)
        {
            if (edge < 0 || _asset.EdgeCount <= edge)
            {
                error = "Select a connection to insert on.";
                return false;
            }

            return TryAddNode(name, chain, position, -1, edge, out error);
        }

        private bool TryAddNode(string name, DspChainAsset chain, Vector2 position, int from, int edge, out string error)
        {
            name = name?.Trim();
            if (!CanName(name, -1, out error))
            {
                return false;
            }

            if (DspGraph.MaximumNodes <= _asset.NodeCount || ((0 <= from || 0 <= edge) && DspGraph.MaximumEdges <= _asset.EdgeCount))
            {
                error = "The graph has reached its node or connection limit.";
                return false;
            }

            DspGraphEdge replaced = 0 <= edge ? _asset.Edges[edge] : null;
            if ((0 <= from && !CanUseNode(from)) || (0 <= edge && (replaced == null || Resolve(replaced.From) < 0 || Resolve(replaced.To) < 0)))
            {
                error = "Choose unambiguous endpoints before adding a connected node.";
                return false;
            }

            if (replaced != null && !CanConnect(edge, Resolve(replaced.From), Resolve(replaced.To), out error))
            {
                return false;
            }

            if (HasReference(name))
            {
                error = "Repair the existing references to this name before adding the node.";
                return false;
            }

            return Commit(
                0 <= edge ? "Insert DSP Node" : "Add DSP Node",
                serialized =>
                {
                    SerializedProperty nodes = serialized.FindProperty("_nodes");
                    SerializedProperty node = Append(nodes);
                    node.FindPropertyRelative("_name").stringValue = name;
                    node.FindPropertyRelative("_chain").objectReferenceValue = chain;
                    SetPosition(node, position);
                    SerializedProperty edges = serialized.FindProperty("_edges");
                    if (replaced != null)
                    {
                        SetEdge(edges.GetArrayElementAtIndex(edge), replaced.From, name);
                        SetEdge(Append(edges), name, replaced.To);
                    }
                    else if (0 <= from)
                    {
                        SetEdge(Append(edges), _asset.Nodes[from].Name, name);
                    }
                    else if (nodes.arraySize == 1)
                    {
                        serialized.FindProperty("_inputNode").stringValue = name;
                        serialized.FindProperty("_outputNode").stringValue = name;
                    }
                },
                out error
            );
        }

        internal bool TryConnect(int from, int to, out string error)
        {
            return TrySetEdge(-1, from, to, out error);
        }

        internal bool TrySetEdge(int edge, int from, int to, out string error)
        {
            if (!CanConnect(edge, from, to, out error))
            {
                return false;
            }

            string source = _asset.Nodes[from].Name;
            string target = _asset.Nodes[to].Name;
            return Commit(
                edge < 0 ? "Connect DSP Nodes" : "Repair DSP Connection",
                serialized =>
                {
                    SerializedProperty edges = serialized.FindProperty("_edges");
                    SetEdge(edge < 0 ? Append(edges) : edges.GetArrayElementAtIndex(edge), source, target);
                },
                out error
            );
        }

        internal bool CanConnect(int from, int to, out string error)
        {
            return CanConnect(-1, from, to, out error);
        }

        private bool CanConnect(int edge, int from, int to, out string error)
        {
            if (IsReadOnly())
            {
                error = "The graph is read-only.";
                return false;
            }

            if (_asset.EdgeCount <= edge || !CanUseNode(from) || !CanUseNode(to))
            {
                error = "Choose two nodes with unique names.";
                return false;
            }

            if (from == to)
            {
                error = "A node cannot connect to itself.";
                return false;
            }

            if (edge < 0 && DspGraph.MaximumEdges <= _asset.EdgeCount)
            {
                error = "The graph has reached its connection limit.";
                return false;
            }

            string source = _asset.Nodes[from].Name;
            string target = _asset.Nodes[to].Name;
            for (int index = 0; index < _asset.EdgeCount; index++)
            {
                DspGraphEdge existing = _asset.Edges[index];
                if (index != edge && existing != null && existing.From == source && existing.To == target)
                {
                    error = "These nodes already have this connection.";
                    return false;
                }
            }

            if (Reaches(to, from, edge, new HashSet<int>()))
            {
                error = "This connection would close a cycle.";
                return false;
            }

            error = string.Empty;
            return true;
        }

        internal bool TryRename(int index, string name, out string error)
        {
            name = name?.Trim();
            if (!CanName(name, index, out error))
            {
                return false;
            }

            if (index < 0 || _asset.NodeCount <= index || _asset.Nodes[index] == null)
            {
                error = "Select a node to rename.";
                return false;
            }

            string previous = _asset.Nodes[index].Name;
            if (previous == name)
            {
                return true;
            }

            if (HasReference(name))
            {
                error = "Repair the existing references to this name before renaming the node.";
                return false;
            }

            if (Resolve(previous) < 0 && HasReference(previous))
            {
                error = "Repair references to the ambiguous name before renaming this node.";
                return false;
            }

            return Commit(
                "Rename DSP Node",
                serialized =>
                {
                    serialized.FindProperty("_nodes").GetArrayElementAtIndex(index).FindPropertyRelative("_name").stringValue = name;
                    SerializedProperty edges = serialized.FindProperty("_edges");
                    for (int row = 0; row < edges.arraySize; row++)
                    {
                        SerializedProperty connection = edges.GetArrayElementAtIndex(row);
                        Replace(connection.FindPropertyRelative("_from"), previous, name);
                        Replace(connection.FindPropertyRelative("_to"), previous, name);
                    }

                    Replace(serialized.FindProperty("_inputNode"), previous, name);
                    Replace(serialized.FindProperty("_outputNode"), previous, name);
                },
                out error
            );
        }

        internal bool TrySetEndpoint(bool input, int index, out string error)
        {
            if (index != -1 && !CanUseNode(index))
            {
                error = "Choose a node with a unique name.";
                return false;
            }

            return Commit(
                input ? "Set DSP Input" : "Set DSP Output",
                serialized =>
                {
                    serialized.FindProperty(input ? "_inputNode" : "_outputNode").stringValue = index == -1 ? string.Empty : _asset.Nodes[index].Name;
                },
                out error
            );
        }

        internal bool TrySetChain(int index, DspChainAsset chain, out string error)
        {
            if (index < 0 || _asset.NodeCount <= index || _asset.Nodes[index] == null)
            {
                error = "Select a node first.";
                return false;
            }

            return Commit(
                "Set DSP Node Chain",
                serialized =>
                {
                    serialized.FindProperty("_nodes").GetArrayElementAtIndex(index).FindPropertyRelative("_chain").objectReferenceValue = chain;
                },
                out error
            );
        }

        internal bool TryMove(IReadOnlyDictionary<int, Vector2> positions, out string error)
        {
            foreach (KeyValuePair<int, Vector2> placement in positions)
            {
                if (placement.Key < 0 || _asset.NodeCount <= placement.Key)
                {
                    error = "The selected node no longer exists.";
                    return false;
                }
            }

            return Commit(
                "Move DSP Nodes",
                serialized =>
                {
                    SerializedProperty nodes = serialized.FindProperty("_nodes");
                    foreach (KeyValuePair<int, Vector2> placement in positions)
                    {
                        SetPosition(nodes.GetArrayElementAtIndex(placement.Key), placement.Value);
                    }
                },
                out error
            );
        }

        internal bool TryDelete(IReadOnlyCollection<int> nodes, IReadOnlyCollection<int> edges, out string error)
        {
            HashSet<int> removedNodes = new(nodes);
            HashSet<int> removedEdges = new(edges);
            foreach (int index in removedEdges)
            {
                if (index < 0 || _asset.EdgeCount <= index)
                {
                    error = "The selected connection no longer exists.";
                    return false;
                }
            }

            HashSet<string> detach = new(StringComparer.Ordinal);
            foreach (int index in removedNodes)
            {
                if (index < 0 || _asset.NodeCount <= index)
                {
                    error = "The selected node no longer exists.";
                    return false;
                }

                string name = _asset.Nodes[index]?.Name;
                if (Resolve(name) < 0)
                {
                    if (HasReference(name))
                    {
                        error = "Repair references to the ambiguous name before deleting this node.";
                        return false;
                    }
                }
                else
                {
                    detach.Add(name);
                }
            }

            return Commit(
                "Delete DSP Selection",
                serialized =>
                {
                    SerializedProperty edgeList = serialized.FindProperty("_edges");
                    for (int index = edgeList.arraySize - 1; 0 <= index; index--)
                    {
                        DspGraphEdge edge = _asset.Edges[index];
                        if (removedEdges.Contains(index) || (edge != null && (detach.Contains(edge.From) || detach.Contains(edge.To))))
                        {
                            edgeList.DeleteArrayElementAtIndex(index);
                        }
                    }

                    SerializedProperty nodeList = serialized.FindProperty("_nodes");
                    for (int index = nodeList.arraySize - 1; 0 <= index; index--)
                    {
                        if (removedNodes.Contains(index))
                        {
                            nodeList.DeleteArrayElementAtIndex(index);
                        }
                    }

                    ClearEndpoint(serialized.FindProperty("_inputNode"), detach);
                    ClearEndpoint(serialized.FindProperty("_outputNode"), detach);
                },
                out error
            );
        }

        internal int Resolve(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return -1;
            }

            int match = -1;
            for (int index = 0; index < _asset.NodeCount; index++)
            {
                if (_asset.Nodes[index]?.Name == name)
                {
                    if (0 <= match)
                    {
                        return -1;
                    }

                    match = index;
                }
            }

            return match;
        }

        private bool CanUseNode(int index)
        {
            return 0 <= index && index < _asset.NodeCount && Resolve(_asset.Nodes[index]?.Name) == index;
        }

        private bool CanName(string name, int except, out string error)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                error = "Enter a node name.";
                return false;
            }

            for (int index = 0; index < _asset.NodeCount; index++)
            {
                if (index != except && _asset.Nodes[index]?.Name == name)
                {
                    error = "That node name is already in use.";
                    return false;
                }
            }

            error = string.Empty;
            return true;
        }

        private bool HasReference(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return false;
            }

            if (_asset.InputNode == name || _asset.OutputNode == name)
            {
                return true;
            }

            foreach (DspGraphEdge edge in _asset.Edges)
            {
                if (edge != null && (edge.From == name || edge.To == name))
                {
                    return true;
                }
            }

            return false;
        }

        private bool Reaches(int from, int to, int excludedEdge, HashSet<int> visited)
        {
            if (from == to)
            {
                return true;
            }

            if (!visited.Add(from))
            {
                return false;
            }

            string name = _asset.Nodes[from].Name;
            for (int index = 0; index < _asset.EdgeCount; index++)
            {
                DspGraphEdge edge = _asset.Edges[index];
                if (index != excludedEdge && edge != null && edge.From == name)
                {
                    int next = Resolve(edge.To);
                    if (0 <= next && Reaches(next, to, excludedEdge, visited))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private bool Commit(string label, Action<SerializedObject> edit, out string error)
        {
            if (IsReadOnly())
            {
                error = "The graph is read-only.";
                return false;
            }

            using SerializedObject serialized = new(_asset);
            edit(serialized);
            Undo.IncrementCurrentGroup();
            int undo = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName(label);
            bool changed = serialized.ApplyModifiedProperties();
            Undo.CollapseUndoOperations(undo);
            if (changed)
            {
                Changed?.Invoke();
            }

            error = string.Empty;
            return true;
        }

        private bool IsReadOnly()
        {
            return AssetDatabase.Contains(_asset) && !AssetDatabase.IsOpenForEdit(_asset);
        }

        private static SerializedProperty Append(SerializedProperty array)
        {
            int index = array.arraySize;
            array.arraySize++;
            return array.GetArrayElementAtIndex(index);
        }

        private static void SetPosition(SerializedProperty node, Vector2 position)
        {
            node.FindPropertyRelative("_hasEditorPosition").boolValue = true;
            node.FindPropertyRelative("_editorPosition").vector2Value = position;
        }

        private static void SetEdge(SerializedProperty edge, string from, string to)
        {
            edge.FindPropertyRelative("_from").stringValue = from;
            edge.FindPropertyRelative("_to").stringValue = to;
        }

        private static void Replace(SerializedProperty property, string before, string after)
        {
            if (!string.IsNullOrEmpty(before) && property != null && property.stringValue == before)
            {
                property.stringValue = after;
            }
        }

        private static void ClearEndpoint(SerializedProperty property, HashSet<string> names)
        {
            if (names.Contains(property.stringValue))
            {
                property.stringValue = string.Empty;
            }
        }
    }
}
