namespace WallstopStudios.DxAudio.Editor
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using UnityEditor;
    using UnityEngine;

    internal static class DspEqCurveAsset
    {
        private const string UndoLabel = "Apply Drawn EQ Fit";

        internal static DspChainAsset.DrawnEqSettings ReadSettings(DspChainAsset chain)
        {
            DspChainAsset.DrawnEqSettings settings = chain.DrawnEq;
            return
                settings?.Target?.Length == DspEqCurveFit.SampleCount
                && DspEqCurveFit.MinimumFrequencyHz < DspEqCurveFit.GetMaximumFrequencyHz(settings.SampleRate)
                && 0 < settings.BandLimit
                && settings.BandLimit <= DspEqCurveFit.MaximumBandCount
                && settings.GeneratedStages != null
                ? settings
                : null;
        }

        internal static DspEqCurveFit.Band[] ReadCurrentBands(DspChainAsset chain, DspChainAsset.DrawnEqSettings settings)
        {
            List<DspEqCurveFit.Band> bands = new();
            foreach (DspEffectAsset effect in chain.Effects)
            {
                if (effect is DspFilterAsset stage && stage != null && stage.Mode == DspFilterMode.Bell && Array.Exists(settings.GeneratedStages, entry => entry.Filter == stage))
                {
                    bands.Add(new DspEqCurveFit.Band(stage.FrequencyHz, stage.Quality, stage.PeakDecibels));
                }
            }

            return bands.ToArray();
        }

        internal static void ApplyFit(DspChainAsset chain, float[] target, int sampleRate, int bandLimit, DspEqCurveFit.Result fit)
        {
            DspChainAsset.DrawnEqSettings saved = ReadSettings(chain);
            string path = AssetDatabase.GetAssetPath(chain);
            if (saved == null || string.IsNullOrEmpty(path) || !AssetDatabase.IsMainAsset(chain) || !AssetDatabase.IsOpenForEdit(chain))
            {
                throw new InvalidOperationException("Apply the fit to an editable EQ chain asset.");
            }

            List<DspChainAsset.DrawnEqStage> generated = new(saved.GeneratedStages.Length);
            Dictionary<DspFilterAsset, int> ownedIndices = new();
            foreach (DspChainAsset.DrawnEqStage entry in saved.GeneratedStages)
            {
                DspFilterAsset stage = entry.Filter;
                if (stage != null && BelongsToChain(stage, path) && !ownedIndices.ContainsKey(stage))
                {
                    ownedIndices.Add(stage, generated.Count);
                    generated.Add(entry);
                }
            }

            List<int> selected = new();
            List<DspEffectAsset> manual = new(chain.Count);
            foreach (DspEffectAsset effect in chain.Effects)
            {
                if (effect is DspFilterAsset stage && stage != null && ownedIndices.TryGetValue(stage, out int index))
                {
                    if (selected.Contains(index))
                    {
                        throw new InvalidOperationException("A generated Bell occurs more than once. Remove the duplicate or save a new EQ chain.");
                    }

                    selected.Add(index);
                    generated[index] = new DspChainAsset.DrawnEqStage(stage, manual.Count);
                }
                else
                {
                    manual.Add(effect);
                }
            }

            // Retain retired bands and their positions for external references and later fits.
            for (int index = 0; index < generated.Count; ++index)
            {
                if (!selected.Contains(index))
                {
                    selected.Add(index);
                }
            }

            int existingCount = selected.Count;
            int newBandPosition = manual.Count;
            if (0 < existingCount)
            {
                newBandPosition = 0;
                foreach (DspChainAsset.DrawnEqStage entry in generated)
                {
                    newBandPosition = Mathf.Max(newBandPosition, Mathf.Min(entry.PrecedingManualStages, manual.Count));
                }
            }

            Undo.IncrementCurrentGroup();
            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName(UndoLabel);
            try
            {
                while (selected.Count < fit.Bands.Length)
                {
                    DspFilterAsset stage = CreateBand(fit.Bands[selected.Count], generated.Count);
                    try
                    {
                        AssetDatabase.AddObjectToAsset(stage, chain);
                        // Unity reports IsSubAsset only after the first save.
                        if (!AssetDatabase.Contains(stage) || !string.Equals(AssetDatabase.GetAssetPath(stage), path, StringComparison.Ordinal))
                        {
                            throw new InvalidOperationException("Unity could not add the new EQ band.");
                        }

                        Undo.RegisterCreatedObjectUndo(stage, UndoLabel);
                    }
                    catch
                    {
                        UnityEngine.Object.DestroyImmediate(stage, true);
                        throw;
                    }

                    selected.Add(generated.Count);
                    generated.Add(new DspChainAsset.DrawnEqStage(stage, newBandPosition));
                }

                Undo.RegisterCompleteObjectUndo(chain, UndoLabel);
                for (int index = 0; index < Math.Min(existingCount, fit.Bands.Length); ++index)
                {
                    SetBand(generated[selected[index]].Filter, fit.Bands[index]);
                }

                List<DspEffectAsset> effects = new(manual.Count + fit.Bands.Length);
                for (int position = 0; position <= manual.Count; ++position)
                {
                    for (int index = 0; index < fit.Bands.Length; ++index)
                    {
                        DspChainAsset.DrawnEqStage entry = generated[selected[index]];
                        if (Mathf.Clamp(entry.PrecedingManualStages, 0, manual.Count) == position)
                        {
                            effects.Add(entry.Filter);
                        }
                    }

                    if (position < manual.Count)
                    {
                        effects.Add(manual[position]);
                    }
                }

                chain.SetDrawnEq(new DspChainAsset.DrawnEqSettings(target, sampleRate, bandLimit, generated.ToArray()), effects);
                EditorUtility.SetDirty(chain);
                AssetDatabase.SaveAssetIfDirty(chain);
                Undo.CollapseUndoOperations(undoGroup);
            }
            catch
            {
                Undo.RevertAllDownToGroup(undoGroup);
                throw;
            }
            finally
            {
                Undo.IncrementCurrentGroup();
            }
        }

        internal static DspChainAsset SaveChain(string path, float[] target, int sampleRate, int bandLimit, DspEqCurveFit.Result result)
        {
            if (
                string.IsNullOrEmpty(path)
                || !path.StartsWith("Assets/", StringComparison.Ordinal)
                || !path.EndsWith(".asset", StringComparison.OrdinalIgnoreCase)
                || !AssetDatabase.IsValidFolder(path.Substring(0, path.LastIndexOf('/')))
                || File.Exists(path)
                || AssetDatabase.LoadMainAssetAtPath(path) != null
            )
            {
                throw new ArgumentException("Choose a new .asset file under Assets.", nameof(path));
            }

            List<DspEffectAsset> effects = new(result.Bands.Length);
            DspChainAsset.DrawnEqStage[] generated = new DspChainAsset.DrawnEqStage[result.Bands.Length];
            DspChainAsset chain = null;
            string createdGuid = null;
            try
            {
                for (int index = 0; index < generated.Length; ++index)
                {
                    DspFilterAsset stage = CreateBand(result.Bands[index], index);
                    generated[index] = new DspChainAsset.DrawnEqStage(stage, 0);
                    effects.Add(stage);
                }

                chain = DspChainAsset.Create(effects);
                chain.SetDrawnEq(new DspChainAsset.DrawnEqSettings(target, sampleRate, bandLimit, generated), effects);
                AssetDatabase.CreateAsset(chain, path);
                if (!AssetDatabase.Contains(chain))
                {
                    throw new InvalidOperationException("Unity could not create the EQ chain.");
                }

                createdGuid = AssetDatabase.AssetPathToGUID(path);
                foreach (DspEffectAsset filter in effects)
                {
                    AssetDatabase.AddObjectToAsset(filter, chain);
                }

                EditorUtility.SetDirty(chain);
                AssetDatabase.SaveAssetIfDirty(chain);
                // Unity registers the sub-assets when it imports the saved file.
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
                DspChainAsset saved = AssetDatabase.LoadAssetAtPath<DspChainAsset>(path);
                if (saved == null || saved.Count != effects.Count)
                {
                    throw new InvalidOperationException("Unity could not save the EQ chain.");
                }

                for (int index = 0; index < saved.Count; ++index)
                {
                    if (saved.Effects[index] == null || !BelongsToChain(saved.Effects[index], path))
                    {
                        throw new InvalidOperationException("Unity could not save an EQ band.");
                    }
                }

                return saved;
            }
            catch
            {
                if (!string.IsNullOrEmpty(createdGuid) && string.Equals(AssetDatabase.AssetPathToGUID(path), createdGuid, StringComparison.Ordinal))
                {
                    AssetDatabase.DeleteAsset(path);
                }

                foreach (DspEffectAsset filter in effects)
                {
                    if (filter != null && !AssetDatabase.Contains(filter))
                    {
                        UnityEngine.Object.DestroyImmediate(filter);
                    }
                }

                if (chain != null && !AssetDatabase.Contains(chain))
                {
                    UnityEngine.Object.DestroyImmediate(chain);
                }

                throw;
            }
        }

        private static bool BelongsToChain(DspEffectAsset stage, string path)
        {
            return AssetDatabase.IsSubAsset(stage) && string.Equals(AssetDatabase.GetAssetPath(stage), path, StringComparison.Ordinal);
        }

        private static DspFilterAsset CreateBand(DspEqCurveFit.Band band, int index)
        {
            DspFilterAsset stage = DspFilterAsset.Create(DspFilterMode.Bell, band.FrequencyHz, band.Quality, band.GainDecibels);
            stage.name = $"Bell {index + 1}";
            return stage;
        }

        private static void SetBand(DspFilterAsset stage, DspEqCurveFit.Band band)
        {
            using SerializedObject serialized = new(stage);
            Undo.RegisterCompleteObjectUndo(stage, UndoLabel);
            serialized.FindProperty("_mode").intValue = (int)DspFilterMode.Bell;
            serialized.FindProperty("_frequencyHz").floatValue = band.FrequencyHz;
            serialized.FindProperty("_quality").floatValue = band.Quality;
            serialized.FindProperty("_peakDecibels").floatValue = band.GainDecibels;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
