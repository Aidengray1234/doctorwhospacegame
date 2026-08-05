using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;

namespace UnityGPTBridge.Editor
{
    internal static class UnityGPTBridgeSettings
    {
        private const string Prefix = "UnityGPTBridge.";

        public static string RemoteName
        {
            get { return EditorPrefs.GetString(Prefix + "RemoteName", "origin"); }
            set { EditorPrefs.SetString(Prefix + "RemoteName", value); }
        }

        public static string WorkBranch
        {
            get { return EditorPrefs.GetString(Prefix + "WorkBranch", "unity-gpt-work"); }
            set { EditorPrefs.SetString(Prefix + "WorkBranch", value); }
        }

        public static string StatusBranch
        {
            get { return EditorPrefs.GetString(Prefix + "StatusBranch", "unity-gpt-status"); }
            set { EditorPrefs.SetString(Prefix + "StatusBranch", value); }
        }

        public static bool AutoExport
        {
            get { return EditorPrefs.GetBool(Prefix + "AutoExport", true); }
            set { EditorPrefs.SetBool(Prefix + "AutoExport", value); }
        }

        public static bool AllowUnityYaml
        {
            get { return EditorPrefs.GetBool(Prefix + "AllowUnityYaml", false); }
            set { EditorPrefs.SetBool(Prefix + "AllowUnityYaml", value); }
        }

        public static int MaxHierarchyObjects
        {
            get { return EditorPrefs.GetInt(Prefix + "MaxHierarchyObjects", 5000); }
            set { EditorPrefs.SetInt(Prefix + "MaxHierarchyObjects", Math.Max(100, value)); }
        }

        public static int EditorLogTailKilobytes
        {
            get { return EditorPrefs.GetInt(Prefix + "EditorLogTailKilobytes", 256); }
            set { EditorPrefs.SetInt(Prefix + "EditorLogTailKilobytes", Math.Max(16, value)); }
        }
    }

    internal static class UnityGPTSafety
    {
        private static readonly HashSet<string> SafeTextExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".cs", ".asmdef", ".asmref", ".json", ".shader", ".compute", ".hlsl", ".cginc",
            ".uxml", ".uss", ".txt", ".md", ".xml", ".yml", ".yaml", ".meta", ".rsp",
            ".props", ".targets", ".js", ".ts", ".ps1", ".bat", ".cmd", ".gitignore"
        };

        private static readonly HashSet<string> UnityYamlExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".unity", ".prefab", ".mat", ".asset", ".anim", ".controller", ".overridecontroller",
            ".physicmaterial", ".physicsmaterial2d", ".rendertexture", ".lighting"
        };

        private static readonly string[] DeniedTopLevelDirectories =
        {
            "Library", "Temp", "Logs", "obj", "Build", "Builds", "MemoryCaptures", "Recordings"
        };

        public static bool TryGetSafeProjectPath(string repositoryPath, out string fullPath, out string reason)
        {
            fullPath = null;
            reason = null;

            if (string.IsNullOrWhiteSpace(repositoryPath))
            {
                reason = "Path is empty.";
                return false;
            }

            string normalized = repositoryPath.Replace('\\', '/').TrimStart('/');
            if (normalized.Contains("../") || normalized == ".." || Path.IsPathRooted(normalized))
            {
                reason = "Absolute paths and parent traversal are blocked.";
                return false;
            }

            string firstSegment = normalized.Split('/')[0];
            for (int i = 0; i < DeniedTopLevelDirectories.Length; i++)
            {
                if (string.Equals(firstSegment, DeniedTopLevelDirectories[i], StringComparison.OrdinalIgnoreCase))
                {
                    reason = "The " + firstSegment + " directory is blocked.";
                    return false;
                }
            }

            bool allowedRoot = normalized.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase)
                               || normalized.StartsWith("Packages/", StringComparison.OrdinalIgnoreCase)
                               || normalized.StartsWith("ProjectSettings/", StringComparison.OrdinalIgnoreCase)
                               || normalized.StartsWith(".unity-gpt/", StringComparison.OrdinalIgnoreCase)
                               || string.Equals(normalized, "AGENTS.md", StringComparison.OrdinalIgnoreCase)
                               || string.Equals(normalized, ".gitignore", StringComparison.OrdinalIgnoreCase);

            if (!allowedRoot)
            {
                reason = "Only Assets, Packages, ProjectSettings, .unity-gpt, AGENTS.md, and .gitignore are allowed.";
                return false;
            }

            string extension = Path.GetExtension(normalized);
            bool extensionAllowed = SafeTextExtensions.Contains(extension)
                                    || string.Equals(Path.GetFileName(normalized), ".gitignore", StringComparison.OrdinalIgnoreCase);

            if (!extensionAllowed && UnityGPTBridgeSettings.AllowUnityYaml)
            {
                extensionAllowed = UnityYamlExtensions.Contains(extension);
            }

            if (!extensionAllowed)
            {
                reason = string.IsNullOrEmpty(extension)
                    ? "Files without an approved text extension are blocked."
                    : "The " + extension + " extension is blocked in the current safety mode.";
                return false;
            }

            string projectRoot = Path.GetFullPath(UnityGPTPaths.ProjectRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string candidate = Path.GetFullPath(Path.Combine(projectRoot, normalized));
            string projectPrefix = projectRoot + Path.DirectorySeparatorChar;

            if (!candidate.StartsWith(projectPrefix, StringComparison.OrdinalIgnoreCase))
            {
                reason = "Resolved path is outside the Unity project.";
                return false;
            }

            fullPath = candidate;
            return true;
        }

        public static bool IsBranchNameSafe(string branch)
        {
            if (string.IsNullOrWhiteSpace(branch))
            {
                return false;
            }

            for (int i = 0; i < branch.Length; i++)
            {
                char c = branch[i];
                bool allowed = char.IsLetterOrDigit(c) || c == '-' || c == '_' || c == '/' || c == '.';
                if (!allowed)
                {
                    return false;
                }
            }

            return !branch.Contains("..") && !branch.StartsWith("-") && !branch.EndsWith("/");
        }
    }
}
