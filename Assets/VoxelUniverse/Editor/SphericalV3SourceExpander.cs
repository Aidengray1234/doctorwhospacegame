using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace DoctorWho.VoxelUniverse.Editor
{
    public static class SphericalV3SourceExpander
    {
        private static readonly string[] BundlePaths =
        {
            "Assets/VoxelUniverse/Editor/SphericalV3Sources.gz.b64.001.txt",
            "Assets/VoxelUniverse/Editor/SphericalV3Sources.gz.b64.002.txt",
            "Assets/VoxelUniverse/Editor/SphericalV3Sources.gz.b64.003.txt",
            "Assets/VoxelUniverse/Editor/SphericalV3Sources.gz.b64.004.txt"
        };
        private const string ExpectedSha256 = "0c4da355ddb07838ea2c6359a324c7b194f5eda4a16236d9664afe2b01e1ddd9";
        private const int ExpectedEntryCount = 19;
        private const string RequiredPrefix = "Assets/VoxelUniverse/";

        [Serializable] private sealed class SourceBundle
        {
            public int schemaVersion;
            public string packageId;
            public List<SourceEntry> entries = new List<SourceEntry>();
        }
        [Serializable] private sealed class SourceEntry
        {
            public string path;
            public string content;
        }

        [MenuItem("Tools/Voxel Universe/1. Build Spherical Voxel Runtime V3 Sources")]
        public static void BuildSources()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
            {
                EditorUtility.DisplayDialog("Voxel Universe V3",
                    "Exit Play Mode and wait for compilation before expanding V3.", "OK");
                return;
            }

            try
            {
                string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
                StringBuilder encoded = new StringBuilder();
                for (int i = 0; i < BundlePaths.Length; i++)
                {
                    string full = Path.GetFullPath(Path.Combine(projectRoot,
                        BundlePaths[i].Replace('/', Path.DirectorySeparatorChar)));
                    if (!File.Exists(full))
                        throw new FileNotFoundException("Missing verified V3 bundle part.", full);
                    encoded.Append(File.ReadAllText(full, Encoding.UTF8).Trim());
                }

                byte[] compressed = Convert.FromBase64String(encoded.ToString());
                string actualHash = ComputeSha256(compressed);
                if (!string.Equals(actualHash, ExpectedSha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("V3 bundle checksum mismatch.");

                string json;
                using (MemoryStream input = new MemoryStream(compressed))
                using (GZipStream gzip = new GZipStream(input, CompressionMode.Decompress))
                using (StreamReader reader = new StreamReader(gzip, Encoding.UTF8))
                    json = reader.ReadToEnd();

                SourceBundle bundle = JsonUtility.FromJson<SourceBundle>(json);
                if (bundle == null || bundle.schemaVersion != 1 || bundle.entries == null
                    || bundle.entries.Count != ExpectedEntryCount)
                    throw new InvalidDataException("V3 source bundle is invalid.");

                for (int i = 0; i < bundle.entries.Count; i++)
                    ValidateTarget(bundle.entries[i].path, projectRoot);

                int changed = 0;
                int unchanged = 0;
                AssetDatabase.StartAssetEditing();
                try
                {
                    for (int i = 0; i < bundle.entries.Count; i++)
                    {
                        SourceEntry entry = bundle.entries[i];
                        string full = Path.GetFullPath(Path.Combine(projectRoot,
                            entry.path.Replace('/', Path.DirectorySeparatorChar)));
                        string existing = File.Exists(full)
                            ? File.ReadAllText(full, Encoding.UTF8) : null;
                        if (string.Equals(existing, entry.content, StringComparison.Ordinal))
                        {
                            unchanged++;
                            continue;
                        }
                        string directory = Path.GetDirectoryName(full);
                        if (!Directory.Exists(directory)) Directory.CreateDirectory(directory);
                        string temp = full + ".v3tmp";
                        File.WriteAllText(temp, entry.content, new UTF8Encoding(false));
                        if (File.Exists(full)) File.Delete(full);
                        File.Move(temp, full);
                        changed++;
                    }
                }
                finally
                {
                    AssetDatabase.StopAssetEditing();
                }

                AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
                Debug.Log("[Voxel Universe V3] Expanded verified package " + bundle.packageId
                    + ": " + changed + " changed, " + unchanged + " unchanged. Playground was not touched.");
                EditorUtility.DisplayDialog("Voxel Universe V3",
                    "V3 source expansion finished.\n\nWait for Unity to compile. "
                    + "The old Voxel Universe tool entries should disappear. Then run:\n"
                    + "Tools → Voxel Universe → Install Spherical Voxel Runtime V3", "OK");
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                EditorUtility.DisplayDialog("Voxel Universe V3",
                    "V3 expansion stopped before modifying the runtime completely.\n\n"
                    + exception.Message, "OK");
            }
        }

        private static void ValidateTarget(string path, string projectRoot)
        {
            if (string.IsNullOrEmpty(path))
                throw new InvalidDataException("A V3 bundle entry has no path.");
            string normalized = path.Replace('\\', '/');
            if (!normalized.StartsWith(RequiredPrefix, StringComparison.Ordinal))
                throw new InvalidDataException("V3 path escaped Assets/VoxelUniverse: " + path);
            if (normalized.IndexOf("Playground", StringComparison.OrdinalIgnoreCase) >= 0)
                throw new InvalidDataException("Playground paths are forbidden: " + path);
            if (normalized.Contains("../") || normalized.Contains("/.."))
                throw new InvalidDataException("Parent traversal is forbidden: " + path);
            string full = Path.GetFullPath(Path.Combine(projectRoot,
                normalized.Replace('/', Path.DirectorySeparatorChar)));
            if (!full.StartsWith(projectRoot, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("V3 path escaped the Unity project: " + path);
        }

        private static string ComputeSha256(byte[] data)
        {
            using (SHA256 sha256 = SHA256.Create())
            {
                byte[] hash = sha256.ComputeHash(data);
                StringBuilder builder = new StringBuilder(hash.Length * 2);
                for (int i = 0; i < hash.Length; i++) builder.Append(hash[i].ToString("x2"));
                return builder.ToString();
            }
        }
    }
}
