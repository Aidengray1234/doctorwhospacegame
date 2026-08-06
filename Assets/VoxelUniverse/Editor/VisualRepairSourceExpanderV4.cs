using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace DoctorWho.VoxelUniverse.Editor
{
    public static class VisualRepairSourceExpanderV4
    {
        private static readonly string[] BundleParts =
        {
            "Assets/VoxelUniverse/Editor/VisualRepairSources.gz.b64.001.txt",
            "Assets/VoxelUniverse/Editor/VisualRepairSources.gz.b64.002.txt",
            "Assets/VoxelUniverse/Editor/VisualRepairSources.gz.b64.003a.txt",
            "Assets/VoxelUniverse/Editor/VisualRepairSources.gz.b64.003b.txt",
            "Assets/VoxelUniverse/Editor/VisualRepairSources.gz.b64.004.txt"
        };

        [MenuItem("Tools/Voxel Universe/1. Build Visual Repair Sources")]
        public static void Expand()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
            {
                EditorUtility.DisplayDialog(
                    "Voxel Universe",
                    "Exit Play Mode and wait for compilation first.",
                    "OK");
                return;
            }

            StringBuilder encoded = new StringBuilder(50000);
            for (int part = 0; part < BundleParts.Length; part++)
            {
                string assetPath = BundleParts[part];
                string fullPath = Path.GetFullPath(assetPath);
                if (!File.Exists(fullPath))
                {
                    EditorUtility.DisplayDialog(
                        "Voxel Universe",
                        "Repair source bundle part is missing: " + assetPath,
                        "OK");
                    return;
                }

                encoded.Append(File.ReadAllText(fullPath).Trim());
            }

            string bundleText;
            try
            {
                byte[] compressed = Convert.FromBase64String(encoded.ToString());
                using (MemoryStream input = new MemoryStream(compressed))
                using (GZipStream gzip = new GZipStream(input, CompressionMode.Decompress))
                using (StreamReader reader = new StreamReader(gzip, Encoding.UTF8))
                {
                    bundleText = reader.ReadToEnd();
                }
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                EditorUtility.DisplayDialog(
                    "Voxel Universe",
                    "The visual repair bundle failed verification/decompression. Nothing was written.",
                    "OK");
                return;
            }

            string[] lines = bundleText.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
            int written = 0;
            try
            {
                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i].TrimEnd('\r');
                    int separator = line.IndexOf('|');
                    if (separator <= 0 || separator >= line.Length - 1)
                        throw new InvalidDataException("Invalid repair bundle line " + (i + 1));

                    string assetPath = line.Substring(0, separator);
                    if (!assetPath.StartsWith("Assets/VoxelUniverse/", StringComparison.Ordinal)
                        || assetPath.IndexOf("Playground", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        throw new InvalidDataException("Unsafe repair target rejected: " + assetPath);
                    }

                    byte[] bytes = Convert.FromBase64String(line.Substring(separator + 1));
                    string fullPath = Path.GetFullPath(assetPath);
                    string directory = Path.GetDirectoryName(fullPath);
                    if (!Directory.Exists(directory)) Directory.CreateDirectory(directory);
                    File.WriteAllText(fullPath, Encoding.UTF8.GetString(bytes), new UTF8Encoding(false));
                    written++;
                }
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                EditorUtility.DisplayDialog(
                    "Voxel Universe",
                    "The repair bundle contained an invalid or unsafe entry. Stop here and send the Console error.",
                    "OK");
                return;
            }

            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            Debug.Log(
                "[Voxel Universe Repair] Wrote " + written
                + " verified repair source files. Wait for compilation, then run "
                + "Tools > Voxel Universe > Install Or Repair Blocks, Textures and LOD.");
            EditorUtility.DisplayDialog(
                "Voxel Universe",
                "Wrote " + written + " repair source files. Wait for Unity to compile, then run:\n\n"
                + "Tools > Voxel Universe > Install Or Repair Blocks, Textures and LOD",
                "OK");
        }
    }
}
