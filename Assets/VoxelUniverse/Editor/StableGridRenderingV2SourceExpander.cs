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
    public static class StableGridRenderingV2SourceExpander
    {
        private const string ExpectedSha256 = "c2f25a8ab2033a9ff91d37e8be4c379501ab38ca38a84b484042fb172319aa28";
        private const int ExpectedEntryCount = 10;
        private static readonly string[] PartPaths =
        {
            "Assets/VoxelUniverse/Editor/StableGridRenderingV2Sources.gz.b64.001.txt",
            "Assets/VoxelUniverse/Editor/StableGridRenderingV2Sources.gz.b64.002.txt"
        };

        [Serializable] private sealed class SourceBundle { public List<SourceEntry> entries = new List<SourceEntry>(); }
        [Serializable] private sealed class SourceEntry { public string path; public string content; }

        [MenuItem("Tools/Voxel Universe/1. Build Stable Grid Rendering V2 Sources")]
        public static void BuildSources()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
            {
                EditorUtility.DisplayDialog("Voxel Universe", "Exit Play Mode and wait for compilation first.", "OK");
                return;
            }
            try
            {
                StringBuilder encoded = new StringBuilder(20736);
                for (int i = 0; i < PartPaths.Length; i++)
                {
                    if (!File.Exists(PartPaths[i])) throw new FileNotFoundException("Missing bundle part: " + PartPaths[i]);
                    encoded.Append(File.ReadAllText(PartPaths[i], Encoding.ASCII).Trim());
                }
                byte[] compressed = Convert.FromBase64String(encoded.ToString());
                string actualHash;
                using (SHA256 sha = SHA256.Create())
                {
                    byte[] hash = sha.ComputeHash(compressed);
                    StringBuilder value = new StringBuilder(hash.Length * 2);
                    for (int i = 0; i < hash.Length; i++) value.Append(hash[i].ToString("x2"));
                    actualHash = value.ToString();
                }
                if (!string.Equals(actualHash, ExpectedSha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Checksum mismatch. Expected " + ExpectedSha256 + " but got " + actualHash);
                string json;
                using (MemoryStream input = new MemoryStream(compressed))
                using (GZipStream gzip = new GZipStream(input, CompressionMode.Decompress))
                using (StreamReader reader = new StreamReader(gzip, Encoding.UTF8)) json = reader.ReadToEnd();
                SourceBundle bundle = JsonUtility.FromJson<SourceBundle>(json);
                if (bundle == null || bundle.entries == null || bundle.entries.Count != ExpectedEntryCount)
                    throw new InvalidDataException("Bundle entry count is invalid.");
                string projectRoot = Directory.GetParent(Application.dataPath).FullName;
                string allowedRoot = Path.GetFullPath(Path.Combine(projectRoot, "Assets", "VoxelUniverse")) + Path.DirectorySeparatorChar;
                int changed = 0;
                for (int i = 0; i < bundle.entries.Count; i++)
                {
                    SourceEntry entry = bundle.entries[i];
                    if (string.IsNullOrEmpty(entry.path) || !entry.path.StartsWith("Assets/VoxelUniverse/", StringComparison.Ordinal)
                        || entry.path.IndexOf("Playground", StringComparison.OrdinalIgnoreCase) >= 0 || entry.path.Contains(".."))
                        throw new InvalidDataException("Rejected target: " + entry.path);
                    string fullPath = Path.GetFullPath(Path.Combine(projectRoot, entry.path.Replace('/', Path.DirectorySeparatorChar)));
                    if (!fullPath.StartsWith(allowedRoot, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("Target escaped VoxelUniverse: " + entry.path);
                    string existing = File.Exists(fullPath) ? File.ReadAllText(fullPath, Encoding.UTF8) : null;
                    if (string.Equals(existing, entry.content, StringComparison.Ordinal)) continue;
                    string directory = Path.GetDirectoryName(fullPath);
                    if (!Directory.Exists(directory)) Directory.CreateDirectory(directory);
                    string temporary = fullPath + ".stable-grid.tmp";
                    File.WriteAllText(temporary, entry.content, new UTF8Encoding(false));
                    File.Copy(temporary, fullPath, true);
                    File.Delete(temporary);
                    changed++;
                }
                AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
                Debug.Log("[Stable Voxel Grid] Expanded " + bundle.entries.Count + " verified files (" + changed + " changed). Playground was not touched.");
                EditorUtility.DisplayDialog("Voxel Universe", "Stable Grid Rendering V2 sources were written.\n\nWait for compilation, then run:\nTools → Voxel Universe → Install Stable Grid Rendering V2", "OK");
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                EditorUtility.DisplayDialog("Voxel Universe", "The package was not expanded.\n\n" + exception.Message, "OK");
            }
        }
    }
}
