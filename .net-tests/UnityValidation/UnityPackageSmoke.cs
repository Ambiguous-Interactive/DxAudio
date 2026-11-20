namespace WallstopStudios.DxAudio.Validation
{
    using System;
    using System.IO;
    using UnityEditor;

    public static class UnityPackageSmoke
    {
        public static void Run()
        {
            string archive = Environment.GetEnvironmentVariable("DXAUDIO_UNITYPACKAGE");
            if (string.IsNullOrEmpty(archive) || !File.Exists(archive))
            {
                throw new InvalidOperationException("The DxAudio Unity import archive is missing.");
            }

            AssetDatabase.importPackageCompleted += OnCompleted;
            AssetDatabase.importPackageFailed += OnFailed;
            AssetDatabase.ImportPackage(archive, false);
        }

        private static void OnCompleted(string packageName)
        {
            EditorApplication.update += VerifyImportedPackage;
        }

        private static void VerifyImportedPackage()
        {
            const string Root = "Packages/com.wallstop-studios.dxaudio";
            UnityEditor.PackageManager.PackageInfo package = UnityEditor.PackageManager.PackageInfo.FindForAssetPath(Root + "/package.json");
            if (package == null || package.source != UnityEditor.PackageManager.PackageSource.Embedded || package.dependencies.Length != 0)
            {
                return;
            }

            string asset = Root + "/Runtime/WallstopStudios.DxAudio.asmdef";
            string guid = AssetDatabase.AssetPathToGUID(asset);
            if (string.IsNullOrEmpty(guid) || !File.Exists(asset + ".meta") || !File.ReadAllText(asset + ".meta").Contains("guid: " + guid, StringComparison.Ordinal))
            {
                return;
            }

            UnityEngine.Debug.Log("DxAudio Unity import validation passed.");
            EditorApplication.Exit(0);
        }

        private static void OnFailed(string packageName, string error)
        {
            UnityEngine.Debug.LogError(error);
            EditorApplication.Exit(1);
        }
    }
}
