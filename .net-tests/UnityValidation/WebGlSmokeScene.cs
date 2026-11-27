namespace WallstopStudios.DxAudio.Validation
{
    using System;
    using System.IO;
    using UnityEditor;
    using UnityEditor.Build.Reporting;
    using UnityEditor.SceneManagement;
    using UnityEngine;
    using UnityEngine.SceneManagement;

    /*
        Builds the WebGL smoke player for the CI lane (PLAN.md W-CI, spec map 01). The disposable validation project
        stages this script into Assets/Editor and runs it with -buildTarget WebGL. The scene is authored in code so the
        repository carries no build-only scene asset, and the build stays uncompressed so the next step can serve it
        with a plain static file server. The receipt is the machine-checkable proof the validator and CI require.
    */
    public static class WebGlSmokeScene
    {
        private const string ScenePath = "Assets/WebGlSmoke.unity";
        private const string Receipt = "DxAudio WebGL build validation passed.";

        public static void Run()
        {
            string output = Environment.GetEnvironmentVariable("DXAUDIO_WEBGL_OUTPUT");
            if (string.IsNullOrEmpty(output))
            {
                throw new InvalidOperationException("DXAUDIO_WEBGL_OUTPUT is required to place the WebGL build.");
            }

            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            GameObject rig = new GameObject("WebGlSmokeRig");
            rig.AddComponent<Camera>();
            rig.AddComponent<AudioListener>();
            rig.AddComponent<SfxPlayer>();
            rig.AddComponent<WebGlSmokePlayer>();
            EditorSceneManager.SaveScene(scene, ScenePath);

            PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Disabled;
            PlayerSettings.WebGL.decompressionFallback = false;

            BuildPlayerOptions options = new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = output,
                target = BuildTarget.WebGL,
            };
            BuildReport report = BuildPipeline.BuildPlayer(options);
            if (report.summary.result != BuildResult.Succeeded)
            {
                throw new InvalidOperationException($"The WebGL build reported {report.summary.result} with {report.summary.totalErrors} error(s).");
            }

            long totalBytes = 0;
            int fileCount = 0;
            foreach (string file in Directory.GetFiles(output, "*", SearchOption.AllDirectories))
            {
                ++fileCount;
                totalBytes += new FileInfo(file).Length;
            }
            Debug.Log($"DxAudio WebGL build inventory: {fileCount} file(s), {totalBytes} byte(s) in {output}");
            Debug.Log(Receipt);
        }
    }
}
