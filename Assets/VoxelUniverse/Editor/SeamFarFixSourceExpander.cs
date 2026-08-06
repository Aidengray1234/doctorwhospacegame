using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace DoctorWho.VoxelUniverse.Editor
{
    public static class SeamFarFixSourceExpander
    {
        private const string BundlePath =
            "Assets/VoxelUniverse/Editor/SeamFarFixSources.gz.b64.txt";
        private const string ExpectedSha256 =
            "176d8c8cfcfaf00f3d3538f7c198de9112d832d200c7c6ab378fcbabc76b154c";

        [Serializable]
        private sealed class SourceFile
        {
            public string path;
            public string content;
        }

        [Serializable]
        private sealed class SourceBundle
        {
            public SourceFile[] files;
        }

        [MenuItem("Tools/Voxel Universe/1. Build Seam And Far LOD Fix")]
        public static void BuildSources()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
            {
                EditorUtility.DisplayDialog("Voxel Universe",
                    "Exit Play Mode and wait for compilation first.", "OK");
                return;
            }

            if (!File.Exists(BundlePath))
            {
                Debug.LogError("[Voxel Universe Seam Fix] Missing bundle: " + BundlePath);
                return;
            }

            try
            {
                string encoded = File.ReadAllText(BundlePath).Trim();
                byte[] compressed = Convert.FromBase64String(encoded);
                string json;
                using (MemoryStream input = new MemoryStream(compressed))
                using (GZipStream gzip = new GZipStream(input, CompressionMode.Decompress))
                using (StreamReader reader = new StreamReader(gzip, Encoding.UTF8))
                    json = reader.ReadToEnd();

                string actualHash = ComputeSha256(Encoding.UTF8.GetBytes(json));
                if (!string.Equals(actualHash, ExpectedSha256,
                    StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException(
                        "Bundle checksum mismatch. Expected " + ExpectedSha256
                        + " but got " + actualHash + ".");

                SourceBundle bundle = JsonUtility.FromJson<SourceBundle>(json);
                if (bundle == null || bundle.files == null || bundle.files.Length == 0)
                    throw new InvalidDataException("The source bundle is empty.");

                int written = 0;
                for (int i = 0; i < bundle.files.Length; i++)
                {
                    SourceFile file = bundle.files[i];
                    ValidateTarget(file.path);
                    string directory = Path.GetDirectoryName(file.path);
                    if (!string.IsNullOrEmpty(directory))
                        Directory.CreateDirectory(directory);

                    string normalized = file.content ?? string.Empty;
                    if (File.Exists(file.path)
                        && string.Equals(File.ReadAllText(file.path), normalized,
                            StringComparison.Ordinal))
                        continue;

                    File.WriteAllText(file.path, normalized, new UTF8Encoding(false));
                    written++;
                }

                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                Debug.Log("[Voxel Universe Seam Fix] Wrote " + written
                    + " changed source files. Wait for compilation, then run "
                    + "Tools > Voxel Universe > Install Or Repair Blocks, Textures and LOD.");
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                EditorUtility.DisplayDialog("Voxel Universe Seam Fix",
                    "The repair source bundle could not be expanded. Check the Console.",
                    "OK");
            }
        }

        private static void ValidateTarget(string path)
        {
            if (string.IsNullOrEmpty(path)
                || !path.StartsWith("Assets/VoxelUniverse/",
                    StringComparison.Ordinal)
                || path.IndexOf("Playground",
                    StringComparison.OrdinalIgnoreCase) >= 0
                || path.Contains(".."))
                throw new InvalidDataException("Rejected target path: " + path);
        }

        private static string ComputeSha256(byte[] data)
        {
            using (System.Security.Cryptography.SHA256 hash =
                   System.Security.Cryptography.SHA256.Create())
            {
                byte[] digest = hash.ComputeHash(data);
                StringBuilder builder = new StringBuilder(digest.Length * 2);
                for (int i = 0; i < digest.Length; i++)
                    builder.Append(digest[i].ToString("x2"));
                return builder.ToString();
            }
        }
    }
}
