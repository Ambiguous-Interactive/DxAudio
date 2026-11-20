namespace WallstopStudios.DxAudio.Validation
{
    using System;
    using System.IO;
    using UnityEditor;
    using UnityEditor.Build.Player;
    using UnityEditor.Compilation;
    using UnityEditor.PackageManager;

    public static class ArchiveSmoke
    {
        public static void Run()
        {
            UnityEditor.PackageManager.PackageInfo package = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(SfxPlayer).Assembly);
            if (package == null || package.source != PackageSource.LocalTarball || package.dependencies.Length != 0)
            {
                throw new InvalidOperationException("Expected an installed, dependency-free DxAudio tarball.");
            }

            AssertEditorSuppliedCompilerPolicy(FindAssembly("WallstopStudios.DxAudio").compilerOptions);
            AssertEditorSuppliedCompilerPolicy(FindAssembly("WallstopStudios.DxAudio.Editor").compilerOptions);

            string output = Path.Combine("Temp", "DxAudioPlayerScripts");
            ScriptCompilationSettings settings = new() { target = BuildTarget.StandaloneWindows64, group = BuildTargetGroup.Standalone };
            ScriptCompilationResult result = PlayerBuildInterface.CompilePlayerScripts(settings, output);
            if (result.assemblies == null || !File.Exists(Path.Combine(output, "WallstopStudios.DxAudio.dll")))
            {
                throw new InvalidOperationException("The archive did not compile a standalone player runtime assembly.");
            }

            UnityEngine.Debug.Log("DxAudio archive and player script validation passed.");
        }

        private static void AssertEditorSuppliedCompilerPolicy(ScriptCompilerOptions options)
        {
            if (!string.IsNullOrEmpty(options.RoslynAnalyzerRulesetPath))
            {
                throw new InvalidOperationException($"DxAudio loaded a ruleset from {options.RoslynAnalyzerRulesetPath}.");
            }

            /*
                Unity supplies its own analyzers and source generators even to a
                pristine project. Package analyzers resolve outside the installation.
            */
            string editorContents = Path.GetFullPath(EditorApplication.applicationContentsPath) + Path.DirectorySeparatorChar;
            foreach (string analyzerPath in options.RoslynAnalyzerDllPaths)
            {
                if (!Path.GetFullPath(analyzerPath).StartsWith(editorContents, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException($"DxAudio loaded an analyzer from {analyzerPath}.");
                }
            }
        }

        private static Assembly FindAssembly(string expected)
        {
            foreach (Assembly assembly in CompilationPipeline.GetAssemblies())
            {
                if (string.Equals(assembly.name, expected, StringComparison.Ordinal))
                {
                    return assembly;
                }
            }

            throw new InvalidOperationException($"Expected editor compilation to include {expected}.");
        }
    }
}
