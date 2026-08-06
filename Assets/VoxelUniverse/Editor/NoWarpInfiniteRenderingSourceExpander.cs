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
    public static class NoWarpInfiniteRenderingSourceExpander
    {
        private const string ExpectedSha256 = "564552521384009d3d97eec9777f98f7895f44d4174ecb01c4f13335e14ae530";
        private const int ExpectedEntryCount = 12;
        private static readonly string[] PartPaths =
        {
            "Assets/VoxelUniverse/Editor/NoWarpInfiniteRenderingSources.gz.b64.001.txt",
            "Assets/VoxelUniverse/Editor/NoWarpInfiniteRenderingSources.gz.b64.002.txt",
            "Assets/VoxelUniverse/Editor/NoWarpInfiniteRenderingSources.gz.b64.003.txt"
        };

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

        [MenuItem("Tools/Voxel Universe/1. Build No-Warp Infinite Rendering Sources")]
        public static void BuildSources()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
            {
                EditorUtility.DisplayDialog("Voxel Universe",
                    "Exit Play Mode and wait for compilation before expanding the source package.", "OK");
                return;
            }

            try
            {
                string base64 = ReadAllParts();
                byte[] compressed = Convert.FromBase64String(base64);
                string actualHash = ComputeSha256(compressed);
                if (!string.Equals(actualHash, ExpectedSha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Bundle checksum mismatch. Expected "
                        + ExpectedSha256 + " but received " + actualHash + ".");

                string json = DecompressUtf8(compressed);
                SourceBundle bundle = JsonUtility.FromJson<SourceBundle>(json);
                if (bundle == null || bundle.entries == null
                    || bundle.entries.Count != ExpectedEntryCount)
                    throw new InvalidDataException("Bundle entry count is invalid.");

                string projectRoot = Directory.GetParent(Application.dataPath).FullName;
                int changed = 0;
                for (int i = 0; i < bundle.entries.Count; i++)
                {
                    SourceEntry entry = bundle.entries[i];
                    ValidateTarget(entry.path, projectRoot);
                    string fullPath = Path.GetFullPath(Path.Combine(projectRoot,
                        entry.path.Replace('/', Path.DirectorySeparatorChar)));
                    string existing = File.Exists(fullPath)
                        ? File.ReadAllText(fullPath, Encoding.UTF8) : null;
                    if (string.Equals(existing, entry.content, StringComparison.Ordinal)) continue;

                    string directory = Path.GetDirectoryName(fullPath);
                    if (!Directory.Exists(directory)) Directory.CreateDirectory(directory);
                    string temporary = fullPath + ".voxel-universe.tmp";
                    File.WriteAllText(temporary, entry.content, new UTF8Encoding(false));
                    File.Copy(temporary, fullPath, true);
                    File.Delete(temporary);
                    changed++;
                }

                AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
                Debug.Log("[Voxel Universe] Expanded " + bundle.entries.Count
                    + " verified no-warp/infinite-rendering files (" + changed
                    + " changed). Playground was not touched.");
                EditorUtility.DisplayDialog("Voxel Universe",
                    "The verified no-warp and infinite-rendering source set was written.\n\nWait for Unity to compile, then run:\nTools → Voxel Universe → Install No-Warp Infinite Rendering",
                    "OK");
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                EditorUtility.DisplayDialog("Voxel Universe",
                    "The source package was not expanded. No scene operation was run.\n\n"
                    + exception.Message, "OK");
            }
        }

        private static string ReadAllParts()
        {
            StringBuilder builder = new StringBuilder(30000);
            for (int i = 0; i < PartPaths.Length; i++)
            {
                if (!File.Exists(PartPaths[i]))
                    throw new FileNotFoundException("Missing bundle part: " + PartPaths[i]);
                builder.Append(File.ReadAllText(PartPaths[i], Encoding.ASCII).Trim());
            }
            return builder.ToString();
        }

        private static string ComputeSha256(byte[] data)
        {
            using (SHA256 sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(data);
                StringBuilder value = new StringBuilder(hash.Length * 2);
                for (int i = 0; i < hash.Length; i++) value.Append(hash[i].ToString("x2"));
                return value.ToString();
            }
        }

        private static string DecompressUtf8(byte[] compressed)
        {
            using (MemoryStream input = new MemoryStream(compressed))
            using (GZipStream gzip = new GZipStream(input, CompressionMode.Decompress))
            using (StreamReader reader = new StreamReader(gzip, Encoding.UTF8))
                return reader.ReadToEnd();
        }

        private static void ValidateTarget(string assetPath, string projectRoot)
        {
            if (string.IsNullOrEmpty(assetPath)
                || !assetPath.StartsWith("Assets/VoxelUniverse/", StringComparison.Ordinal)
                || assetPath.IndexOf("Playground", StringComparison.OrdinalIgnoreCase) >= 0
                || assetPath.Contains(".."))
                throw new InvalidDataException("Rejected target path: " + assetPath);

            string fullPath = Path.GetFullPath(Path.Combine(projectRoot,
                assetPath.Replace('/', Path.DirectorySeparatorChar)));
            string allowedRoot = Path.GetFullPath(Path.Combine(projectRoot,
                "Assets", "VoxelUniverse")) + Path.DirectorySeparatorChar;
            if (!fullPath.StartsWith(allowedRoot, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Target escaped Assets/VoxelUniverse: " + assetPath);
        }
    }
}
