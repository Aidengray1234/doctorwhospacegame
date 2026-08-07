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
    public static class WorkerTerrainHorizonLodSourceExpander
    {
        private static readonly string[] BundlePaths =
        {
            "Assets/VoxelUniverse/Editor/WorkerTerrainHorizonLodSources.gz.b64.001.txt",
            "Assets/VoxelUniverse/Editor/WorkerTerrainHorizonLodSources.gz.b64.002.txt",
            "Assets/VoxelUniverse/Editor/WorkerTerrainHorizonLodSources.gz.b64.003.txt",
            "Assets/VoxelUniverse/Editor/WorkerTerrainHorizonLodSources.gz.b64.004.txt"
        };
        private const string ExpectedSha256 =
            "5f25bfb0cf80c5332bb025f22d73d2aa746c2808178cd9fc677c37fba9a5cafd";
        private const int ExpectedEntryCount = 11;

        [Serializable]
        private sealed class SourceBundle
        {
            public List<SourceEntry> entries = new List<SourceEntry>();
        }

        [Serializable]
        private sealed class SourceEntry
        {
            public string path;
            public string content;
        }

        [MenuItem("Tools/Voxel Universe/1. Build Worker Terrain + Horizon LOD Sources")]
        public static void BuildSources()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
            {
                EditorUtility.DisplayDialog("Voxel Universe",
                    "Exit Play Mode and wait for compilation before expanding the worker-terrain package.",
                    "OK");
                return;
            }

            try
            {
                string projectRoot = Directory.GetParent(Application.dataPath).FullName;
                StringBuilder base64Builder = new StringBuilder();
                for (int bundleIndex = 0; bundleIndex < BundlePaths.Length; bundleIndex++)
                {
                    string fullBundlePath = Path.GetFullPath(Path.Combine(projectRoot,
                        BundlePaths[bundleIndex].Replace('/', Path.DirectorySeparatorChar)));
                    if (!File.Exists(fullBundlePath))
                        throw new FileNotFoundException("Verified worker-terrain bundle part is missing.",
                            fullBundlePath);
                    base64Builder.Append(File.ReadAllText(fullBundlePath, Encoding.UTF8).Trim());
                }
                string base64 = base64Builder.ToString();
                byte[] compressed = Convert.FromBase64String(base64);
                string actualHash = ComputeSha256(compressed);
                if (!string.Equals(actualHash, ExpectedSha256,
                    StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Bundle checksum mismatch. Expected "
                        + ExpectedSha256 + " but received " + actualHash + ".");

                string json = DecompressUtf8(compressed);
                SourceBundle bundle = JsonUtility.FromJson<SourceBundle>(json);
                if (bundle == null || bundle.entries == null
                    || bundle.entries.Count != ExpectedEntryCount)
                    throw new InvalidDataException("Bundle entry count is invalid.");

                for (int i = 0; i < bundle.entries.Count; i++)
                    ValidateTarget(bundle.entries[i].path, projectRoot);

                int changed = 0;
                AssetDatabase.StartAssetEditing();
                try
                {
                    for (int i = 0; i < bundle.entries.Count; i++)
                    {
                        SourceEntry entry = bundle.entries[i];
                        string fullPath = Path.GetFullPath(Path.Combine(projectRoot,
                            entry.path.Replace('/', Path.DirectorySeparatorChar)));
                        string existing = File.Exists(fullPath)
                            ? File.ReadAllText(fullPath, Encoding.UTF8) : null;
                        if (string.Equals(existing, entry.content, StringComparison.Ordinal))
                            continue;

                        string directory = Path.GetDirectoryName(fullPath);
                        if (!Directory.Exists(directory)) Directory.CreateDirectory(directory);
                        string temporary = fullPath + ".worker-lod.tmp";
                        File.WriteAllText(temporary, entry.content, new UTF8Encoding(false));
                        File.Copy(temporary, fullPath, true);
                        File.Delete(temporary);
                        changed++;
                    }
                }
                finally
                {
                    AssetDatabase.StopAssetEditing();
                }

                AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
                Debug.Log("[Voxel Universe] Expanded " + bundle.entries.Count
                    + " verified worker-terrain/horizon-LOD files (" + changed
                    + " changed). No Playground path was written.");
                EditorUtility.DisplayDialog("Voxel Universe",
                    "Worker terrain + horizon LOD sources were expanded.\n\n"
                    + "Wait for Unity to compile, then run:\n"
                    + "Tools → Voxel Universe → Install Worker Terrain + Horizon LOD",
                    "OK");
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                EditorUtility.DisplayDialog("Voxel Universe",
                    "The worker-terrain package was NOT expanded.\n\n"
                    + exception.Message, "OK");
            }
        }

        private static void ValidateTarget(string relativePath, string projectRoot)
        {
            if (string.IsNullOrEmpty(relativePath))
                throw new InvalidDataException("A bundle target path is empty.");
            string normalized = relativePath.Replace('\\', '/');
            if (!normalized.StartsWith("Assets/VoxelUniverse/",
                StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Rejected target outside Assets/VoxelUniverse: "
                    + relativePath);
            if (normalized.IndexOf("Playground",
                StringComparison.OrdinalIgnoreCase) >= 0)
                throw new InvalidDataException("Rejected Playground target: " + relativePath);

            string fullPath = Path.GetFullPath(Path.Combine(projectRoot,
                normalized.Replace('/', Path.DirectorySeparatorChar)));
            string allowedRoot = Path.GetFullPath(Path.Combine(projectRoot,
                "Assets/VoxelUniverse")) + Path.DirectorySeparatorChar;
            if (!fullPath.StartsWith(allowedRoot, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Rejected path traversal target: " + relativePath);
        }

        private static string ComputeSha256(byte[] data)
        {
            using (SHA256 sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(data);
                StringBuilder builder = new StringBuilder(hash.Length * 2);
                for (int i = 0; i < hash.Length; i++)
                    builder.Append(hash[i].ToString("x2"));
                return builder.ToString();
            }
        }

        private static string DecompressUtf8(byte[] compressed)
        {
            using (MemoryStream input = new MemoryStream(compressed))
            using (GZipStream gzip = new GZipStream(input, CompressionMode.Decompress))
            using (StreamReader reader = new StreamReader(gzip, Encoding.UTF8))
                return reader.ReadToEnd();
        }
    }
}
