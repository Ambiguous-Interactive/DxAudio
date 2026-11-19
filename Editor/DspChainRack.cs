namespace WallstopStudios.DxAudio.Editor
{
    using System;
    using System.Collections.Generic;
    using UnityEditor;
    using UnityEditor.UIElements;
    using UnityEngine.UIElements;

    internal sealed class DspChainRack : VisualElement
    {
        private readonly DspChainAsset _chain;
        private readonly VisualElement _stages = new();
        private readonly List<DspEffectAsset> _shown = new();

        internal DspChainRack(DspChainAsset chain)
        {
            _chain = chain;
            Add(new Label(chain.name));
            Add(_stages);

            ObjectField addStage = new("Add Existing Effect") { objectType = typeof(DspEffectAsset), allowSceneObjects = false };
            addStage.RegisterValueChangedCallback(evt =>
            {
                if (evt.newValue is DspEffectAsset effect && effect != null)
                {
                    Edit(
                        "Add DSP Effect",
                        effects =>
                        {
                            int index = effects.arraySize;
                            effects.arraySize++;
                            effects.GetArrayElementAtIndex(index).objectReferenceValue = effect;
                        }
                    );
                    addStage.SetValueWithoutNotify(null);
                }
            });
            addStage.SetEnabled(AssetDatabase.IsOpenForEdit(chain));
            Add(addStage);

            Foldout eq = new() { text = "Drawn EQ", value = false };
            eq.Add(new DspEqCurveAuthoring(chain));
            Add(eq);
            this.TrackSerializedObjectValue(new SerializedObject(chain), _ => RefreshStages());
            RefreshStages(force: true);
        }

        private void RefreshStages(bool force = false)
        {
            IReadOnlyList<DspEffectAsset> effects = _chain.Effects;
            if (!force && SameOrder(effects))
            {
                return;
            }

            _shown.Clear();
            _stages.Clear();
            bool editable = AssetDatabase.IsOpenForEdit(_chain);
            for (int index = 0; index < effects.Count; index++)
            {
                DspEffectAsset effect = effects[index];
                _shown.Add(effect);

                int slot = index;
                VisualElement row = new();
                row.style.flexDirection = FlexDirection.Row;
                Button up = new(() => Move(slot, slot - 1)) { text = "↑", tooltip = "Move effect earlier" };
                up.SetEnabled(editable && 0 < slot);
                row.Add(up);
                Button down = new(() => Move(slot, slot + 1)) { text = "↓", tooltip = "Move effect later" };
                down.SetEnabled(editable && slot + 1 < effects.Count);
                row.Add(down);
                Button remove = new(() => Remove(slot)) { text = "Remove" };
                remove.SetEnabled(editable);
                row.Add(remove);

                Foldout stage = new() { text = effect == null ? $"{slot + 1}. Empty stage" : $"{slot + 1}. {effect.name}", value = false };
                stage.Add(row);
                if (effect != null)
                {
                    stage.Add(new HelpBox("Edits apply to every chain that uses this effect.", HelpBoxMessageType.Info));
                    stage.Add(new InspectorElement(effect));
                }

                _stages.Add(stage);
            }
        }

        private bool SameOrder(IReadOnlyList<DspEffectAsset> current)
        {
            if (current.Count != _shown.Count)
            {
                return false;
            }

            for (int index = 0; index < current.Count; index++)
            {
                if (!ReferenceEquals(current[index], _shown[index]))
                {
                    return false;
                }
            }

            return true;
        }

        private void Move(int from, int to)
        {
            Edit(
                "Reorder DSP Effect",
                effects =>
                {
                    effects.MoveArrayElement(from, to);
                }
            );
        }

        private void Remove(int index)
        {
            Edit(
                "Remove DSP Effect",
                effects =>
                {
                    int before = effects.arraySize;
                    effects.DeleteArrayElementAtIndex(index);
                    if (effects.arraySize == before)
                    {
                        effects.DeleteArrayElementAtIndex(index);
                    }
                }
            );
        }

        private void Edit(string label, Action<SerializedProperty> edit)
        {
            if (!AssetDatabase.IsOpenForEdit(_chain))
            {
                return;
            }

            using SerializedObject serialized = new(_chain);
            edit(serialized.FindProperty("_effects"));
            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName(label);
            if (serialized.ApplyModifiedProperties())
            {
                RefreshStages();
            }

            Undo.CollapseUndoOperations(group);
        }
    }
}
