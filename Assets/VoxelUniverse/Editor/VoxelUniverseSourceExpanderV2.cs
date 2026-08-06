using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace DoctorWho.VoxelUniverse.Editor
{
    public static class VoxelUniverseSourceExpanderV2
    {
        private const string BundlePrefix = "Assets/VoxelUniverse/Editor/VoxelUniverseRuntime.bundle.";
        private const int BundlePartCount = 9;
        private const string RequiredPrefix = "Assets/VoxelUniverse/";

        [Serializable]
        private sealed class SourceBundle
        {
            public int schemaVersion;
            public string packageId;
            public SourceFile[] files;
        }

        [Serializable]
        private sealed class SourceFile
        {
            public string path;
            public string base64;
        }

        [MenuItem("Tools/Voxel Universe/1. Build Runtime Sources")]
        public static void BuildRuntimeSources()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorUtility.DisplayDialog("Voxel Universe", "Exit Play Mode before expanding the runtime sources.", "OK");
                return;
            }

            SourceBundle bundle;
            try
            {
                StringBuilder encoded = new StringBuilder();
                for (int part = 1; part <= BundlePartCount; part++)
                {
                    string path = BundlePrefix + part.ToString("000") + ".txt";
                    TextAsset asset = AssetDatabase.LoadAssetAtPath<TextAsset>(path);
                    if (asset == null)
                        throw new FileNotFoundException("Missing runtime bundle part: " + path);
                    encoded.Append(asset.text.Trim());
                }

                byte[] compressed = Convert.FromBase64String(encoded.ToString());
                string json;
                using (MemoryStream input = new MemoryStream(compressed))
                using (GZipStream gzip = new GZipStream(input, CompressionMode.Decompress))
                using (StreamReader reader = new StreamReader(gzip, Encoding.UTF8))
                    json = reader.ReadToEnd();

                bundle = JsonUtility.FromJson<SourceBundle>(json);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                EditorUtility.DisplayDialog("Voxel Universe", "The runtime source bundle could not be decoded.\n\n" + exception.Message, "OK");
                return;
            }

            if (bundle == null || bundle.schemaVersion != 1 || bundle.files == null || bundle.files.Length == 0)
            {
                EditorUtility.DisplayDialog("Voxel Universe", "The runtime source bundle is empty or uses an unsupported schema.", "OK");
                return;
            }

            int written = 0;
            int unchanged = 0;
            try
            {
                string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
                for (int i = 0; i < bundle.files.Length; i++)
                {
                    SourceFile entry = bundle.files[i];
                    ValidatePath(entry.path);
                    byte[] bytes = Convert.FromBase64String(entry.base64 ?? string.Empty);
                    string fullPath = Path.GetFullPath(entry.path);

                    if (!fullPath.StartsWith(projectRoot, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException("Bundle path escaped the Unity project: " + entry.path);

                    string directory = Path.GetDirectoryName(fullPath);
                    if (!Directory.Exists(directory)) Directory.CreateDirectory(directory);

                    if (File.Exists(fullPath))
                    {
                        byte[] current = File.ReadAllBytes(fullPath);
                        if (BytesEqual(current, bytes))
                        {
                            unchanged++;
                            continue;
                        }
                    }

                    string temporary = fullPath + ".voxel-universe-tmp";
                    File.WriteAllBytes(temporary, bytes);
                    if (File.Exists(fullPath)) File.Delete(fullPath);
                    File.Move(temporary, fullPath);
                    written++;
                }
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                EditorUtility.DisplayDialog(
                    "Voxel Universe",
                    "Source expansion stopped before completion.\n\n" + exception.Message,
                    "OK");
                return;
            }

            AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
            Debug.Log("[Voxel Universe] Expanded package " + bundle.packageId + ": "
                      + written + " files written, " + unchanged + " unchanged. Playground was not modified.");
            EditorUtility.DisplayDialog(
                "Voxel Universe",
                "Runtime source expansion finished.\n\nWritten: " + written
                + "\nUnchanged: " + unchanged
                + "\n\nWait for Unity to finish compiling. Then run:\nTools → Voxel Universe → Install Production Runtime",
                "OK");
        }

        private static void ValidatePath(string path)
        {
            if (string.IsNullOrEmpty(path))
                throw new InvalidOperationException("A bundle entry has no path.");

            string normalized = path.Replace('\\', '/');
            if (!normalized.StartsWith(RequiredPrefix, StringComparison.Ordinal))
                throw new InvalidOperationException("Bundle path is outside Assets/VoxelUniverse: " + path);
            if (normalized.IndexOf("Playground", StringComparison.OrdinalIgnoreCase) >= 0)
                throw new InvalidOperationException("Playground paths are forbidden: " + path);
            if (normalized.Contains("../") || normalized.Contains("/.."))
                throw new InvalidOperationException("Parent traversal is forbidden: " + path);
        }

        private static bool BytesEqual(byte[] a, byte[] b)
        {
            if (ReferenceEquals(a, b)) return true;
            if (a == null || b == null || a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++)
                if (a[i] != b[i]) return false;
            return true;
        }
    }
}
