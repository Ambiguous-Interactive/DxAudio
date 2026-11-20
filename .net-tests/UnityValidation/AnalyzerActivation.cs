namespace WallstopStudios.DxAudio.Tests.Editor
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using NUnit.Framework;
    using UnityEditor;
    using UnityEditor.Compilation;
    using UnityEditorInternal;
    using UnityEngine;

    [TestFixture]
    internal sealed class AnalyzerActivation
    {
        private static bool HasFile(string[] paths, string name)
        {
            foreach (string path in paths)
            {
                if (string.Equals(Path.GetFileName(path), name, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsDxAudioAssembly(string name)
        {
            return string.Equals(name, "WallstopStudios.DxAudio", StringComparison.Ordinal) || name.StartsWith("WallstopStudios.DxAudio.", StringComparison.Ordinal);
        }

        private static bool IsImplicitUnityAssembly(string name)
        {
            return name is "UnityEngine.TestRunner" or "UnityEditor.TestRunner" or "UnityEngine.UI" or "UnityEditor.UI";
        }

        [Test]
        public void AllExpectedAssembliesUseThePinnedAnalyzers()
        {
            bool integration = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DXMESSAGING_VERSION"));
            string[] expected = integration
                ? new[]
                {
                    "WallstopStudios.DxAudio",
                    "WallstopStudios.DxAudio.Editor",
                    "WallstopStudios.DxAudio.Tests.Editor",
                    "WallstopStudios.DxAudio.Samples.Validation",
                    "WallstopStudios.DxAudio.Integration.DxMessaging",
                    "WallstopStudios.DxAudio.Tests.Integration.DxMessaging",
                }
                : new[] { "WallstopStudios.DxAudio", "WallstopStudios.DxAudio.Editor", "WallstopStudios.DxAudio.Tests.Editor", "WallstopStudios.DxAudio.Samples.Validation" };
            Assembly[] assemblies = CompilationPipeline.GetAssemblies();
            foreach (string name in expected)
            {
                Assembly assembly = Array.Find(assemblies, candidate => string.Equals(candidate.name, name, StringComparison.Ordinal));
                Assert.That(assembly, Is.Not.Null, name);
                ScriptCompilerOptions options = assembly.compilerOptions;
                Assert.That(HasFile(options.RoslynAnalyzerDllPaths, "WallstopStudios.UnityHelpers.Analyzers.dll"), Is.True, name);
                Assert.That(HasFile(options.RoslynAnalyzerDllPaths, "nunit.analyzers.dll"), Is.True, name);
                Assert.That(string.Equals(Path.GetFileName(options.RoslynAnalyzerRulesetPath), "WarningsAsErrors.ruleset", StringComparison.Ordinal), Is.True, name);
            }
        }

        [Test]
        public void AssemblyDefinitionsMatchTheCompilerReferenceGraph()
        {
            foreach (Assembly assembly in CompilationPipeline.GetAssemblies())
            {
                if (!IsDxAudioAssembly(assembly.name))
                {
                    continue;
                }

                string path = CompilationPipeline.GetAssemblyDefinitionFilePathFromAssemblyName(assembly.name);
                AssemblyDefinitionAsset asset = AssetDatabase.LoadAssetAtPath<AssemblyDefinitionAsset>(path);
                Assert.That(asset != null, Is.True, assembly.name);
                AssemblyDefinition definition = JsonUtility.FromJson<AssemblyDefinition>(asset.text);
                HashSet<string> declared = new(definition.references, StringComparer.Ordinal);
                HashSet<string> compiled = new(StringComparer.Ordinal);
                foreach (Assembly reference in assembly.assemblyReferences)
                {
                    if (declared.Contains(reference.name) || !IsImplicitUnityAssembly(reference.name))
                    {
                        compiled.Add(reference.name);
                    }
                }

                Assert.That(compiled, Is.EquivalentTo(declared), assembly.name);
            }
        }

        [Test]
        public void EditorOnlyAssembliesContainNoConcreteMonoBehaviours()
        {
            HashSet<string> editorAssemblies = new(StringComparer.Ordinal);
            foreach (Assembly assembly in CompilationPipeline.GetAssemblies())
            {
                if (IsDxAudioAssembly(assembly.name) && (assembly.flags & AssemblyFlags.EditorAssembly) == AssemblyFlags.EditorAssembly)
                {
                    editorAssemblies.Add(assembly.name);
                }
            }

            Assert.That(editorAssemblies, Does.Contain("WallstopStudios.DxAudio.Editor"));
            List<string> misplaced = new();
            foreach (Type type in TypeCache.GetTypesDerivedFrom<MonoBehaviour>())
            {
                if (!type.IsAbstract && !type.ContainsGenericParameters && editorAssemblies.Contains(type.Assembly.GetName().Name))
                {
                    misplaced.Add(type.FullName);
                }
            }

            Assert.That(misplaced, Is.Empty);
        }

        [Serializable]
        private sealed class AssemblyDefinition
        {
            public string[] references = Array.Empty<string>();
        }
    }
}
